**English** | [Русский](signaling-aspnetcore.ru.md)

# Connecting through an ASP.NET Core signaling server

Before two devices can connect, they have to exchange an offer, an answer and ICE candidates. A small
[SignalR](https://learn.microsoft.com/aspnet/core/signalr/introduction) hub can relay those messages.
The server never sees any audio or video: once connected, media flows directly between the devices.

```
Device A  <--- SignalR (offer, answer, candidates) --->  ASP.NET Core  <--- SignalR --->  Device B
    ^                                                                                         ^
    +-------------------------------- WebRTC media, directly ---------------------------------+
```

The code below is complete and ready to run:

- server: [`samples/Maui.WebRTC.SignalingServer`](../samples/Maui.WebRTC.SignalingServer)
- client: [`tests/Maui.WebRTC.SignalingSmoke/SignalingClient.cs`](../tests/Maui.WebRTC.SignalingSmoke/SignalingClient.cs)

## 1. The server

Create a project with `dotnet new web`. SignalR is already part of ASP.NET Core.

`Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();
app.MapHub<SignalingHub>("/signaling");
app.Run();
```

Copy [`SignalingHub.cs`](../samples/Maui.WebRTC.SignalingServer/SignalingHub.cs) into the project. It offers two methods to clients:

| Method | Purpose |
|---|---|
| `Join(room)` | Enters a room of at most two peers. The peer already there receives `PeerJoined`. |
| `Signal(type, payload)` | Relays a message to the other peer in the room, as `Signal`. |

When a peer disconnects, the other one receives `PeerLeft`.

Start it so that other devices can reach it:

```powershell
dotnet run --project samples/Maui.WebRTC.SignalingServer --urls http://0.0.0.0:5199
```

## 2. The MAUI app

Install the packages:

```powershell
dotnet add package Maui.WebRTC --prerelease
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

Register the library in `MauiProgram.cs`:

```csharp
builder.UseMauiApp<App>().UseWebRTC();
```

Copy [`SignalingClient.cs`](../tests/Maui.WebRTC.SignalingSmoke/SignalingClient.cs) into your app, and put the views on a page:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:webrtc="clr-namespace:Maui.WebRTC;assembly=Maui.WebRTC"
             x:Class="MyApp.CallPage">
    <Grid RowDefinitions="*,*">
        <webrtc:VideoView x:Name="LocalVideo" />
        <webrtc:VideoView x:Name="RemoteVideo" Grid.Row="1" />
    </Grid>
</ContentPage>
```

Then start the call. Add your tracks **before** joining, because the offer only includes tracks that already exist.

```csharp
using Maui.WebRTC;

public partial class CallPage : ContentPage
{
    PeerConnectionFactory? _factory;
    PeerConnection? _connection;
    VideoSource? _source;
    VideoTrack? _video;
    AudioTrack? _audio;
    CameraCapturer? _camera;
    SignalingClient? _signaling;

    public CallPage() => InitializeComponent();

    public async Task StartCallAsync(string room)
    {
        if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted ||
            await Permissions.RequestAsync<Permissions.Microphone>() != PermissionStatus.Granted)
            return;

        _factory = new PeerConnectionFactory();
        _source = _factory.CreateVideoSource();
        _video = _factory.CreateVideoTrack(_source);
        _audio = _factory.CreateAudioTrack();
        _camera = new CameraCapturer();
        _camera.Start(_source);
        LocalVideo.Track = _video;

        _connection = _factory.CreatePeerConnection();
        _connection.AddTrack(_video);
        _connection.AddTrack(_audio);

        // Events arrive on a background thread, and views may only be touched on the main one.
        _connection.TrackReceived += track =>
        {
            if (track is VideoTrack remote)
                Dispatcher.Dispatch(() => RemoteVideo.Track = remote);
        };

        _signaling = new SignalingClient(new Uri("http://192.168.0.80:5199/signaling"), _connection, room);
        _signaling.PeerLeft += () => Dispatcher.Dispatch(EndCall);
        await _signaling.JoinAsync();
    }

    void EndCall()
    {
        LocalVideo.Track = null;
        RemoteVideo.Track = null;
        _camera?.Dispose();
        _ = _signaling?.DisposeAsync();
        _connection?.Dispose();
        _audio?.Dispose();
        _video?.Dispose();
        _source?.Dispose();
        _factory?.Dispose();
    }
}
```

Call `StartCallAsync` with the same room name on both devices. The peer that joined first makes the offer once the second
one arrives. Everything else happens automatically.

On Android, also add what the [README](../README.md#platforms) lists: the arm64 `RuntimeIdentifier`, the manifest permissions
and `AudioManager.Mode`.

## 3. Reaching the server during development

Use the address of the computer running the server, not `localhost`: on a phone, `localhost` is the phone itself.

- **Android and plain HTTP.** Android blocks unencrypted traffic by default. For development only, add
  `android:usesCleartextTraffic="true"` to `<application>` in `Platforms/Android/AndroidManifest.xml`.
- **Windows Firewall.** Allow the port, from an administrator terminal:
  ```powershell
  New-NetFirewallRule -DisplayName "Maui.WebRTC signaling" -Direction Inbound -Protocol TCP -LocalPort 5199 -Action Allow
  ```
- **Phone on USB.** Instead of the two steps above, you can forward the port and use `http://127.0.0.1:5199/signaling` on the phone.
  The call itself still needs both devices to be on a network where they can reach each other.
  ```powershell
  adb reverse tcp:5199 tcp:5199
  ```

## 4. Going to production

The hub in this repository is a starting point. Before exposing it to the internet:

- **Use HTTPS.** Remove `usesCleartextTraffic` and switch to an `https://` address.
- **Authenticate users.** Anyone who knows a room name can currently join it. Add `[Authorize]` to the hub, decide who may enter
  which room, and pass a token to the client:
  ```csharp
  new SignalingClient(hubUrl, connection, room, accessToken: () => Task.FromResult<string?>(token));
  ```
- **Add STUN and TURN.** The connection above only works when the devices can reach each other directly. Across the internet, use
  STUN, and for networks that block direct connections you need a TURN server with temporary credentials from your server.
  [Running your own coturn](turn.md) covers the setup, the credentials endpoint and a check.
- **Running several server instances?** Rooms are kept in the memory of one process. Move them to shared storage and add a
  [SignalR backplane](https://learn.microsoft.com/aspnet/core/signalr/scale) such as Redis.
- **Behind a reverse proxy,** make sure WebSocket upgrades are passed through.

## Limitations

- A room holds two peers. Group calls need a different design, usually with a media server.
- `SignalingClient` rejoins the room after a lost connection to the signaling server, which doesn't affect a call already running.
  If the other peer leaves, start a new call with a new `PeerConnection`: reusing the old one for a new peer isn't supported.

## Checking that it works

This runs a call between two peers on Windows through a real server, without any UI:

```powershell
dotnet run --project samples/Maui.WebRTC.SignalingServer --urls http://127.0.0.1:5199
# in a second terminal:
dotnet run --project tests/Maui.WebRTC.SignalingSmoke -- http://127.0.0.1:5199/signaling
```

Expected output:

```
call through signaling server OK: hello over webrtc
full room rejected OK
peer left notification OK
```
