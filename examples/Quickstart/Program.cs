using OriginChainDB;

using var db = Client.FromEnvironment();
var result = await db.Sql.RunAsync("SELECT $1 AS value", [7]);
Console.WriteLine(result.Rows[0]);
