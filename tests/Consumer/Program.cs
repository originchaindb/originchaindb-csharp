using OriginChainDB;
using System.Net;
static void Check(bool value) { if (!value) throw new Exception("Contract assertion failed"); }
using var client = Client.FromEnvironment();
var result = await client.Sql.RunAsync("contract:select", [7, "quote' / 雪", null]);
Check(result.Rows[0].GetProperty("id").GetInt64() == 9007199254740993);
Check(result.Rows[0].GetProperty("amount").GetString() == "1.00000000000000001" && result.Raw.GetProperty("future_field").GetProperty("kept").GetBoolean());
result = await client.Sql.RunAsync("contract:update");
Check(result.RowsAffected == 2 && result.RowsBuffered == 3 && result.Rows.Count == 1 && result.Columns![0] == "id");
result = await client.Sql.RunAsync("contract:unknown");
Check(result.Kind == "future_statement" && result.RowsAffected is null);
foreach (var name in new[] { "batch", "unavailable", "unauthorized", "redirect", "malformed", "invalid-rows" })
{
    var caught = false;
    try { await client.Sql.RunAsync("contract:" + name); }
    catch (OriginChainException e)
    {
        caught = true;
        if (name == "batch") Check(e.Result!.Results[0].RowsAffected == 1);
        if (name == "unauthorized") Check(e.Status == HttpStatusCode.Unauthorized && e.RequestId == "84f92822-5837-432e-981a-7b24ad65e16c");
        if (name == "unavailable") Check(e.Status == HttpStatusCode.ServiceUnavailable && e.RetryAfter == "0");
    }
    Check(caught);
}
await client.Rows.GetAsync("demo/items", "a b?/%");
await client.Rows.PutAsync("demo.items", new Dictionary<string, object?> { ["id"] = "one", ["nullable"] = null });
await client.Rows.PutBatchAsync("demo.items", [new Dictionary<string, object?> { ["id"] = "one" }, new Dictionary<string, object?> { ["id"] = "two" }]);
await client.Schemas.ListAsync(); await client.Schemas.RegisterAsync("[schema]\nname=\"demo.items\"\n");
await client.Vector.PutAsync("demo.items", "one", [1, 0], new Dictionary<string, object?> { ["tag"] = "test" });
Check((await client.Vector.SearchAsync("demo.items", [1, 0])).Select(hit => hit.Id).SequenceEqual(new[] { "one", "two" })); await client.Vector.DeleteAsync("demo.items", "one");
await client.Fts.IndexAsync("demo.items", "body", "one", "hello world"); Check((await client.Fts.SearchAsync("demo.items", "body", "hello")).Hits[0].DocId == "one");
var phrase = await client.Fts.SearchAsync("demo.items", "body", "phrase", new FtsSearchOptions { Mode = "phrase" });
Check(phrase.Hits[0].DocId == "two" && phrase.Hits[0].Score == 0);
var enriched = await client.Fts.SearchAsync("demo.items", "body", "highlight", new FtsSearchOptions { Highlight = true, Facets = ["tag"] });
Check(enriched.Facets is not null && enriched.Hits[0].Highlights is not null);
await client.Graph.QueryAsync("MATCH (n) RETURN $name", new Dictionary<string, object?> { ["name"] = "one" });
await client.Graph.NeighborsAsync("demo.items", "one", "related"); await client.Graph.BfsAsync("demo.items", "one", "related");
Console.WriteLine(".NET consumer assertions passed");
