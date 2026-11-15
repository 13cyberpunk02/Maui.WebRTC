using System.Runtime.InteropServices;
using Maui.WebRTC.Native;

namespace Maui.WebRTC;

/// <summary>How a <see cref="PeerConnectionFactory"/> captures and cleans up audio.</summary>
public sealed class PeerConnectionFactoryOptions
{
    /// <summary>
    /// Android: use the phone's built-in echo canceller instead of libwebrtc's own. Off by default:
    /// built-in ones differ a lot between phones and many let the other side hear themselves, while
    /// libwebrtc's (AEC3) behaves the same on every device. Ignored on other platforms.
    /// </summary>
    public bool UseHardwareEchoCanceller { get; init; }

    /// <summary>Android: likewise for noise suppression. Off by default. Ignored on other platforms.</summary>
    public bool UseHardwareNoiseSuppressor { get; init; }
}

/// <summary>Owns the libwebrtc threads and media engine. Create one per application.</summary>
public sealed class PeerConnectionFactory : IDisposable
{
    nint _handle;
#if ANDROID
    AndroidAudioDevice? _audioDevice;
#endif

    public PeerConnectionFactory() : this(null)
    {
    }

    public PeerConnectionFactory(PeerConnectionFactoryOptions? options)
    {
        EnsureNativeLibraryLoaded();
        options ??= new PeerConnectionFactoryOptions();

#if ANDROID
        _audioDevice = AndroidAudioDevice.Create(options);
        _handle = NativeMethods.FactoryCreateWithAudioDevice(_audioDevice.Handle);
        if (_handle == 0)
            _audioDevice.Dispose();
#else
        _handle = NativeMethods.FactoryCreate();
#endif
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

    /// <summary>
    /// Windows: the speakers and headphones calls can be heard on, as the system names them. Empty on Android,
    /// where the app routes audio with AudioManager (speakerphone, earpiece, Bluetooth) instead.
    /// </summary>
    public IReadOnlyList<AudioDevice> AudioOutputs
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            var count = Math.Max(0, NativeMethods.FactoryAudioOutputCount(_handle));
            var devices = new List<AudioDevice>(count);
            for (var i = 0; i < count; i++)
            {
                if (NativeMethods.TakeString(NativeMethods.FactoryAudioOutputName(_handle, i)) is { } name)
                    devices.Add(new AudioDevice(i, name));
            }
            return devices;
        }
    }

    /// <summary>
    /// Windows: plays calls on <paramref name="device"/>, or on the system's default communications device for null.
    /// Works during a call. Returns false where it isn't supported or the device refused.
    /// </summary>
    public bool SetAudioOutput(AudioDevice? device)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        return NativeMethods.FactorySetAudioOutput(_handle, device?.Index ?? -1) == 0;
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0)
            NativeMethods.FactoryDestroy(handle);
#if ANDROID
        // After the factory: the native side holds its own reference to the device for as long as it needs it.
        Interlocked.Exchange(ref _audioDevice, null)?.Dispose();
#endif
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
