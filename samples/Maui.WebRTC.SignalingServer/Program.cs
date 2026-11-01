using Maui.WebRTC.SignalingServer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();

var app = builder.Build();
app.MapHub<SignalingHub>("/signaling");
app.Run();
