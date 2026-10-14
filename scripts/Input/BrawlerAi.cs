namespace ClownSquad.Input;

using Godot;
using ClownSquad.Actors;
using ClownSquad.Combat;

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
    private readonly ulong _specialCooldownMs;
    private readonly float _specialRange;
    private readonly float _depthTolerance;

    private ulong _nextAttackMs;
    private ulong _nextSpecialMs;

    public BrawlerAi(Fighter self, float attackRange = 1.8f, float backoffRange = 1.1f,
                     ulong attackCooldownMs = 800, ulong specialCooldownMs = 0,
                     float specialRange = 5.5f, float depthTolerance = 0.55f)
    {
        _self = self;
        _attackRange = attackRange;
        _backoffRange = backoffRange;
        _attackCooldownMs = attackCooldownMs;
        _specialCooldownMs = specialCooldownMs;
        _specialRange = specialRange;
        _depthTolerance = depthTolerance;
        // Stagger the first special so it is not thrown the instant combat opens.
        _nextSpecialMs = Time.GetTicksMsec() + specialCooldownMs / 2;
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
        ulong nowMs = Time.GetTicksMsec();

        // Specials reach further than a swing, so they are checked first and can
        // open from outside melee range - that is what makes them threatening.
        if (_specialCooldownMs > 0 && dist <= _specialRange && nowMs >= _nextSpecialMs)
        {
            _nextSpecialMs = nowMs + _specialCooldownMs;
            return new InputCommand(Vector2.Zero, false, false, false, true);
        }

        float depthOffset = Mathf.Abs(delta.Z);

        if (dist > _attackRange)
        {
            move = new Vector2(dir.X, dir.Z);
        }
        else if (depthOffset > _depthTolerance)
        {
            // In range but off the lane: step along depth only. Swinging from
            // here would whiff, which reads as the enemy being broken.
            move = new Vector2(0f, Mathf.Sign(delta.Z));
        }
        else if (dist < _backoffRange)
        {
            // Too close to swing comfortably; give ground rather than overlap.
            move = new Vector2(-dir.X, -dir.Z);
        }
        else
        {
            // Only so many enemies may swing at once; the rest hold position.
            if (nowMs >= _nextAttackMs && AttackTokens.TryAcquire(_self, 900))
            {
                attack = true;
                _nextAttackMs = nowMs + _attackCooldownMs;
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
