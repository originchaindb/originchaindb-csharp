using OriginChainDB;

try
{
    using var db = Client.FromEnvironment();
    // section:setup
    var schema = "sdk_examples.products";
    await db.Schemas.RegisterAsync(await File.ReadAllTextAsync("examples/products.toml"));
    // endsection

    // section:rows
    await db.Rows.PutAsync(schema, new Dictionary<string, object?> {
        ["id"] = "p2", ["name"] = "Gadget", ["description"] = "A red gadget",
    });
    await db.Rows.PutAsync(schema, new Dictionary<string, object?> {
        ["id"] = "p1", ["name"] = "Widget", ["description"] = "A small blue widget", ["related_to"] = "p2",
    });
    var product = await db.Rows.GetAsync(schema, "p1");
    Console.WriteLine(product);
    // endsection

    // section:sql
    var result = await db.Sql.RunAsync(
        "SELECT id, name FROM sdk_examples.products WHERE id = $1", ["p1"]);
    foreach (var row in result.Rows)
        Console.WriteLine($"{row.GetProperty("id")} {row.GetProperty("name")}");
    // endsection

    // section:vector
    double[] embedding = [0.1, 0.2, 0.3, 0.4];
    await db.Vector.PutAsync(schema, "p1", embedding, new Dictionary<string, object?> { ["category"] = "tools" });
    var hits = await db.Vector.SearchAsync(schema, embedding, new VectorSearchOptions { K = 5 });
    foreach (var hit in hits) Console.WriteLine($"{hit.Id} {hit.Score}");
    // endsection

    // section:fts
    await db.Fts.IndexAsync(schema, "description", "p1", "A small blue widget");
    var matches = await db.Fts.SearchAsync(schema, "description", "blue widget", new FtsSearchOptions { K = 5 });
    foreach (var hit in matches.Hits) Console.WriteLine($"{hit.DocId} {hit.Score}");
    // endsection

    // section:graph
    var neighbors = await db.Graph.NeighborsAsync(schema, "p1", "related");
    Console.WriteLine(neighbors);
    // endsection
}
catch (OriginChainException error)
{
    Console.Error.WriteLine($"Request failed: {error.Kind}, HTTP {error.Status}, request ID {error.RequestId}");
    throw;
}
