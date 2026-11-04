using OriginChainDB;
using System.Diagnostics;
using System.Text.Json;

using var db = Client.FromEnvironment();
var report = new Dictionary<string, object>();
async Task<double> Query(string mode)
{
    var start = Stopwatch.GetTimestamp();
    var result = await db.Sql.QueryAsync("bench:" + mode);
    if (result.Rows.Count != (mode == "large" ? 2048 : 1)) throw new Exception("Unexpected result size");
    return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
for (var i = 0; i < 10; i++) await Query("warm");
foreach (var (mode, count) in new[] { ("sequential",100), ("concurrent",64), ("large",10) })
{
    var start = Stopwatch.GetTimestamp();
    var values = new double[count];
    if (mode == "concurrent")
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async worker => { for (var i = worker; i < count; i += 8) values[i] = await Query(mode); }));
    else for (var i = 0; i < count; i++) values[i] = await Query(mode);
    report[mode + "_ms"] = values;
    report[mode + "_rps"] = count / Stopwatch.GetElapsedTime(start).TotalSeconds;
}
Console.WriteLine("OC_BENCHMARK " + JsonSerializer.Serialize(report));
