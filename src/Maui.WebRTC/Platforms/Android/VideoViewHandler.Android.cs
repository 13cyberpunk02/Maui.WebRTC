using Android.Runtime;
using Android.Views;
using Maui.WebRTC.Native;
using Microsoft.Maui.Handlers;

namespace Maui.WebRTC.Handlers;

public partial class VideoViewHandler : ViewHandler<VideoView, SurfaceView>
{
    SurfaceCallback? _surfaceCallback;

    protected override SurfaceView CreatePlatformView() => new(Context);

    protected override void ConnectHandler(SurfaceView platformView)
    {
        base.ConnectHandler(platformView);

        // The renderer creates the JVM-side state, so the native library has to be loaded first.
        PeerConnectionFactory.EnsureLoaded();
        _renderer = new VideoRenderer();
        _surfaceCallback = new SurfaceCallback(_renderer);
        platformView.Holder!.AddCallback(_surfaceCallback);
    }

    protected override void DisconnectHandler(SurfaceView platformView)
    {
        platformView.Holder!.RemoveCallback(_surfaceCallback);
        _surfaceCallback?.Dispose();
        _surfaceCallback = null;
        _renderer?.Dispose();
        _renderer = null;

        base.DisconnectHandler(platformView);
    }

    sealed class SurfaceCallback(VideoRenderer renderer) : Java.Lang.Object, ISurfaceHolderCallback
    {
        public void SurfaceCreated(ISurfaceHolder holder) =>
            NativeMethods.RendererSetSurface(renderer.Handle, holder.Surface!.Handle);

        public void SurfaceChanged(ISurfaceHolder holder, [GeneratedEnum] global::Android.Graphics.Format format, int width, int height)
        {
            // The renderer reads the window size on every frame.
        }

        public void SurfaceDestroyed(ISurfaceHolder holder) =>
            NativeMethods.RendererSetSurface(renderer.Handle, 0);
    }
}
