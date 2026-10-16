namespace ClownSquad.Net;

using System.Collections.Generic;
using Godot;
using Steamworks;
using Steamworks.Data;

/// <summary>
/// Godot's high-level multiplayer carried over Steam peer-to-peer instead of
/// ENet (docs/STEAM.md §4): friends join from the Steam friends list or an
/// invite, with no IP addresses and no port forwarding; Steam relays the
/// traffic when it has to. Everything above it (NetSession's RPCs, the lobby,
/// playing stages) is unchanged.
///
/// The host is peer 1. A guest's peer number comes from its Steam ID. On the
/// wire every packet starts with one byte saying what it is: data (then the
/// Godot channel and transfer mode), or the little handshake: a guest's HELLO,
/// the host's WELCOME, and BYE when either leaves.
/// </summary>
public partial class SteamPeer : MultiplayerPeerExtension
{
    private const byte Data = 0, Hello = 1, Welcome = 2, Bye = 3;
    private const int SteamChannel = 0;

    private readonly bool _server;
    private readonly SteamId _host;
    private readonly int _id;
    private readonly Dictionary<int, SteamId> _peers = new();
    private readonly Queue<(int from, int channel, TransferModeEnum mode, byte[] data)> _in = new();
    private ConnectionStatus _status;
    private int _target;
    private int _channel;
    private TransferModeEnum _mode = TransferModeEnum.Reliable;
    private bool _refuse;
    private double _helloAt;
    private bool _hooked;

    private SteamPeer(bool server, SteamId host)
    {
        _server = server;
        _host = host;
        _id = server ? 1 : IdFor(SteamClient.SteamId);
        _status = server ? ConnectionStatus.Connected : ConnectionStatus.Connecting;
        Hook();
    }

    /// <summary>Host a game: guests reach this machine through Steam.</summary>
    public static SteamPeer CreateHost() => new(true, default);

    /// <summary>Join a friend's game, by their Steam ID.</summary>
    public static SteamPeer CreateClient(SteamId host) => new(false, host);

    /// <summary>A guest's peer number: from its Steam ID, never 0 or 1.</summary>
    private static int IdFor(SteamId id) => (int)(id.Value & 0x3FFFFFFF) | 0x40000000;

