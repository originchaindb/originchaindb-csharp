using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OriginChainDB;

/// <summary>Reusable, thread-safe database client. Requests are never replayed automatically.</summary>
public sealed class Client : IDisposable
{
    private readonly string endpoint;
    private readonly string token;
    private readonly HttpClient http;
    private readonly TimeSpan timeout;
    private bool disposed;
    public SqlService Sql { get; }
    public RowsService Rows { get; }
    public SchemasService Schemas { get; }
    public VectorService Vector { get; }
    public FtsService Fts { get; }
    public GraphService Graph { get; }

    /// <summary>Use the opaque URL copied from Connect. Dispose the client at application shutdown.</summary>
    public Client(string url, string token, TimeSpan? timeout = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length > 0 || url.Any(c => c <= 32 || c == 127 || c is '?' or '#' or '\\') ||
            !(uri.Scheme == "https" || uri.Scheme == "http" && (uri.Host == "localhost" || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip))))
            throw new ArgumentException("Use the HTTPS database URL copied from Connect; HTTP is only allowed on loopback", nameof(url));
        if (string.IsNullOrEmpty(token) || token.Any(c => c <= 32 || c >= 127)) throw new ArgumentException("Token must be nonempty printable ASCII without spaces", nameof(token));
        this.timeout = timeout ?? TimeSpan.FromSeconds(60);
        if (this.timeout <= TimeSpan.Zero || this.timeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(timeout));
        endpoint = url.TrimEnd('/'); this.token = token;
        http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 100,
            ConnectTimeout = TimeSpan.FromSeconds(Math.Min(10, this.timeout.TotalSeconds))
        })
        { Timeout = Timeout.InfiniteTimeSpan };
        Sql = new(this); Rows = new(this); Schemas = new(this); Vector = new(this); Fts = new(this); Graph = new(this);
    }
    public static Client FromEnvironment(TimeSpan? timeout = null) => new(Environment.GetEnvironmentVariable("OC_URL") ?? "", Environment.GetEnvironmentVariable("OC_TOKEN") ?? "", timeout);
    public override string ToString() => "OriginChainDB Client";
    public void Dispose() { disposed = true; http.Dispose(); }

    internal static string Path(params string[] parts) => string.Join('/', parts.Select(part =>
        string.IsNullOrEmpty(part) || part is "." or ".." || part.Any(c => c < 32 || c == 127)
        ? throw new ArgumentException("Invalid path name") : Uri.EscapeDataString(part)));

    internal async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body = null,
        IReadOnlyDictionary<string, string>? query = null, string? text = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var address = endpoint + "/" + path;
        if (query is { Count: > 0 }) address += "?" + string.Join('&', query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        using var request = new HttpRequestMessage(method, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("originchaindb-csharp/1.0.0-rc.1");
        request.Headers.Add("X-OC-Logical-Request-Id", Guid.NewGuid().ToString("N"));
        if (text is not null) request.Content = new StringContent(text, Encoding.UTF8, "text/plain");
        else if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int length;
            while ((length = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + length > 33_554_432) throw new OriginChainException("protocol", response.StatusCode);
                buffer.Write(chunk, 0, length);
            }
            var raw = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            JsonElement value;
            try { using var document = JsonDocument.Parse(raw.Length == 0 ? "null" : raw); value = document.RootElement.Clone(); }
            catch (JsonException)
            {
                if (response.IsSuccessStatusCode && !(response.Content.Headers.ContentType?.MediaType?.StartsWith("text/", StringComparison.Ordinal) ?? false)) throw new OriginChainException("protocol", response.StatusCode);
                value = JsonSerializer.SerializeToElement(raw);
            }
            if (!response.IsSuccessStatusCode)
            {
                var kind = (int)response.StatusCode switch { 401 => "authentication", 403 => "permission", 429 => "rate_limit", _ => "http" };
                throw new OriginChainException(kind, response.StatusCode, value,
                    response.Headers.TryGetValues("X-OC-Request-Id", out var ids) ? ids.FirstOrDefault() : null,
                    response.Headers.TryGetValues("Retry-After", out var retry) ? retry.FirstOrDefault() : null);
            }
            return value;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new OriginChainException("timeout"); }
        catch (HttpRequestException) { throw new OriginChainException("transport"); }
        catch (IOException) { throw new OriginChainException("transport"); }
    }
    public Task<JsonElement> UsageAsync(CancellationToken cancellationToken = default) => RequestAsync(HttpMethod.Get, "usage", cancellationToken: cancellationToken);
    public Task<JsonElement> QueryMetricsAsync(int minutes = 60, int bucketSeconds = 60, CancellationToken cancellationToken = default) =>
        RequestAsync(HttpMethod.Get, "query-metrics", query: new Dictionary<string, string> { ["minutes"] = minutes.ToString(), ["bucket_seconds"] = bucketSeconds.ToString() }, cancellationToken: cancellationToken);
}
