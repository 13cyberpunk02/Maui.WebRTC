using Maui.WebRTC;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"libwebrtc: {PeerConnectionFactory.NativeVersion}");
if (args.Contains("-vv"))
    PeerConnectionFactory.SetNativeLogLevel(NativeLogLevel.Info);

for (var i = 0; i < 3; i++)
{
    using var factory = new PeerConnectionFactory();
    Console.WriteLine($"factory create/destroy #{i + 1}: OK");
}

for (var i = 0; i < 3; i++)
{
    Console.WriteLine($"loopback #{i + 1}: {await LoopbackCheck.RunAsync(TimeSpan.FromSeconds(20), args.Contains("-v") ? Console.WriteLine : null)}");
}

for (var i = 0; i < 2; i++)
{
    Console.WriteLine($"video #{i + 1}: {await LoopbackCheck.RunVideoAsync(TimeSpan.FromSeconds(20))}");
}

return 0;
