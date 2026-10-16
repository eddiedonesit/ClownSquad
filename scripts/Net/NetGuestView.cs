namespace ClownSquad.Net;

using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;
using ClownSquad.Input;

/// <summary>
/// A guest's half of playing a stage online (ADR 0010, phase 2): no fight
/// runs here. The stage's scenery is built as usual; everything that moves is
/// a puppet of the host's, built and updated from <see cref="NetMirror"/>
/// descriptions and snapshots, smoothed between them. The guest's controls go
/// to the host every frame. Effects arrive as <see cref="Fx"/> events and are
/// replayed. The camera follows the host's.
/// </summary>
public partial class NetGuestView : Node3D
{
    private sealed class Puppet
    {
        public int Version;
        public List<Node3D> Nodes = new();
        public Transform3D Target;
        public bool Placed;
    }

    private readonly Action<float> _camera;
    private readonly System.Collections.Generic.Dictionary<int, Puppet> _puppets = new();
    private readonly List<LocalDeviceInput> _devices = new();
    private readonly NetHud _hud = new();
    private int _attacks, _dodges, _jumps, _specials;
    private int _lastSeq = -1;
    private float _camTarget;
    private bool _camPlaced;
    private float _camX;
    private Func<InputCommand>? _scripted;

    // Prediction: the guest's own clown is drawn ahead of the last snapshot by
    // the walking the host has not shown yet, so it answers the stick at once.
    // The lead fades as snapshots catch up (over about one round trip).
    private Vector3 _lead;
    private Vector2 _lastMove;
    // "-- lag 120" (debug builds): pretend the network adds this many ms each way, to test with.
    private readonly double _lag;
    private readonly Queue<(double due, Action act)> _delayed = new();
    private Action<byte[]>? _onSnapshot;

    /// <summary>Snapshots taken in, and the puppets standing right now, for tests.</summary>
    public int Received { get; private set; }
    public int PuppetCount => _puppets.Count;
    public int Effects { get; private set; }
    public int SpritePuppets
    {
        get
        {
            int n = 0;
            foreach (Puppet p in _puppets.Values) foreach (Node3D node in p.Nodes) if (node is AnimatedSprite3D) n++;
            return n;
        }
    }

    public NetGuestView(Action<float> camera)
    {
        _camera = camera;
        string[] args = OS.IsDebugBuild() ? OS.GetCmdlineUserArgs() : System.Array.Empty<string>();
        int at = System.Array.IndexOf(args, "lag");
        if (at >= 0 && at + 1 < args.Length && double.TryParse(args[at + 1], out double ms)) _lag = ms / 1000.0;
    }

    private void Later(Action act)
    {
        if (_lag <= 0) { act(); return; }
        _delayed.Enqueue((Time.GetTicksMsec() / 1000.0 + _lag, act));
    }

    /// <summary>Tests drive the guest's clown from a script instead of the keyboard.</summary>
    public void Script(Func<InputCommand> input) => _scripted = input;

    public override void _Ready()
    {
        _devices.Add(new LocalDeviceInput(LocalDeviceInput.Keyboard));
        foreach (int pad in Godot.Input.GetConnectedJoypads()) _devices.Add(new LocalDeviceInput(pad));
        AddChild(_hud);
        if (NetSession.Instance is NetSession net)
        {
            net.SimulatedLag = _lag;
            _onSnapshot = b => Later(() => OnSnapshot(b));
            net.SnapshotReceived += _onSnapshot;
            net.DescribeReceived += OnDescribe;
            net.FxReceived += OnFx;
            net.StageResult += _hud.ShowBanner;
        }
    }

    public override void _ExitTree()
    {
        if (NetSession.Instance is not NetSession net) return;

        if (_onSnapshot is not null) net.SnapshotReceived -= _onSnapshot;
        net.DescribeReceived -= OnDescribe;
        net.FxReceived -= OnFx;
        net.StageResult -= _hud.ShowBanner;
    }

    // ------------------------------------------------------------ the controls

    public override void _PhysicsProcess(double delta)
    {
        if (NetSession.Instance is not { Mode: NetSession.Role.Guest } net) return;
        Vector2 move = Vector2.Zero;
        bool held = false;
        void Take(InputCommand c)
        {
            if (c.Move.LengthSquared() > move.LengthSquared()) move = c.Move;
            held |= c.AttackHeld;
            if (c.AttackPressed) _attacks++;
            if (c.DodgePressed) _dodges++;
            if (c.JumpPressed) _jumps++;
            if (c.SpecialPressed) _specials++;
        }
        if (_scripted is not null) Take(_scripted());
        else foreach (LocalDeviceInput d in _devices) Take(d.Poll());
        int counts = RemoteInput.Pack(_attacks, _dodges, _jumps, _specials);
        Later(() => net.SendInput(move, held, counts));
        _lastMove = move;

        // Walk the clown ahead on this screen while it is free to walk (idle or walking on the host).
        float dt = (float)delta;
        bool free = MyPuppetClip() is string clip && (clip.Contains("walk") || clip.Contains("idle"));
        if (free) _lead += new Vector3(move.X, 0f, move.Y) * net.MySpeed * dt;
        float settle = (float)Mathf.Max(net.Rtt + 1.0 / NetHost.Rate, 0.05);
        _lead *= Mathf.Exp(-dt / (free ? settle : settle * 0.3f));
        if (_lead.Length() > net.MySpeed * 0.5f) _lead = _lead.Normalized() * net.MySpeed * 0.5f;
    }

