# OriginChainDB for .NET

The official .NET client for SQL, vector search, full-text search, and graph queries.

[![CI](https://github.com/originchaindb/originchaindb-csharp/actions/workflows/ci.yml/badge.svg)](https://github.com/originchaindb/originchaindb-csharp/actions/workflows/ci.yml)

## Get started

Requires **.NET 8+**. This is the unpublished 1.0 release candidate. Build from
source and pin a reviewed commit; registry publication is currently on hold.

```sh
git clone https://github.com/originchaindb/originchaindb-csharp.git
cd originchaindb-csharp
dotnet pack OriginChainDB -c Release
```

From your application directory, install the built package using its local source:

```sh
dotnet add package OriginChainDB --version 1.0.0-rc.1 --source /absolute/path/to/originchaindb-csharp/OriginChainDB/bin/Release
```

Copy your database address and access token from Connect into `OC_URL` and
`OC_TOKEN`. Keep tokens in your server's environment or secret manager.

```csharp
using OriginChainDB;

using var db = Client.FromEnvironment();
var result = await db.Sql.RunAsync("SELECT $1 AS value", [7]);
Console.WriteLine(result.Rows[0]);
```

Reuse the client for your application's lifetime. Pass values as parameters;
do not interpolate application data into query text.

## One API family

Use `sql`, `schemas`, `rows`, `vector`, `fts`, and `graph` for the same database
concepts across every OriginChainDB SDK. Names use this language's normal casing.

- SQL results include returned rows, confirmed counts, metadata and batch results.
- Full-text search provides the same hit structure for ranked and unranked queries.
- Access tokens are sent as bearer credentials to the exact database address.
- Failed writes are not retried automatically.

[Using the client](docs/api.md) Â· [Behavior](docs/contract.md) Ã‚Â· [Contributing](CONTRIBUTING.md) Ã‚Â·
[Changes](CHANGELOG.md) Ã‚Â· [Security](SECURITY.md)

## Development

```sh
dotnet build OriginChainDB -c Release
python tests/contract/run.py -- dotnet run --project tests/Consumer
dotnet pack OriginChainDB -c Release
```

The HTTP contract runner requires Python 3.10+ for testing only. No database
credentials are needed for the offline suite. Native runtime and dependency
requirements above apply to applications.
