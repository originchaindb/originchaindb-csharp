using System.Text.Json;

namespace OriginChainDB;

public sealed class SqlService
{
    private readonly Client client;
    internal SqlService(Client client) => this.client = client;
    public async Task<SqlResult> RunAsync(string statement, IReadOnlyList<object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        var value = await client.RequestAsync(HttpMethod.Post, "sql", new { sql = statement, @params = parameters ?? [] }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = new SqlResult(value);
        if (result.Failed) throw new OriginChainException("batch", System.Net.HttpStatusCode.OK, result: result);
        return result;
    }
    public Task<SqlResult> ExecuteAsync(string statement, IReadOnlyList<object?>? parameters = null, CancellationToken cancellationToken = default) => RunAsync(statement, parameters, cancellationToken);
    public async Task<SqlResult> QueryAsync(string statement, IReadOnlyList<object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(statement, parameters, cancellationToken).ConfigureAwait(false);
        if (result.Kind != "select") throw new OriginChainException("protocol");
        return result;
    }
}

public sealed class SchemasService
{
    private readonly Client client;
    internal SchemasService(Client client) => this.client = client;
    public Task<JsonElement> ListAsync(CancellationToken cancellationToken = default) => client.RequestAsync(HttpMethod.Get, "schemas", cancellationToken: cancellationToken);
    public Task<JsonElement> GetAsync(string name, CancellationToken cancellationToken = default) => client.RequestAsync(HttpMethod.Get, Client.Path("schemas", name), cancellationToken: cancellationToken);
    public Task<JsonElement> RegisterAsync(string toml, CancellationToken cancellationToken = default) => client.RequestAsync(HttpMethod.Post, "schemas", text: toml, cancellationToken: cancellationToken);
}

public sealed partial class RowsService
{
    private readonly Client client;
    internal RowsService(Client client) => this.client = client;
    public Task<JsonElement> GetAsync(string schema, string key, CancellationToken cancellationToken = default) => client.RequestAsync(HttpMethod.Get, Client.Path("rows", schema, key), cancellationToken: cancellationToken);
    public Task<JsonElement> PutAsync(string schema, IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default) => client.RequestAsync(HttpMethod.Post, Client.Path("rows", schema), row, cancellationToken: cancellationToken);
    public Task<JsonElement> PutBatchAsync(string schema, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, CancellationToken cancellationToken = default) => client.RequestAsync(HttpMethod.Post, Client.Path("rows", schema, "_batch"), rows, cancellationToken: cancellationToken);
}

public sealed record VectorSearchOptions
{
    public int K { get; init; } = 10;
    public string Metric { get; init; } = "cosine";
    public IReadOnlyDictionary<string, object?>? Filter { get; init; }
}

public sealed class VectorService
{
    private readonly Client client;
    internal VectorService(Client client) => this.client = client;
    private static void Validate(IReadOnlyList<double> values)
    {
        if (values.Count == 0 || values.Any(v => !double.IsFinite(v))) throw new ArgumentException("Vector must contain finite numbers", nameof(values));
    }
    public Task<JsonElement> PutAsync(string table, string id, IReadOnlyList<double> embedding, IReadOnlyDictionary<string, object?>? metadata = null, CancellationToken cancellationToken = default)
    {
        Validate(embedding);
        var body = new Dictionary<string, object?> { ["id"] = id, ["embedding"] = embedding, ["dim"] = embedding.Count };
        if (metadata is not null) body["metadata"] = metadata;
        return client.RequestAsync(HttpMethod.Post, Client.Path("vector", table, "put"), body, cancellationToken: cancellationToken);
    }
    public async Task<IReadOnlyList<VectorHit>> SearchAsync(string table, IReadOnlyList<double> query, VectorSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        Validate(query); options ??= new();
        if (options.K <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        var body = new Dictionary<string, object?> { ["query"] = query, ["dim"] = query.Count, ["k"] = options.K, ["metric"] = options.Metric };
        if (options.Filter is not null) body["filter"] = options.Filter;
        return VectorHit.Decode(await client.RequestAsync(HttpMethod.Post, Client.Path("vector", table, "topk"), body, cancellationToken: cancellationToken).ConfigureAwait(false));
    }
    public Task<JsonElement> DeleteAsync(string table, string id, CancellationToken cancellationToken = default) => client.RequestAsync(HttpMethod.Delete, Client.Path("vector", table, id), cancellationToken: cancellationToken);
}

public sealed record FtsSearchOptions
{
    public string Mode { get; init; } = "bm25";
    public int K { get; init; } = 10;
    public bool Highlight { get; init; }
    public int? Fuzzy { get; init; }
    public IReadOnlyList<string>? Facets { get; init; }
}

public sealed class FtsService
{
    private readonly Client client;
    internal FtsService(Client client) => this.client = client;
    public Task<JsonElement> IndexAsync(string table, string field, string id, string text, CancellationToken cancellationToken = default) =>
        client.RequestAsync(HttpMethod.Post, Client.Path("fts", table, field), new { doc_id = id, text }, cancellationToken: cancellationToken);
    public async Task<FtsResult> SearchAsync(string table, string field, string query, FtsSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.K <= 0 || options.Mode is not ("bm25" or "boolean" or "phrase")) throw new ArgumentException("Invalid search mode or k", nameof(options));
        var args = new Dictionary<string, string> { ["q"] = query, ["mode"] = options.Mode };
        if (options.Mode == "bm25") args["k"] = options.K.ToString();
        if (options.Highlight) args["highlight"] = "true";
        if (options.Fuzzy is not null) args["fuzzy"] = options.Fuzzy.Value.ToString();
        if (options.Facets is not null) args["facets"] = string.Join(',', options.Facets);
        return new FtsResult(await client.RequestAsync(HttpMethod.Get, Client.Path("fts", table, field), query: args, cancellationToken: cancellationToken).ConfigureAwait(false));
    }
}

public sealed class GraphService
{
    private readonly Client client;
    internal GraphService(Client client) => this.client = client;
    public Task<JsonElement> QueryAsync(string cypher, IReadOnlyDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cypher);
        return client.RequestAsync(HttpMethod.Post, "cypher", new { cypher, @params = parameters ?? new Dictionary<string, object?>() }, cancellationToken: cancellationToken);
    }
    public Task<JsonElement> NeighborsAsync(string schema, string key, string relation, CancellationToken cancellationToken = default) =>
        client.RequestAsync(HttpMethod.Get, Client.Path("graph", schema, "neighbors"), query: new Dictionary<string, string> { ["pk"] = key, ["rel"] = relation }, cancellationToken: cancellationToken);
    public Task<JsonElement> BfsAsync(string schema, string key, string relation, int maxDepth = 5, CancellationToken cancellationToken = default)
    {
        if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        return client.RequestAsync(HttpMethod.Get, Client.Path("graph", schema, "bfs"), query: new Dictionary<string, string> { ["pk"] = key, ["rel"] = relation, ["max_depth"] = maxDepth.ToString() }, cancellationToken: cancellationToken);
    }
}
