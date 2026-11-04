using OriginChainDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json.Serialization;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient("originchain", http => http.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton(provider => new Client(new ClientOptions
{
    Url = Environment.GetEnvironmentVariable("OC_URL") ?? "",
    // Replace with your application's current credential cache when required.
    TokenProvider = _ => ValueTask.FromResult(Environment.GetEnvironmentVariable("OC_TOKEN") ?? ""),
    HttpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("originchain"),
    DisposeHttpClient = true,
    OnRequest = evt => Console.WriteLine($"{evt.Operation} {evt.Status} {evt.Duration}")
}));
using var host = builder.Build();
var client = host.Services.GetRequiredService<Client>();
var result = await client.Sql.RunAsync("SELECT $1 AS id, $2 AS name", ["p1", "Widget"]);
foreach (var product in result.RowsAs<Product>()) Console.WriteLine(product);

sealed record Product([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("name")] string Name);
