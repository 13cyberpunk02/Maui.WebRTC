**English** | [Русский](turn.ru.md)

# Your own TURN server for calls over the internet

Without TURN, a call connects only when the devices can reach each other directly: on one network, or through a simple
home router. Mobile carriers, corporate and guest networks often prevent that, and then the traffic has to go through a
relay server: TURN.

This guide runs [coturn](https://github.com/coturn/coturn) in Docker on a Linux server, gives the app temporary
credentials from your ASP.NET Core server, and checks that it all works. The files are in [`samples/turn`](../samples/turn).

```
Device A ──► TURN ──► Device B        media goes through the server only when a direct path failed
    │                    │
    └──── your server ───┘            issues temporary TURN credentials and carries the signaling
```

## 1. What you need

- **A Linux server with a public IPv4 address.** An inexpensive VPS will do, for example with Ubuntu 24.04. One core and
  1 GB of memory are enough to start: coturn barely uses the CPU and is limited by the network.
- **A domain name**, such as `turn.example.com`, with an A record pointing at the server. The TLS certificate needs it.
- **Traffic.** All the audio and video of a call that couldn't connect directly goes through TURN. A video call is roughly
  1–2 Mbit/s in each direction per participant. Check how much traffic the server's plan includes.

## 2. Ports and firewall

| Port | Protocol | Purpose |
|---|---|---|
| 3478 | UDP and TCP | STUN and TURN |
| 5349 | TCP | TURN over TLS, for networks that only let HTTPS-like traffic through |
| 49152–65535 | UDP | the relayed audio and video itself: each call needs a port of its own |
| 80 | TCP | only to get the Let's Encrypt certificate |

Open them both in the server's firewall and in the cloud's security settings, if it has any (security groups and the
like). With `ufw`:

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

## 4. coturn's files

Copy `samples/turn/docker-compose.yml` and `samples/turn/turnserver.conf` to `/opt/coturn` on the server:

```bash
sudo mkdir -p /opt/coturn/certs
# from your computer:
scp samples/turn/docker-compose.yml samples/turn/turnserver.conf user@turn.example.com:/tmp/
# on the server:
sudo mv /tmp/docker-compose.yml /tmp/turnserver.conf /opt/coturn/
sudo chmod 600 /opt/coturn/turnserver.conf
```

Create a secret. Only coturn and your app server will know it; it never goes into the app:

```bash
openssl rand -hex 32
```

Open `/opt/coturn/turnserver.conf` and replace:

- `realm` and `server-name` with your domain;
- `static-auth-secret` with the secret you created.

Then check whether the server sees its own public address:

```bash
ip -4 addr            # the addresses the server has
curl -4 ifconfig.me   # the address the internet sees it by
```

If the public address isn't among its own (common in clouds, where the machine only has an internal `10.x` or `172.x`),
uncomment `external-ip` and give `public/internal`, for example `external-ip=203.0.113.10/10.0.0.5`. Otherwise coturn
tells clients an internal address they can't reach, and calls through it don't connect.

## 5. TLS certificate

```bash
sudo apt install -y certbot
sudo certbot certonly --standalone -d turn.example.com
```

coturn doesn't run as root in its container, so the certificate is copied over with permissions it can read. Create
`/etc/letsencrypt/renewal-hooks/deploy/coturn.sh` so this also happens on every automatic renewal:

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

`65534` is the `nobody` user. If coturn's log says it can't read the key, check which user it runs as with
`docker compose -f /opt/coturn/docker-compose.yml exec coturn id` and put that uid in the script.

## 6. Start

```bash
cd /opt/coturn
sudo docker compose up -d
sudo docker compose logs -f
```

The log should have no `ERROR` lines. It should list the addresses coturn listens on and mention the loaded certificate.

`docker-compose.yml` uses `network_mode: host`, so coturn sees the clients' real addresses and the thousands of ports
don't have to be published through Docker. That only works on Linux.

## 7. Temporary credentials from your server

The app must not contain a permanent TURN password: anyone can dig it out of the build in minutes. Instead, your server
gives each signed-in user a password valid for a few hours, signed with a shared secret, and coturn checks the signature
(`use-auth-secret` in the config).

The full code is in [`samples/Maui.WebRTC.SignalingServer/TurnCredentials.cs`](../samples/Maui.WebRTC.SignalingServer/TurnCredentials.cs):

```csharp
var expires = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
var username = $"{expires}:{userId}";
var signature = HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(username));
var credential = Convert.ToBase64String(signature);
```

Serve them from an endpoint only signed-in users can reach:

```csharp
app.MapGet("/turn-credentials", (ClaimsPrincipal user) =>
        TurnCredentialIssuer.Issue(secret, urls, user.FindFirstValue(ClaimTypes.NameIdentifier)!, TimeSpan.FromHours(12)))
    .RequireAuthorization();
```

Put the secret and addresses in the server's settings, not in code, for example as environment variables:

```bash
Turn__Secret=your-secret
Turn__Urls__0=stun:turn.example.com:3478
Turn__Urls__1=turn:turn.example.com:3478?transport=udp
Turn__Urls__2=turn:turn.example.com:3478?transport=tcp
Turn__Urls__3=turns:turn.example.com:5349?transport=tcp
```

All four are needed: UDP is the fastest, TCP gets through where UDP is blocked, and TLS on 5349 where only secure
connections are let through.

A password is valid until the time in its username, checked against the coturn server's clock. Keep time sync enabled on
it (Ubuntu has it on by default).

## 8. The app

Get credentials before each call and pass them to every address:

```csharp
var turn = await http.GetFromJsonAsync<TurnCredentials>("https://api.example.com/turn-credentials");
var iceServers = turn!.Urls.Select(url => new IceServer(url, turn.Username, turn.Credential)).ToArray();
var connection = factory.CreatePeerConnection(iceServers);
```

Fetching a password per call is simpler than tracking its expiry, and it can never run out in the middle of a call.

## 9. Check

`tests/Maui.WebRTC.TurnSmoke` connects two peers inside one process and passes them **only** each other's `relay`
candidates. With the direct path closed, they can only connect if the traffic really goes through TURN.

Start the sample server with your TURN settings, then the test (on Windows):

```powershell
$env:Turn__Secret = "your-secret"
$env:Turn__Urls__0 = "turn:turn.example.com:3478?transport=udp"
$env:Turn__Urls__1 = "turn:turn.example.com:3478?transport=tcp"
dotnet run --project samples/Maui.WebRTC.SignalingServer --urls http://127.0.0.1:5299
# in a second terminal:
dotnet run --project tests/Maui.WebRTC.TurnSmoke -- http://127.0.0.1:5299/turn-credentials
```

Expected output:

```
gathered: callee host 4, callee relay 2, ...
echo: through TURN
route: ... -> relay/udp 203.0.113.10:49164
TURN relay OK
```

### On your own computer first

You can try it before renting a server, with Docker Desktop. The computer's local network address stands in for the
public one: libwebrtc doesn't use TURN at `127.0.0.1`.

```powershell
$env:TURN_LOCAL_IP = "192.168.0.80"   # your computer's address
docker compose -f samples/turn/docker-compose.local.yml up -d

$env:Turn__Secret = "local-development-secret"
$env:Turn__Urls__0 = "turn:$($env:TURN_LOCAL_IP):3478?transport=udp"
$env:Turn__Urls__1 = "turn:$($env:TURN_LOCAL_IP):3478?transport=tcp"
dotnet run --project samples/Maui.WebRTC.SignalingServer --urls http://127.0.0.1:5299
# in a second terminal:
dotnet run --project tests/Maui.WebRTC.TurnSmoke -- http://127.0.0.1:5299/turn-credentials
```

This config's secret is right there in the repository, so stop the container once you're done (in the same shell, where
`TURN_LOCAL_IP` is set):

```powershell
docker compose -f samples/turn/docker-compose.local.yml down
```

## 10. When it doesn't work

| What you see | Cause and fix |
|---|---|
| The test prints `No relay candidates` | Requests don't reach coturn, or it rejects the password. Check port 3478 in the firewall and the cloud. `401 Unauthorized` in coturn's log means a wrong secret or an expired password: compare the secret on both servers and the clock on the coturn server. |
| There are `relay` candidates but no connection | The 49152–65535/UDP range is closed, or `external-ip` is wrong: coturn's log shows an internal address for the session instead of the public one. |
| Works only over UDP, or only without TLS | TCP 3478 or 5349 is closed, or coturn didn't read the certificate: look for `cert` or `pkey` in its log. |
| Works on Wi-Fi but not on mobile data | The app got only `stun:` addresses and no `turn:` ones. Check what the credentials endpoint returns. |

## 11. Security and upkeep

- **Keep the secret** only in coturn's and the app server's settings. To change it, change both and restart coturn:
  old passwords stop working at once.
- **`denied-peer-ip`** in the config forbids relaying into internal networks. Without it, anyone with a password could
  reach machines in the provider's network through your server. Don't remove those lines.
- **Limits** `user-quota` and `total-quota` set how many relays one user and everyone together can open. Raise them as
  you grow.
- **Updates:** change the image version in `docker-compose.yml`, then `docker compose pull` and `docker compose up -d`.
- **Load:** watch the traffic in the provider's dashboard. A lot of it is a reason to consider a media server for group
  calls.
