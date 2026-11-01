using Maui.WebRTC.Handlers;

namespace Maui.WebRTC;

/// <summary>Native view that renders a WebRTC video track, keeping its aspect ratio.</summary>
public class VideoView : View
{
    public static readonly BindableProperty TrackProperty =
        BindableProperty.Create(nameof(Track), typeof(VideoTrack), typeof(VideoView));

    /// <summary>The track to show. Null shows nothing.</summary>
    public VideoTrack? Track
    {
        get => (VideoTrack?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    /// <summary>How many frames this view has received; useful to tell "no video" from "black video".</summary>
    public long FramesRendered => (Handler as VideoViewHandler)?.FramesRendered ?? 0;
}
