namespace ClownSquad.Net;

using System;
using Godot;
using Steamworks;
using Steamworks.Data;

/// <summary>
/// Steam invites and "Join game" from the friends list. When the player
/// accepts one, the game enters that friend's Steam lobby, finds the host
/// (the lobby's owner) and asks the title screen to join them; started from
/// an invite (Steam passes "+connect_lobby id"), it does the same at launch.
/// A request that arrives away from the title waits there until it is shown.
/// </summary>
public static class SteamInvites
{
    /// <summary>A host to join, waiting for the title screen to take it.</summary>
    public static SteamId? Pending { get; private set; }

    /// <summary>Raised when a host to join arrives; the title screen joins it.</summary>
    public static event Action<SteamId>? JoinRequested;

    private static bool _hooked;

    public static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        SteamFriends.OnGameLobbyJoinRequested += (lobby, friend) => _ = Enter(lobby);
        SteamMatchmaking.OnLobbyEntered += OnEntered;
        // Started by accepting an invite: "+connect_lobby <id>" on the command line.
        string[] args = OS.GetCmdlineArgs();
        int at = Array.IndexOf(args, "+connect_lobby");
        if (at >= 0 && at + 1 < args.Length && ulong.TryParse(args[at + 1], out ulong id))
            _ = Enter(new Lobby(id));
    }

    private static async System.Threading.Tasks.Task Enter(Lobby lobby)
    {
        RoomEnter result = await lobby.Join();
        if (result != RoomEnter.Success) GD.Print($"Steam: could not enter lobby {lobby.Id} ({result}).");
    }

    private static void OnEntered(Lobby lobby)
    {
        // Our own lobby (we are hosting) is no invitation.
        if (lobby.Owner.Id == SteamClient.SteamId) return;
        if (lobby.GetData("game") != "clownsquad") { lobby.Leave(); return; }
        SteamId host = lobby.Owner.Id;
        lobby.Leave();                                  // the game connects directly; the lobby was only the way in
        GD.Print($"Steam: invited to {host}'s game.");
        Pending = host;
        JoinRequested?.Invoke(host);
    }

    /// <summary>The title screen took the pending request.</summary>
    public static SteamId? Take()
    {
        SteamId? p = Pending;
        Pending = null;
        return p;
    }
}
