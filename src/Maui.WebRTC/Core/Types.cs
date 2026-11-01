namespace Maui.WebRTC;

public sealed record IceServer(string Url, string? Username = null, string? Password = null);

public sealed record SessionDescription(string Type, string Sdp);

public sealed record IceCandidate(string SdpMid, int SdpMLineIndex, string Candidate);

// Values of these enums match libwebrtc's; the native layer passes them through unchanged.
public enum SignalingState
{
    Stable,
    HaveLocalOffer,
    HaveLocalPrAnswer,
    HaveRemoteOffer,
    HaveRemotePrAnswer,
    Closed,
}

public enum PeerConnectionState
{
    New,
    Connecting,
    Connected,
    Disconnected,
    Failed,
    Closed,
}

public enum IceGatheringState
{
    New,
    Gathering,
    Complete,
}

public enum DataChannelState
{
    Connecting,
    Open,
    Closing,
    Closed,
}

public enum NativeLogLevel
{
    Verbose,
    Info,
    Warning,
    Error,
    None,
}

public readonly record struct DataChannelMessage(byte[] Data, bool IsBinary)
{
    public string Text => System.Text.Encoding.UTF8.GetString(Data);
}

public sealed class WebRtcException(string message) : Exception(message);
