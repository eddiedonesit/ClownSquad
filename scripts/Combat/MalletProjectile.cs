namespace ClownSquad.Combat;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Visuals;
using ClownSquad.World;

/// <summary>
/// The thrown mallet. Flies out along the lane spinning, hits each enemy once
/// on the way, turns at the screen edge, and hits each of them again as it
/// boomerangs home to be caught.
///
/// Exempt from the profile-sprite rule (ADR 0004): it tumbles end over end, so
/// it has no heading to draw.
/// </summary>
public partial class MalletProjectile : Node3D
{
    private const string Sheet = "res://assets/sprites/mallet_spin.png";
    private const string Frames = "res://assets/sprites/mallet_spin.frames.json";

    private const float OutSpeed = 15f;
    private const float ReturnSpeed = 18f;
    private const float MaxTravel = 11f;
    private const float FlightHeight = 0.85f;
    private const float HitReach = 1.0f;       // along the lane
    private const float HitDepth = 0.95f;      // across it
    private const float CatchDistance = 0.7f;
    private const float OutDamage = 18f;
    private const float ReturnDamage = 14f;

    private PlayerFighter _owner = null!;
    private Vector3 _dir;
    private bool _returning;
    private float _travelled;
    private float _age;
    private AnimatedSprite3D _sprite = null!;
    private readonly HashSet<Fighter> _hitOut = new();
    private readonly HashSet<Fighter> _hitBack = new();

    public static void Spawn(PlayerFighter owner, Vector3 direction)
    {
        var mallet = new MalletProjectile { _owner = owner, _dir = direction.Normalized() };
        owner.GetParent().AddChild(mallet);
        Vector3 p = owner.GlobalPosition;
        mallet.GlobalPosition = new Vector3(p.X + mallet._dir.X * 0.8f, 0f, p.Z);
    }

    public override void _Ready()
    {
        _sprite = new AnimatedSprite3D
        {
            SpriteFrames = FrameLibrary.Load(Sheet, Frames),
            PixelSize = 0.0225f,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            Shaded = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, FlightHeight, 0f),
            // Mirrored when thrown left, so it always tumbles forward.
            FlipH = _dir.X < 0f,
        };
        AddChild(_sprite);
        _sprite.Play("mallet_spin");
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;

        // The owner was freed, or got its mallet back some other way (a respawn).
        if (!GodotObject.IsInstanceValid(_owner) || _owner.Armed || _age > 8f)
        {
            if (GodotObject.IsInstanceValid(_owner)) _owner.CatchMallet();
            QueueFree();
            return;
        }

        if (!_returning) FlyOut(dt);
        else FlyHome(dt);
    }

    private void FlyOut(float dt)
    {
        float step = OutSpeed * dt;
        Vector3 next = GlobalPosition + _dir * step;
        _travelled += step;

        // The screen edge is as far as it goes.
        bool atEdge = false;
        if (Arena.Current is Arena arena)
        {
            next = arena.Clamp(next, FlightHeight + 0.5f, 0.4f, out int wall);
            atEdge = wall != 0;
        }
        GlobalPosition = next;
        HitAlongPath(_hitOut, OutDamage, _dir);

        if (atEdge || _travelled >= MaxTravel)
        {
            _returning = true;
            _sprite.FlipH = !_sprite.FlipH;   // tumble back the other way
        }
    }

    private void FlyHome(float dt)
    {
        Vector3 target = _owner.GlobalPosition;
        Vector3 to = new(target.X - GlobalPosition.X, 0f, target.Z - GlobalPosition.Z);
        float dist = to.Length();
        if (dist <= CatchDistance)
        {
            _owner.CatchMallet();
            QueueFree();
            return;
        }

        Vector3 dir = to / dist;
        GlobalPosition += dir * Mathf.Min(ReturnSpeed * dt, dist);
        HitAlongPath(_hitBack, ReturnDamage, dir);
    }

    private void HitAlongPath(HashSet<Fighter> already, float damage, Vector3 travel)
    {
        foreach (Node node in GetTree().GetNodesInGroup(Fighter.Group))
        {
            if (node is not Fighter f || f == _owner || f.Hostile == _owner.Hostile) continue;
            if (already.Contains(f) || !f.CanBeHit()) continue;

            Vector3 to = f.GlobalPosition - GlobalPosition;
            if (Mathf.Abs(to.X) > HitReach || Mathf.Abs(to.Z) > HitDepth) continue;
            if (f.GlobalPosition.Y > FlightHeight + 1.6f) continue;

            already.Add(f);
            Vector3 push = new(Mathf.Sign(travel.X) == 0 ? _dir.X : Mathf.Sign(travel.X), 0f, 0f);
            _owner.StrikeFromProjectile(f, damage, push, 3.5f, 0f);
        }
    }
}
