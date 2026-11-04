using System.Security.Cryptography;
using System.Text;

namespace Maui.WebRTC.SignalingServer;

/// <summary>What a client passes to its <c>IceServer</c>s: one entry per URL, all with the same credentials.</summary>
public record TurnCredentials(string[] Urls, string Username, string Credential, int Ttl);

/// <summary>
/// Temporary TURN credentials in the form coturn checks with <c>use-auth-secret</c>:
/// the username carries its expiry time, and the password is that username signed with the shared secret.
/// </summary>
public static class TurnCredentialIssuer
{
    public static TurnCredentials Issue(string secret, string[] urls, string userId, TimeSpan lifetime)
    {
        var expires = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
        var username = $"{expires}:{userId}";
        var signature = HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(username));
        return new TurnCredentials(urls, username, Convert.ToBase64String(signature), (int)lifetime.TotalSeconds);
    }
}
