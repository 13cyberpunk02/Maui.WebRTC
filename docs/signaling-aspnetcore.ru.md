[English](signaling-aspnetcore.md) | **Русский**

# Подключение через сигнальный сервер на ASP.NET Core

Прежде чем два устройства смогут соединиться, они должны обменяться offer, answer и ICE-кандидатами. Передавать эти сообщения может
небольшой хаб [SignalR](https://learn.microsoft.com/aspnet/core/signalr/introduction).
Аудио и видео через сервер не идут: после соединения медиа передаётся напрямую между устройствами.

```
Устройство A  <--- SignalR (offer, answer, кандидаты) --->  ASP.NET Core  <--- SignalR --->  Устройство B
      ^                                                                                           ^
      +------------------------------ медиа WebRTC, напрямую -------------------------------------+
```

Приведённый ниже код полный и готов к запуску:

- сервер: [`samples/Maui.WebRTC.SignalingServer`](../samples/Maui.WebRTC.SignalingServer)
- клиент: [`tests/Maui.WebRTC.SignalingSmoke/SignalingClient.cs`](../tests/Maui.WebRTC.SignalingSmoke/SignalingClient.cs)

## 1. Сервер

Создайте проект командой `dotnet new web`. SignalR уже входит в ASP.NET Core.

`Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();
app.MapHub<SignalingHub>("/signaling");
app.Run();
```

Скопируйте в проект [`SignalingHub.cs`](../samples/Maui.WebRTC.SignalingServer/SignalingHub.cs). Для клиентов в нём есть два метода:

| Метод | Назначение |
|---|---|
| `Join(room)` | Вход в комнату максимум на двух участников. Тот, кто уже внутри, получает `PeerJoined`. |
| `Signal(type, payload)` | Пересылает сообщение второму участнику комнаты как `Signal`. |

Когда участник отключается, второй получает `PeerLeft`.

Запустите сервер так, чтобы он был доступен с других устройств:

```powershell
dotnet run --project samples/Maui.WebRTC.SignalingServer --urls http://0.0.0.0:5199
```

## 2. Приложение MAUI

Установите пакеты:

```powershell
dotnet add package Maui.WebRTC --prerelease
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

Подключите библиотеку в `MauiProgram.cs`:

```csharp
builder.UseMauiApp<App>().UseWebRTC();
```

Скопируйте в приложение [`SignalingClient.cs`](../tests/Maui.WebRTC.SignalingSmoke/SignalingClient.cs) и добавьте контролы на страницу:

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

Затем начните звонок. Добавляйте треки **до** входа в комнату: в offer попадают только уже существующие треки.

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

        // События приходят в фоновом потоке, а с контролами можно работать только в главном.
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

Вызовите `StartCallAsync` с одним и тем же именем комнаты на обоих устройствах. Тот, кто вошёл первым, сделает offer, когда появится
второй. Всё остальное происходит автоматически.

На Android добавьте также то, что перечислено в [README](../README.ru.md#платформы): `RuntimeIdentifier` для arm64, разрешения в манифесте
и `AudioManager.Mode`.

## 3. Как достучаться до сервера при разработке

Указывайте адрес компьютера, на котором запущен сервер, а не `localhost`: на телефоне `localhost` — это сам телефон.

- **Android и обычный HTTP.** По умолчанию Android блокирует незашифрованный трафик. Только для разработки добавьте
  `android:usesCleartextTraffic="true"` в `<application>` в файле `Platforms/Android/AndroidManifest.xml`.
- **Брандмауэр Windows.** Откройте порт в терминале с правами администратора:
  ```powershell
  New-NetFirewallRule -DisplayName "Maui.WebRTC signaling" -Direction Inbound -Protocol TCP -LocalPort 5199 -Action Allow
  ```
- **Телефон по USB.** Вместо двух шагов выше можно пробросить порт и указать на телефоне `http://127.0.0.1:5199/signaling`.
  Самому звонку всё равно нужно, чтобы устройства были в сети, где они видят друг друга.
  ```powershell
  adb reverse tcp:5199 tcp:5199
  ```

## 4. Перед выходом в продакшен

Хаб из этого репозитория — отправная точка. Прежде чем открывать его в интернет:

- **Используйте HTTPS.** Уберите `usesCleartextTraffic` и перейдите на адрес `https://`.
- **Проверяйте пользователей.** Сейчас войти в комнату может любой, кто знает её имя. Добавьте хабу `[Authorize]`, решите, кому в какую
  комнату можно, и передавайте токен в клиент:
  ```csharp
  new SignalingClient(hubUrl, connection, room, accessToken: () => Task.FromResult<string?>(token));
  ```
- **Добавьте STUN и TURN.** Код выше работает, только когда устройства видят друг друга напрямую. Через интернет используйте STUN, а для сетей,
  которые блокируют прямые соединения, нужен TURN-сервер с временными паролями от вашего сервера. Развёртывание, эндпоинт
  паролей и проверка описаны в гайде [Свой TURN-сервер](turn.ru.md).
- **Несколько экземпляров сервера?** Комнаты хранятся в памяти одного процесса. Перенесите их в общее хранилище и добавьте
  [backplane для SignalR](https://learn.microsoft.com/aspnet/core/signalr/scale), например Redis.
- **За обратным прокси** убедитесь, что проходят WebSocket-подключения (upgrade).

## Ограничения

- В комнате два участника. Для групповых звонков нужна другая архитектура, обычно с медиасервером.
- `SignalingClient` заново входит в комнату после потери связи с сигнальным сервером, и уже идущий звонок это не затрагивает.
  Если собеседник ушёл, начинайте новый звонок с новым `PeerConnection`: использовать старый для нового собеседника нельзя.

## Как проверить, что всё работает

Эти команды проводят звонок между двумя участниками на Windows через настоящий сервер, без интерфейса:

```powershell
dotnet run --project samples/Maui.WebRTC.SignalingServer --urls http://127.0.0.1:5199
# во втором терминале:
dotnet run --project tests/Maui.WebRTC.SignalingSmoke -- http://127.0.0.1:5199/signaling
```

Ожидаемый вывод:

```
call through signaling server OK: hello over webrtc
full room rejected OK
peer left notification OK
```
