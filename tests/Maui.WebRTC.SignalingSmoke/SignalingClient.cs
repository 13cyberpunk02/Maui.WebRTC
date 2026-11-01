using System.Text.Json;
using System.Threading.Channels;
using Maui.WebRTC;
using Microsoft.AspNetCore.SignalR.Client;

/// <summary>
/// Connects a PeerConnection to the signaling hub. Whoever is already in the room when the other
/// peer joins makes the offer. Copy this class into your app.
/// </summary>
public sealed class SignalingClient : IAsyncDisposable
{
    readonly HubConnection _hub;
    readonly PeerConnection _connection;
    readonly string _room;
    readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>();
    readonly Task _worker;

    /// <summary>Raised when the other peer leaves the room.</summary>
    public event Action? PeerLeft;

    /// <summary>Raised when a signaling step fails, for example on a malformed message.</summary>
    public event Action<Exception>? Failed;

    public SignalingClient(Uri hubUrl, PeerConnection connection, string room, Func<Task<string?>>? accessToken = null)
    {
        _connection = connection;
        _room = room;
        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, options => options.AccessTokenProvider = accessToken)
            .WithAutomaticReconnect()
            .Build();

        // Messages are handled strictly one at a time, so a candidate can't overtake its description.
        _hub.On("PeerJoined", () => Enqueue(MakeOfferAsync));
        _hub.On<string, string>("Signal", (type, payload) => Enqueue(() => HandleSignalAsync(type, payload)));
        _hub.On("PeerLeft", () => PeerLeft?.Invoke());

        // The server forgets a connection's groups when it drops, so the room has to be joined again.
        _hub.Reconnected += _ => _hub.InvokeAsync("Join", _room);

        _connection.IceCandidateGathered += OnCandidateGathered;
        _worker = Task.Run(ProcessWorkAsync);
    }

    public async Task JoinAsync(CancellationToken cancellationToken = default)
    {
        await _hub.StartAsync(cancellationToken);
        await _hub.InvokeAsync("Join", _room, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _connection.IceCandidateGathered -= OnCandidateGathered;
        _work.Writer.TryComplete();
        await _worker;
        await _hub.DisposeAsync();
    }

    void Enqueue(Func<Task> step) => _work.Writer.TryWrite(step);

    async Task ProcessWorkAsync()
    {
        await foreach (var step in _work.Reader.ReadAllAsync())
        {
            try
            {
                await step();
            }
            catch (Exception ex)
            {
                Failed?.Invoke(ex);
            }
        }
    }

    async Task MakeOfferAsync()
    {
        var offer = await _connection.CreateOfferAsync();
        // Sent first so that our candidates, which start with SetLocalDescription, arrive after it.
        await _hub.InvokeAsync("Signal", offer.Type, offer.Sdp);
        await _connection.SetLocalDescriptionAsync(offer);
    }

    async Task HandleSignalAsync(string type, string payload)
    {
        switch (type)
        {
            case "offer":
                await _connection.SetRemoteDescriptionAsync(new SessionDescription(type, payload));
                var answer = await _connection.CreateAnswerAsync();
                await _hub.InvokeAsync("Signal", answer.Type, answer.Sdp);
                await _connection.SetLocalDescriptionAsync(answer);
                break;
            case "answer":
                await _connection.SetRemoteDescriptionAsync(new SessionDescription(type, payload));
                break;
            case "candidate":
                await _connection.AddIceCandidateAsync(JsonSerializer.Deserialize<IceCandidate>(payload)!);
                break;
        }
    }

    void OnCandidateGathered(IceCandidate candidate) =>
        Enqueue(() => _hub.InvokeAsync("Signal", "candidate", JsonSerializer.Serialize(candidate)));
}
