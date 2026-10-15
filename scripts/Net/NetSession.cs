namespace ClownSquad.Net;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using ClownSquad.Progression;

/// <summary>One player in an online lobby, as everyone sees them.</summary>
public sealed class NetMember
{
    public long Peer { get; set; }
    public string Name { get; set; } = "";
    public string Look { get; set; } = "pip";
    public int Level { get; set; } = 1;
    public WeaponKind Weapon { get; set; }
    public bool IsHost => Peer == 1;
}

/// <summary>
/// Online co-op, phase 1 (ADR 0010): host or join by IP address over ENet
/// (Godot's high-level multiplayer), no matchmaking. The host listens on
/// <see cref="Port"/>; a guest connects, says hello with their game version
/// and their own character (from their own save), and the host sends
/// everyone the lobby roster. The host keeps each guest's full character
/// record, which is what it will play them as once stages run online
/// (phase 2).
///
/// Lives under the scene root as "NetSession", so it survives scene changes
/// and has the same node path on every machine, which Godot's RPCs need.
/// </summary>
public partial class NetSession : Node
{
    public const int Port = 7777;
    public const int MaxGuests = 3;
    /// <summary>Bumped whenever the messages change; host and guest must match.</summary>
    public const int Protocol = 1;

    public enum Role { Offline, Host, Guest }

    public static NetSession? Instance { get; private set; }

    public Role Mode { get; private set; } = Role.Offline;
    public IReadOnlyList<NetMember> Members => _members;
    /// <summary>The full character each guest brought, by peer id (host only).</summary>
    public IReadOnlyDictionary<long, CharacterRecord> GuestRecords => _records;
    /// <summary>What is happening, for the lobby screen.</summary>
    public string Status { get; private set; } = "";

    /// <summary>The roster or status changed.</summary>
    public event Action? Changed;
    /// <summary>The session ended (failed to connect, kicked, host left), with why.</summary>
    public event Action<string>? Ended;

    private readonly List<NetMember> _members = new();
    private readonly Dictionary<long, CharacterRecord> _records = new();
    private CharacterRecord _local = new();
    private ENetMultiplayerPeer? _peer;
    private MultiplayerApi _api = null!;

