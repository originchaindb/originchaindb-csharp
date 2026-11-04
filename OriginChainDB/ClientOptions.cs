namespace OriginChainDB;

/// <summary>Application integration settings. Configure exactly one credential source.</summary>
public sealed class ClientOptions
{
    public required string Url { get; init; }
    public string? Token { get; init; }
    /// <summary>Called once per request. Honor cancellation; authentication failures are never replayed.</summary>
    public Func<CancellationToken, ValueTask<string>>? TokenProvider { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>Local metadata observer. Keep it short; exceptions do not alter the request result.</summary>
    public Action<RequestEvent>? OnRequest { get; init; }
    /// <summary>Optional application/factory client. Configure its handler with AllowAutoRedirect=false
    /// and no automatic write retries. The application is responsible for custom handler behavior.</summary>
    public HttpClient? HttpClient { get; init; }
    /// <summary>Whether disposing the SDK also disposes an injected HttpClient. Default false.</summary>
    public bool DisposeHttpClient { get; init; }
    public override string ToString() => "OriginChainDB ClientOptions";
}

/// <summary>Transport metadata excluding URLs, SQL, parameters, table names, bodies and credentials.
/// Null ErrorKind means HTTP success; result decoding can still fail afterwards.</summary>
public sealed record RequestEvent(string Method, string Operation, TimeSpan Duration,
    int? Status, string? RequestId, string? ErrorKind)
{
    internal static string OperationName(string path) => path.Split('/')[0] switch
    {
        var name and ("sql" or "rows" or "schemas" or "vector" or "fts" or "graph" or "cypher" or "usage" or "query-metrics") => name,
        _ => "other"
    };
}
