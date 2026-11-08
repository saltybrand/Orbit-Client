using System.IO;
using System.Net;
using System.Net.Http;

namespace OrbitClient.Scripts;

internal static class OrbitHttp
{
    public static async Task<HttpResponseMessage> GetAsync(HttpClient client, Func<HttpRequestMessage> createRequest, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            HttpResponseMessage response;
            try
            {
                using var request = createRequest();
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), token).ConfigureAwait(false);
                continue;
            }
            if (attempt >= 2 || !IsTransient(response.StatusCode)) return response;
            var delay = response.Headers.RetryAfter?.Delta ??
                (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(250 * (attempt + 1)));
            if (delay > TimeSpan.FromSeconds(2)) return response;
            response.Dispose();
            await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, token).ConfigureAwait(false);
        }
    }

    public static bool IsTransient(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    public static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximumBytes, CancellationToken token)
    {
        if (maximumBytes <= 0 || response.Content.Headers.ContentLength > maximumBytes) throw new IOException("The response is too large.");
        using var output = new MemoryStream();
        using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[32768];
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes) throw new IOException("The response is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}

public sealed class OrbitApiException(string message, HttpStatusCode status) : IOException(message)
{
    public HttpStatusCode Status { get; } = status;
}
