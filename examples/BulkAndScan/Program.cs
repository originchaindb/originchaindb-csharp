// Run against a development database; writes sdk_iteration.items.
using OriginChainDB;

static IEnumerable<IReadOnlyDictionary<string, object?>> Input()
{
    foreach (var id in "abcde")
        yield return new Dictionary<string, object?> { ["id"] = id.ToString(), ["name"] = $"Item {id}" };
}
using var db = Client.FromEnvironment();
using var cancellation = new CancellationTokenSource();
var schema = "sdk_iteration.items";
await db.Schemas.RegisterAsync(await File.ReadAllTextAsync("examples/items.toml"));
var rows = Input();
// section:bulk
await foreach (var batch in db.Rows.WriteBatchesAsync(schema, rows,
    new() { BatchSize = 2 }, cancellation.Token))
{
    Console.WriteLine($"{batch.Offset} {batch.Count} {batch.Inserted}");
}
// endsection
// section:scan
await foreach (var row in db.Rows.ScanAsync(schema, "id",
    new() { PageSize = 2 }, cancellation.Token))
{
    Console.WriteLine($"{row.GetProperty("id")} {row.GetProperty("name")}");
}
// endsection
// Save a checkpoint only after successfully processing the complete page.
await foreach (var page in db.Rows.PagesAsync(schema, "id",
    new() { PageSize = 2, After = "b" }, cancellation.Token))
{
    foreach (var row in page.Rows) Console.WriteLine($"resumed {row.GetProperty("id")}");
    Console.WriteLine($"next cursor {page.NextCursor ?? "complete"}");
}
