using Maui.WebRTC.Native;

namespace Maui.WebRTC;

public sealed partial class CameraCapturer
{
    nint _camera;

    public static partial int DeviceCount => NativeMethods.CameraCount();

    public partial void Start(VideoSource source, int width, int height, int fps)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_camera != 0)
            throw new InvalidOperationException("The camera is already started.");

        _camera = NativeMethods.CameraStart(0, width, height, fps, source.Handle);
        if (_camera == 0)
            throw new WebRtcException("Failed to open the camera.");
    }

    public partial void Stop()
    {
        var camera = Interlocked.Exchange(ref _camera, 0);
        if (camera != 0)
            NativeMethods.CameraStop(camera);
    }
}
