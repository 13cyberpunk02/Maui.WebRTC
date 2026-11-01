using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Maui.WebRTC.SignalingServer;

/// <summary>
/// Relays offers, answers and ICE candidates between the two peers of a room. It never sees any media:
/// that flows directly between the peers. Add [Authorize] and your own room rules before going public.
/// </summary>
public sealed class SignalingHub : Hub
{
    const int MaxPeersPerRoom = 2;
    const string RoomKey = "room";

    static readonly ConcurrentDictionary<string, int> Rooms = new();

    public async Task Join(string room)
    {
        if (Context.Items.ContainsKey(RoomKey))
            throw new HubException("Already in a room.");

        var count = Rooms.AddOrUpdate(room, 1, (_, current) => current + 1);
        if (count > MaxPeersPerRoom)
        {
            Leave(room);
            throw new HubException("The room is full.");
        }

        Context.Items[RoomKey] = room;
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
        await Clients.OthersInGroup(room).SendAsync("PeerJoined");
    }

    /// <summary>The room comes from the connection, so a client can only reach the room it joined.</summary>
    public Task Signal(string type, string payload)
    {
        if (Context.Items[RoomKey] is not string room)
            throw new HubException("Join a room first.");

        return Clients.OthersInGroup(room).SendAsync("Signal", type, payload);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items[RoomKey] is string room)
        {
            Leave(room);
            await Clients.OthersInGroup(room).SendAsync("PeerLeft");
        }

        await base.OnDisconnectedAsync(exception);
    }

    static void Leave(string room)
    {
        if (Rooms.AddOrUpdate(room, 0, (_, current) => current - 1) <= 0)
            Rooms.TryRemove(new KeyValuePair<string, int>(room, 0));
    }
}
