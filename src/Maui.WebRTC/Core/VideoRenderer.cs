using Maui.WebRTC.Native;

namespace Maui.WebRTC;

internal sealed class VideoRenderer : IDisposable
{
    nint _handle = NativeMethods.RendererCreate();

    internal nint Handle => _handle;

    public long FramesRendered => _handle == 0 ? 0 : NativeMethods.RendererFrames(_handle);

    public void SetTrack(VideoTrack? track) => NativeMethods.RendererSetTrack(_handle, track?.Handle ?? 0);

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0)
            NativeMethods.RendererDestroy(handle);
    }
}
