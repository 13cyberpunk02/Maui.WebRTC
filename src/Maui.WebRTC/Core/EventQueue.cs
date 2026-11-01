using System.Diagnostics;
using System.Threading.Channels;

namespace Maui.WebRTC;

/// <summary>
/// Moves events off libwebrtc's signaling thread while keeping their order, so user handlers may
/// block or call Dispose. Native callbacks only ever enqueue, which never blocks.
/// </summary>
internal sealed class EventQueue
{
    readonly Channel<Action> _channel = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });

    public EventQueue() => _ = Task.Run(RunAsync);

    public void Post(Action action) => _channel.Writer.TryWrite(action);

    public void Complete() => _channel.Writer.TryComplete();

    async Task RunAsync()
    {
        await foreach (var action in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // There is no caller to surface this to, and one bad handler must not stop the rest.
                Trace.TraceError($"Maui.WebRTC event handler failed: {ex}");
            }
        }
    }
}
