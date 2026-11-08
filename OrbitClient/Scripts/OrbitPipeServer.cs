using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OrbitClient.Scripts
{
    public class OrbitPipeServer
    {
        private
        const string PipeName = "OrbitClientPipe";

        private NamedPipeServerStream? pipe;
        private StreamReader? reader;
        private StreamWriter? writer;

        private CancellationTokenSource cts = new CancellationTokenSource();

        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        public bool IsConnected => pipe != null && pipe.IsConnected;

        public event Action<OrbitPipeMessage>? MessageReceived;
        public event Action? ModConnected;
        public event Action? ModDisconnected;

        public Task StartAsync()
        {
            return Task.Run(ServerLoop);
        }

        private async Task ServerLoop()
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    pipe = new NamedPipeServerStream(
                      PipeName,
                      PipeDirection.InOut,
                      1,
                      PipeTransmissionMode.Byte,
                      PipeOptions.Asynchronous
                    );

                    Console.WriteLine("[Orbit Pipe] Waiting for Gorilla Tag...");

                    await pipe.WaitForConnectionAsync(cts.Token);

                    Console.WriteLine("[Orbit Pipe] Gorilla Tag connected.");

                    reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);

                    writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true)
                    {
                        AutoFlush = true
                    };

                    ModConnected?.Invoke();

                    await ReceiveLoop();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Orbit Pipe] " + ex);
                }
                finally
                {
                    bool wasConnected = pipe != null && pipe.IsConnected;

                    Cleanup();

                    if (wasConnected)
                    {
                        Console.WriteLine("[Orbit Pipe] Gorilla Tag disconnected.");

                        ModDisconnected?.Invoke();
                    }
                }

                if (!cts.IsCancellationRequested)
                {
                    await Task.Delay(1000);
                }
            }
        }

        private async Task ReceiveLoop()
        {
            if (reader == null)
                return;

            while (pipe != null && pipe.IsConnected && !cts.IsCancellationRequested)
            {
                try
                {
                    string? line = await reader.ReadLineAsync();

                    if (line == null)
                        break;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    OrbitPipeMessage? message = JsonSerializer.Deserialize<OrbitPipeMessage>(
                      line,
                      new JsonSerializerOptions
                      {
                          PropertyNameCaseInsensitive = true
                      }
                    );

                    if (message == null)
                        continue;

                    Console.WriteLine($"[MOD -> ORBIT] {message.Type}");

                    MessageReceived?.Invoke(message);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine("[Orbit Pipe] Invalid JSON: " + ex.Message);
                }
                catch (IOException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[Orbit Pipe] Receive failed: " + ex);
                    break;
                }
            }
        }

        public async Task SendAsync(string type, string data = "")
        {
            OrbitPipeMessage message = new OrbitPipeMessage
            {
                Type = type,
                Data = data
            };

            await SendMessageAsync(message);
        }

        public async Task SendAsync<T>(string type, T data)
        {
            OrbitPipeMessage message = new OrbitPipeMessage
            {
                Type = type,
                Data = JsonSerializer.Serialize(data)
            };

            await SendMessageAsync(message);
        }

        private async Task SendMessageAsync(OrbitPipeMessage message)
        {
            if (writer == null || pipe == null || !pipe.IsConnected)
            {
                Console.WriteLine("[Orbit Pipe] Cannot send. Mod is not connected.");

                return;
            }

            await sendLock.WaitAsync();

            try
            {
                string json = JsonSerializer.Serialize(message);

                await writer.WriteLineAsync(json);

                await writer.FlushAsync();

                Console.WriteLine($"[ORBIT -> MOD] {message.Type}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Orbit Pipe] Send failed: " + ex);
            }
            finally
            {
                sendLock.Release();
            }
        }

        public void Stop()
        {
            try
            {
                cts.Cancel();
            }
            catch { }

            Cleanup();
        }

        private void Cleanup()
        {
            try
            {
                reader?.Dispose();
            }
            catch { }

            try
            {
                writer?.Dispose();
            }
            catch { }

            try
            {
                pipe?.Dispose();
            }
            catch { }

            reader = null;
            writer = null;
            pipe = null;
        }
    }

    public class OrbitPipeMessage
    {
        public string Type
        {
            get;
            set;
        } = "";
        public string Data
        {
            get;
            set;
        } = "";
    }
}