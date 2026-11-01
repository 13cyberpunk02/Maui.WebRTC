using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Maui.WebRTC.Native;

namespace Maui.WebRTC;

/// <summary>
/// A WebRTC connection to a remote peer. Events are raised in order on a background thread,
/// never on libwebrtc's own threads and not on the UI thread.
/// </summary>
public sealed unsafe class PeerConnection : IDisposable
{
    readonly EventQueue _events = new();
    readonly List<DataChannel> _channels = [];
    readonly List<MediaTrack> _remoteTracks = [];
    readonly GCHandle _self;
    nint _handle;

    public event Action<SignalingState>? SignalingStateChanged;
    public event Action<PeerConnectionState>? ConnectionStateChanged;
    public event Action<IceGatheringState>? IceGatheringStateChanged;
    public event Action<IceCandidate>? IceCandidateGathered;
    public event Action? NegotiationNeeded;
    public event Action<DataChannel>? DataChannelReceived;
    /// <summary>A remote track. It is disposed together with this connection.</summary>
    public event Action<MediaTrack>? TrackReceived;

    internal PeerConnection(nint factory, IceServer[] iceServers)
    {
        _self = GCHandle.Alloc(this);

        var natives = new NativeIceServer[iceServers.Length];
        try
        {
            for (var i = 0; i < iceServers.Length; i++)
            {
                natives[i].Url = Marshal.StringToCoTaskMemUTF8(iceServers[i].Url);
                natives[i].Username = Marshal.StringToCoTaskMemUTF8(iceServers[i].Username);
                natives[i].Password = Marshal.StringToCoTaskMemUTF8(iceServers[i].Password);
            }

            var callbacks = new NativePcCallbacks
            {
                OnSignalingState = &OnSignalingState,
                OnConnectionState = &OnConnectionState,
                OnIceGatheringState = &OnIceGatheringState,
                OnIceCandidate = &OnIceCandidate,
                OnNegotiationNeeded = &OnNegotiationNeeded,
                OnDataChannel = &OnDataChannel,
                OnTrack = &OnTrack,
            };

            fixed (NativeIceServer* servers = natives)
            {
                _handle = NativeMethods.PcCreate(factory, servers, natives.Length, &callbacks, GCHandle.ToIntPtr(_self));
            }
        }
        finally
        {
            foreach (var native in natives)
            {
                Marshal.FreeCoTaskMem(native.Url);
                Marshal.FreeCoTaskMem(native.Username);
                Marshal.FreeCoTaskMem(native.Password);
            }
        }

        if (_handle == 0)
        {
            _self.Free();
            _events.Complete();
            throw new WebRtcException("Failed to create the native PeerConnection. Check the ICE server URLs.");
        }
    }

    internal EventQueue Events => _events;

    public Task<SessionDescription> CreateOfferAsync()
    {
        var pending = BeginSdpOperation(out var userData);
        NativeMethods.PcCreateOffer(Handle, &OnSdpResult, userData);
        return pending;
    }

    public Task<SessionDescription> CreateAnswerAsync()
    {
        var pending = BeginSdpOperation(out var userData);
        NativeMethods.PcCreateAnswer(Handle, &OnSdpResult, userData);
        return pending;
    }

    public Task SetLocalDescriptionAsync(SessionDescription description)
    {
        var pending = BeginOperation(out var userData);
        NativeMethods.PcSetLocalDescription(Handle, description.Type, description.Sdp, &OnResult, userData);
        return pending;
    }

    public Task SetRemoteDescriptionAsync(SessionDescription description)
    {
        var pending = BeginOperation(out var userData);
        NativeMethods.PcSetRemoteDescription(Handle, description.Type, description.Sdp, &OnResult, userData);
        return pending;
    }

    /// <summary>The remote description must already be set, otherwise the task fails.</summary>
    public Task AddIceCandidateAsync(IceCandidate candidate)
    {
        var pending = BeginOperation(out var userData);
        NativeMethods.PcAddIceCandidate(Handle, candidate.SdpMid, candidate.SdpMLineIndex, candidate.Candidate, &OnResult, userData);
        return pending;
    }

    public DataChannel CreateDataChannel(string label)
    {
        var native = NativeMethods.PcCreateDataChannel(Handle, label);
        if (native == 0)
            throw new WebRtcException($"Failed to create data channel '{label}'.");
        return Track(native);
    }

