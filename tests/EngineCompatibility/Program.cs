// Private integration worker. Credentials arrive over stdin and are never logged.
using OriginChainDB;
using System.Text.Json;

static void Check(bool value) { if (!value) throw new Exception("Engine compatibility assertion failed"); }
var current = "unset"; var calls = 0;
using var db = new Client(new ClientOptions { Url = Environment.GetEnvironmentVariable("OC_URL")!, TokenProvider = _ => { calls++; return ValueTask.FromResult(current); } });
string? line;
while ((line = await Console.In.ReadLineAsync()) is not null)
{
    using var document = JsonDocument.Parse(line);
    var cmd = document.RootElement; current = cmd.GetProperty("token").GetString()!;
    var before = calls; var status = 200;
    try
    {
        switch (cmd.GetProperty("action").GetString())
        {
            case "query":
                var parameters = cmd.TryGetProperty("params", out var p) ? p.EnumerateArray().Select(value => (object?)value).ToList() : [];
                var result = await db.Sql.RunAsync(cmd.GetProperty("sql").GetString()!, parameters);
                Check(cmd.GetProperty("status").GetInt32() == 200 && result.Rows.Count == (cmd.TryGetProperty("rows", out var n) ? n.GetInt32() : 0));
                break;
            case "bulk":
                var rows = "abcde".Select((id, value) => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["id"] = id.ToString(), ["value"] = value });
                var reports = new List<BatchResult>();
                await foreach (var batch in db.Rows.WriteBatchesAsync(cmd.GetProperty("schema").GetString()!, rows, new() { BatchSize = 2 })) reports.Add(batch);
                Check(reports.SequenceEqual(new[] { new BatchResult(0, 2, 2), new BatchResult(2, 2, 2), new BatchResult(4, 1, 1) }));
                break;
            case "scan":
                var ids = new List<string>();
                await foreach (var row in db.Rows.ScanAsync(cmd.GetProperty("schema").GetString()!, "id", new() { PageSize = 2 })) ids.Add(row.GetProperty("id").GetString()!);
                Check(ids.SequenceEqual(new[] { "a", "b", "c", "d", "e" }));
                break;
            default: throw new Exception("Unknown compatibility action");
        }
    }
    catch (OriginChainException error) { status = (int)(error.Status ?? 0); Check(cmd.GetProperty("status").GetInt32() != 200 && status == cmd.GetProperty("status").GetInt32()); }
    Check(calls - before == cmd.GetProperty("requests").GetInt32());
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, status, requests = calls - before }));
}
