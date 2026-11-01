using Maui.WebRTC.Native;

namespace Maui.WebRTC;

/// <summary>An audio or video track, either created locally or received from the remote peer.</summary>
public abstract class MediaTrack : IDisposable
{
    bool _enabled = true;
    nint _handle;

    private protected MediaTrack(nint handle) => _handle = handle;

    /// <summary>A disabled track sends silence or black frames.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            NativeMethods.TrackSetEnabled(Handle, value ? 1 : 0);
            _enabled = value;
        }
    }

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
            NativeMethods.TrackDestroy(handle);
    }

    internal static MediaTrack FromNative(nint handle) =>
        NativeMethods.TrackKind(handle) == 1 ? new VideoTrack(handle) : new AudioTrack(handle);
}

public sealed class AudioTrack : MediaTrack
{
    internal AudioTrack(nint handle) : base(handle)
    {
    }
}

public sealed class VideoTrack : MediaTrack
{
    internal VideoTrack(nint handle) : base(handle)
    {
    }
}
