namespace ClownSquad.Input;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;
using ClownSquad.World;

/// <summary>
/// A player-side bot that plays like a careful player: it reads every
/// telegraph on the floor, every burning or glass-strewn patch, every shot in
/// the air and Bertie's belly-flops, and gets out of the way first, rolling
/// through what it cannot outrun and jumping rings of fire. When it is safe it
/// picks a target, comes in on its lane (round the back of anything blocking),
/// and swings a full combo. Used by the automated probes and the demo
/// recording, so late worlds and bosses can be tested without a human.
/// </summary>
public sealed class SmartBot : IInputSource
{
    private readonly PlayerFighter _self;
    private ulong _nextPress;
    private ulong _nextDodge;
    private ulong _nextJump;
    private Vector3 _escape;
    private ulong _escapeUntil;

    /// <summary>Walk right when there is nothing to fight (scrolling stages).</summary>
    public bool AdvanceWhenIdle { get; set; } = true;

    private const float AttackRange = 1.55f;
    private const float LaneTolerance = 0.35f;

    public SmartBot(PlayerFighter self) => _self = self;

    public string Describe() => "BOT";

    public InputCommand Poll()
    {
        if (_self.IsDead || !_self.IsInsideTree()) return InputCommand.None;
        SceneTree tree = _self.GetTree();
        ulong now = GameClock.Ms;
        Vector3 me = Flat(_self.GlobalPosition);

        // Rings of fire: jump them as they sweep past.
        foreach (Node n in tree.GetNodesInGroup(Hazard.Group))
            if (n is Hazard h && h.RingNear(me) && _self.Grounded && now >= _nextJump)
            {
                _nextJump = now + 600;
                return new InputCommand(Vector2.Zero, false, false, true);
            }

        // Shots: roll out of the path of anything about to arrive.
        foreach (Node n in tree.GetNodesInGroup(EnemyShot.Group))
            if (n is EnemyShot s && Incoming(s, me, out Vector3 away) && now >= _nextDodge)
            {
                _nextDodge = now + 700;
                return new InputCommand(new Vector2(away.X, away.Z), false, true);
            }

        // Standing in danger: run for the safest nearby ground, rolling if it is about to land.
        float here = Danger(me, out float urgency);
        if (here > 0f)
        {
            Vector3 dir = Escape(me);
            _escape = dir;
            _escapeUntil = now + 250;
            if (urgency > 0.72f && now >= _nextDodge && _self.Grounded)
            {
                _nextDodge = now + 650;
                return new InputCommand(new Vector2(dir.X, dir.Z), false, true);
            }
            return new InputCommand(new Vector2(dir.X, dir.Z), false, false);
        }
        if (now < _escapeUntil) return new InputCommand(new Vector2(_escape.X, _escape.Z) * 0.6f, false, false);

        // Safe: pick a fight.
        Fighter? target = PickTarget(tree, me);
        if (target is null)
            return AdvanceWhenIdle ? new InputCommand(new Vector2(1f, 0f), false, false) : InputCommand.None;

        Vector3 t = Flat(target.GlobalPosition);
        float side = me.X >= t.X ? 1f : -1f;
        // A blocker is hit from behind: go round to the side it is not facing.
        if (target is CastFighter cf && cf.IsGuarding) side = -cf.Facing.X;
        // A pop gun fights from down the lane; everything else up close.
        float reach = _self.Weapon == ClownSquad.Progression.WeaponKind.PopGun ? 5.5f : AttackRange + target.BodyHalfWidth * 0.6f;
        Vector3 spot = new(t.X + side * reach * 0.8f, 0f, t.Z);
        // Pinned at the screen edge with the target against it: come in from the other side.
        if (Arena.Current is Arena edge && Flat(edge.Clamp(spot, _self.BodyHeight, _self.BodyHalfWidth, out _) - spot).Length() > 0.05f)
        {
            side = -side;
            spot = new Vector3(t.X + side * reach * 0.8f, 0f, t.Z);
        }
        if (Danger(spot, out _) > 0f) spot = new Vector3(t.X + side * (reach + 1.5f), 0f, t.Z);   // wait out of the danger

        Vector3 to = spot - me;
        float dx = t.X - me.X, dz = t.Z - me.Z;
        bool inRange = Mathf.Abs(dx) <= reach + 0.25f && Mathf.Abs(dx) > 0.2f;
        bool onLane = Mathf.Abs(dz) <= LaneTolerance;
        bool facing = Mathf.Sign(dx) == Mathf.Sign(_self.Facing.X);

        if (inRange && onLane && target.CanBeHit())
        {
            if (!facing) return new InputCommand(new Vector2(Mathf.Sign(dx), 0f), false, false);     // turn round
            if (now >= _nextPress)
            {
                _nextPress = now + 130;      // fast enough to chain the whole combo
                return new InputCommand(Vector2.Zero, true, false);
            }
            return InputCommand.None;
        }

        Vector2 move = to.Length() > 0.15f ? new Vector2(to.X, to.Z).Normalized() : Vector2.Zero;
        // Never step into a telegraph on the way in.
        Vector3 next = me + new Vector3(move.X, 0f, move.Y) * 0.6f;
        if (Danger(next, out _) > 0f) move = Vector2.Zero;
        return new InputCommand(move, false, false);
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);