    // -------------------------------------------------------------- the picture

    private void OnDescribe(int id, int version, byte[] bytes)
    {
        if (_puppets.TryGetValue(id, out Puppet? old))
        {
            if (old.Version >= version) return;
            if (old.Nodes.Count > 0 && IsInstanceValid(old.Nodes[0])) old.Nodes[0].QueueFree();
        }
        List<Node3D> nodes = NetMirror.Build(bytes);
        if (nodes.Count == 0) return;
        AddChild(nodes[0]);
        nodes[0].Visible = false;                  // until its first state arrives
        var p = new Puppet { Version = version, Nodes = nodes };
        if (old is not null) { p.Target = old.Target; p.Placed = old.Placed; if (p.Placed) nodes[0].GlobalTransform = old.Target; }
        _puppets[id] = p;
    }

    private void OnSnapshot(byte[] bytes)
    {
        Variant v;
        try { v = GD.BytesToVar(NetMirror.Unpack(bytes)); }
        catch (System.IO.InvalidDataException) { return; }
        if (v.VariantType != Variant.Type.Array) return;
        Godot.Collections.Array snap = v.AsGodotArray();
        int seq = (int)snap[0];
        if (seq <= _lastSeq) return;
        _lastSeq = seq;
        Received++;

        _camTarget = (float)snap[1];
        if (!_camPlaced) { _camX = _camTarget; _camPlaced = true; }
        _hud.Show(snap[2].AsGodotArray());

        var live = new HashSet<int>();
        foreach (Variant rv in snap[3].AsGodotArray())
        {
            Godot.Collections.Array root = rv.AsGodotArray();
            int id = (int)root[0];
            live.Add(id);
            if (!_puppets.TryGetValue(id, out Puppet? p) || p.Version != (int)root[1]) continue;
            foreach (Variant cv in root[2].AsGodotArray())
            {
                Godot.Collections.Array change = cv.AsGodotArray();
                int index = (int)change[0];
                if (index >= p.Nodes.Count || !IsInstanceValid(p.Nodes[index])) continue;
                Godot.Collections.Array st = change[1].AsGodotArray();
                if (index == 0)
                {
                    p.Target = (Transform3D)st[0];
                    p.Nodes[0].Visible = (bool)st[1];
                    // Placed outright the first time, and on a jump too big to be movement (a respawn).
                    if (!p.Placed || p.Nodes[0].GlobalPosition.DistanceTo(p.Target.Origin) > 3f)
                    {
                        p.Nodes[0].GlobalTransform = p.Target;
                        p.Placed = true;
                    }
                }
                else NetMirror.Apply(p.Nodes[index], st);
            }
        }
        var gone = new List<int>();
        foreach ((int id, Puppet p) in _puppets) if (!live.Contains(id)) gone.Add(id);
        foreach (int id in gone)
        {
            if (_puppets[id].Nodes.Count > 0 && IsInstanceValid(_puppets[id].Nodes[0])) _puppets[id].Nodes[0].QueueFree();
            _puppets.Remove(id);
        }
    }

    private void OnFx(FxEvent e)
    {
        Effects++;
        Fx.Replay(e, GetParent());
    }

    /// <summary>The clip the guest's own clown is showing, from the host.</summary>
    private string? MyPuppetClip()
    {
        if (NetSession.Instance is not { MyFighter: > 0 } net || !_puppets.TryGetValue(net.MyFighter, out Puppet? p)) return null;
        foreach (Node3D n in p.Nodes) if (n is AnimatedSprite3D s) return s.Animation;
        return null;
    }

    public override void _Process(double delta)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        while (_delayed.Count > 0 && _delayed.Peek().due <= now) _delayed.Dequeue().act();
        if (NetSession.Instance is NetSession pn) _hud.ShowPing(pn.Rtt);
        // Snapshots come thirty times a second; in between, everything glides
        // the rest of the way, which hides the steps without adding much lag.
        float k = 1f - Mathf.Exp(-22f * (float)delta);
        foreach (Puppet p in _puppets.Values)
        {
            if (!p.Placed || p.Nodes.Count == 0 || !IsInstanceValid(p.Nodes[0])) continue;
            Node3D root = p.Nodes[0];
            bool mine = NetSession.Instance is { MyFighter: > 0 } me && _puppets.TryGetValue(me.MyFighter, out Puppet? own) && own == p;
            Transform3D target = p.Target;
            if (mine) target.Origin += _lead;
            root.GlobalTransform = root.GlobalTransform.InterpolateWith(target, k);
            // and it turns the moment the stick does
            if (mine && Mathf.Abs(_lastMove.X) > 0.3f)
                foreach (Node3D n in p.Nodes)
                    if (n is AnimatedSprite3D s)
                    {
                        bool flip = _lastMove.X < 0f;
                        s.FlipH = flip;
                        if (s.MaterialOverride is ShaderMaterial sm && sm.Shader is not null) sm.SetShaderParameter("flip", flip);
                    }
        }
        if (_camPlaced)
        {
            _camX = Mathf.Lerp(_camX, _camTarget, k);
            _camera(_camX);
        }
    }
}
