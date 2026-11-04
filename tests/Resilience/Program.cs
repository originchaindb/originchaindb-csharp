using OriginChainDB;
using System.Text.Json.Serialization;

static void Check(bool value) { if (!value) throw new Exception("Resilience assertion failed"); }
static async Task<OriginChainException> Fails(Client client, string name)
{
    try { await client.Sql.RunAsync("fault:" + name); }
    catch (OriginChainException error) { return error; }
    throw new Exception("Expected SDK failure");
}
var url = Environment.GetEnvironmentVariable("OC_URL")!;
var token = Environment.GetEnvironmentVariable("OC_TOKEN")!;
using var client = new Client(url, token);
foreach (var (name, status) in new[] { ("expired",401), ("revoked",401), ("forbidden",403), ("limited",429) })
{
    var error = await Fails(client, name);
    Check((int)error.Status! == status && error.RequestId == "84f92822-5837-432e-981a-7b24ad65e16c");
    if (name == "limited") Check(error.RetryAfter == "2");
}
await Fails(client, "interrupted");
using var slow = new Client(url, token, TimeSpan.FromMilliseconds(150));
Check((await Fails(slow, "timeout")).Kind == "timeout");
var calls = 0;
var events = new List<RequestEvent>();
using var applicationHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false });
using (var rotating = new Client(new ClientOptions { Url = url,
    TokenProvider = _ => ValueTask.FromResult("rotate-" + Interlocked.Increment(ref calls)),
    OnRequest = events.Add, HttpClient = applicationHttp }))
{
    Check((await Fails(rotating, "rotate")).Status == System.Net.HttpStatusCode.Unauthorized && calls == 1);
    var result = await rotating.Sql.RunAsync("fault:rotate");
    Check(calls == 2 && events.Count == 2 && events[0].ErrorKind == "authentication" && events[1].ErrorKind is null);
    Check(events[1].Operation == "sql" && events[1].Duration > TimeSpan.Zero);
    var description = string.Join(";", events);
    Check(!description.Contains("rotate-") && !description.Contains(url));
    var products = result.RowsAs<Product>();
    Check(products[0].Id == 9007199254740993L && products[0].Amount == "1.00000000000000001");
    try { result.RowsAs<Bad>(); throw new Exception("Invalid mapping accepted"); }
    catch (RowDecodeException error) { Check(error.Row == 0); }
}
// An injected client remains application-owned after the SDK is disposed.
applicationHttp.DefaultRequestHeaders.Add("X-Ownership-Check", "still-open");
using var invalid = new Client(new ClientOptions { Url = url, TokenProvider = _ => ValueTask.FromResult("bad\nheader") });
Check((await Fails(invalid, "invalid-provider")).Kind == "credential");
using var observed = new Client(new ClientOptions { Url = url, Token = token, OnRequest = _ => throw new Exception("observer") });
Check((await observed.Sql.RunAsync("fault:observer")).Rows.Count == 1);

var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var pendingEvents = new List<RequestEvent>();
using var pending = new Client(new ClientOptions { Url = url, OnRequest = pendingEvents.Add,
    TokenProvider = async cancellation => { started.SetResult(); await Task.Delay(Timeout.Infinite, cancellation); return "never"; } });
using var cancel = new CancellationTokenSource();
var request = pending.Sql.RunAsync("not sent", cancellationToken: cancel.Token);
await started.Task;
cancel.Cancel();
try { await request; throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { Check(pendingEvents.Single().ErrorKind == "canceled"); }
Console.WriteLine(".NET resilience assertions passed");

sealed record Product([property: JsonPropertyName("id")] long Id, [property: JsonPropertyName("amount")] string Amount);
sealed record Bad([property: JsonPropertyName("id")] byte Id);
