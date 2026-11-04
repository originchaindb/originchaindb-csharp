using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OriginChainDB;

/// <summary>One acknowledged input range. Earlier results remain valid after a later failure.</summary>
public sealed record BatchResult(long Offset, int Count, int Inserted);
public sealed record BatchOptions
{
    public int BatchSize { get; init; } = 1000;
    public int MaxBytes { get; init; } = 1024 * 1024;
}
/// <summary>An unconfirmed range. Inspect unknown outcomes before retrying.</summary>
public sealed class BatchWriteException : Exception
{
    public long Offset { get; }
    public int Count { get; }
    public string Outcome { get; }
    public BatchWriteException(long offset, int count, string outcome, Exception innerException)
        : base($"Batch at input offset {offset} failed ({outcome})", innerException)
        => (Offset, Count, Outcome) = (offset, count, outcome);
}
public sealed record PageOptions
{
    public int PageSize { get; init; } = 1000;
    public string? After { get; init; }
}
/// <summary>One keyset page. A null NextCursor marks completion.</summary>
public sealed record RowPage(IReadOnlyList<JsonElement> Rows, string? NextCursor);

public sealed partial class RowsService
{
    /// <summary>Lazily write bounded atomic JSON batches. No automatic replay.</summary>
    public IAsyncEnumerable<BatchResult> WriteBatchesAsync(string schema, IEnumerable<IReadOnlyDictionary<string, object?>> rows, BatchOptions? options = null, CancellationToken cancellationToken = default)
        => WriteBatchesAsync(schema, AsAsync(rows, cancellationToken), options, cancellationToken);

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> AsAsync(IEnumerable<IReadOnlyDictionary<string, object?>> rows, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        await Task.CompletedTask.ConfigureAwait(false);
        foreach (var row in rows) { cancellationToken.ThrowIfCancellationRequested(); yield return row; }
    }

    /// <summary>Consume an asynchronous producer with one batch and at most one lookahead row in memory.</summary>
    public async IAsyncEnumerable<BatchResult> WriteBatchesAsync(string schema, IAsyncEnumerable<IReadOnlyDictionary<string, object?>> rows, BatchOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.BatchSize is < 1 or > 10000 || options.MaxBytes is < 2 or > 8388608) throw new ArgumentException("Invalid batch bounds", nameof(options));
        var path = Client.Path("rows", schema, "_batch");
        ArgumentNullException.ThrowIfNull(rows);
        cancellationToken.ThrowIfCancellationRequested();
        await using var input = rows.GetAsyncEnumerator(cancellationToken);
        var batch = new List<JsonElement>();
        var size = 2;
        long offset = 0;
        async Task<BatchResult> Flush()
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var value = await client.RequestAsync(HttpMethod.Post, path, batch, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("inserted", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var inserted) || inserted < 0 || inserted > batch.Count)
                    throw new OriginChainException("protocol");
                return new(offset, batch.Count, inserted);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { throw new BatchWriteException(offset, batch.Count, "unknown", error); }
        }
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool hasNext;
            try { hasNext = await input.MoveNextAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { throw new BatchWriteException(offset, batch.Count, "not_sent", error); }
            if (!hasNext) break;
            JsonElement row;
            int length;
            try
            {
                ArgumentNullException.ThrowIfNull(input.Current);
                row = JsonSerializer.SerializeToElement(input.Current);
                length = Encoding.UTF8.GetByteCount(row.GetRawText());
                if (length + 2 > options.MaxBytes) throw new ArgumentException("Row exceeds MaxBytes");
            }
            catch (Exception error) { throw new BatchWriteException(offset, batch.Count + 1, "not_sent", error); }
            if (batch.Count > 0 && size + 1 + length > options.MaxBytes)
            {
                var result = await Flush().ConfigureAwait(false);
                offset += batch.Count; batch.Clear(); size = 2;
                yield return result;
            }
            size += length + (batch.Count == 0 ? 0 : 1); batch.Add(row);
            if (batch.Count == options.BatchSize)
            {
                var result = await Flush().ConfigureAwait(false);
                offset += batch.Count; batch.Clear(); size = 2;
                yield return result;
            }
        }
        if (batch.Count > 0) yield return await Flush().ConfigureAwait(false);
    }

    private static int CompareKeys(string left, string right) => Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right));

    /// <summary>Keyset pages over a unique immutable non-null string key with binary ordering. No snapshot guarantee.</summary>
    public async IAsyncEnumerable<RowPage> PagesAsync(string schema, string key, PageOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options ??= new();
        var parts = schema.Split('.');
        if (parts.Length > 2 || parts.Append(key).Any(p => !Regex.IsMatch(p, "\\A[A-Za-z_][A-Za-z0-9_]*\\z", RegexOptions.CultureInvariant)) || options.PageSize is < 1 or > 10000)
            throw new ArgumentException("Invalid page options");
        var table = string.Join('.', parts.Select(p => $"\"{p}\""));
        var after = options.After;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var condition = after is null ? "" : $" WHERE \"{key}\" > $1";
            var sql = $"SELECT * FROM {table}{condition} ORDER BY \"{key}\" ASC LIMIT {options.PageSize}";
            var result = await client.Sql.QueryAsync(sql, after is null ? [] : [after], cancellationToken).ConfigureAwait(false);
            if (result.Rows.Count > options.PageSize) throw new OriginChainException("protocol");
            var last = after;
            foreach (var row in result.Rows)
            {
                if (!row.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) throw new OriginChainException("protocol");
                var next = value.GetString()!;
                if (last is not null && CompareKeys(next, last) <= 0) throw new OriginChainException("protocol");
                last = next;
            }
            if (result.Rows.Count == 0) yield break;
            after = result.Rows.Count < options.PageSize ? null : last;
            yield return new RowPage(result.Rows, after);
            if (after is null) yield break;
        }
    }

    /// <summary>Flatten one bounded page at a time without prefetching.</summary>
    public async IAsyncEnumerable<JsonElement> ScanAsync(string schema, string key, PageOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var page in PagesAsync(schema, key, options, cancellationToken).ConfigureAwait(false))
            foreach (var row in page.Rows) { cancellationToken.ThrowIfCancellationRequested(); yield return row; }
    }
}
