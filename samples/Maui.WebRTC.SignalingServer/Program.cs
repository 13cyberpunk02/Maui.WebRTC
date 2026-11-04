using Maui.WebRTC.SignalingServer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();
app.MapHub<SignalingHub>("/signaling");

// TURN credentials for docs/turn.md, served only when a secret is configured.
// A real app puts this behind [Authorize] and uses the signed-in user's id instead of a random one.
if (app.Configuration["Turn:Secret"] is { Length: > 0 } turnSecret)
{
    var turnUrls = app.Configuration.GetSection("Turn:Urls").Get<string[]>() ?? [];
    app.MapGet("/turn-credentials", () =>
        TurnCredentialIssuer.Issue(turnSecret, turnUrls, Guid.NewGuid().ToString("N"), TimeSpan.FromHours(12)));
}

app.Run();
