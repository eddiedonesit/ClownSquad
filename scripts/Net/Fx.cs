namespace ClownSquad.Net;

using System;
using Godot;
using ClownSquad.Audio;
using ClownSquad.Visuals;

/// <summary>The kinds of fire-and-forget effect a fight produces.</summary>
public enum FxKind : byte { Sound, Impact, Pop, Shake, Punch, Striker }

/// <summary>One effect, as plain data: what can be sent to another machine and replayed there.</summary>
public readonly record struct FxEvent(FxKind Kind, string Text, Vector3 At, float A, float B, Color Color, bool Flag);

/// <summary>
/// Every effect the fight makes (sounds, impact bursts, pop text, camera shake
/// and punch) goes through here rather than straight to Sfx, ImpactEffect,
/// PopText or CameraShake. Offline that changes nothing: Fx plays the effect.
/// Online, the host's network session listens to <see cref="Emitted"/> and
/// sends each effect to the guests, who <see cref="Replay"/> it; one hook
/// instead of 300 call sites (2026-10-15 review, R4; ADR 0010).
///
/// Menus keep calling Sfx directly: their sounds belong to the machine whose
/// menu it is.
/// </summary>
public static class Fx
{
    /// <summary>Raised for each effect played by the simulation. The host's network session forwards these.</summary>
    public static event Action<FxEvent>? Emitted;

    public static AudioStreamPlayer? Sound(string bank, float pitch = 1f, float volumeDb = 0f)
    {
        Emitted?.Invoke(new FxEvent(FxKind.Sound, bank, default, pitch, volumeDb, default, false));
        return Sfx.Play(bank, pitch, volumeDb);
    }

    public static void Impact(Node parent, Vector3 at, float scale = 1f, bool glow = true)
    {
        Emitted?.Invoke(new FxEvent(FxKind.Impact, "", at, scale, 0f, default, glow));
        ImpactEffect.Spawn(parent, at, scale, glow);
    }

    public static void Pop(Node parent, Vector3 at, string text, Color color, float size = 1f, float rise = 0.9f)
    {
        Emitted?.Invoke(new FxEvent(FxKind.Pop, text, at, size, rise, color, false));
        PopText.Spawn(parent, at, text, color, size, rise);
    }

    public static void Shake(Node from, float amplitude, float duration)
    {
        Emitted?.Invoke(new FxEvent(FxKind.Shake, "", default, amplitude, duration, default, false));
        CameraShake.Request(from, amplitude, duration);
    }

    public static void Punch(Node from, float strength)
    {
        Emitted?.Invoke(new FxEvent(FxKind.Punch, "", default, strength, 0f, default, false));
        CameraShake.RequestPunch(from, strength);
    }

    /// <summary>Big Bertha's strength tester springing up at a spot, strength 0..1.</summary>
    public static void Striker(Node parent, Vector3 at, float strength)
    {
        Emitted?.Invoke(new FxEvent(FxKind.Striker, "", at, strength, 0f, default, false));
        HighStriker.Spawn(parent, at, strength);
    }

    /// <summary>Play an effect that came from another machine, here only (never raised again).</summary>
    public static void Replay(FxEvent e, Node scene)
    {
        switch (e.Kind)
        {
            case FxKind.Sound: Sfx.Play(e.Text, e.A, e.B); break;
            case FxKind.Impact: ImpactEffect.Spawn(scene, e.At, e.A, e.Flag); break;
            case FxKind.Pop: PopText.Spawn(scene, e.At, e.Text, e.Color, e.A, e.B); break;
            case FxKind.Shake: CameraShake.Request(scene, e.A, e.B); break;
            case FxKind.Punch: CameraShake.RequestPunch(scene, e.A); break;
            case FxKind.Striker: HighStriker.Spawn(scene, e.At, e.A); break;
        }
    }
}
