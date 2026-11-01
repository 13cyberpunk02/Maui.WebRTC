using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Maui.WebRTC.Sample;

/// <summary>
/// Minimal signaling for the sample: one peer listens, the other connects, and JSON lines go over TCP.
/// WebRTC does not define signaling; a real app would use its own server. There is no authentication
/// or encryption here, so this is for trying things out on a trusted network only.
/// </summary>
sealed class LanSignaling : IDisposable
{
	public const int Port = 8765;

	readonly TcpClient _client;
	readonly StreamWriter _writer;
	readonly StreamReader _reader;
	readonly SemaphoreSlim _sendLock = new(1, 1);

	LanSignaling(TcpClient client)
	{
		_client = client;
		var stream = client.GetStream();
		_writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
		_reader = new StreamReader(stream, Encoding.UTF8);
	}

	public static async Task<LanSignaling> ListenAsync()
	{
		var listener = new TcpListener(IPAddress.Any, Port);
		listener.Start();
		try
		{
			return new LanSignaling(await listener.AcceptTcpClientAsync());
		}
		finally
		{
			listener.Stop();
		}
	}

	public static async Task<LanSignaling> ConnectAsync(string host)
	{
		var client = new TcpClient();
		await client.ConnectAsync(host, Port);
		return new LanSignaling(client);
	}

	public async Task SendAsync(string type, string? sdp = null, IceCandidate? candidate = null)
	{
		var line = JsonSerializer.Serialize(new Dictionary<string, object?>
		{
			["type"] = type,
			["sdp"] = sdp,
			["mid"] = candidate?.SdpMid,
			["index"] = candidate?.SdpMLineIndex,
			["candidate"] = candidate?.Candidate,
		});

		await _sendLock.WaitAsync();
		try
		{
			await _writer.WriteLineAsync(line);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	/// <summary>
	/// Handles messages one at a time, so a candidate can never overtake the description it depends on.
	/// Returns when the other side disconnects.
	/// </summary>
	public async Task RunAsync(PeerConnection connection)
	{
		while (await _reader.ReadLineAsync() is { } line)
		{
			using var message = JsonDocument.Parse(line);
			var root = message.RootElement;
			switch (root.GetProperty("type").GetString())
			{
				case "offer":
					var offer = new SessionDescription("offer", root.GetProperty("sdp").GetString()!);
					await connection.SetRemoteDescriptionAsync(offer);
					var answer = await connection.CreateAnswerAsync();
					// Sent first so that our candidates, which start with SetLocalDescription, arrive after it.
					await SendAsync(answer.Type, answer.Sdp);
					await connection.SetLocalDescriptionAsync(answer);
					break;
				case "answer":
					await connection.SetRemoteDescriptionAsync(new SessionDescription("answer", root.GetProperty("sdp").GetString()!));
					break;
				case "candidate":
					await connection.AddIceCandidateAsync(new IceCandidate(
						root.GetProperty("mid").GetString()!,
						root.GetProperty("index").GetInt32(),
						root.GetProperty("candidate").GetString()!));
					break;
			}
		}
	}

	public void Dispose()
	{
		_client.Dispose();
		_sendLock.Dispose();
	}
}
