using Maui.WebRTC.Native;

namespace Maui.WebRTC;

public sealed partial class CameraCapturer
{
    nint _camera;

    public static partial int DeviceCount => NativeMethods.CameraCount();

    // Desktop cameras don't say which way they face.
    public static partial IReadOnlyList<CameraDevice> Devices =>
        Enumerable.Range(0, NativeMethods.CameraCount())
            .Select(index => new CameraDevice(index, NativeMethods.TakeString(NativeMethods.CameraName(index)) ?? $"Camera {index + 1}", CameraFacing.External))
            .ToList();

    partial void OpenPlatform(VideoSource source, CameraDevice device, int width, int height, int fps)
    {
        _camera = NativeMethods.CameraStart(device.Index, width, height, fps, source.Handle);
        if (_camera == 0)
            throw new WebRtcException($"Failed to open the camera \"{device.Name}\".");
    }

    partial void StopPlatform()
    {
        var camera = Interlocked.Exchange(ref _camera, 0);
        if (camera != 0)
            NativeMethods.CameraStop(camera);
    }
}
