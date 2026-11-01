using System.Runtime.InteropServices;
using Maui.WebRTC.Native;

namespace Maui.WebRTC;

/// <summary>Owns the libwebrtc threads and media engine. Create one per application.</summary>
public sealed class PeerConnectionFactory : IDisposable
{
    nint _handle;

    public PeerConnectionFactory()
    {
        EnsureNativeLibraryLoaded();

        _handle = NativeMethods.FactoryCreate();
        if (_handle == 0)
            throw new WebRtcException("Failed to create the native PeerConnectionFactory.");
    }

    /// <summary>Version of libwebrtc this package was built against.</summary>
    public static string NativeVersion
    {
        get
        {
            EnsureNativeLibraryLoaded();
            return Marshal.PtrToStringUTF8(NativeMethods.Version())!;
        }
    }

    /// <summary>Sets libwebrtc's own logging, written to stderr (logcat on Android).</summary>
    public static void SetNativeLogLevel(NativeLogLevel level)
    {
        EnsureNativeLibraryLoaded();
        NativeMethods.SetLogLevel((int)level);
    }

    public PeerConnection CreatePeerConnection(IEnumerable<IceServer>? iceServers = null)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        return new PeerConnection(_handle, iceServers?.ToArray() ?? []);
    }

    public VideoSource CreateVideoSource()
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        return new VideoSource(NativeMethods.VideoSourceCreate());
    }

    public VideoTrack CreateVideoTrack(VideoSource source, string label = "video")
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        var track = NativeMethods.FactoryCreateVideoTrack(_handle, source.Handle, label);
        return track != 0 ? new VideoTrack(track) : throw new WebRtcException("Failed to create the video track.");
    }

    /// <summary>Creates a track capturing the microphone. Requires the RECORD_AUDIO permission on Android.</summary>
    public AudioTrack CreateAudioTrack(string label = "audio")
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        var track = NativeMethods.FactoryCreateAudioTrack(_handle, label);
        return track != 0 ? new AudioTrack(track) : throw new WebRtcException("Failed to create the audio track.");
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0)
            NativeMethods.FactoryDestroy(handle);
    }

    internal static void EnsureLoaded() => EnsureNativeLibraryLoaded();

    static void EnsureNativeLibraryLoaded() => NativeLibraryLoader.Value.ToString();

    static readonly Lazy<bool> NativeLibraryLoader = new(() =>
    {
#if ANDROID
        // P/Invoke's dlopen never runs JNI_OnLoad, and libwebrtc needs it to set up JNI.
        Java.Lang.JavaSystem.LoadLibrary("maui_webrtc");
        NativeMethods.AndroidSetContext(global::Android.App.Application.Context.Handle);
#endif
        return true;
    });
}
