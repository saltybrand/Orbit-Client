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
        readonly string pipeName;

        public OrbitPipeServer(string pipeName = "OrbitClientPipe") => this.pipeName = pipeName;

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
                      pipeName,
                      PipeDirection.InOut,
                      1,
                      PipeTransmissionMode.Byte,
                      PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
                    );

                    Console.WriteLine("[Orbit Pipe] Waiting for Gorilla Tag...");

                    await pipe.WaitForConnectionAsync(cts.Token);

                    Console.WriteLine("[Orbit Pipe] Gorilla Tag connected.");

                    reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);

                    writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true)
                    {
                        AutoFlush = true
                    };
                    inboundOffset = inboundCount = 0;

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

            var rateWindow = DateTime.UtcNow;
            var messageCount = 0;
            while (pipe != null && pipe.IsConnected && !cts.IsCancellationRequested)
            {
                try
                {
                    string? line = await ReadBoundedLineAsync(reader);

                    if (line == null)
                        break;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    if (DateTime.UtcNow - rateWindow >= TimeSpan.FromSeconds(1)) { rateWindow = DateTime.UtcNow; messageCount = 0; }
                    if (++messageCount > 30) throw new IOException("Too many pipe messages.");

                    OrbitPipeMessage? message = JsonSerializer.Deserialize<OrbitPipeMessage>(
                      line,
                      new JsonSerializerOptions
                      {
                          PropertyNameCaseInsensitive = true
                      }
                    );

                    if (message == null)
                        continue;
                    message.Type ??= ""; message.Data ??= "";

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

        private readonly char[] inboundBuffer = new char[512];
        private int inboundOffset, inboundCount;
        private async Task<string?> ReadBoundedLineAsync(StreamReader input)
        {
            var line = new StringBuilder();
            while (!cts.IsCancellationRequested)
            {
                if (inboundOffset == inboundCount)
                {
                    inboundCount = await input.ReadAsync(inboundBuffer, 0, inboundBuffer.Length);
                    inboundOffset = 0;
                    if (inboundCount == 0) return line.Length == 0 ? null : line.ToString();
                }
                var character = inboundBuffer[inboundOffset++];
                if (character == '\n') return line.ToString().TrimEnd('\r');
                if (line.Length >= 16384) throw new IOException("Pipe message is too large.");
                line.Append(character);
            }
            return null;
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
