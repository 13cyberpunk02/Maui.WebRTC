using System.Runtime.InteropServices;

namespace Maui.WebRTC.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeIceServer
{
    public nint Url;
    public nint Username;
    public nint Password;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativePcCallbacks
{
    public delegate* unmanaged[Cdecl]<nint, int, void> OnSignalingState;
    public delegate* unmanaged[Cdecl]<nint, int, void> OnConnectionState;
    public delegate* unmanaged[Cdecl]<nint, int, void> OnIceGatheringState;
    public delegate* unmanaged[Cdecl]<nint, byte*, int, byte*, void> OnIceCandidate;
    public delegate* unmanaged[Cdecl]<nint, void> OnNegotiationNeeded;
    public delegate* unmanaged[Cdecl]<nint, nint, void> OnDataChannel;
    public delegate* unmanaged[Cdecl]<nint, nint, void> OnTrack;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDcCallbacks
{
    public delegate* unmanaged[Cdecl]<nint, int, void> OnState;
    public delegate* unmanaged[Cdecl]<nint, byte*, nuint, int, void> OnMessage;
}

internal static unsafe partial class NativeMethods
{
    const string Library = "maui_webrtc";

    [LibraryImport(Library, EntryPoint = "mrtc_version")]
    internal static partial nint Version();

    [LibraryImport(Library, EntryPoint = "mrtc_set_log_level")]
    internal static partial void SetLogLevel(int level);

#if ANDROID
    [LibraryImport(Library, EntryPoint = "mrtc_android_set_context")]
    internal static partial void AndroidSetContext(nint applicationContext);
#endif

    [LibraryImport(Library, EntryPoint = "mrtc_factory_create")]
    internal static partial nint FactoryCreate();

    [LibraryImport(Library, EntryPoint = "mrtc_factory_destroy")]
    internal static partial void FactoryDestroy(nint factory);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_create")]
    internal static partial nint PcCreate(nint factory, NativeIceServer* iceServers, int iceServerCount, NativePcCallbacks* callbacks, nint userData);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_destroy")]
    internal static partial void PcDestroy(nint pc);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_create_offer")]
    internal static partial void PcCreateOffer(nint pc, delegate* unmanaged[Cdecl]<nint, byte*, byte*, byte*, void> callback, nint userData);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_create_answer")]
    internal static partial void PcCreateAnswer(nint pc, delegate* unmanaged[Cdecl]<nint, byte*, byte*, byte*, void> callback, nint userData);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_set_local_description", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void PcSetLocalDescription(nint pc, string type, string sdp, delegate* unmanaged[Cdecl]<nint, byte*, void> callback, nint userData);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_set_remote_description", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void PcSetRemoteDescription(nint pc, string type, string sdp, delegate* unmanaged[Cdecl]<nint, byte*, void> callback, nint userData);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_add_ice_candidate", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void PcAddIceCandidate(nint pc, string sdpMid, int sdpMLineIndex, string candidate, delegate* unmanaged[Cdecl]<nint, byte*, void> callback, nint userData);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_get_stats")]
    internal static partial void PcGetStats(nint pc, delegate* unmanaged[Cdecl]<nint, byte*, void> callback, nint userData);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_create_data_channel", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint PcCreateDataChannel(nint pc, string label);

    [LibraryImport(Library, EntryPoint = "mrtc_dc_set_callbacks")]
    internal static partial void DcSetCallbacks(nint channel, NativeDcCallbacks* callbacks, nint userData);

    [LibraryImport(Library, EntryPoint = "mrtc_dc_label")]
    internal static partial nint DcLabel(nint channel);

    [LibraryImport(Library, EntryPoint = "mrtc_dc_state")]
    internal static partial int DcState(nint channel);

    [LibraryImport(Library, EntryPoint = "mrtc_dc_send")]
    internal static partial int DcSend(nint channel, byte* data, nuint size, int isBinary);

    [LibraryImport(Library, EntryPoint = "mrtc_dc_close")]
    internal static partial void DcClose(nint channel);

    [LibraryImport(Library, EntryPoint = "mrtc_dc_destroy")]
    internal static partial void DcDestroy(nint channel);

    [LibraryImport(Library, EntryPoint = "mrtc_string_free")]
    internal static partial void StringFree(nint value);

    [LibraryImport(Library, EntryPoint = "mrtc_video_source_create")]
    internal static partial nint VideoSourceCreate();

    [LibraryImport(Library, EntryPoint = "mrtc_video_source_destroy")]
    internal static partial void VideoSourceDestroy(nint source);

    [LibraryImport(Library, EntryPoint = "mrtc_video_source_push")]
    internal static partial void VideoSourcePush(nint source, int width, int height, byte* y, int yStride, byte* u, int uStride, byte* v, int vStride, int uvPixelStride, int rotation);

    [LibraryImport(Library, EntryPoint = "mrtc_factory_create_audio_track", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint FactoryCreateAudioTrack(nint factory, string label);

    [LibraryImport(Library, EntryPoint = "mrtc_factory_create_video_track", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint FactoryCreateVideoTrack(nint factory, nint source, string label);

    [LibraryImport(Library, EntryPoint = "mrtc_track_kind")]
    internal static partial int TrackKind(nint track);

    [LibraryImport(Library, EntryPoint = "mrtc_track_set_enabled")]
    internal static partial void TrackSetEnabled(nint track, int enabled);

    [LibraryImport(Library, EntryPoint = "mrtc_track_destroy")]
    internal static partial void TrackDestroy(nint track);

    [LibraryImport(Library, EntryPoint = "mrtc_pc_add_track", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int PcAddTrack(nint pc, nint track, string streamId);

    [LibraryImport(Library, EntryPoint = "mrtc_renderer_create")]
    internal static partial nint RendererCreate();

    [LibraryImport(Library, EntryPoint = "mrtc_renderer_destroy")]
    internal static partial void RendererDestroy(nint renderer);

    [LibraryImport(Library, EntryPoint = "mrtc_renderer_set_track")]
    internal static partial void RendererSetTrack(nint renderer, nint track);

    [LibraryImport(Library, EntryPoint = "mrtc_renderer_frames")]
    internal static partial long RendererFrames(nint renderer);

#if ANDROID
    [LibraryImport(Library, EntryPoint = "mrtc_renderer_set_surface")]
    internal static partial void RendererSetSurface(nint renderer, nint surface);
#else
    [LibraryImport(Library, EntryPoint = "mrtc_renderer_get_swap_chain")]
    internal static partial nint RendererGetSwapChain(nint renderer);

    [LibraryImport(Library, EntryPoint = "mrtc_renderer_set_size")]
    internal static partial void RendererSetSize(nint renderer, int width, int height, float scale);

    [LibraryImport(Library, EntryPoint = "mrtc_camera_count")]
    internal static partial int CameraCount();

    [LibraryImport(Library, EntryPoint = "mrtc_camera_start")]
    internal static partial nint CameraStart(int deviceIndex, int width, int height, int fps, nint source);

    [LibraryImport(Library, EntryPoint = "mrtc_camera_stop")]
    internal static partial void CameraStop(nint camera);
#endif
}
