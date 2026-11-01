namespace Maui.WebRTC;

internal static partial class LoopbackCheck
{
    /// <summary>Sends a test pattern from one peer to the other and waits for decoded frames.</summary>
    public static async Task<string> RunVideoAsync(TimeSpan timeout)
    {
        using var factory = new PeerConnectionFactory();
        using var caller = factory.CreatePeerConnection();
        using var callee = factory.CreatePeerConnection();
        using var source = factory.CreateVideoSource();
        using var track = factory.CreateVideoTrack(source);
        using var renderer = new VideoRenderer();
        using var pattern = new TestPatternCapturer(source);

        var received = new TaskCompletionSource<VideoTrack>(TaskCreationOptions.RunContinuationsAsynchronously);
        callee.TrackReceived += t =>
        {
            if (t is VideoTrack video)
                received.TrySetResult(video);
        };
        caller.IceCandidateGathered += c => _ = callee.AddIceCandidateAsync(c);
        callee.IceCandidateGathered += c => _ = caller.AddIceCandidateAsync(c);

        caller.AddTrack(track);

        var offer = await caller.CreateOfferAsync();
        await callee.SetRemoteDescriptionAsync(offer);
        await caller.SetLocalDescriptionAsync(offer);
        var answer = await callee.CreateAnswerAsync();
        await caller.SetRemoteDescriptionAsync(answer);
        await callee.SetLocalDescriptionAsync(answer);

        var remote = await received.Task.WaitAsync(timeout);
        renderer.SetTrack(remote);

        var deadline = DateTime.UtcNow + timeout;
        while (renderer.FramesRendered < 30)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Only {renderer.FramesRendered} frames decoded within {timeout.TotalSeconds:0}s");
            await Task.Delay(100);
        }

        renderer.SetTrack(null);
        return "video OK (30+ frames decoded)";
    }
}
