using System.Net;
using System.Text.Json;

namespace OriginChainDB;

/// <summary>Error messages omit server data. Inspect Body and Result explicitly for diagnostics.</summary>
public sealed class OriginChainException : Exception
{
    public string Kind { get; }
    public HttpStatusCode? Status { get; }
    public JsonElement? Body { get; }
    public string? RequestId { get; }
    public string? RetryAfter { get; }
    public SqlResult? Result { get; }
    internal OriginChainException(string kind, HttpStatusCode? status = null, JsonElement? body = null,
        string? requestId = null, string? retryAfter = null, SqlResult? result = null, Exception? inner = null)
        : base($"OriginChainDB {kind} error" + (status is null ? "" : $" (HTTP {(int)status})"), inner)
    { Kind = kind; Status = status; Body = body; RequestId = requestId; RetryAfter = retryAfter; Result = result; }
}

/// <summary>Complete SQL result. JsonElement retains numeric text without floating point conversion.</summary>
public sealed class SqlResult
{
    public string Kind { get; }
    public IReadOnlyList<JsonElement> Rows { get; }
    public IReadOnlyList<string>? Columns { get; }
    public JsonElement? ColumnTypes { get; }
    public long? RowsAffected { get; }
    public long? RowsBuffered { get; }
    public IReadOnlyList<SqlResult> Results { get; }
    public bool StoppedOnError { get; }
    public JsonElement Raw { get; }
    internal bool Failed => StoppedOnError || Kind == "error" || Raw.TryGetProperty("error", out _) || Results.Any(r => r.Failed);

    internal SqlResult(JsonElement value, bool child = false)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new OriginChainException("protocol");
        Raw = value.Clone();
        if (value.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String) Kind = kind.GetString()!;
        else if (child && value.TryGetProperty("error", out _)) Kind = "error";
        else throw new OriginChainException("protocol");
        Rows = Array(value, "rows").Select(row => row.ValueKind == JsonValueKind.Object ? row.Clone() : throw new OriginChainException("protocol")).ToArray();
        Results = Array(value, "results").Select(item => new SqlResult(item, true)).ToArray();
        if (value.TryGetProperty("columns", out var columns) || value.TryGetProperty("returning", out columns))
        {
            if (columns.ValueKind != JsonValueKind.Array) throw new OriginChainException("protocol");
            Columns = columns.EnumerateArray().Select(col => col.ValueKind == JsonValueKind.String ? col.GetString()! : throw new OriginChainException("protocol")).ToArray();
        }
        ColumnTypes = value.TryGetProperty("column_types", out var types) ? types.Clone() : null;
        RowsAffected = Count(value, "rows_affected") ?? (Kind == "insert" ? Count(value, "inserted") : Kind == "select" ? 0 : null);
        RowsBuffered = Count(value, "rows_buffered");
        if (value.TryGetProperty("stopped_on_error", out var stopped))
        {
            if (stopped.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new OriginChainException("protocol");
            StoppedOnError = stopped.GetBoolean();
        }
    }
    private static IEnumerable<JsonElement> Array(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var array)) return [];
        if (array.ValueKind != JsonValueKind.Array) throw new OriginChainException("protocol");
        return array.EnumerateArray();
    }
    private static long? Count(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var count) || count.ValueKind == JsonValueKind.Null) return null;
        if (count.ValueKind != JsonValueKind.Number || !count.TryGetInt64(out var n) || n < 0) throw new OriginChainException("protocol");
        return n;
    }
}