    /// <summary>
    /// The session node, created the first time it is asked for. It joins the
    /// scene root on the next frame (the root may be busy building a scene),
    /// but works at once: it talks to the tree's multiplayer API directly, and
    /// is in the tree long before any message can arrive.
    /// </summary>
    public static NetSession Ensure(SceneTree tree)
    {
        if (Instance is not null && IsInstanceValid(Instance)) return Instance;
        var s = new NetSession { Name = "NetSession", ProcessMode = ProcessModeEnum.Always };
        s.Attach(tree.GetMultiplayer());
        Instance = s;
        tree.Root.CallDeferred(Node.MethodName.AddChild, s);
        return s;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private void Attach(MultiplayerApi api)
    {
        _api = api;
        _api.PeerConnected += id => GD.Print($"Net: peer {id} connected.");
        _api.PeerDisconnected += OnPeerLeft;
        _api.ConnectedToServer += OnConnected;
        _api.ConnectionFailed += () => End("Could not reach the host. Check the address, and that the host's port " + Port + " is open.");
        _api.ServerDisconnected += () => End("The host closed the game.");
    }

    // ------------------------------------------------------------ starting

    /// <summary>Open a game on <see cref="Port"/> for up to three guests.</summary>
    public Error Host(CharacterRecord me)
    {
        Leave(quiet: true);
        _local = me;
        _peer = new ENetMultiplayerPeer();
        Error err = _peer.CreateServer(Port, MaxGuests);
        if (err != Error.Ok)
        {
            _peer = null;
            Status = $"Could not open port {Port} ({err}). Is another copy of the game hosting?";
            Changed?.Invoke();
            return err;
        }
        _api.MultiplayerPeer = _peer;
        Mode = Role.Host;
        _members.Add(MemberFor(1, me));
        Status = "Waiting for friends to join...";
        GD.Print($"Net: hosting on port {Port}.");
        Changed?.Invoke();
        return Error.Ok;
    }

    /// <summary>Connect to a host by IP address (or host name).</summary>
    public Error Join(string address, CharacterRecord me)
    {
        Leave(quiet: true);
        _local = me;
        _peer = new ENetMultiplayerPeer();
        Error err = _peer.CreateClient(address.Trim(), Port);
        if (err != Error.Ok)
        {
            _peer = null;
            Status = $"Could not start connecting ({err}).";
            Changed?.Invoke();
            return err;
        }
        _api.MultiplayerPeer = _peer;
        Mode = Role.Guest;
        Status = $"Connecting to {address.Trim()}...";
        GD.Print($"Net: joining {address.Trim()}:{Port}.");
        Changed?.Invoke();
        return Error.Ok;
    }

    /// <summary>Leave the game (a host closes it for everyone).</summary>
    public void Leave(bool quiet = false)
    {
        if (_peer is not null)
        {
            _peer.Close();
            _api.MultiplayerPeer = new OfflineMultiplayerPeer();
            _peer = null;
        }
        bool was = Mode != Role.Offline;
        Mode = Role.Offline;
        _members.Clear();
        _records.Clear();
        Status = "";
        if (was && !quiet) Changed?.Invoke();
    }

    /// <summary>This machine's addresses on the local network, for the host to read out.</summary>
    public static List<string> LocalAddresses()
    {
        var list = new List<string>();
        foreach (string a in IP.GetLocalAddresses())
            if (a.Contains('.') && !a.StartsWith("127.") && !a.StartsWith("169.254.")) list.Add(a);
        // Home networks first; 172.x is usually a virtual adapter (Docker, WSL).
        static int Rank(string a) => a.StartsWith("192.168.") ? 0 : a.StartsWith("10.") ? 1 : 2;
        list.Sort((x, y) => Rank(x).CompareTo(Rank(y)));
        return list;
    }

    // ----------------------------------------------------------- messages

    private void OnConnected()
    {
        Status = "Connected. Saying hello...";
        Changed?.Invoke();
        RpcId(1, MethodName.Hello, Protocol, SaveSystem.CharacterToJson(_local));
    }

    /// <summary>Guest to host: my game version and the character I am bringing.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Hello(int protocol, string recordJson)
    {
        if (Mode != Role.Host) return;
        long from = _api.GetRemoteSenderId();
        if (protocol != Protocol)
        {
            RpcId(from, MethodName.Kick, $"Different game versions (host {Protocol}, you {protocol}). Update both copies.");
            return;
        }
        if (SaveSystem.CharacterFromJson(recordJson) is not CharacterRecord rec)
        {
            RpcId(from, MethodName.Kick, "Your character could not be read.");
            return;
        }
        _records[from] = rec;
        _members.RemoveAll(m => m.Peer == from);
        _members.Add(MemberFor(from, rec));
        GD.Print($"Net: {rec.Name} (level {rec.Level}) joined as peer {from}.");
        Status = $"{rec.Name} joined.";
        SendRoster();
    }

    /// <summary>Host to everyone: who is in the game.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Roster(string json)
    {
        List<NetMember>? list = JsonSerializer.Deserialize<List<NetMember>>(json);
        _members.Clear();
        if (list is not null) _members.AddRange(list);
        Status = $"In the lobby: {_members.Count} player(s). The host starts the game.";
        Changed?.Invoke();
    }

    /// <summary>Host to one guest: you cannot join, and why.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Kick(string why) => End(why);

    private void SendRoster()
    {
        Rpc(MethodName.Roster, JsonSerializer.Serialize(_members));
        Changed?.Invoke();
    }

    private void OnPeerLeft(long id)
    {
        GD.Print($"Net: peer {id} left.");
        if (Mode != Role.Host) return;
        NetMember? gone = _members.Find(m => m.Peer == id);
        _members.RemoveAll(m => m.Peer == id);
        _records.Remove(id);
        if (gone is not null) Status = $"{gone.Name} left.";
        SendRoster();
    }

    private void End(string why)
    {
        GD.Print($"Net: session ended: {why}");
        Leave(quiet: true);
        Status = why;
        Ended?.Invoke(why);
        Changed?.Invoke();
    }

    private static NetMember MemberFor(long peer, CharacterRecord r) => new()
    {
        Peer = peer, Name = r.Name.Length > 0 ? r.Name : "CLOWN", Look = r.Look.Length > 0 ? r.Look : "pip", Level = r.Level, Weapon = r.Weapon,
    };
}
