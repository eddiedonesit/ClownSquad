namespace ClownSquad.Input;

using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;
using ClownSquad.World;

/// <summary>
/// Drives a fighter against the nearest opposing fighter. Per ADR 0003 this is
/// just another input source: nothing downstream knows the commands came from
/// code rather than a gamepad.
///
/// Enemies take turns. One holding an attack token closes in, lines up on the
/// target's depth lane and swings; the rest wait in a loose ring, spaced apart,
/// until a token frees up. That turn-taking is what gives a crowd readable
/// openings instead of a dogpile.
/// </summary>
public sealed class BrawlerAi : IInputSource
{
    private readonly Fighter _self;
    private readonly float _attackRange;
    private readonly float _backoffRange;
    private readonly ulong _attackCooldownMs;
    private readonly ulong _specialCooldownMs;
    private readonly float _specialRange;
    private readonly float _depthTolerance;

    // Per-enemy quirks, fixed at birth, so a crowd does not move in lockstep.
    private readonly float _standoff;
    private readonly float _laneBias;

    private ulong _nextAttackMs;
    private ulong _nextSpecialMs;

    /// <summary>Above zero, this fighter prefers to stay this far away (throwers), only closing in when it has to.</summary>
    private readonly float _keepAway;

    public BrawlerAi(Fighter self, float attackRange = 1.8f, float backoffRange = 1.1f,
                     ulong attackCooldownMs = 800, ulong specialCooldownMs = 0,
                     float specialRange = 5.5f, float depthTolerance = 0.55f, float keepAway = 0f)
    {
        _self = self;
        _keepAway = keepAway;
        _attackRange = attackRange;
        _backoffRange = backoffRange;
        _specialCooldownMs = specialCooldownMs;
        _specialRange = specialRange;
        _depthTolerance = depthTolerance;

        ulong id = self.GetInstanceId();
        _attackCooldownMs = (ulong)(attackCooldownMs * (0.8f + (id % 7) / 7f * 0.5f));
        _standoff = attackRange + 1.3f + (id % 3) * 0.5f;
        _laneBias = ((int)(id % 5) - 2) * 0.6f;

        // Stagger the first special so it is not thrown the instant combat opens.
        _nextSpecialMs = GameClock.Ms + specialCooldownMs / 2;
    }

    public string Describe() => "AI";

    /// <summary>Walk right when there is no one to fight. For AI driving a player through a stage.</summary>
    public bool AdvanceWhenIdle { get; set; }

    public InputCommand Poll()
    {
        Fighter? target = FindTarget();
        if (_self.IsDead) return InputCommand.None;
        // With nothing to fight, a player-side AI walks on, the way a player
        // heads right through a scrolling stage to find the next group.
        if (target is null) return AdvanceWhenIdle ? new InputCommand(new Vector2(1f, 0f), false, false, false, false, false) : InputCommand.None;

        Vector3 delta = target.GlobalPosition - _self.GlobalPosition;
        delta.Y = 0f;
        float dist = delta.Length();
        if (dist < 0.01f) return InputCommand.None;

        Vector3 dir = delta / dist;
        ulong now = GameClock.Ms;

        // Specials reach further than a swing and are checked first, so they
        // can open from outside melee range.
        if (_specialCooldownMs > 0 && dist <= _specialRange && now >= _nextSpecialMs)
        {
            _nextSpecialMs = now + _specialCooldownMs;
            return new InputCommand(Vector2.Zero, false, false, false, true);
        }

        Vector2 move = Vector2.Zero;
        bool attack = false;

        // Throwers back off when crowded and hover at their range, lined up
        // roughly on the target's lane, so their throws come down the lane.
        if (_keepAway > 0f && dist < _keepAway * 1.3f)
        {
            if (dist < _keepAway * 0.65f)
                move = new Vector2(-dir.X, -dir.Z);
            else
                move = new Vector2(0f, Mathf.Clamp(delta.Z, -1f, 1f) * 0.6f);
            move += Separation();
            return new InputCommand(move, false, false, false);
        }

        // Player-side AI (used by tests) is never rationed; enemies need a token.
        bool rested = now >= _nextAttackMs;
        bool engaged = rested && (!_self.Hostile || AttackTokens.TryAcquire(_self, 500));

        if (engaged)
        {
            if (dist > _attackRange)
            {
                move = new Vector2(dir.X, dir.Z);
            }
            else if (Mathf.Abs(delta.Z) > _depthTolerance)
            {
                // In range but off the lane: step along depth only. Swinging
                // from here would whiff.
                move = new Vector2(0f, Mathf.Sign(delta.Z));
            }
            else if (dist < _backoffRange)
            {
                move = new Vector2(-dir.X, -dir.Z);
            }
            else
            {
                attack = true;
                _nextAttackMs = now + _attackCooldownMs;   // and the token lapses
            }
        }
        else
        {
            // Wait on our own side of the target, offset in depth.
            float side = _self.GlobalPosition.X >= target.GlobalPosition.X ? 1f : -1f;
            Vector3 hold = target.GlobalPosition + new Vector3(side * _standoff, 0f, _laneBias);
            Vector3 toHold = hold - _self.GlobalPosition;
            toHold.Y = 0f;
            if (toHold.Length() > 0.4f)
            {
                Vector3 h = toHold.Normalized() * 0.75f;
                move = new Vector2(h.X, h.Z);
            }
        }

        move += Separation();
        return new InputCommand(move, attack, false, false);
    }

    /// <summary>Steer away from allies standing too close, so a crowd spreads out.</summary>
    private Vector2 Separation()
    {
        const float Personal = 1.15f;
        Vector2 push = Vector2.Zero;
        foreach (Node node in _self.GetTree().GetNodesInGroup(Fighter.Group))
        {
            if (node is not Fighter f || f == _self || f.Hostile != _self.Hostile || f.IsDead) continue;
            Vector3 away = _self.GlobalPosition - f.GlobalPosition;
            away.Y = 0f;
            float d = away.Length();
            if (d >= Personal) continue;
            // Two bodies on exactly the same spot still need a direction to part in.
            Vector3 n = d < 0.01f ? new Vector3(_laneBias >= 0f ? 1f : -1f, 0f, 0.5f) : away / d;
            push += new Vector2(n.X, n.Z) * (1f - d / Personal);
        }
        return push * 0.9f;
    }

    /// <summary>Nearest living fighter on the opposing side.</summary>
    private Fighter? FindTarget()
    {
        Fighter? best = null;
        float bestDist = float.MaxValue;
        foreach (Node node in _self.GetTree().GetNodesInGroup(Fighter.Group))
        {
            if (node is not Fighter f || f == _self || f.IsDead) continue;
            if (f.Hostile == _self.Hostile) continue;
            float d = f.GlobalPosition.DistanceTo(_self.GlobalPosition);
            if (d < bestDist) { bestDist = d; best = f; }
        }
        return best;
    }
}
