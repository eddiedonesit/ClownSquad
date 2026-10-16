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

    public NetGuestView(Action<float> camera) => _camera = camera;

    /// <summary>Tests drive the guest's clown from a script instead of the keyboard.</summary>
    public void Script(Func<InputCommand> input) => _scripted = input;

    public override void _Ready()
    {
        _devices.Add(new LocalDeviceInput(LocalDeviceInput.Keyboard));
        foreach (int pad in Godot.Input.GetConnectedJoypads()) _devices.Add(new LocalDeviceInput(pad));
        AddChild(_hud);
        if (NetSession.Instance is NetSession net)
        {
            net.SnapshotReceived += OnSnapshot;
            net.DescribeReceived += OnDescribe;
            net.FxReceived += OnFx;
            net.StageResult += _hud.ShowBanner;
        }
    }

    public override void _ExitTree()
    {
        if (NetSession.Instance is not NetSession net) return;
        net.SnapshotReceived -= OnSnapshot;
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
        net.SendInput(move, held, RemoteInput.Pack(_attacks, _dodges, _jumps, _specials));
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

    public override void _Process(double delta)
    {
        // Snapshots come thirty times a second; in between, everything glides
        // the rest of the way, which hides the steps without adding much lag.
        float k = 1f - Mathf.Exp(-22f * (float)delta);
        foreach (Puppet p in _puppets.Values)
        {
            if (!p.Placed || p.Nodes.Count == 0 || !IsInstanceValid(p.Nodes[0])) continue;
            Node3D root = p.Nodes[0];
            root.GlobalTransform = root.GlobalTransform.InterpolateWith(p.Target, k);
        }
        if (_camPlaced)
        {
            _camX = Mathf.Lerp(_camX, _camTarget, k);
            _camera(_camX);
        }
    }
}
