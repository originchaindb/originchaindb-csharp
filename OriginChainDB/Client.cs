using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace OriginChainDB;

/// <summary>Reusable, thread-safe database client. Requests are never replayed automatically.</summary>
public sealed class Client : IDisposable
{
    private readonly string endpoint;
    private readonly string? token;
    private readonly Func<CancellationToken, ValueTask<string>>? tokenProvider;
    private readonly Action<RequestEvent>? observer;
    private readonly bool ownsHttp;
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
        : this(new ClientOptions { Url = url, Token = token, Timeout = timeout ?? TimeSpan.FromSeconds(60) }) { }

    /// <summary>Configure rotating credentials, local diagnostics and an application-owned HttpClient.</summary>
    public Client(ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var url = options.Url;
        var token = options.Token;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) ||
            uri.UserInfo.Length > 0 || url.Any(c => c <= 32 || c == 127 || c is '?' or '#' or '\\') ||
            !(uri.Scheme == "https" || uri.Scheme == "http" && (uri.Host == "localhost" || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip))))
            throw new ArgumentException("Use the HTTPS database URL copied from Connect; HTTP is only allowed on loopback", nameof(url));
        if (options.TokenProvider is not null && token is not null) throw new ArgumentException("Configure either Token or TokenProvider, not both", nameof(options));
        if (options.TokenProvider is null && !ValidToken(token)) throw new ArgumentException("Token must be nonempty printable ASCII without spaces", nameof(options));
        this.timeout = options.Timeout;
        if (this.timeout <= TimeSpan.Zero || this.timeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(options));
        endpoint = url.TrimEnd('/'); this.token = token;
        tokenProvider = options.TokenProvider;
        observer = options.OnRequest;
        ownsHttp = options.HttpClient is null || options.DisposeHttpClient;
        http = options.HttpClient ?? new HttpClient(new SocketsHttpHandler
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
    public void Dispose() { disposed = true; if (ownsHttp) http.Dispose(); }

    private static bool ValidToken(string? value) => !string.IsNullOrEmpty(value) && !value.Any(c => c <= 32 || c >= 127);

    internal static string Path(params string[] parts) => string.Join('/', parts.Select(part =>
        string.IsNullOrEmpty(part) || part is "." or ".." || part.Any(c => c < 32 || c == 127)
        ? throw new ArgumentException("Invalid path name") : Uri.EscapeDataString(part)));

    internal async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body = null,
        IReadOnlyDictionary<string, string>? query = null, string? text = null, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var observed = new Observation();
        string? errorKind = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try { return await RequestCoreAsync(method, path, body, query, text, observed, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { errorKind = "timeout"; throw new OriginChainException("timeout"); }
        catch (OperationCanceledException) { errorKind = "canceled"; throw; }
        catch (OriginChainException error) { errorKind = error.Kind; throw; }
        catch { errorKind = "client"; throw; }
        finally
        {
            if (observer is not null)
            {
                var value = new RequestEvent(method.Method, RequestEvent.OperationName(path), Stopwatch.GetElapsedTime(started), observed.Status, observed.RequestId, errorKind);
                try { observer(value); } catch (Exception) { /* observation must not alter results */ }
            }
        }
    }

    private sealed class Observation { public int? Status; public string? RequestId; }

    private async Task<JsonElement> RequestCoreAsync(HttpMethod method, string path, object? body,
        IReadOnlyDictionary<string, string>? query, string? text, Observation observed, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var currentToken = token;
        if (tokenProvider is not null)
        {
            try { currentToken = await tokenProvider(cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { throw new OriginChainException("credential"); }
            if (!ValidToken(currentToken)) throw new OriginChainException("credential");
        }
        var address = endpoint + "/" + path;
        if (query is { Count: > 0 }) address += "?" + string.Join('&', query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        using var request = new HttpRequestMessage(method, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", currentToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("originchaindb-csharp/1.0.0-rc.1");
        request.Headers.Add("X-OC-Logical-Request-Id", Guid.NewGuid().ToString("N"));
        if (text is not null) request.Content = new StringContent(text, Encoding.UTF8, "text/plain");
        else if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            observed.Status = (int)response.StatusCode;
            observed.RequestId = response.Headers.TryGetValues("X-OC-Request-Id", out var requestIds) ? requestIds.FirstOrDefault() : null;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int length;
            while ((length = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
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
        catch (HttpRequestException) { throw new OriginChainException("transport"); }
        catch (IOException) { throw new OriginChainException("transport"); }
    }
    public Task<JsonElement> UsageAsync(CancellationToken cancellationToken = default) => RequestAsync(HttpMethod.Get, "usage", cancellationToken: cancellationToken);
    public Task<JsonElement> QueryMetricsAsync(int minutes = 60, int bucketSeconds = 60, CancellationToken cancellationToken = default) =>
        RequestAsync(HttpMethod.Get, "query-metrics", query: new Dictionary<string, string> { ["minutes"] = minutes.ToString(), ["bucket_seconds"] = bucketSeconds.ToString() }, cancellationToken: cancellationToken);
}
