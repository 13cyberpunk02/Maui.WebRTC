using System.Runtime.InteropServices;
using Maui.WebRTC.Native;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;

namespace Maui.WebRTC.Handlers;

public partial class VideoViewHandler : ViewHandler<VideoView, SwapChainPanel>
{
    // ISwapChainPanelNative from microsoft.ui.xaml.media.dxinterop.h
    static readonly Guid SwapChainPanelNativeId = new("63aad0b8-7c24-40ff-85a8-640d944cc325");

    protected override SwapChainPanel CreatePlatformView() => new();

    protected override void ConnectHandler(SwapChainPanel platformView)
    {
        base.ConnectHandler(platformView);

        _renderer = new VideoRenderer();
        SetSwapChain(platformView, NativeMethods.RendererGetSwapChain(_renderer.Handle));
        platformView.SizeChanged += OnPanelSizeChanged;
        platformView.CompositionScaleChanged += OnPanelScaleChanged;
    }

    protected override void DisconnectHandler(SwapChainPanel platformView)
    {
        platformView.SizeChanged -= OnPanelSizeChanged;
        platformView.CompositionScaleChanged -= OnPanelScaleChanged;
        SetSwapChain(platformView, 0);
        _renderer?.Dispose();
        _renderer = null;

        base.DisconnectHandler(platformView);
    }

    void OnPanelSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) => UpdateRendererSize((SwapChainPanel)sender);

    void OnPanelScaleChanged(SwapChainPanel sender, object args) => UpdateRendererSize(sender);

    void UpdateRendererSize(SwapChainPanel panel)
    {
        if (_renderer is null)
            return;

        var scale = panel.CompositionScaleX;
        NativeMethods.RendererSetSize(
            _renderer.Handle,
            (int)Math.Ceiling(panel.ActualWidth * scale),
            (int)Math.Ceiling(panel.ActualHeight * scale),
            scale);
    }

    static unsafe void SetSwapChain(SwapChainPanel panel, nint swapChain)
    {
        var unknown = ((WinRT.IWinRTObject)panel).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in SwapChainPanelNativeId, out var native));
        try
        {
            // SetSwapChain is the first method after IUnknown's three.
            var vtable = *(nint**)native;
            var setSwapChain = (delegate* unmanaged[Stdcall]<nint, nint, int>)vtable[3];
            Marshal.ThrowExceptionForHR(setSwapChain(native, swapChain));
        }
        finally
        {
            Marshal.Release(native);
        }
    }
}
