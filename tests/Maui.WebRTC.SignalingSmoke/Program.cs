using Maui.WebRTC;

// Runs a full call between two peers through a running signaling server:
//   dotnet run --project samples/Maui.WebRTC.SignalingServer --urls http://127.0.0.1:5199
//   dotnet run --project tests/Maui.WebRTC.SignalingSmoke -- http://127.0.0.1:5199/signaling
var hubUrl = new Uri(args.Length > 0 ? args[0] : "http://127.0.0.1:5199/signaling");
var room = Guid.NewGuid().ToString("N");

using var factory = new PeerConnectionFactory();
using var first = factory.CreatePeerConnection();
using var second = factory.CreatePeerConnection();

var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
var echo = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

second.DataChannelReceived += channel => channel.MessageReceived += message => channel.Send(message.Text);

var channel = first.CreateDataChannel("signaling-check");
channel.StateChanged += state =>
{
    if (state == DataChannelState.Open)
        channel.Send("hello over webrtc");
};
channel.MessageReceived += message => echo.TrySetResult(message.Text);

await using var firstClient = new SignalingClient(hubUrl, first, room);
await using var secondClient = new SignalingClient(hubUrl, second, room);
firstClient.Failed += ex => failed.TrySetResult(ex);
secondClient.Failed += ex => failed.TrySetResult(ex);

var left = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
firstClient.PeerLeft += () => left.TrySetResult();

await firstClient.JoinAsync();
await secondClient.JoinAsync();

var finished = await Task.WhenAny(echo.Task, failed.Task, Task.Delay(TimeSpan.FromSeconds(20)));
if (finished == failed.Task)
    throw await failed.Task;
if (finished != echo.Task)
    throw new TimeoutException($"No echo within 20s; channel state: {channel.State}");
Console.WriteLine($"call through signaling server OK: {await echo.Task}");

await using (var third = new SignalingClient(hubUrl, factory.CreatePeerConnection(), room))
{
    try
    {
        await third.JoinAsync();
        throw new InvalidOperationException("A third peer got into a full room.");
    }
    catch (Microsoft.AspNetCore.SignalR.HubException)
    {
        Console.WriteLine("full room rejected OK");
    }
}

await secondClient.DisposeAsync();
await left.Task.WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine("peer left notification OK");
return 0;
