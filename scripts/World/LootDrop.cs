namespace ClownSquad.World;

using System;
using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Progression;

/// <summary>
/// An item lying in the ring. It pops out of the enemy in a little arc, then
/// sits bobbing in a pool of its rarity's light, with a beam above anything
/// Rare or better so good drops are seen across the screen. Walk near and it
/// is drawn to you; touch it and it goes into your bag for the stage.
/// </summary>
public partial class LootDrop : Node3D
{
    private const string Icons = "res://assets/arena/loot_icons.png";
    private const float Magnet = 1.7f;
    private const float Touch = 0.45f;

    public ItemData Item { get; }
    public event Action<LootDrop, PlayerFighter>? PickedUp;

    private Vector3 _velocity;
    private bool _landed;
    private double _age;
    private Sprite3D _icon = null!;
    private PlayerFighter? _owedTo;    // set when the stage-end sweep sends it to someone
    private bool _taken;

    public LootDrop(ItemData item, Vector3 toss)
    {
        Item = item;
        _velocity = toss;
    }

    public override void _Ready()
    {
        Color c = Loot.RarityColor(Item.Rarity);
        _icon = new Sprite3D
        {
            Texture = GD.Load<Texture2D>(Icons),
            Hframes = 4,
            Frame = (int)Item.Slot,
            PixelSize = 0.011f,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            Shaded = false,
            Position = new Vector3(0f, 0.38f, 0f),
        };
        AddChild(_icon);

        int r = (int)Item.Rarity;
        AddChild(new OmniLight3D
        {
            LightColor = c,
            LightEnergy = 1.2f + 0.7f * r,
            OmniRange = 1.2f + 0.35f * r,
            Position = new Vector3(0f, 0.45f, 0.25f),
        });

        if (Item.Rarity >= ItemRarity.Rare)
        {
            // A soft column of light, the way loot calls out in every ARPG.
            AddChild(new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(0.16f + 0.04f * r, 2.4f) },
                Position = new Vector3(0f, 1.3f, 0f),
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
                    AlbedoTexture = BeamGradient(),
                    AlbedoColor = new Color(c.R, c.G, c.B, 0.55f),
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
    }

    private static GradientTexture2D? _beam;

    private static GradientTexture2D BeamGradient() => _beam ??= new GradientTexture2D
    {
        Gradient = new Gradient
        {
            Offsets = new[] { 0f, 0.5f, 1f },
            Colors = new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) },
        },
        FillFrom = new Vector2(0f, 0.5f),
        FillTo = new Vector2(1f, 0.5f),
        Width = 32,
        Height = 4,
    };

    /// <summary>Send this drop to a player regardless of distance (the stage-clear sweep).</summary>
    public void SendTo(PlayerFighter player) => _owedTo = player;

    public override void _PhysicsProcess(double delta)
    {
        if (_taken) return;
        float dt = (float)delta;
        _age += delta;

        if (!_landed)
        {
            _velocity.Y -= 18f * dt;
            GlobalPosition += _velocity * dt;
            if (GlobalPosition.Y <= 0f && _velocity.Y < 0f)
            {
                GlobalPosition = new Vector3(GlobalPosition.X, 0f, GlobalPosition.Z);
                _landed = true;
                Sfx.Play("clack", 1.6f, -8f);
            }
            if (Arena.Current is Arena arena) GlobalPosition = arena.Clamp(GlobalPosition, 0.8f, 0.3f, out _);
            return;
        }

        _icon.Position = new Vector3(0f, 0.38f + 0.06f * Mathf.Sin((float)_age * 3.2f), 0f);

        PlayerFighter? target = _owedTo;
        if (target is null)
        {
            float best = Magnet;
            foreach (Node n in GetTree().GetNodesInGroup(Fighter.Group))
            {
                if (n is not PlayerFighter p || p.Hostile || p.IsDead || p.Record is null) continue;
                float d = Flat(p.GlobalPosition - GlobalPosition).Length();
                if (d < best) { best = d; target = p; }
            }
        }
        if (target is null || !IsInstanceValid(target)) return;

        Vector3 to = Flat(target.GlobalPosition - GlobalPosition);
        if (to.Length() <= Touch)
        {
            _taken = true;
            PickedUp?.Invoke(this, target);
            QueueFree();
            return;
        }
        float speed = _owedTo is null ? 6f : 14f;
        GlobalPosition += to.Normalized() * Mathf.Min(speed * dt, to.Length());
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
}
