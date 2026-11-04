// Proves that calls get through a TURN server: two peers in this process exchange only their relay
// candidates, so the only path between them runs through TURN.
//   dotnet run --project tests/Maui.WebRTC.TurnSmoke -- http://127.0.0.1:5199/turn-credentials
using System.Net.Http.Json;
using System.Text.Json;
using Maui.WebRTC;

Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length < 1)
{
    Console.WriteLine("Usage: Maui.WebRTC.TurnSmoke <credentials URL>   e.g. http://127.0.0.1:5199/turn-credentials");
    return 2;
}

using var http = new HttpClient();
var credentials = await http.GetFromJsonAsync<Credentials>(args[0], JsonSerializerOptions.Web)
    ?? throw new InvalidOperationException("The server returned no credentials.");
Console.WriteLine($"credentials: {credentials.Username}, valid for {credentials.Ttl / 3600}h, urls: {string.Join(", ", credentials.Urls)}");
var iceServers = credentials.Urls.Select(url => new IceServer(url, credentials.Username, credentials.Credential)).ToArray();

using var factory = new PeerConnectionFactory();
using var caller = factory.CreatePeerConnection(iceServers);
using var callee = factory.CreatePeerConnection(iceServers);

var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var echo = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var gathered = new Dictionary<string, int>();

void Connect(string name, PeerConnection from, PeerConnection to)
{
    from.IceCandidateGathered += candidate =>
    {
        var type = CandidateType(candidate.Candidate);
        lock (gathered)
            gathered[$"{name} {type}"] = gathered.GetValueOrDefault($"{name} {type}") + 1;
        // Host and server-reflexive candidates would let the peers meet directly; they stay behind.
        if (type == "relay")
            _ = to.AddIceCandidateAsync(candidate);
    };
    from.ConnectionStateChanged += state =>
    {
        if (state == PeerConnectionState.Failed)
            failure.TrySetResult($"{name}: connection failed");
    };
}

Connect("caller", caller, callee);
Connect("callee", callee, caller);
callee.DataChannelReceived += channel => channel.MessageReceived += message => channel.Send(message.Text);

var channel = caller.CreateDataChannel("turn");
channel.StateChanged += state =>
{
    if (state == DataChannelState.Open)
        channel.Send("through TURN");
};
channel.MessageReceived += message => echo.TrySetResult(message.Text);

var offer = await caller.CreateOfferAsync();
await callee.SetRemoteDescriptionAsync(offer);
await caller.SetLocalDescriptionAsync(offer);
var answer = await callee.CreateAnswerAsync();
await caller.SetRemoteDescriptionAsync(answer);
await callee.SetLocalDescriptionAsync(answer);

var first = await Task.WhenAny(echo.Task, failure.Task, Task.Delay(TimeSpan.FromSeconds(20)));
lock (gathered)
    Console.WriteLine("gathered: " + string.Join(", ", gathered.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}")));

if (first != echo.Task)
{
    Console.WriteLine(first == failure.Task ? await failure.Task : "no echo within 20s");
    Console.WriteLine(gathered.Keys.Any(key => key.EndsWith("relay"))
        ? "Relay candidates were gathered, but the peers could not reach each other through them."
        : "No relay candidates: the TURN server is unreachable or rejected the credentials.");
    return 1;
}

Console.WriteLine($"echo: {await echo.Task}");
Console.WriteLine($"route: {await RouteAsync(caller)}");
Console.WriteLine("TURN relay OK");
return 0;

static string CandidateType(string candidate)
{
    var parts = candidate.Split(' ');
    var index = Array.IndexOf(parts, "typ");
    return index >= 0 && index + 1 < parts.Length ? parts[index + 1] : "unknown";
}

static async Task<string> RouteAsync(PeerConnection connection)
{
    using var report = JsonDocument.Parse(await connection.GetStatsJsonAsync());
    var stats = report.RootElement.EnumerateArray().ToList();
    string Describe(string id) => stats
        .Where(s => Text(s, "id") == id)
        .Select(s => $"{Text(s, "candidateType")}/{Text(s, "protocol")} {Text(s, "address")}:{(s.TryGetProperty("port", out var p) ? p.GetInt32() : 0)}")
        .FirstOrDefault() ?? "?";

    var pair = stats.FirstOrDefault(s => Text(s, "type") == "candidate-pair" && Text(s, "state") == "succeeded"
        && s.TryGetProperty("nominated", out var nominated) && nominated.GetBoolean());
    return pair.ValueKind == JsonValueKind.Undefined
        ? "no nominated pair"
        : $"{Describe(Text(pair, "localCandidateId"))} -> {Describe(Text(pair, "remoteCandidateId"))}";

    static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
}

record Credentials(string[] Urls, string Username, string Credential, int Ttl);