    private void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        SteamNetworking.OnP2PSessionRequest += OnSessionRequest;
        SteamNetworking.OnP2PConnectionFailed += OnConnectionFailed;
    }

    private void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        SteamNetworking.OnP2PSessionRequest -= OnSessionRequest;
        SteamNetworking.OnP2PConnectionFailed -= OnConnectionFailed;
    }

    private void OnSessionRequest(SteamId who)
    {
        // A host takes anyone who asks (the lobby is friends-only); a guest only its host.
        if ((_server && !_refuse) || (!_server && who == _host)) SteamNetworking.AcceptP2PSessionWithUser(who);
    }

    private void OnConnectionFailed(SteamId who, P2PSessionError error)
    {
        GD.Print($"Steam: connection to {who} failed ({error}).");
        if (!_server && who == _host) { _status = ConnectionStatus.Disconnected; return; }
        foreach ((int id, SteamId s) in _peers)
            if (s == who) { Drop(id); break; }
    }

    private static void Send(SteamId to, byte[] packet, bool reliable) =>
        SteamNetworking.SendP2PPacket(to, packet, packet.Length, SteamChannel, reliable ? P2PSend.Reliable : P2PSend.UnreliableNoDelay);

    private static void Control(SteamId to, byte kind) => Send(to, new[] { kind }, true);

    // -------------------------------------------------------- the peer API

    public override void _Poll()
    {
        if (_status == ConnectionStatus.Disconnected) return;
        // A guest says hello until the host welcomes it (the first packet also opens the P2P session).
        if (!_server && _status == ConnectionStatus.Connecting)
        {
            double now = Time.GetTicksMsec() / 1000.0;
            if (now - _helloAt > 0.5) { _helloAt = now; Control(_host, Hello); }
        }
        while (SteamNetworking.IsP2PPacketAvailable(SteamChannel))
        {
            P2Packet? got = SteamNetworking.ReadP2PPacket(SteamChannel);
            if (got is not P2Packet p || p.Data is null || p.Data.Length == 0) break;
            Receive(p.SteamId, p.Data);
        }
    }

    private void Receive(SteamId from, byte[] d)
    {
        switch (d[0])
        {
            case Hello when _server:
            {
                int id = IdFor(from);
                Control(from, Welcome);
                if (_refuse || _peers.ContainsKey(id)) return;
                _peers[id] = from;
                EmitSignal(MultiplayerPeer.SignalName.PeerConnected, id);
                return;
            }
            case Welcome when !_server && from == _host:
                if (_status == ConnectionStatus.Connected) return;
                _status = ConnectionStatus.Connected;
                _peers[1] = _host;
                EmitSignal(MultiplayerPeer.SignalName.PeerConnected, 1);
                return;
            case Bye:
                foreach ((int id, SteamId s) in _peers)
                    if (s == from) { Drop(id); break; }
                if (!_server && from == _host) _status = ConnectionStatus.Disconnected;
                return;
            case Data when d.Length >= 3:
            {
                int sender = _server ? IdFor(from) : 1;
                if (!_peers.ContainsKey(sender)) return;           // not (yet) one of ours
                byte[] body = new byte[d.Length - 3];
                System.Array.Copy(d, 3, body, 0, body.Length);
                _in.Enqueue((sender, d[1], (TransferModeEnum)d[2], body));
                return;
            }
        }
    }

    private void Drop(int id)
    {
        if (!_peers.Remove(id, out SteamId s)) return;
        SteamNetworking.CloseP2PSessionWithUser(s);
        EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, id);
    }

    public override Error _PutPacketScript(byte[] buffer)
    {
        if (_status != ConnectionStatus.Connected) return Error.Unconfigured;
        byte[] packet = new byte[buffer.Length + 3];
        packet[0] = Data;
        packet[1] = (byte)_channel;
        packet[2] = (byte)_mode;
        System.Array.Copy(buffer, 0, packet, 3, buffer.Length);
        bool reliable = _mode == TransferModeEnum.Reliable;
        if (!_server) { Send(_host, packet, reliable); return Error.Ok; }
        foreach ((int id, SteamId s) in _peers)
            if (_target == 0 || _target == id || (_target < 0 && -_target != id)) Send(s, packet, reliable);
        return Error.Ok;
    }

    public override int _GetAvailablePacketCount() => _in.Count;
    public override byte[] _GetPacketScript() => _in.Count > 0 ? _in.Dequeue().data : System.Array.Empty<byte>();
    public override int _GetPacketPeer() => _in.Count > 0 ? _in.Peek().from : 0;
    public override int _GetPacketChannel() => _in.Count > 0 ? _in.Peek().channel : 0;
    public override TransferModeEnum _GetPacketMode() => _in.Count > 0 ? _in.Peek().mode : TransferModeEnum.Reliable;
    public override int _GetMaxPacketSize() => 512 * 1024;

    public override void _SetTargetPeer(int peer) => _target = peer;
    public override void _SetTransferChannel(int channel) => _channel = channel;
    public override int _GetTransferChannel() => _channel;
    public override void _SetTransferMode(TransferModeEnum mode) => _mode = mode;
    public override TransferModeEnum _GetTransferMode() => _mode;

    public override bool _IsServer() => _server;
    public override int _GetUniqueId() => _id;
    public override bool _IsServerRelaySupported() => false;
    public override void _SetRefuseNewConnections(bool enable) => _refuse = enable;
    public override bool _IsRefusingNewConnections() => _refuse;
    public override ConnectionStatus _GetConnectionStatus() => _status;

    public override void _DisconnectPeer(int peer, bool force)
    {
        if (_peers.TryGetValue(peer, out SteamId s)) Control(s, Bye);
        Drop(peer);
    }

    public override void _Close()
    {
        foreach (SteamId s in _peers.Values)
        {
            Control(s, Bye);
            SteamNetworking.CloseP2PSessionWithUser(s);
        }
        _peers.Clear();
        _in.Clear();
        _status = ConnectionStatus.Disconnected;
        Unhook();
    }
}
