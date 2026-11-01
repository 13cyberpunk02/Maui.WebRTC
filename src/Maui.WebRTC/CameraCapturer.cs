namespace Maui.WebRTC;

/// <summary>
/// Captures from the device camera into a <see cref="VideoSource"/>. The app must already hold the
/// camera permission on Android. On phones the front camera is preferred.
/// </summary>
public sealed partial class CameraCapturer : IDisposable
{
    bool _disposed;

    public static partial int DeviceCount { get; }

    public partial void Start(VideoSource source, int width = 1280, int height = 720, int fps = 30);

    public partial void Stop();

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
