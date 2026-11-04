[English](turn.md) | **Русский**

# Свой TURN-сервер для звонков через интернет

Без TURN звонок соединится, только если устройства могут достучаться друг до друга напрямую: в одной сети или через
простой домашний роутер. Мобильные операторы, корпоративные и гостевые сети часто это не позволяют, и тогда трафик
должен пройти через сервер-посредник — TURN.

Этот гайд разворачивает [coturn](https://github.com/coturn/coturn) в Docker на Linux-сервере, выдаёт приложению
временные пароли с вашего сервера на ASP.NET Core и проверяет, что всё работает. Готовые файлы лежат в
[`samples/turn`](../samples/turn).

```
Устройство A ──► TURN ──► Устройство B        медиа идёт через сервер, только если напрямую не получилось
      │                        │
      └──── ваш сервер ────────┘               выдаёт временные пароли TURN и передаёт сигнализацию
```

## 1. Что понадобится

- **Сервер с Linux и публичным IPv4-адресом.** Подойдёт недорогой VPS, например с Ubuntu 24.04. Для начала хватит
  1 ядра и 1 ГБ памяти: coturn почти не нагружает процессор, а упирается в сетевой канал.
- **Доменное имя**, например `turn.example.com`, с A-записью на адрес сервера. Оно нужно для TLS-сертификата.
- **Трафик.** Через TURN идёт весь звук и видео звонка, который не смог соединиться напрямую. Видеозвонок — это
  примерно 1–2 Мбит/с в каждую сторону на участника. Посмотрите, сколько трафика входит в тариф сервера.

## 2. Порты и брандмауэр

| Порт | Протокол | Зачем |
|---|---|---|
| 3478 | UDP и TCP | STUN и TURN |
| 5349 | TCP | TURN поверх TLS, для сетей, где проходит только HTTPS-подобный трафик |
| 49152–65535 | UDP | сам пересылаемый звук и видео: каждому звонку нужен свой порт |
| 80 | TCP | только для получения сертификата Let's Encrypt |

Откройте их и в брандмауэре сервера, и в настройках безопасности облака, если они там есть (security group и т. п.).
Для `ufw`:

```bash
sudo ufw allow 3478/udp
sudo ufw allow 3478/tcp
sudo ufw allow 5349/tcp
sudo ufw allow 49152:65535/udp
sudo ufw allow 80/tcp
sudo ufw allow OpenSSH
sudo ufw enable
```

## 3. Docker

```bash
curl -fsSL https://get.docker.com | sudo sh
```

## 4. Файлы coturn

Скопируйте на сервер `samples/turn/docker-compose.yml` и `samples/turn/turnserver.conf` в `/opt/coturn`:

```bash
sudo mkdir -p /opt/coturn/certs
# с вашего компьютера:
scp samples/turn/docker-compose.yml samples/turn/turnserver.conf user@turn.example.com:/tmp/
# на сервере:
sudo mv /tmp/docker-compose.yml /tmp/turnserver.conf /opt/coturn/
sudo chmod 600 /opt/coturn/turnserver.conf
```

Создайте секрет. Его будут знать только coturn и ваш сервер приложения, в приложение он не попадает:

```bash
openssl rand -hex 32
```

Откройте `/opt/coturn/turnserver.conf` и замените:

- `realm` и `server-name` — на ваш домен;
- `static-auth-secret` — на созданный секрет.

Затем проверьте, видит ли сервер свой публичный адрес:

```bash
ip -4 addr        # адреса, которые есть у сервера
curl -4 ifconfig.me   # адрес, под которым его видит интернет
```

Если публичного адреса нет среди своих (так бывает у многих облаков: у машины только внутренний `10.x` или `172.x`),
раскомментируйте `external-ip` и укажите `публичный/внутренний`, например `external-ip=203.0.113.10/10.0.0.5`.
Без этого coturn будет сообщать клиентам недоступный внутренний адрес, и звонки через него не соединятся.

## 5. TLS-сертификат

```bash
sudo apt install -y certbot
sudo certbot certonly --standalone -d turn.example.com
```

coturn в контейнере работает не от root, поэтому сертификат копируется ему с подходящими правами. Создайте
`/etc/letsencrypt/renewal-hooks/deploy/coturn.sh`, чтобы это происходило и при каждом автоматическом продлении:

```bash
#!/bin/sh
set -e
LIVE=/etc/letsencrypt/live/turn.example.com
install -m 0644 "$LIVE/fullchain.pem" /opt/coturn/certs/fullchain.pem
install -m 0600 -o 65534 -g 65534 "$LIVE/privkey.pem" /opt/coturn/certs/privkey.pem
docker compose -f /opt/coturn/docker-compose.yml restart
```

```bash
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
sudo /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
```

`65534` — это пользователь `nobody`. Если в журнале coturn окажется, что ключ не читается, проверьте, от кого он
работает: `docker compose -f /opt/coturn/docker-compose.yml exec coturn id`, и поставьте этот uid в скрипт.

## 6. Запуск

```bash
cd /opt/coturn
sudo docker compose up -d
sudo docker compose logs -f
```

В журнале не должно быть строк `ERROR`. Должны появиться адреса, на которых coturn слушает, и строка о загруженном
сертификате.

`docker-compose.yml` использует `network_mode: host`: coturn сразу видит настоящие адреса клиентов, а тысячи портов
не нужно публиковать через Docker. Это работает только на Linux.

## 7. Временные пароли на вашем сервере

В приложении не должно быть постоянного пароля TURN: его достанут из сборки за минуты. Вместо этого ваш сервер выдаёт
каждому вошедшему пользователю пароль на несколько часов, подписанный общим секретом, а coturn проверяет подпись
(`use-auth-secret` в конфиге).

Код целиком — в [`samples/Maui.WebRTC.SignalingServer/TurnCredentials.cs`](../samples/Maui.WebRTC.SignalingServer/TurnCredentials.cs):

```csharp
var expires = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
var username = $"{expires}:{userId}";
var signature = HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(username));
var credential = Convert.ToBase64String(signature);
```

Отдайте их через эндпоинт, доступный только вошедшим:

```csharp
app.MapGet("/turn-credentials", (ClaimsPrincipal user) =>
        TurnCredentialIssuer.Issue(secret, urls, user.FindFirstValue(ClaimTypes.NameIdentifier)!, TimeSpan.FromHours(12)))
    .RequireAuthorization();
```

Секрет и адреса задайте в настройках сервера, а не в коде, например через переменные окружения:

```bash
Turn__Secret=ваш-секрет
Turn__Urls__0=stun:turn.example.com:3478
Turn__Urls__1=turn:turn.example.com:3478?transport=udp
Turn__Urls__2=turn:turn.example.com:3478?transport=tcp
Turn__Urls__3=turns:turn.example.com:5349?transport=tcp
```

Все четыре адреса нужны: UDP быстрее всего, TCP проходит там, где UDP закрыт, а TLS на 5349 — там, где пропускают
только защищённые соединения.

Пароль действителен до времени в его имени пользователя, а время сверяется с часами сервера coturn. Держите на нём
синхронизацию времени включённой (на Ubuntu она включена по умолчанию).

## 8. Приложение

Перед каждым звонком получите пароли и передайте их всем адресам:

```csharp
var turn = await http.GetFromJsonAsync<TurnCredentials>("https://api.example.com/turn-credentials");
var iceServers = turn!.Urls.Select(url => new IceServer(url, turn.Username, turn.Credential)).ToArray();
var connection = factory.CreatePeerConnection(iceServers);
```

Брать пароль на каждый звонок проще, чем следить за сроком: тогда он никогда не истечёт посреди разговора.

## 9. Проверка

`tests/Maui.WebRTC.TurnSmoke` соединяет два участника внутри одного процесса и передаёт им друг другу **только**
кандидаты `relay`. Прямой путь закрыт, так что соединение получится, только если трафик действительно идёт через
TURN.

Запустите сервер-образец с настройками вашего TURN, а затем тест (на Windows):

```powershell
$env:Turn__Secret = "ваш-секрет"
$env:Turn__Urls__0 = "turn:turn.example.com:3478?transport=udp"
$env:Turn__Urls__1 = "turn:turn.example.com:3478?transport=tcp"
dotnet run --project samples/Maui.WebRTC.SignalingServer --urls http://127.0.0.1:5299
# во втором терминале:
dotnet run --project tests/Maui.WebRTC.TurnSmoke -- http://127.0.0.1:5299/turn-credentials
```

Ожидаемый вывод:

```
gathered: callee host 4, callee relay 2, ...
echo: through TURN
route: ... -> relay/udp 203.0.113.10:49164
TURN relay OK
```

### Сначала на своём компьютере

Попробовать можно и до аренды сервера, с Docker Desktop. Публичный адрес TURN заменяет адрес компьютера в локальной
сети: libwebrtc не обращается к TURN по `127.0.0.1`.

```powershell
$env:TURN_LOCAL_IP = "192.168.0.80"   # адрес вашего компьютера
docker compose -f samples/turn/docker-compose.local.yml up -d

$env:Turn__Secret = "local-development-secret"
$env:Turn__Urls__0 = "turn:$($env:TURN_LOCAL_IP):3478?transport=udp"
$env:Turn__Urls__1 = "turn:$($env:TURN_LOCAL_IP):3478?transport=tcp"
dotnet run --project samples/Maui.WebRTC.SignalingServer --urls http://127.0.0.1:5299
# во втором терминале:
dotnet run --project tests/Maui.WebRTC.TurnSmoke -- http://127.0.0.1:5299/turn-credentials
```

Секрет этого конфига записан прямо в репозитории, поэтому после проверки остановите контейнер (в той же оболочке, где
задан `TURN_LOCAL_IP`):

```powershell
docker compose -f samples/turn/docker-compose.local.yml down
```

## 10. Если не работает

| Что видно | Причина и что делать |
|---|---|
| Тест пишет `No relay candidates` | До coturn не доходят запросы или он отклоняет пароль. Проверьте порт 3478 в брандмауэре и облаке. В журнале coturn `401 Unauthorized` значит неверный секрет или истёкший пароль: сравните секрет на обоих серверах и время на сервере coturn. |
| Кандидаты `relay` есть, но соединения нет | Закрыт диапазон 49152–65535/UDP или неверный `external-ip`: в журнале coturn у сессии внутренний адрес вместо публичного. |
| Работает только по UDP или только без TLS | Закрыт TCP 3478 или 5349, либо coturn не прочитал сертификат: ищите в журнале строки про `cert` или `pkey`. |
| Работает в Wi-Fi, но не в мобильной сети | Приложение не получило адреса `turn:`, а только `stun:`. Проверьте ответ эндпоинта паролей. |

## 11. Безопасность и обслуживание

- **Секрет** храните только в настройках coturn и сервера приложения. Чтобы сменить его, поменяйте значение в обоих
  местах и перезапустите coturn: старые пароли сразу перестанут действовать.
- **`denied-peer-ip`** в конфиге запрещает пересылку во внутренние сети. Без этого любой, у кого есть пароль, мог бы
  через ваш сервер достучаться до машин в сети провайдера. Не удаляйте эти строки.
- **Ограничения** `user-quota` и `total-quota` задают, сколько соединений можно открыть одному пользователю и всем
  вместе. Увеличивайте их по мере роста.
- **Обновления:** смените версию образа в `docker-compose.yml`, затем `docker compose pull` и `docker compose up -d`.
- **Нагрузка:** следите за трафиком в панели провайдера. Если его становится много, это повод подумать о медиасервере
  для групповых звонков.
