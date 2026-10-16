namespace ClownSquad.Net;

using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

/// <summary>
/// The host's half of playing a stage online (ADR 0010, phase 2). Lives in
/// the fight scene; thirty times a second it sends every guest a snapshot of
/// what is on screen (through <see cref="NetMirror"/>), the camera, and the
/// HUD's numbers, and it forwards every <see cref="Fx"/> effect as it plays.
/// The fight itself runs exactly as offline.
/// </summary>
public partial class NetHost : Node
{
    public const float Rate = 30f;
    private const int KeyframeEvery = 15;          // each thing sent whole twice a second (staggered), to heal dropped packets

    private sealed class Tracked
    {
        public int Version;
        public long Signature;
        public int Fresh;                          // snapshots left that send everything, after a description
        public List<(Node3D node, int parent)> Nodes = new();
        public List<byte[]?> Last = new();
    }

    private readonly Func<float> _cameraX;
    private readonly Func<Godot.Collections.Array> _hud;
    private readonly System.Collections.Generic.Dictionary<int, Tracked> _tracked = new();
    private double _acc;
    private int _seq;

    /// <summary>Snapshots sent, for tests.</summary>
    public int Sent { get; private set; }

    public NetHost(Func<float> cameraX, Func<Godot.Collections.Array> hud)
    {
        _cameraX = cameraX;
        _hud = hud;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready() => Fx.Emitted += Forward;

    public override void _ExitTree() => Fx.Emitted -= Forward;

    private static void Forward(FxEvent e) => NetSession.Instance?.SendFx(e);

    public override void _Process(double delta)
    {
        if (NetSession.Instance is not { Mode: NetSession.Role.Host } net || net.Members.Count < 2) return;
        _acc += delta;
        if (_acc < 1.0 / Rate) return;
        _acc = 0;
        net.SendSnapshot(Snapshot(net));
        Sent++;
    }

    private byte[] Snapshot(NetSession net)
    {
        var roots = new Godot.Collections.Array();
        var live = new HashSet<int>();
        foreach ((int id, Node node) in NetRegistry.Entries())
        {
            if (node is not Node3D n3 || !n3.IsInsideTree()) continue;
            live.Add(id);
            List<(Node3D node, int parent)> nodes = NetMirror.Walk(n3);
            long sig = NetMirror.Signature(nodes);
            if (!_tracked.TryGetValue(id, out Tracked? t)) _tracked[id] = t = new Tracked();
            if (t.Signature != sig || t.Nodes.Count == 0)
            {
                t.Version++;
                t.Signature = sig;
                t.Nodes = nodes;
                t.Last = new List<byte[]?>(new byte[nodes.Count][]);
                t.Fresh = 3;
                net.SendDescribe(id, t.Version, NetMirror.Describe(nodes));
            }
            bool all = (id + _seq) % KeyframeEvery == 0 || t.Fresh > 0;
            if (t.Fresh > 0) t.Fresh--;
            var changes = new Godot.Collections.Array();
            for (int i = 0; i < t.Nodes.Count; i++)
            {
                Node3D n = t.Nodes[i].node;
                if (!IsInstanceValid(n)) continue;
                Godot.Collections.Array st = NetMirror.State(n, i == 0);
                byte[] b = GD.VarToBytes(st);
                if (!all && i > 0 && t.Last[i] is byte[] last && Same(last, b)) continue;
                t.Last[i] = b;
                changes.Add(new Godot.Collections.Array { i, st });
            }
            roots.Add(new Godot.Collections.Array { id, t.Version, changes });
        }
        var gone = new List<int>();
        foreach (int id in _tracked.Keys) if (!live.Contains(id)) gone.Add(id);
        foreach (int id in gone) _tracked.Remove(id);
        return NetMirror.Pack(GD.VarToBytes(new Godot.Collections.Array { _seq++, _cameraX(), _hud(), roots }));
    }

    private static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
}
