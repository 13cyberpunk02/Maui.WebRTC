namespace Maui.WebRTC;

/// <summary>Which way a camera looks. Desktop cameras don't say, and count as <see cref="External"/>.</summary>
public enum CameraFacing { Front, Back, External }

/// <summary>
/// A camera to capture from, see <see cref="CameraCapturer.Devices"/>. On Windows the name is the one the system shows,
/// virtual cameras such as OBS's included; on a phone it says which way the camera faces.
/// </summary>
public sealed record CameraDevice(int Index, string Name, CameraFacing Facing);

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

    /// <summary>The cameras there are: front and back on a phone, every connected camera on Windows.</summary>
    public static partial IReadOnlyList<CameraDevice> Devices { get; }

    /// <summary>Whether there is another camera to <see cref="Switch"/> to.</summary>
    public static bool CanSwitch => DeviceCount > 1;

    /// <summary>The camera in use, or null when not capturing.</summary>
    public CameraDevice? Device { get; private set; }

    /// <summary>Which way the camera in use looks; <see cref="CameraFacing.External"/> on Windows.</summary>
    public CameraFacing Facing => Device?.Facing ?? CameraFacing.External;

    /// <summary>
    /// Starts capturing. <paramref name="facing"/> picks the camera on a phone, falling back to any other;
    /// on Windows the first connected camera is used.
    /// </summary>
    public void Start(VideoSource source, int width = 1280, int height = 720, int fps = 30, CameraFacing facing = CameraFacing.Front)
    {
        var devices = Devices;
        if (devices.Count == 0)
            throw new WebRtcException("This device has no camera.");
        Start(source, devices.FirstOrDefault(device => device.Facing == facing) ?? devices[0], width, height, fps);
    }

    /// <summary>Starts capturing from a camera picked from <see cref="Devices"/>.</summary>
    public void Start(VideoSource source, CameraDevice device, int width = 1280, int height = 720, int fps = 30)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source is not null)
            throw new InvalidOperationException("The camera is already started.");

        OpenPlatform(source, device, width, height, fps);
        (_source, _width, _height, _fps, Device) = (source, width, height, fps, device);
    }

    /// <summary>
    /// Moves to the next camera while capturing: between front and back on a phone, to the next connected
    /// camera on Windows. Does nothing when there is only one camera.
    /// </summary>
    public void Switch()
    {
        var devices = Devices;
        if (Device is not { } current || devices.Count < 2)
        {
            EnsureStarted();
            return;
        }

        // On a phone the other facing; otherwise simply the next one, wrapping around.
        var next = current.Facing != CameraFacing.External
            ? devices.FirstOrDefault(device => device.Facing != current.Facing && device.Facing != CameraFacing.External)
            : null;
        next ??= devices[(devices.FindIndex(device => device.Index == current.Index) + 1) % devices.Count];
        SwitchTo(next);
    }

    /// <summary>
    /// Moves to another camera while capturing. Frames keep going into the same source, so tracks and calls carry on
    /// without renegotiating.
    /// </summary>
    public void SwitchTo(CameraDevice device)
    {
        var source = EnsureStarted();
        if (device == Device)
            return;

        StopPlatform();
        Device = null;
        OpenPlatform(source, device, _width, _height, _fps);
        Device = device;
    }

    public void Stop()
    {
        _source = null;
        Device = null;
        StopPlatform();
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }

    VideoSource EnsureStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _source ?? throw new InvalidOperationException("The camera isn't started.");
    }

    partial void OpenPlatform(VideoSource source, CameraDevice device, int width, int height, int fps);

    partial void StopPlatform();
}

static class CameraDeviceListExtensions
{
    public static int FindIndex(this IReadOnlyList<CameraDevice> devices, Predicate<CameraDevice> match)
    {
        for (var i = 0; i < devices.Count; i++)
        {
            if (match(devices[i]))
                return i;
        }
        return -1;
    }
}
