[English](README.md) | **Русский**

# Maui.WebRTC

Нативный WebRTC для .NET MAUI на Android и Windows, без WebView: API на C# и XAML-контрол `VideoView`
поверх libwebrtc от Google.

> Статус: предварительная версия. Звонки между телефоном и компьютером проверены в локальной сети; соединения через
> интернет со STUN и TURN пока не проверялись.

## Платформы

| Платформа | Архитектуры | Примечания |
|---|---|---|
| Android 7.0+ (API 24) | arm64-v8a | эмуляторы x86_64 не поддерживаются: для них нет готовой сборки libwebrtc |
| Windows 10 1809+ | x64 | |

Android-приложение должно собираться только под arm64:

```xml
<RuntimeIdentifier Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">android-arm64</RuntimeIdentifier>
```

В манифесте также нужны `CAMERA`, `RECORD_AUDIO`, `MODIFY_AUDIO_SETTINGS`, `INTERNET` и `ACCESS_NETWORK_STATE`.
Первые два разрешения нужно запрашивать во время работы приложения, а на время звонка стоит задать
`AudioManager.Mode = Mode.InCommunication`.

## Использование

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
connection.IceCandidateGathered += candidate => { /* отправить собеседнику */ };
connection.TrackReceived += track =>
{
    if (track is VideoTrack remote)
        MainThread.BeginInvokeOnMainThread(() => RemoteVideo.Track = remote);
};

var offer = await connection.CreateOfferAsync();
// Отправьте offer собеседнику, затем:
await connection.SetLocalDescriptionAsync(offer);
```

События приходят по порядку в фоновом потоке, поэтому для работы с UI обработчик должен переключиться на главный поток.

## Сигнализация

WebRTC не определяет, как собеседники обмениваются offer, answer и ICE-кандидатами: это задача приложения. Передавайте их
по порядку и устанавливайте удалённое описание раньше, чем добавляете кандидатов, иначе `AddIceCandidateAsync` завершится ошибкой.

**[Подключение через сигнальный сервер на ASP.NET Core](docs/signaling-aspnetcore.ru.md)** — рекомендуемый способ.

В `samples/Maui.WebRTC.Sample` есть и полный звонок через обычный TCP, но он без авторизации и только для доверенных сетей.

## Кодеки

В готовой сборке libwebrtc нет H.264 и H.265, поэтому звонки используют VP8, VP9 или AV1 с программным кодированием.

## Сборка

Нужны Visual Studio с инструментами C++, CMake, Android NDK и рабочие нагрузки .NET MAUI. Выполните по порядку:

```powershell
./scripts/fetch-webrtc.ps1
./scripts/fetch-libcxx.ps1
./scripts/fetch-clang.ps1
./scripts/build-native-windows.ps1
./scripts/build-native-android.ps1
dotnet pack src/Maui.WebRTC/Maui.WebRTC.csproj -c Release -o artifacts/packages
```

`fetch-clang.ps1` скачивает именно тот clang от Chromium, которым собрана libwebrtc, и нативная сборка запускает его, потому что
слой на C++ должен совпадать по ABI с готовой библиотекой. Зачем нужен каждый флаг, описано в `native/CMakeLists.txt`.

`tests/Maui.WebRTC.NativeSmoke` проверяет каналы данных и видео на Windows без интерфейса, а
`tests/Maui.WebRTC.SignalingSmoke` проводит звонок через сигнальный сервер.

## Лицензия

[MIT](LICENSE) © 13CyberPunk02. Бинарные файлы также содержат libwebrtc и её зависимости под их собственными лицензиями;
они включены в пакет в папке `licenses/`.
