using Microsoft.Maui.Handlers;

namespace Maui.WebRTC.Handlers;

public partial class VideoViewHandler
{
    public static readonly IPropertyMapper<VideoView, VideoViewHandler> Mapper =
        new PropertyMapper<VideoView, VideoViewHandler>(ViewHandler.ViewMapper)
        {
            // WinUI's SwapChainPanel throws if Background is set; video covers the whole surface anyway.
            [nameof(IView.Background)] = static (_, _) => { },
            [nameof(VideoView.Track)] = static (handler, view) => handler._renderer?.SetTrack(view.Track),
        };

    VideoRenderer? _renderer;

    public VideoViewHandler() : base(Mapper)
    {
    }

    internal long FramesRendered => _renderer?.FramesRendered ?? 0;
}
