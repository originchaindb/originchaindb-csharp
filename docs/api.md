# Using the client

Create `db` as shown in the [quickstart](../README.md). These independent examples
assume an existing `demo.items` schema and an appropriate search index.

```csharp
var result = await db.Sql.RunAsync("SELECT $1 AS value", [7]);
var row = await db.Rows.GetAsync("demo.items", "one");
var vectors = await db.Vector.SearchAsync("demo.items", [1, 0]);
var text = await db.Fts.SearchAsync("demo.items", "body", "hello");
var graph = await db.Graph.QueryAsync("MATCH (n) RETURN $name",
    new Dictionary<string, object?> { ["name"] = "one" });
```

## Results, errors, and lifetime

`SqlResult.Rows` contains `JsonElement` values. Use `GetInt64`, `GetDecimal`,
or `GetRawText` according to the column type. `RowsAffected` is `long?`.
Vector hits expose `Id`, `Score`, `Metadata`, and `Raw`. Full-text results expose
`Hits`, `Facets`, and `Raw`.

Catch `OriginChainException`; inspect `Kind`, `Status`, and `RequestId`.
`Result` contains a partial SQL result for batch errors.

Use `Client.FromEnvironment(TimeSpan.FromSeconds(10))`. Every operation accepts
a `cancellationToken` parameter. Caller cancellation raises
`OperationCanceledException`; SDK timeouts have kind `timeout`. Dispose the client
at application shutdown, or use `using` for a bounded lifetime.

See the [shared behavior contract](contract.md) for parameter binding, batch
semantics, defaults, and retry behavior. The [contract consumer](../tests/Consumer/Program.cs)
exercises the full common API against a local fixture.