    /// <summary>Returns the standard WebRTC stats report (packets, audio level, chosen route...) as a JSON array.</summary>
    public Task<string> GetStatsJsonAsync()
    {
        var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        NativeMethods.PcGetStats(Handle, &OnStats, GCHandle.ToIntPtr(GCHandle.Alloc(source)));
        return source.Task;
    }

    /// <summary>Sends a local track to the remote peer. Takes effect with the next offer or answer.</summary>
    public void AddTrack(MediaTrack track, string streamId = "stream")
    {
        if (NativeMethods.PcAddTrack(Handle, track.Handle, streamId) == 0)
            throw new WebRtcException("Failed to add the track.");
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle == 0)
            return;

        DataChannel[] channels;
        lock (_channels)
            channels = [.. _channels];
        foreach (var channel in channels)
            channel.Dispose();

        // Blocks until no callback can fire any more, which makes freeing the handle safe.
        NativeMethods.PcDestroy(handle);
        _self.Free();
        _events.Complete();

        foreach (var track in _remoteTracks)
            track.Dispose();
    }

    nint Handle
    {
        get
        {
            var handle = _handle;
            ObjectDisposedException.ThrowIf(handle == 0, this);
            return handle;
        }
    }

    DataChannel Track(nint native)
    {
        var channel = new DataChannel(native, this);
        lock (_channels)
            _channels.Add(channel);
        return channel;
    }

    internal void Untrack(DataChannel channel)
    {
        lock (_channels)
            _channels.Remove(channel);
    }

    static Task<SessionDescription> BeginSdpOperation(out nint userData)
    {
        var source = new TaskCompletionSource<SessionDescription>(TaskCreationOptions.RunContinuationsAsynchronously);
        userData = GCHandle.ToIntPtr(GCHandle.Alloc(source));
        return source.Task;
    }

    static Task BeginOperation(out nint userData)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        userData = GCHandle.ToIntPtr(GCHandle.Alloc(source));
        return source.Task;
    }

    static PeerConnection From(nint userData) => (PeerConnection)GCHandle.FromIntPtr(userData).Target!;

    static string ToManaged(byte* value) => Marshal.PtrToStringUTF8((nint)value) ?? string.Empty;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnSignalingState(nint userData, int state)
    {
        var self = From(userData);
        self._events.Post(() => self.SignalingStateChanged?.Invoke((SignalingState)state));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnConnectionState(nint userData, int state)
    {
        var self = From(userData);
        self._events.Post(() => self.ConnectionStateChanged?.Invoke((PeerConnectionState)state));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnIceGatheringState(nint userData, int state)
    {
        var self = From(userData);
        self._events.Post(() => self.IceGatheringStateChanged?.Invoke((IceGatheringState)state));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnIceCandidate(nint userData, byte* sdpMid, int sdpMLineIndex, byte* candidate)
    {
        var self = From(userData);
        var value = new IceCandidate(ToManaged(sdpMid), sdpMLineIndex, ToManaged(candidate));
        self._events.Post(() => self.IceCandidateGathered?.Invoke(value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnNegotiationNeeded(nint userData)
    {
        var self = From(userData);
        self._events.Post(() => self.NegotiationNeeded?.Invoke());
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnDataChannel(nint userData, nint native)
    {
        var self = From(userData);
        var channel = self.Track(native);
        self._events.Post(() => self.DataChannelReceived?.Invoke(channel));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnTrack(nint userData, nint native)
    {
        var self = From(userData);
        var track = MediaTrack.FromNative(native);
        self._remoteTracks.Add(track);
        self._events.Post(() => self.TrackReceived?.Invoke(track));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnStats(nint userData, byte* json)
    {
        var handle = GCHandle.FromIntPtr(userData);
        var source = (TaskCompletionSource<string>)handle.Target!;
        handle.Free();
        source.SetResult(ToManaged(json));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnSdpResult(nint userData, byte* type, byte* sdp, byte* error)
    {
        var handle = GCHandle.FromIntPtr(userData);
        var source = (TaskCompletionSource<SessionDescription>)handle.Target!;
        handle.Free();

        if (error != null)
            source.SetException(new WebRtcException(ToManaged(error)));
        else
            source.SetResult(new SessionDescription(ToManaged(type), ToManaged(sdp)));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnResult(nint userData, byte* error)
    {
        var handle = GCHandle.FromIntPtr(userData);
        var source = (TaskCompletionSource)handle.Target!;
        handle.Free();

        if (error != null)
            source.SetException(new WebRtcException(ToManaged(error)));
        else
            source.SetResult();
    }
}
