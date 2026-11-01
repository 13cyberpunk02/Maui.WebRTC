using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Maui.WebRTC.Native;

namespace Maui.WebRTC;

/// <summary>
/// A channel for arbitrary data. Events are delivered through the owning connection's queue, so
/// subscribe straight after obtaining the channel; earlier events are not replayed.
/// </summary>
public sealed unsafe class DataChannel : IDisposable
{
    readonly PeerConnection _owner;
    readonly GCHandle _self;
    nint _handle;

    public event Action<DataChannelState>? StateChanged;
    public event Action<DataChannelMessage>? MessageReceived;

    internal DataChannel(nint handle, PeerConnection owner)
    {
        _handle = handle;
        _owner = owner;
        _self = GCHandle.Alloc(this);

        var label = NativeMethods.DcLabel(handle);
        Label = Marshal.PtrToStringUTF8(label) ?? string.Empty;
        NativeMethods.StringFree(label);

        var callbacks = new NativeDcCallbacks { OnState = &OnState, OnMessage = &OnMessage };
        NativeMethods.DcSetCallbacks(handle, &callbacks, GCHandle.ToIntPtr(_self));
    }

    public string Label { get; }

    public DataChannelState State => (DataChannelState)NativeMethods.DcState(Handle);

    public bool Send(string text) => Send(Encoding.UTF8.GetBytes(text), isBinary: false);

    public bool Send(ReadOnlySpan<byte> data) => Send(data, isBinary: true);

    public void Close() => NativeMethods.DcClose(Handle);

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle == 0)
            return;

        _owner.Untrack(this);
        // Blocks until no callback can fire any more, which makes freeing the handle safe.
        NativeMethods.DcDestroy(handle);
        _self.Free();
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

    bool Send(ReadOnlySpan<byte> data, bool isBinary)
    {
        fixed (byte* pointer = data)
            return NativeMethods.DcSend(Handle, pointer, (nuint)data.Length, isBinary ? 1 : 0) != 0;
    }

    static DataChannel From(nint userData) => (DataChannel)GCHandle.FromIntPtr(userData).Target!;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnState(nint userData, int state)
    {
        var self = From(userData);
        self._owner.Events.Post(() => self.StateChanged?.Invoke((DataChannelState)state));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static void OnMessage(nint userData, byte* data, nuint size, int isBinary)
    {
        var self = From(userData);
        var message = new DataChannelMessage(new ReadOnlySpan<byte>(data, (int)size).ToArray(), isBinary != 0);
        self._owner.Events.Post(() => self.MessageReceived?.Invoke(message));
    }
}
