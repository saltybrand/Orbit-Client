using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace OrbitClient.Scripts;

public sealed class OrbitOAuthCallback : IDisposable
{
    public
    const int Port = 43827;
    private readonly TcpListener listener;
    public Uri RedirectUri
    {
        get;
    }
    public string Verifier
    {
        get;
    } = Base64Url(RandomNumberGenerator.GetBytes(32));
    public string Challenge => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));

    public OrbitOAuthCallback()
    {
        RedirectUri = new Uri($"http://127.0.0.1:{Port}/orbit-auth/{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}/");
        listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Server.ExclusiveAddressUse = true;
        try
        {
            listener.Start(4);
        }
        catch (SocketException)
        {
            throw new IOException("The sign-in callback port is busy. Close another Orbit window and try again.");
        }
    }

    public static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task<string> WaitForCodeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using
            var connection = await listener.AcceptTcpClientAsync(cancellationToken);
            using
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using
            var stream = connection.GetStream();
            var header = new List<byte>();
            var buffer = new byte[1];
            try
            {
                while (header.Count < 8192)
                {
                    if (await stream.ReadAsync(buffer, timeout.Token) == 0) break;
                    header.Add(buffer[0]);
                    if (header.Count >= 4 && header.TakeLast(4).SequenceEqual(new byte[] {
              13,
              10,
              13,
              10
            })) break;
                }
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
                var request = lines[0].Split(' ');
                var valid = request.Length == 3 && request[0] == "GET" && request[2] == "HTTP/1.1" &&
                  request[1].StartsWith(RedirectUri.AbsolutePath + "?", StringComparison.Ordinal) &&
                  lines.Any(x => x.Equals($"Host: 127.0.0.1:{Port}", StringComparison.OrdinalIgnoreCase));
                var values = valid ? ParseQuery(new Uri(RedirectUri, request[1]).Query) : new Dictionary<string,
                  string>();
                var hasCode = values.TryGetValue("code", out
                  var code) && !string.IsNullOrWhiteSpace(code);
                var denied = values.ContainsKey("error") || values.ContainsKey("error_code");
                var message = valid && (hasCode || denied) ?
                  "You can close this tab and return to Orbit to finish signing in." :
                  "This is not the active Orbit sign-in request.";
                var body = Encoding.UTF8.GetBytes("<!doctype html><html><head><meta charset='utf-8'><title>Orbit sign-in</title></head><body><h1>Orbit</h1><p>" + message + "</p></body></html>");
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid ? "200 OK " : "400 Bad Request ")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; frame-ancestors 'none'\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
                if (valid && denied) throw new IOException("Discord sign-in was declined. You can try again.");
                if (valid && hasCode) return code!;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (IOException) when (!cancellationToken.IsCancellationRequested && !connection.Connected) { }
        }
    }

    public static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string,
          string>(StringComparer.Ordinal);
        foreach (var item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            if (!values.TryAdd(key, pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : ""))
                return new();
        }
        return values;
    }

    public void Dispose() => listener.Stop();
}