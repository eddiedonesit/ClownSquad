namespace ClownSquad.World;

using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Visuals;
using ClownSquad.Net;

/// <summary>
/// A crate standing along the walkway. Players can smash it like an enemy
/// (it is on the hostile side, so swings, throws and spins all land) but it
/// never fights back and enemies ignore it. Inside is a snack that heals.
/// The genre staple: a reason to break the scenery, and a way to recover
/// between groups without a menu.
/// </summary>
public partial class Crate : Fighter
{
    protected override string SheetPath => "res://assets/stages/crate.png";
    protected override float SpritePixelSize => 0.01125f;
    public override bool Hostile => true;
    public override float BodyHeight => 1.0f;
    public override float BodyHalfWidth => 0.5f;
    protected override float StunScale => 0f;
    protected override float LaunchScale => 0f;
    public override string HurtSound => "clack";
    public override string DeathSound => "pop";

    public override void _Ready()
    {
        DisplayName = "CRATE";
        MaxHealth = 30f;
        base._Ready();
        Died += _ =>
        {
            Fx.Sound("thump", 1.3f);
            Fx.Impact(GetParent(), GlobalPosition + new Vector3(0f, 0.4f, 0f), 1.0f, glow: false);
            var food = new Food((int)(GetInstanceId() % 4));
            GetParent().AddChild(food);
            food.GlobalPosition = GlobalPosition;
            QueueFree();
        };
    }
}

/// <summary>A snack on the floor. Walk over it to heal a third of your health.</summary>
public partial class Food : Node3D
{
    private const string Sheet = "res://assets/stages/food.png";
    private static readonly string[] Names = { "COTTON CANDY", "HOT DOG", "POPCORN", "CANDY APPLE" };
    private readonly int _kind;
    private Sprite3D _sprite = null!;
    private double _age;

    public Food(int kind) => _kind = kind;

    public override void _Ready()
    {
        _sprite = new Sprite3D
        {
            Texture = GD.Load<Texture2D>(Sheet), Hframes = 4, Frame = _kind,
            PixelSize = 0.012f, TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard, Position = new Vector3(0f, 0.35f, 0f),
        };
        if (LitSprite.For(Sheet) is ShaderMaterial lit)
        {
            LitSprite.Apply(lit, Colors.White, 0f, false);
            _sprite.MaterialOverride = lit;
        }
        AddChild(_sprite);
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.9f, 0.6f), LightEnergy = 0.5f, OmniRange = 0.9f, Position = new Vector3(0f, 0.4f, 0.3f) });
    }

    public override void _PhysicsProcess(double delta)
    {
        _age += delta;
        _sprite.Position = new Vector3(0f, 0.35f + 0.05f * Mathf.Sin((float)_age * 4f), 0f);
        // Left where it lies for anyone at full health, so nobody wastes it.
        foreach (Node n in Fighter.Live)
        {
            if (n is not PlayerFighter p || p.Hostile || p.IsDead || p.Health >= p.MaxHealth) continue;
            Vector3 d = p.GlobalPosition - GlobalPosition;
            if (new Vector2(d.X, d.Z).Length() > 0.6f) continue;
            float heal = p.MaxHealth / 3f;
            p.Heal(heal);
            Fx.Pop(GetParent(), p.GlobalPosition + new Vector3(0f, p.BodyHeight + 0.3f, 0f),
                          $"{Names[_kind]} +{heal:F0}", new Color(0.6f, 1f, 0.6f), 0.8f, 1f);
            Fx.Sound("coin", 1.5f);
            Fx.Sound("boing_small", 1.6f, -6f);
            QueueFree();
            return;
        }
    }
}
