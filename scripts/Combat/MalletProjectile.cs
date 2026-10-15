namespace ClownSquad.Combat;

using System.Collections.Generic;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
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
    /// <summary>What was thrown and how the skill tree has changed it.</summary>
    public readonly record struct Throw(string SpinSheet, float Damage, float Range, bool Explodes, bool Launches)
    {
        public static readonly Throw Mallet = new("mallet_spin_hd", 1f, 1f, false, false);
    }

    private string Sheet =>
        ArtStyle.Sheet("res://assets/sprites/mallet_spin.png", $"res://assets/sprites/{_throw.SpinSheet}.png");

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
    private Throw _throw = Throw.Mallet;
    private Vector3 _dir;
    private bool _returning;
    private float _travelled;
    private float _age;
    private AnimatedSprite3D _sprite = null!;
    private readonly HashSet<Fighter> _hitOut = new();
    private readonly HashSet<Fighter> _hitBack = new();

    public static void Spawn(PlayerFighter owner, Vector3 direction, Throw? how = null)
    {
        var mallet = new MalletProjectile { _owner = owner, _dir = direction.Normalized(), _throw = how ?? Throw.Mallet };
        owner.GetParent().AddChild(mallet);
        Vector3 p = owner.GlobalPosition;
        mallet.GlobalPosition = new Vector3(p.X + mallet._dir.X * 0.8f, 0f, p.Z);
        Sfx.Play("whirr");
    }

    public override void _Ready()
    {
        _sprite = new AnimatedSprite3D
        {
            SpriteFrames = FrameLibrary.Load(Sheet, ArtStyle.FramesFor(Sheet)),
            PixelSize = ArtStyle.Pixel(0.0225f),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            Shaded = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0f, FlightHeight, 0f),
            // Mirrored when thrown left, so it always tumbles forward.
            FlipH = _dir.X < 0f,
        };
        if (LitSprite.For(Sheet) is ShaderMaterial lit)
        {
            LitSprite.Apply(lit, Colors.White, 0f, _sprite.FlipH);
            _sprite.MaterialOverride = lit;
        }
        AddChild(_sprite);
        _sprite.Play("mallet_spin");

        // It carries its own light, so a throw sweeps a warm glow across
        // everyone it passes.
        AddChild(new OmniLight3D
        {
            LightColor = new Color(1f, 0.6f, 0.4f),
            LightEnergy = 1.8f,
            OmniRange = 2.2f,
            Position = new Vector3(0f, FlightHeight, 0.35f),
        });
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

        if (atEdge || _travelled >= MaxTravel * _throw.Range)
        {
            _returning = true;
            if (_throw.Explodes) Explode();
            _sprite.FlipH = !_sprite.FlipH;   // tumble back the other way
            if (_sprite.MaterialOverride is ShaderMaterial m) LitSprite.Apply(m, Colors.White, 0f, _sprite.FlipH);
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

    /// <summary>EGGSPLOSIVE: it goes off where it turns.</summary>
    private void Explode()
    {
        ImpactEffect.Spawn(GetParent(), GlobalPosition, 1.8f);
        Sfx.Play("kaboom", 1.3f, -5f);
        CameraShake.Request(this, 0.1f, 0.22f);
        foreach (Node node in Fighter.Live)
        {
            if (node is not Fighter f || f.Hostile == _owner.Hostile || !f.CanBeHit()) continue;
            Vector3 d = f.GlobalPosition - GlobalPosition;
            d.Y = 0f;
            if (d.Length() > 2.0f) continue;
            _owner.StrikeFromProjectile(f, OutDamage * _throw.Damage, d.LengthSquared() > 0.01f ? d.Normalized() : _dir, 5f, 4.5f);
        }
    }

    private void HitAlongPath(HashSet<Fighter> already, float damage, Vector3 travel)
    {
        foreach (Node node in Fighter.Live)
        {
            if (node is not Fighter f || f == _owner || f.Hostile == _owner.Hostile) continue;
            if (already.Contains(f) || !f.CanBeHit()) continue;

            Vector3 to = f.GlobalPosition - GlobalPosition;
            if (Mathf.Abs(to.X) > HitReach || Mathf.Abs(to.Z) > HitDepth) continue;
            if (f.GlobalPosition.Y > FlightHeight + 1.6f) continue;

            already.Add(f);
            Vector3 push = new(Mathf.Sign(travel.X) == 0 ? _dir.X : Mathf.Sign(travel.X), 0f, 0f);
            _owner.StrikeFromProjectile(f, damage * _throw.Damage, push, 3.5f, _throw.Launches ? 5.5f : 0f);
        }
    }
}
