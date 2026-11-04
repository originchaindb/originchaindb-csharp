# OriginChainDB for .NET

The official .NET SDK for working with OriginChainDB through SQL, rows,
vector search, full-text search, and graph relationships. Use one database client
for these operations, with native .NET types and conventions.

[![CI](https://github.com/originchaindb/originchaindb-csharp/actions/workflows/ci.yml/badge.svg)](https://github.com/originchaindb/originchaindb-csharp/actions/workflows/ci.yml)

**Requirements:** .NET 8+. **Package:** `OriginChainDB`.

> This README describes the **1.0.0-rc.1 source candidate**. It has not been
> published to package registries. Follow the source installation below; an older
> registry package may expose a different API.

[Install](#installation) | [Connect](#connect-and-run-your-first-query) |
[Examples](#product-catalog-example) | [Results](#understanding-sql-results) |
[Errors](#handling-errors) | [Source](#source-layout)

## Installation

Build the local NuGet package:

```sh
git clone https://github.com/originchaindb/originchaindb-csharp.git
cd originchaindb-csharp
dotnet pack OriginChainDB -c Release
```

From your application directory, install it from the local output folder:

```sh
dotnet add package OriginChainDB --version 1.0.0-rc.1 --source /absolute/path/to/originchaindb-csharp/OriginChainDB/bin/Release
```

Replace the source path with your checkout. The client uses `HttpClient` and
`System.Text.Json`; no additional JSON package is needed.

Pin a reviewed source commit in your application or build process. See the
[changelog](CHANGELOG.md) before updating.

## Connect and run your first query

Copy the complete database address from **Connect** into `OC_URL`, and the
database access token into `OC_TOKEN`. The SDK sends the token as a bearer
credential. It uses the address as supplied; do not add or derive a tenant path.

macOS/Linux shell:

```sh
export OC_URL="<database HTTPS address from Connect>"
export OC_TOKEN="<database access token>"
```

PowerShell:

```powershell
$env:OC_URL = "<database HTTPS address from Connect>"
$env:OC_TOKEN = "<database access token>"
```

Replace the placeholders, keep credentials out of source control, and use a
secret manager for deployed applications. HTTPS is required except for local
loopback development. SQL tools use a separate database login from Connect.

Use this as `Program.cs` in a .NET 8+ console application and run `dotnet run`.

```csharp
using OriginChainDB;

using var db = Client.FromEnvironment();
var result = await db.Sql.RunAsync("SELECT $1 AS value", [7]);
Console.WriteLine(result.Rows[0]);
```

The query needs no tables. Its returned row contains `value` equal to `7`.
`$1` binds the first parameter; `$2` binds the second. Bind values instead of
interpolating application input into SQL. Parameters do not substitute table or
column names; those identifiers must come from trusted application code.

## Product catalog example

The complete [catalog program](examples/Catalog/Program.cs) creates a small product catalog and
demonstrates the same sequence shown below. It uses the included
[products.toml](examples/products.toml) schema and writes two rows plus search
data under `sdk_examples.products`. **Run it against a development database.**
Your token needs permission to register schemas and read/write the relevant data.

Run from the SDK checkout root after installation and setting the two variables:

```sh
dotnet run --project examples/Catalog
```

The sections below are excerpts from that complete program. C# snippets belong inside an async method or top-level program, after creating `db`.
Required imports are `OriginChainDB` plus standard .NET implicit usings; the complete file includes them.

### Register the schema

The schema declares string columns `id`, `name`, `description`, and `related_to`.
`id` is the primary key. The `related` relation connects a product to the product
named by `related_to`. Registration reads the TOML file; it does not require
you to construct a schema request manually.

```csharp
var schema = "sdk_examples.products";
await db.Schemas.RegisterAsync(await File.ReadAllTextAsync("examples/products.toml"));
```

### Write and read rows

Create the referenced product first, then the product pointing to it. `put`
writes the supplied row; `get` retrieves it by primary key. The example prints
the row for `p1`, named `Widget`.

```csharp
await db.Rows.PutAsync(schema, new Dictionary<string, object?> {
    ["id"] = "p2", ["name"] = "Gadget", ["description"] = "A red gadget",
});
await db.Rows.PutAsync(schema, new Dictionary<string, object?> {
    ["id"] = "p1", ["name"] = "Widget", ["description"] = "A small blue widget", ["related_to"] = "p2",
});
var product = await db.Rows.GetAsync(schema, "p1");
Console.WriteLine(product);
```

For multiple rows, the rows namespace also provides a batch-put operation.
Treat its response as a batch report, not as a promise of transaction atomicity.

### Query with SQL

Use `sql.run` (with the language's casing) for a complete statement result.
`execute` is an alias; `query` is intended for SELECT results.

```csharp
var result = await db.Sql.RunAsync(
    "SELECT id, name FROM sdk_examples.products WHERE id = $1", ["p1"]);
foreach (var row in result.Rows)
    Console.WriteLine($"{row.GetProperty("id")} {row.GetProperty("name")}");
```

Use the same parameter binding for INSERT, UPDATE, and DELETE. Read the
server-reported write count rather than assuming a successful request changed a row.

### Search by vector similarity

Store an embedding with its product ID, then request up to five nearest hits.
The default metric is cosine. These four numbers are a small illustrative vector;
generate real embeddings with your chosen model, and use that same model and
dimension for stored vectors and queries. The SDK does not generate embeddings.

```csharp
double[] embedding = [0.1, 0.2, 0.3, 0.4];
await db.Vector.PutAsync(schema, "p1", embedding, new Dictionary<string, object?> { ["category"] = "tools" });
var hits = await db.Vector.SearchAsync(schema, embedding, new VectorSearchOptions { K = 5 });
foreach (var hit in hits) Console.WriteLine($"{hit.Id} {hit.Score}");
```

Each hit exposes an ID, score, optional metadata, and the original hit. Do not
assume the score is a probability; its interpretation depends on the metric.

### Search text

Index the description explicitly, then use ranked BM25 search. Search defaults
to ten hits; the example asks for five. Boolean and phrase modes, highlighting,
fuzzy matching, and facets are available through the language's search options.

```csharp
await db.Fts.IndexAsync(schema, "description", "p1", "A small blue widget");
var matches = await db.Fts.SearchAsync(schema, "description", "blue widget", new FtsSearchOptions { K = 5 });
foreach (var hit in matches.Hits) Console.WriteLine($"{hit.DocId} {hit.Score}");
```

The result contains `hits`, optional `facets`, and the original response.
Hits have a document ID and score; unranked modes use zero for the score.
Writing a row does not imply this example's separate full-text or vector index
entry has been updated: update the relevant index when your application changes data.

### Follow graph relationships

The schema's `related` relation and the `p1.related_to = p2` row value create the
connection used here. The result includes the neighbor `p2`.

```csharp
var neighbors = await db.Graph.NeighborsAsync(schema, "p1", "related");
Console.WriteLine(neighbors);
```

The graph namespace also supports breadth-first traversal and Cypher queries.
Cypher parameters bind **by name**; SQL parameters bind **by position**.
See the [API guide](docs/api.md) and source links below for exact signatures.

## Bulk writes and paginated reads

For larger datasets, `WriteBatchesAsync` sends bounded batches and yields an acknowledgment
for each input range. `ScanAsync` reads rows lazily in ordered pages; `PagesAsync` also
exposes a continuation value for resumable processing. These operations use the
same limits and failure rules across all SDKs.

```csharp
await foreach (var batch in db.Rows.WriteBatchesAsync(schema, rows,
    new() { BatchSize = 2 }, cancellation.Token))
{
    Console.WriteLine($"{batch.Offset} {batch.Count} {batch.Inserted}");
}
```

This excerpt uses `db`, `schema` and the lazy `rows` producer from the
[complete example](examples/BulkAndScan/Program.cs). See the [bulk and pagination guide](docs/bulk-and-pagination.md)
for setup, reads, checkpoints, cancellation and recovery. Failed writes are never
replayed automatically; paginated reads require a unique immutable string key.

## Understanding SQL results

Rows are `IReadOnlyList<JsonElement>`; choose `GetInt64`, `GetDecimal` or `GetRawText` for the column. SQL returns a result object rather than only an array:

| Member | Meaning |
| --- | --- |
| `Kind` | Statement kind, such as select, insert, update or delete |
| `Rows` | Returned rows, including RETURNING rows when the server supplies them |
| `Columns` | Column names, when supplied |
| `ColumnTypes` | Column type metadata, when supplied |
| `RowsAffected` | Server-reported write count; missing means unknown |
| `RowsBuffered` | Buffered count, when supplied; it is not proof of a committed write |
| `Results` | Individual results for a multi-statement response |
| `Raw` | Original response, including additional server fields |

Write counts use `long?`; `null` means the server did not report
a count. Empty rows are not evidence of a failed statement: a write may return
only counts. Conversely, a malformed response is an error, not an empty success.

Numeric and temporal **strings** remain strings. Convert them deliberately in
your application when you know the column type. Unsupported write-count ranges
raise a protocol error instead of silently rounding.

Multi-statement batches can partially succeed. A failed batch exposes the
available statement results through its error. Do not assume batches are atomic
or replay an entire batch without checking which statements completed.

## Timeouts and client lifetime

The default request timeout is 60 seconds. All operations are async and
accept a `CancellationToken`:

```csharp
using var db = Client.FromEnvironment(TimeSpan.FromSeconds(10));
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var result = await db.Sql.RunAsync("SELECT $1 AS value", [7], cancellation.Token);
```

Share a client for the application's lifetime and dispose it at shutdown.
Caller cancellation raises `OperationCanceledException`; an SDK deadline failure
has error kind `timeout`.

Requests are **not retried automatically by the SDK by default**, and redirects
are not followed. A timeout or disconnect can happen after a write committed.
Canceling the local wait does not prove rollback. Only retry an operation when
your application can safely reconcile or repeat it.

## Handling errors

```csharp
try
{
    var result = await db.Sql.RunAsync("SELECT $1 AS value", [7]);
    Console.WriteLine(result.Rows.Count);
}
catch (OriginChainException error)
{
    Console.Error.WriteLine($"{error.Kind}: HTTP {error.Status}, request ID {error.RequestId}");
    if (error.Result is not null)
        Console.Error.WriteLine($"Partial batch: {error.Result.Results.Count}");
}
```

`Kind` distinguishes protocol and batch failures from transport and HTTP errors.
Local argument failures use normal .NET argument exceptions.

| Symptom | What to check |
| --- | --- |
| 401 or 403 | Token validity, expiry, database access and operation grants |
| 404 | Complete database address, schema name and resource ID |
| 400 or 422 | SQL syntax, parameter order, schema fields and vector dimensions |
| 429 | Capacity/rate limits and any server-provided retry timing |
| Timeout or 5xx after a write | Check the outcome before retrying |
| Empty search results | Indexed data, query terms, dimensions, filters and feature availability |

Include the SDK version and request ID in a support report. Redact credentials
and application data. Available database features and token permissions still
determine which operations can run.

## Application integration

The [API v1 reference](docs/api/v1/README.md) covers typed row conversion,
rotating credentials, local request diagnostics, native HTTP configuration,
and cancellation. The [complete integration example](examples/Integration/Program.cs) shows
these features together using a read-only query. See the
[migration guide](docs/migration-v1.md) when upgrading an earlier application.

## Source layout

| Path | Purpose |
| --- | --- |
| [OriginChainDB/Client.cs](OriginChainDB/Client.cs) | Connection, HTTP transport and cancellation |
| [OriginChainDB/Resources.cs](OriginChainDB/Resources.cs) | Operation services and options |
| [OriginChainDB/OriginChainException.cs](OriginChainDB/OriginChainException.cs) | Error and SQL result models |
| [OriginChainDB/VectorHit.cs](OriginChainDB/VectorHit.cs), [OriginChainDB/FtsResult.cs](OriginChainDB/FtsResult.cs) | Search results |
| [examples/](examples/) | Quickstart, product catalog and schema |
| [tests/contract/run.py](tests/contract/run.py) | Shared offline HTTP conformance fixture |
| [docs/api.md](docs/api.md), [docs/contract.md](docs/contract.md) | Native API notes and shared behavior |

All eight SDKs share the same core concepts and HTTP contract. Method casing,
JSON types and concurrency follow each language. Historical Python/TypeScript
helpers are broader than this common core; do not assume every advanced helper
exists in every SDK.

## Development and support

From the checkout root:

```sh
dotnet build OriginChainDB -c Release
python tests/contract/run.py -- dotnet run --project tests/Consumer
dotnet pack OriginChainDB -c Release
```

The contract runner requires Python 3.10+ for testing only and uses a local
fixture; it does not require database credentials. Application runtime
requirements are listed at the top. Live examples use your selected database.

Read [Contributing](CONTRIBUTING.md) before sending a change. Report bugs through
[GitHub issues](https://github.com/originchaindb/originchaindb-csharp/issues); report security concerns
privately as described in [Security](SECURITY.md). See [LICENSE](LICENSE) for MIT terms.

See [quality and build verification](docs/quality.md) for benchmarks, API compatibility checks, package installation checks and signed build provenance.
