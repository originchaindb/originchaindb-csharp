using OriginChainDB;
using System.Text.Json;

static void Check(bool value) { if (!value) throw new Exception("Iteration assertion failed"); }
static IEnumerable<IReadOnlyDictionary<string, object?>> Source(List<char> consumed)
{
    var row = new Dictionary<string, object?>();
    foreach (var id in "abcde") { consumed.Add(id); row["id"] = id.ToString(); yield return row; }
}
static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> stream) { var result = new List<T>(); await foreach (var item in stream) result.Add(item); return result; }
using var db = Client.FromEnvironment();
var consumed = new List<char>();
var batches = db.Rows.WriteBatchesAsync("bulk.ok", Source(consumed), new() { BatchSize = 2 });
Check(consumed.Count == 0);
await using (var iterator = batches.GetAsyncEnumerator())
{
    Check(await iterator.MoveNextAsync()); var first = iterator.Current;
    Check(consumed.Count == 2 && first == new BatchResult(0, 2, 2));
    Check(await iterator.MoveNextAsync() && iterator.Current == new BatchResult(2, 2, 2));
    Check(await iterator.MoveNextAsync() && iterator.Current == new BatchResult(4, 1, 1));
    Check(!await iterator.MoveNextAsync());
}
foreach (var (name, offset, count) in new[] { ("fail",2,4), ("lost",0,2), ("unknown",0,2) })
{
    consumed = [];
    try { await Collect(db.Rows.WriteBatchesAsync("bulk." + name, Source(consumed), new() { BatchSize = 2 })); throw new Exception("Expected failure"); }
    catch (BatchWriteException error) { Check(error.Offset == offset && error.Count == 2 && error.Outcome == "unknown" && error.InnerException != null && consumed.Count == count); }
}
var reports = await Collect(db.Rows.WriteBatchesAsync("bulk.bytes", "abc".Select(id => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["id"] = id.ToString(), ["value"] = new string('x', 20) }), new() { MaxBytes = 64 }));
Check(reports.Select(b => b.Count).SequenceEqual(new[] { 1, 1, 1 }));
consumed = [];
await foreach (var item in db.Rows.WriteBatchesAsync("bulk.stop", Source(consumed), new() { BatchSize = 2 })) break;
Check(consumed.Count == 2);
Check((await Collect(db.Rows.WriteBatchesAsync("bulk.never", Array.Empty<IReadOnlyDictionary<string, object?>>() ))).Count == 0);
try { await Collect(db.Rows.WriteBatchesAsync("bulk.never", Source([]), new() { MaxBytes = 4 })); throw new Exception("Expected failure"); }
catch (BatchWriteException error) { Check(error.Offset == 0 && error.Outcome == "not_sent"); }
try { await Collect(db.Rows.WriteBatchesAsync("bulk.never", Source([]), new() { BatchSize = 0 })); throw new Exception("Expected failure"); } catch (ArgumentException) { }
using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
try { await Collect(db.Rows.WriteBatchesAsync("bulk.never", Source([]), cancellationToken: cancellation.Token)); throw new Exception("Expected failure"); } catch (OperationCanceledException) { }
Check((await Collect(db.Rows.ScanAsync("scan.items", "id", new() { PageSize = 2 }))).Select(row => row.GetProperty("id").GetString()).SequenceEqual(new[] { "a", "b", "c", "d", "e" }));
await foreach (var row in db.Rows.ScanAsync("scan.stop", "id", new() { PageSize = 2 })) { Check(row.GetProperty("id").GetString() == "a"); break; }
Check((await Collect(db.Rows.PagesAsync("scan.resume", "id", new() { PageSize = 2, After = "b" }))).Select(p => p.NextCursor).SequenceEqual(new[] { "d", null }));
foreach (var name in new[] { "bad", "numeric", "oversize" })
{
    try { await Collect(db.Rows.PagesAsync("scan." + name, "id", new() { PageSize = 2 })); throw new Exception("Expected failure"); }
    catch (OriginChainException error) { Check(error.Kind == "protocol"); }
}
Check((await Collect(db.Rows.ScanAsync("scan.empty", "id", new() { PageSize = 2 }))).Count == 0);
try { await Collect(db.Rows.ScanAsync("scan.items;DELETE", "id")); throw new Exception("Expected failure"); } catch (ArgumentException) { }
Console.WriteLine(".NET iteration assertions passed");