    /// <summary>How dangerous a spot is, and how soon the worst of it lands (0..1).</summary>
    private float Danger(Vector3 p, out float urgency)
    {
        SceneTree tree = _self.GetTree();
        float d = 0f;
        urgency = 0f;
        foreach (Node n in tree.GetNodesInGroup(Telegraph.Group))
            if (n is Telegraph t && IsInstanceValid(t) && t.Covers(p))
            {
                d += 1f + t.Urgency * 2f;
                urgency = Mathf.Max(urgency, t.Urgency);
            }
        foreach (Node n in tree.GetNodesInGroup(Hazard.Group))
        {
            if (n is Hazard h && h.Covers(p)) { d += 1.5f; urgency = Mathf.Max(urgency, 0.5f); }
            if (n is GlassField g && g.Covers(p)) d += 1f;
        }
        foreach (Node n in tree.GetNodesInGroup(Fighter.Group))
            if (n is BossClown b && b.SlamComing && Flat(b.GlobalPosition - p).Length() < BossClown.SlamReach + 0.5f)
            {
                d += 3f;
                urgency = Mathf.Max(urgency, b.Hanging ? 0.8f : 0.5f);
            }
        return d;
    }

    private static bool IsInstanceValid(GodotObject o) => GodotObject.IsInstanceValid(o);

    /// <summary>The safest direction to run in from here, preferring the way back toward the fight.</summary>
    private Vector3 Escape(Vector3 me)
    {
        Vector3 best = Vector3.Zero;
        float bestScore = float.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            float a = Mathf.Tau * i / 16f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float score = 0f;
            foreach (float dist in new[] { 1.0f, 2.2f, 3.4f })
            {
                Vector3 p = me + dir * dist;
                if (Arena.Current is Arena arena)
                {
                    Vector3 c = arena.Clamp(p, _self.BodyHeight, _self.BodyHalfWidth, out _);
                    if (Flat(c - p).Length() > 0.05f) { score += 6f; continue; }    // a wall: no escape that way
                }
                score += Danger(p, out _) * (dist < 1.5f ? 1.5f : 1f);
            }
            score += i % 2 == 0 ? 0f : 0.05f;        // a nudge toward the main axes
            if (score < bestScore) { bestScore = score; best = dir; }
        }
        return best == Vector3.Zero ? new Vector3(0f, 0f, 1f) : best;
    }

    /// <summary>Whether a shot will pass through this spot soon, and which way is out of it.</summary>
    private bool Incoming(EnemyShot s, Vector3 me, out Vector3 away)
    {
        away = Vector3.Zero;
        for (float t = 0.08f; t <= 0.5f; t += 0.08f)
        {
            Vector3 p = s.Predict(t);
            if (p.Y > _self.BodyHeight + 0.3f || p.Y < -0.2f) continue;
            Vector3 d = Flat(p) - me;
            if (d.Length() > s.HitRadius + 0.6f) continue;
            Vector3 v = Flat(s.Velocity);
            away = v.LengthSquared() > 0.01f ? new Vector3(-v.Z, 0f, v.X).Normalized() : new Vector3(0f, 0f, 1f);
            if (away.Dot(-d) < 0f) away = -away;          // roll away from its line, not across it
            return true;
        }
        return false;
    }

    /// <summary>The nearest hittable enemy, weighted toward the wounded; bosses that cannot be hit are skipped.</summary>
    private Fighter? PickTarget(SceneTree tree, Vector3 me)
    {
        Fighter? best = null;
        float bestScore = float.MaxValue;
        foreach (Node n in tree.GetNodesInGroup(Fighter.Group))
        {
            if (n is not Fighter f || !f.Hostile || f.IsDead || !f.CanBeHit()) continue;
            if (f.GlobalPosition.Y > 1.5f) continue;                        // up on a trapeze
            float dist = Flat(f.GlobalPosition) .DistanceTo(me);
            float score = dist + f.Health / Mathf.Max(1f, f.MaxHealth) * 2f;
            if (score < bestScore) { bestScore = score; best = f; }
        }
        return best;
    }
}
