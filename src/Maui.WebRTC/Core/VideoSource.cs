using Maui.WebRTC.Native;

namespace Maui.WebRTC;

/// <summary>Feeds frames into video tracks. Frames may be pushed from any thread.</summary>
public sealed unsafe class VideoSource : IDisposable
{
    nint _handle;

    internal VideoSource(nint handle) => _handle = handle;

    /// <summary>Pushes an I420 frame. <paramref name="rotation"/> is how far it must be turned clockwise to display upright.</summary>
    public void PushI420(int width, int height, ReadOnlySpan<byte> y, int yStride, ReadOnlySpan<byte> u, int uStride, ReadOnlySpan<byte> v, int vStride, int rotation = 0)
    {
        fixed (byte* yPointer = y, uPointer = u, vPointer = v)
            PushRaw(width, height, yPointer, yStride, uPointer, uStride, vPointer, vStride, 1, rotation);
    }

    internal void PushRaw(int width, int height, byte* y, int yStride, byte* u, int uStride, byte* v, int vStride, int uvPixelStride, int rotation) =>
        NativeMethods.VideoSourcePush(Handle, width, height, y, yStride, u, uStride, v, vStride, uvPixelStride, rotation);

    internal nint Handle
    {
        get
        {
            var handle = _handle;
            ObjectDisposedException.ThrowIf(handle == 0, this);
            return handle;
        }
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0)
            NativeMethods.VideoSourceDestroy(handle);
    }
}
