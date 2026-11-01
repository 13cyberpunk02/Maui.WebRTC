using Maui.WebRTC.Handlers;

namespace Maui.WebRTC;

public static class AppHostBuilderExtensions
{
    public static MauiAppBuilder UseWebRTC(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<VideoView, VideoViewHandler>());
        return builder;
    }
}
