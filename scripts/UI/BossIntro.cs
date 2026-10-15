namespace ClownSquad.UI;

using Godot;
using ClownSquad.Actors;
using ClownSquad.Audio;
using ClownSquad.Visuals;

/// <summary>
/// A boss's entrance. A drum roll while a spotlight swings onto them, then
/// the reveal: the crowd goes "ooooh", the boss's signature sound plays (a
/// tiger's roar, a car horn, a ta-da), the camera leans in and the title card
/// slams across. The boss holds still for it (BossFighter.Presenting) and
/// cannot be hit until it is over.
/// </summary>
public partial class BossIntro : Node3D
{
    public const float Seconds = 2.6f;
    private const float Reveal = 1.35f;

    private Fighter _boss = null!;
    private string[] _signature = System.Array.Empty<string>();
    private bool _drumroll = true;
    private string _title = "", _tag = "";
    private SpotLight3D _spot = null!;
    private float _t;
    private bool _revealed;

    public static void Play(Fighter boss, string title, string tagline, string[] signature, bool drumroll = true)
    {
        var intro = new BossIntro { _boss = boss, _signature = signature, _title = title, _tag = tagline, _drumroll = drumroll };
        boss.GetParent().AddChild(intro);
    }

    public override void _Ready()
    {
        _spot = new SpotLight3D
        {
            LightColor = new Color(1f, 0.95f, 0.8f), LightEnergy = 0f, SpotRange = 14f, SpotAngle = 16f,
            SpotAttenuation = 0.4f, ShadowEnabled = true, LightVolumetricFogEnergy = 2f,
        };
        AddChild(_spot);
        if (_drumroll) Sfx.Play("drumroll");
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (!IsInstanceValid(_boss)) { QueueFree(); return; }
        // The spot swings in from the side and settles on the boss.
        Vector3 target = _boss.GlobalPosition + new Vector3(0f, _boss.BodyHeight * 0.5f, 0f);
        float swing = Mathf.Clamp(1f - _t / Reveal, 0f, 1f);
        Vector3 from = target + new Vector3(-3f * swing, 7f, 3f);
        _spot.GlobalPosition = from;
        _spot.LookAt(target + new Vector3(swing * 4f, 0f, 0f), Vector3.Up);
        _spot.LightEnergy = _t < Reveal ? Mathf.Lerp(2f, 6f, _t / Reveal) : Mathf.Max(0f, 9f * (1f - (_t - Reveal) / (Seconds - Reveal)));

        if (!_revealed && _t >= Reveal)
        {
            _revealed = true;
            Sfx.Play("crowd_ooh");
            foreach (string s in _signature) Sfx.Play(s);
            CameraShake.RequestPunch(_boss, 0.9f);
            CameraShake.Request(_boss, 0.08f, 0.3f);
            BossCard.Show(_boss, _title, _tag);
        }
        if (_t > Seconds) QueueFree();
    }
}
