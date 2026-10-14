namespace ClownSquad.Input;

using Godot;
using ClownSquad.Actors;

/// <summary>
/// Drives a fighter toward the nearest opposing fighter and swings when close.
/// Per ADR 0003 this is just another input source: nothing downstream knows the
/// commands came from code rather than a gamepad.
/// </summary>
public sealed class BrawlerAi : IInputSource
{
    private readonly Fighter _self;
    private readonly float _attackRange;
    private readonly float _backoffRange;
    private readonly ulong _attackCooldownMs;

    private ulong _nextAttackMs;

    public BrawlerAi(Fighter self, float attackRange = 1.8f, float backoffRange = 1.1f,
                     ulong attackCooldownMs = 1500)
    {
        _self = self;
        _attackRange = attackRange;
        _backoffRange = backoffRange;
        _attackCooldownMs = attackCooldownMs;
    }

    public string Describe() => "AI";

    public InputCommand Poll()
    {
        Fighter? target = FindTarget();
        if (target is null || _self.IsDead) return InputCommand.None;

        Vector3 delta = target.GlobalPosition - _self.GlobalPosition;
        delta.Y = 0f;
        float dist = delta.Length();
        if (dist < 0.01f) return InputCommand.None;

        Vector3 dir = delta / dist;
        Vector2 move = Vector2.Zero;
        bool attack = false;

        if (dist > _attackRange)
        {
            move = new Vector2(dir.X, dir.Z);
        }
        else if (dist < _backoffRange)
        {
            // Too close to swing comfortably; give ground rather than overlap.
            move = new Vector2(-dir.X, -dir.Z);
        }
        else
        {
            ulong now = Time.GetTicksMsec();
            if (now >= _nextAttackMs)
            {
                attack = true;
                _nextAttackMs = now + _attackCooldownMs;
            }
        }

        return new InputCommand(move, attack, false, false);
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
