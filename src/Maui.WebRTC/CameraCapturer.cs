namespace Maui.WebRTC;

/// <summary>Which way a camera looks. Desktop cameras don't say, and count as <see cref="External"/>.</summary>
public enum CameraFacing { Front, Back, External }

/// <summary>
/// Captures from the device camera into a <see cref="VideoSource"/>. The app must already hold the
/// camera permission on Android. On phones the front camera is preferred.
/// </summary>
public sealed partial class CameraCapturer : IDisposable
{
    bool _disposed;
    VideoSource? _source;
    int _width;
    int _height;
    int _fps;

    public static partial int DeviceCount { get; }

    /// <summary>Whether there is another camera to <see cref="Switch"/> to.</summary>
    public static bool CanSwitch => DeviceCount > 1;

    /// <summary>The camera in use; <see cref="CameraFacing.External"/> on Windows.</summary>
    public CameraFacing Facing { get; private set; }

    /// <summary>
    /// Starts capturing. <paramref name="facing"/> picks the camera on a phone, falling back to any other;
    /// on Windows the first connected camera is used.
    /// </summary>
    public void Start(VideoSource source, int width = 1280, int height = 720, int fps = 30, CameraFacing facing = CameraFacing.Front)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source is not null)
            throw new InvalidOperationException("The camera is already started.");

        StartPlatform(source, width, height, fps, facing);
        (_source, _width, _height, _fps) = (source, width, height, fps);
    }

    /// <summary>
    /// Moves to the next camera while capturing: between front and back on a phone, to the next connected
    /// camera on Windows. Frames keep going into the same source, so tracks and calls carry on.
    /// Does nothing when there is only one camera.
    /// </summary>
    public void Switch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source is not { } source)
            throw new InvalidOperationException("The camera isn't started.");
        if (!CanSwitch)
            return;

        SwitchPlatform(source, _width, _height, _fps);
    }

    public void Stop()
    {
        _source = null;
        StopPlatform();
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    partial void StartPlatform(VideoSource source, int width, int height, int fps, CameraFacing facing);

    partial void SwitchPlatform(VideoSource source, int width, int height, int fps);

    partial void StopPlatform();
}
