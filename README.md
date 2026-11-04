**English** | [Русский](https://github.com/13cyberpunk02/Maui.WebRTC/blob/main/README.ru.md)

# Maui.WebRTC

Native WebRTC for .NET MAUI on Android and Windows, without a WebView: a C# API and a XAML `VideoView`
over Google's libwebrtc.

> Status: preview. Calls between a phone and a PC have been tested on a local network, and connections through a TURN
> server in a local test; calls between devices on different networks over the internet have not been tested yet.

## Platforms

| Platform | Architectures | Notes |
|---|---|---|
| Android 7.0+ (API 24) | arm64-v8a | x86_64 emulators are not supported: the prebuilt libwebrtc has no such build |
| Windows 10 1809+ | x64 | |

An Android app must build for arm64 only:

```xml
<RuntimeIdentifier Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">android-arm64</RuntimeIdentifier>
```

It also needs `CAMERA`, `RECORD_AUDIO`, `MODIFY_AUDIO_SETTINGS`, `INTERNET` and `ACCESS_NETWORK_STATE` in its manifest,
must request the first two at runtime, and should set `AudioManager.Mode = Mode.InCommunication` during calls.

## Usage

```csharp
builder.UseMauiApp<App>().UseWebRTC();
```

```xml
<ContentPage xmlns:webrtc="clr-namespace:Maui.WebRTC;assembly=Maui.WebRTC">
    <webrtc:VideoView x:Name="RemoteVideo" />
</ContentPage>
```

```csharp
var factory = new PeerConnectionFactory();
var source = factory.CreateVideoSource();
var video = factory.CreateVideoTrack(source);
var audio = factory.CreateAudioTrack();
var camera = new CameraCapturer();
camera.Start(source);

var connection = factory.CreatePeerConnection([new IceServer("stun:stun.example.org:3478")]);
connection.AddTrack(video);
connection.AddTrack(audio);
connection.IceCandidateGathered += candidate => { /* send to the other peer */ };
connection.TrackReceived += track =>
{
    if (track is VideoTrack remote)
        MainThread.BeginInvokeOnMainThread(() => RemoteVideo.Track = remote);
};

var offer = await connection.CreateOfferAsync();
// Send the offer to the other peer, then:
await connection.SetLocalDescriptionAsync(offer);
```

Events are raised in order on a background thread, so handlers must switch to the main thread to touch the UI.

## Signaling

WebRTC does not define how peers exchange offers, answers and candidates: that is up to the app. Deliver them in order
and set the remote description before adding candidates, or `AddIceCandidateAsync` fails.

**[Connecting through an ASP.NET Core signaling server](https://github.com/13cyberpunk02/Maui.WebRTC/blob/main/docs/signaling-aspnetcore.md)** is the recommended way.

For calls over the internet you also need a TURN server: **[running your own coturn](https://github.com/13cyberpunk02/Maui.WebRTC/blob/main/docs/turn.md)**.

`samples/Maui.WebRTC.Sample` also shows a complete call over plain TCP, which is unauthenticated and only for trusted networks.

## Codecs

The prebuilt libwebrtc comes without H.264 and H.265, so calls use VP8, VP9 or AV1, encoded in software.

## Building

Needs Visual Studio with the C++ tools, CMake, the Android NDK and the .NET MAUI workloads. Run in order:

```powershell
./scripts/fetch-webrtc.ps1
./scripts/fetch-libcxx.ps1
./scripts/fetch-clang.ps1
./scripts/build-native-windows.ps1
./scripts/build-native-android.ps1
dotnet pack src/Maui.WebRTC/Maui.WebRTC.csproj -c Release -o artifacts/packages
```

`fetch-clang.ps1` downloads the exact Chromium clang that built libwebrtc, and the native build then runs it, because the
C++ layer must match the prebuilt library's ABI. `native/CMakeLists.txt` records why each flag is needed.

`tests/Maui.WebRTC.NativeSmoke` checks data channels and video on Windows without any UI, and
`tests/Maui.WebRTC.SignalingSmoke` runs a call through the signaling server, and `tests/Maui.WebRTC.TurnSmoke` checks that
a connection gets through a TURN server.

## License

[MIT](https://github.com/13cyberpunk02/Maui.WebRTC/blob/main/LICENSE) © 13CyberPunk02. The binaries also contain libwebrtc and its dependencies under their own licenses;
the package includes them in `licenses/`.
