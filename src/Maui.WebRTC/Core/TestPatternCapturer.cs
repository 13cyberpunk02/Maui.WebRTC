namespace Maui.WebRTC;

/// <summary>Pushes a moving test pattern into a source, for trying out video without a camera.</summary>
public sealed class TestPatternCapturer : IDisposable
{
    readonly CancellationTokenSource _stop = new();
    readonly Task _loop;

    public TestPatternCapturer(VideoSource source, int width = 640, int height = 480, int fps = 30) =>
        _loop = Task.Run(() => RunAsync(source, width, height, fps, _stop.Token));

    public void Dispose()
    {
        _stop.Cancel();
        _loop.GetAwaiter().GetResult();
        _stop.Dispose();
    }

    static async Task RunAsync(VideoSource source, int width, int height, int fps, CancellationToken token)
    {
        var chromaWidth = (width + 1) / 2;
        var chromaHeight = (height + 1) / 2;
        var y = new byte[width * height];
        var u = new byte[chromaWidth * chromaHeight];
        var v = new byte[chromaWidth * chromaHeight];
        const int barWidth = 40;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / fps));
        for (var frame = 0; ; frame++)
        {
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                    y[row * width + column] = (byte)((column + frame * 4) / barWidth % 2 == 0 ? 220 : 40);
            }

            // Top half blue-ish, bottom half red-ish, so a flip or colour swap is easy to spot.
            for (var row = 0; row < chromaHeight; row++)
            {
                var top = row < chromaHeight / 2;
                u.AsSpan(row * chromaWidth, chromaWidth).Fill((byte)(top ? 200 : 90));
                v.AsSpan(row * chromaWidth, chromaWidth).Fill((byte)(top ? 90 : 200));
            }

            source.PushI420(width, height, y, width, u, chromaWidth, v, chromaWidth);

            try
            {
                await timer.WaitForNextTickAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
