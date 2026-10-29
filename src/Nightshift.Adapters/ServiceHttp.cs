using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Nightshift.Adapters;

public enum ServiceFailure { Authentication, Authorization, Transport, Configuration, Uncertain, Protocol }
public sealed class ServiceException(ServiceFailure kind, string message) : Exception(message)
{
    public ServiceFailure Kind { get; } = kind;
}
public sealed class ServiceHttp : IDisposable
{
    private readonly HttpClient client;
    private readonly HttpClient anonymous;
    private readonly Uri origin;
    private readonly Func<AuthenticationHeaderValue?> authentication;
    private readonly string? apiVersion;
    public ServiceHttp(Uri origin, Func<AuthenticationHeaderValue?> authentication, string? apiVersion = null,
        HttpMessageHandler? handler = null, HttpMessageHandler? anonymousHandler = null)
    {
        if (origin.Scheme != "https" || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0) throw new ArgumentException("Service origin must be credential-free HTTPS");
        this.origin = origin; this.authentication = authentication; this.apiVersion = apiVersion;
        client = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        anonymous = new(anonymousHandler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        client.Timeout = anonymous.Timeout = TimeSpan.FromSeconds(20);
    }
    public static string Segment(string s) => Uri.EscapeDataString(s);
    public static string Text(JsonElement element, string property) => element.TryGetProperty(property, out var p) && p.ValueKind is JsonValueKind.String or JsonValueKind.Number ? p.ToString() : "";
    public static void Correlate(bool matches) { if (!matches) throw new ServiceException(ServiceFailure.Protocol, "Native response does not match repository/candidate/pipeline/run correlation"); }
    public async Task<(HttpStatusCode Status, byte[] Body, Uri? Location)> Send(HttpMethod method, string relative, object? body, CancellationToken token, int bound = 2 * 1024 * 1024)
    {
        var uri = new Uri(origin, relative);
        if (uri.Scheme != origin.Scheme || uri.Authority != origin.Authority || !uri.AbsolutePath.StartsWith(origin.AbsolutePath, StringComparison.Ordinal)) throw new ServiceException(ServiceFailure.Configuration, "Cross-origin service request refused");
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = authentication(); request.Headers.UserAgent.ParseAdd("Nightshift/0.3");
        if (apiVersion != null) request.Headers.Add("X-GitHub-Api-Version", apiVersion);
        request.Headers.Accept.ParseAdd("application/json");
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new ServiceException(ServiceFailure.Authentication, "Service authentication failed (401)");
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new ServiceException(ServiceFailure.Authorization, "Service authorization failed (403)");
            if ((int)response.StatusCode >= 500 || (int)response.StatusCode is 408 or 429) throw new ServiceException(ServiceFailure.Transport, "Service temporarily unavailable (" + (int)response.StatusCode + ")");
            if ((int)response.StatusCode >= 400) throw new ServiceException(ServiceFailure.Configuration, "Service request rejected (" + (int)response.StatusCode + ")");
            byte[] bytes = await Bounded(await response.Content.ReadAsStreamAsync(token), bound, token);
            return (response.StatusCode, bytes, response.Headers.Location);
        }
        catch (HttpRequestException) { throw new ServiceException(ServiceFailure.Transport, "Service transport failed; dispatch may be uncertain"); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new ServiceException(ServiceFailure.Transport, "Service timed out; dispatch may be uncertain"); }
    }
    public async Task<JsonElement> Json(HttpMethod method, string relative, object? body, CancellationToken token)
    {
        var response = await Send(method, relative, body, token);
        if ((int)response.Status >= 300) throw new ServiceException(ServiceFailure.Protocol, "Unexpected redirect; credentials were not forwarded");
        try { using var doc = JsonDocument.Parse(response.Body); return doc.RootElement.Clone(); }
        catch (JsonException) { throw new ServiceException(ServiceFailure.Protocol, "Invalid service JSON"); }
    }
    public async Task<string> Logs(string relative, bool zip, CancellationToken token)
    {
        var response = await Send(HttpMethod.Get, relative, null, token, 2 * 1024 * 1024);
        byte[] bytes = response.Body;
        if ((int)response.Status is >= 300 and < 400)
        {
            var location = response.Location;
            if (location == null || !location.IsAbsoluteUri || location.Scheme != "https" || location.UserInfo.Length > 0
                || !(location.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase)
                    || location.Host.EndsWith(".actions.githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new ServiceException(ServiceFailure.Protocol, "Unapproved log download origin");
            using var download = await anonymous.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, token);
            if (!download.IsSuccessStatusCode) throw new ServiceException(ServiceFailure.Transport, "Anonymous log download failed; redirects are not followed");
            bytes = await Bounded(await download.Content.ReadAsStreamAsync(token), 2 * 1024 * 1024, token);
        }
        if (!zip) return Redact(Encoding.UTF8.GetString(bytes));
        using var archive = new ZipArchive(new MemoryStream(bytes)); var text = new StringBuilder();
        foreach (var entry in archive.Entries.Take(128))
        {
            if (entry.Length + text.Length > 1024 * 1024) throw new ServiceException(ServiceFailure.Protocol, "Expanded log bounds exceeded");
            byte[] content = await Bounded(entry.Open(), 1024 * 1024 - text.Length, token);
            text.AppendLine(entry.FullName); text.AppendLine(Encoding.UTF8.GetString(content));
        }
        return Redact(text.ToString());
    }
    private string Redact(string value)
    {
        string? credential = authentication()?.Parameter;
        return string.IsNullOrEmpty(credential) ? value : value.Replace(credential, "[REDACTED]", StringComparison.Ordinal);
    }
    private static async Task<byte[]> Bounded(Stream stream, int bound, CancellationToken token)
    {
        await using (stream)
        {
            using var bytes = new MemoryStream(); byte[] buffer = new byte[8192]; int n;
            while ((n = await stream.ReadAsync(buffer, token)) > 0) { if (bytes.Length + n > bound) throw new ServiceException(ServiceFailure.Protocol, "Service response bounds exceeded"); bytes.Write(buffer, 0, n); }
            return bytes.ToArray();
        }
    }
    public void Dispose() { client.Dispose(); anonymous.Dispose(); }
}
