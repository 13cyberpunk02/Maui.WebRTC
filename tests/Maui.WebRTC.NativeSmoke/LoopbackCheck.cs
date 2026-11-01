namespace Maui.WebRTC;

/// <summary>
/// Connects two peers inside one process and echoes text and binary messages over a data channel.
/// Shared by the console smoke test and the sample app, so both platforms run the same check.
/// </summary>
internal static partial class LoopbackCheck
{
    public static async Task<string> RunAsync(TimeSpan timeout, Action<string>? log = null)
    {
        using var factory = new PeerConnectionFactory();
        using var caller = factory.CreatePeerConnection();
        using var callee = factory.CreatePeerConnection();

        var textEcho = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var binaryEcho = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[] payload = [0, 1, 2, 250, 255];

        void Forward(string name, PeerConnection from, PeerConnection to)
        {
            from.IceCandidateGathered += candidate =>
            {
                log?.Invoke($"{name} candidate: {candidate.SdpMid}/{candidate.SdpMLineIndex} {candidate.Candidate}");
                _ = to.AddIceCandidateAsync(candidate).ContinueWith(
                    t => failure.TrySetResult($"AddIceCandidate: {t.Exception!.InnerException!.Message}"),
                    TaskContinuationOptions.OnlyOnFaulted);
            };
            from.SignalingStateChanged += state => log?.Invoke($"{name} signaling: {state}");
            from.IceGatheringStateChanged += state => log?.Invoke($"{name} gathering: {state}");
            from.ConnectionStateChanged += state =>
            {
                log?.Invoke($"{name} connection: {state}");
                if (state == PeerConnectionState.Failed)
                    failure.TrySetResult("Connection state: Failed");
            };
        }

        Forward("caller", caller, callee);
        Forward("callee", callee, caller);

        callee.DataChannelReceived += channel => channel.MessageReceived += message =>
        {
            if (message.IsBinary)
                channel.Send(message.Data);
            else
                channel.Send(message.Text);
        };

        var channel = caller.CreateDataChannel("loopback");
        channel.StateChanged += state =>
        {
            if (state == DataChannelState.Open)
            {
                channel.Send("ping ✓");
                channel.Send(payload);
            }
        };
        channel.MessageReceived += message =>
        {
            if (message.IsBinary)
                binaryEcho.TrySetResult(message.Data);
            else
                textEcho.TrySetResult(message.Text);
        };

        // Each side gets the remote description before the other starts gathering candidates, so a
        // candidate can never arrive ahead of the description it belongs to.
        var offer = await caller.CreateOfferAsync();
        await callee.SetRemoteDescriptionAsync(offer);
        await caller.SetLocalDescriptionAsync(offer);
        var answer = await callee.CreateAnswerAsync();
        await caller.SetRemoteDescriptionAsync(answer);
        await callee.SetLocalDescriptionAsync(answer);

        var echoes = Task.WhenAll(textEcho.Task, binaryEcho.Task);
        var first = await Task.WhenAny(echoes, failure.Task, Task.Delay(timeout));
        if (first == failure.Task)
            throw new WebRtcException(await failure.Task);
        if (first != echoes)
            throw new TimeoutException($"No echo within {timeout.TotalSeconds:0}s; channel state: {channel.State}");

        if (await textEcho.Task != "ping ✓")
            throw new WebRtcException($"Wrong text echo: {await textEcho.Task}");
        if (!(await binaryEcho.Task).SequenceEqual(payload))
            throw new WebRtcException("Wrong binary echo");

        return "data channel echo OK (text + binary)";
    }
}
