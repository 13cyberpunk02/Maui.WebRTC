using System.Text.Json;

namespace Maui.WebRTC.Sample;

static class CallStats
{
	/// <summary>Boils the stats report down to what shows whether media really flows.</summary>
	public static string Summarize(string json)
	{
		long audioPackets = 0, audioSent = 0, framesDecoded = 0, framesSent = 0;
		double audioLevel = 0;
		var route = "no route";
		var candidates = new Dictionary<string, string>();

		using var report = JsonDocument.Parse(json);
		foreach (var stat in report.RootElement.EnumerateArray())
		{
			var type = Text(stat, "type");
			var kind = Text(stat, "kind");
			switch (type)
			{
				case "inbound-rtp" when kind == "audio":
					audioPackets = Number(stat, "packetsReceived");
					audioLevel = stat.TryGetProperty("audioLevel", out var level) ? level.GetDouble() : 0;
					break;
				case "inbound-rtp" when kind == "video":
					framesDecoded = Number(stat, "framesDecoded");
					break;
				case "outbound-rtp" when kind == "audio":
					audioSent = Number(stat, "packetsSent");
					break;
				case "outbound-rtp" when kind == "video":
					framesSent = Number(stat, "framesEncoded");
					break;
				case "local-candidate" or "remote-candidate":
					candidates[Text(stat, "id")] = $"{Text(stat, "candidateType")}/{Text(stat, "protocol")}/{Text(stat, "networkType")}";
					break;
			}
		}

		foreach (var stat in report.RootElement.EnumerateArray())
		{
			if (Text(stat, "type") == "candidate-pair" && Text(stat, "state") == "succeeded"
				&& stat.TryGetProperty("nominated", out var nominated) && nominated.GetBoolean())
			{
				candidates.TryGetValue(Text(stat, "localCandidateId"), out var local);
				candidates.TryGetValue(Text(stat, "remoteCandidateId"), out var remote);
				route = $"{local} -> {remote}";
			}
		}

		return $"audio rx {audioPackets} pkts level {audioLevel:0.000}, tx {audioSent} | video decoded {framesDecoded}, encoded {framesSent} | {route}";
	}

	static string Text(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

	static long Number(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetInt64() : 0;
}
