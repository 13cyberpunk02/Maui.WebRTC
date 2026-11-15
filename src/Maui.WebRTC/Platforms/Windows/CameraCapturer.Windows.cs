using Maui.WebRTC.Native;

namespace Maui.WebRTC;

public sealed partial class CameraCapturer
{
    nint _camera;
    int _deviceIndex;

    public static partial int DeviceCount => NativeMethods.CameraCount();

    // Desktop cameras don't say which way they face, so the first one is used whatever was asked for.
    partial void StartPlatform(VideoSource source, int width, int height, int fps, CameraFacing facing) =>
        Open(source, 0, width, height, fps);

    partial void SwitchPlatform(VideoSource source, int width, int height, int fps)
    {
        var next = (_deviceIndex + 1) % DeviceCount;
        StopPlatform();
        Open(source, next, width, height, fps);
    }

    partial void StopPlatform()
    {
        var camera = Interlocked.Exchange(ref _camera, 0);
        if (camera != 0)
            NativeMethods.CameraStop(camera);
    }

    void Open(VideoSource source, int deviceIndex, int width, int height, int fps)
    {
        _camera = NativeMethods.CameraStart(deviceIndex, width, height, fps, source.Handle);
        if (_camera == 0)
            throw new WebRtcException("Failed to open the camera.");
        _deviceIndex = deviceIndex;
        Facing = CameraFacing.External;
    }
}
