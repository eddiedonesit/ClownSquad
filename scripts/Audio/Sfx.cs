namespace ClownSquad.Audio;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Plays the generated sound kit. Every file in assets/audio is a bank keyed
/// by name; numbered files (bonk_1, bonk_2) are variants of one bank. Each
/// play picks a random variant and nudges the pitch, which is what stops a
/// long fight sounding like one sample on repeat.
///
/// Gameplay calls <see cref="Play"/> with a bank name and nothing else; the
/// mix lives here, in one table, rather than scattered through the code.
/// </summary>
public partial class Sfx : Node
{
    private const string Folder = "res://assets/audio";
    private const int Voices = 24;
    private const ulong RepeatGuardMs = 40;   // the same bank twice inside this is one sound

    public static Sfx? Instance { get; private set; }

    /// <summary>
    /// Mix levels in dB. Pure-tone sounds (squeaks, slide whistles) carry far
    /// more energy than noisy hits at the same peak, so they sit lower.
    /// </summary>
    private static readonly Dictionary<string, float> Level = new()
    {
        ["bonk"] = -1f, ["slap"] = -3f, ["whoosh"] = -9f, ["squeak"] = -12f,
        ["honk"] = -4f, ["honk_low"] = -2f, ["boing"] = -8f, ["boing_small"] = -12f,
        ["slide_up"] = -13f, ["slide_down"] = -12f, ["pop"] = -6f, ["thump"] = -5f,
        ["clack"] = -4f, ["zip"] = -9f, ["whirr"] = -9f, ["kazoo_rise"] = -8f,
        ["fwee"] = -14f, ["ding"] = -8f, ["kaboom"] = 0f, ["wah_short"] = -6f,
        ["sad_trombone"] = -3f, ["fanfare"] = -4f, ["victory"] = -3f,
        ["level_up"] = -3f, ["crit"] = -2f, ["blip"] = -14f, ["coin"] = -9f, ["crowd_cheer"] = -8f,
        ["crowd_ooh"] = -6f, ["crowd_gasp"] = -6f, ["crowd_boo"] = -7f, ["crowd_laugh"] = -7f, ["drumroll"] = -5f,
        ["belly_drum"] = -3f, ["ball_bounce"] = -7f, ["whip_crack"] = 0f, ["tiger_roar"] = -3f, ["car_horn"] = -6f,
        ["engine"] = -9f, ["twang"] = -8f, ["tada"] = -6f, ["fire_roar"] = -7f, ["glass_smash"] = -4f,
        ["giggle"] = -6f, ["clang"] = -4f, ["ringmaster_fanfare"] = -4f, ["squawk"] = -6f,
    };

    private readonly Dictionary<string, List<AudioStream>> _banks = new();
    private readonly Dictionary<string, ulong> _lastPlayed = new();
    private readonly List<AudioStreamPlayer> _voices = new();
    private readonly RandomNumberGenerator _rng = new();
    private AudioStreamPlayer _music = null!;
    private int _next;

    /// <summary>Plays and misses per bank, for tests: a miss is a name with no file behind it.</summary>
    public static readonly Dictionary<string, int> Played = new();
    public static readonly HashSet<string> Missing = new();
    public static int BankCount => Instance?._banks.Count ?? 0;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        _rng.Randomize();
        LoadBanks();
        // Volumes live on their own buses, set from the player's settings.
        ClownSquad.Progression.GameSettings.Load();
        ClownSquad.Progression.GameSettings.EnsureBuses();

        for (int i = 0; i < Voices; i++)
        {
            var v = new AudioStreamPlayer { Bus = ClownSquad.Progression.GameSettings.SfxBus };
            AddChild(v);
            _voices.Add(v);
        }
        _music = new AudioStreamPlayer { VolumeDb = -13f, Bus = ClownSquad.Progression.GameSettings.MusicBus };
        AddChild(_music);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private void LoadBanks()
    {
        using DirAccess? dir = DirAccess.Open(Folder);
        if (dir is null) { GD.PushWarning("Sfx: no audio folder; run tools/refresh_sprites.sh"); return; }

        foreach (string entry in dir.GetFiles())
        {
            // Exported builds list the .import stubs rather than the sources.
            string file = entry.EndsWith(".import") ? entry[..^7] : entry;
            if (!file.EndsWith(".wav")) continue;

            string name = file[..^4];
            int us = name.LastIndexOf('_');
            if (us > 0 && int.TryParse(name[(us + 1)..], out _)) name = name[..us];   // bonk_2 -> bonk

            if (GD.Load<AudioStream>($"{Folder}/{file}") is not AudioStream stream) continue;
            if (!_banks.TryGetValue(name, out List<AudioStream>? bank)) _banks[name] = bank = new();
            if (!bank.Contains(stream)) bank.Add(stream);
        }
    }

    /// <summary>Play a sound by bank name. Returns the voice, so a held sound can be cut off.</summary>
    public static AudioStreamPlayer? Play(string bank, float pitch = 1f, float volumeDb = 0f) =>
        Instance?.PlayInternal(bank, pitch, volumeDb);

    public static void Stop(AudioStreamPlayer? voice)
    {
        if (voice is not null && GodotObject.IsInstanceValid(voice)) voice.Stop();
    }

    private AudioStreamPlayer? PlayInternal(string bank, float pitch, float volumeDb)
    {
        if (!_banks.TryGetValue(bank, out List<AudioStream>? streams) || streams.Count == 0)
        {
            Missing.Add(bank);
            return null;
        }
        Played[bank] = Played.TryGetValue(bank, out int c) ? c + 1 : 1;

        ulong now = Time.GetTicksMsec();
        if (_lastPlayed.TryGetValue(bank, out ulong last) && now - last < RepeatGuardMs) return null;
        _lastPlayed[bank] = now;

        AudioStreamPlayer v = _voices[_next];
        _next = (_next + 1) % _voices.Count;
        v.Stream = streams[_rng.RandiRange(0, streams.Count - 1)];
        v.PitchScale = Mathf.Max(0.1f, pitch * _rng.RandfRange(0.95f, 1.05f));
        v.VolumeDb = (Level.TryGetValue(bank, out float level) ? level : -6f) + volumeDb;
        v.Play();
        return v;
    }

    /// <summary>
    /// Start a song, replacing whatever is playing; asking for the song already
    /// playing leaves it alone. Songs (tools/gen_music.py) open with an intro
    /// that plays once, then loop from the frame named in music_loops.json.
    /// A song that has not been generated falls back to the old march.
    /// </summary>
    public static void PlayMusic(string bank)
    {
        if (Instance is not Sfx s) return;
        if (!s._banks.ContainsKey(bank)) bank = "music_march";
        if (!s._banks.TryGetValue(bank, out List<AudioStream>? streams)) return;
        if (s._music.Playing && s._musicBank == bank) return;
        if (streams[0] is AudioStreamWav wav)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = LoopStart(bank);
            wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
        }
        s._musicBank = bank;
        s._music.Stream = streams[0];
        s._music.VolumeDb = MusicDb;
        s._music.Play();
    }

    private const float MusicDb = -11f;
    private string _musicBank = "";
    private static Godot.Collections.Dictionary? _loops;

    private static int LoopStart(string bank)
    {
        if (_loops is null)
        {
            using var f = FileAccess.Open($"{Folder}/music_loops.json", FileAccess.ModeFlags.Read);
            _loops = f is not null && Json.ParseString(f.GetAsText()).Obj is Godot.Collections.Dictionary d ? d : new();
        }
        return _loops.TryGetValue(bank, out Variant v) ? (int)v : 0;
    }

    public static void StopMusic(float fadeSeconds = 0.6f)
    {
        if (Instance is not Sfx s || !s._music.Playing) return;
        Tween t = s.CreateTween();
        t.TweenProperty(s._music, "volume_db", -60f, fadeSeconds);
        t.TweenCallback(Callable.From(s._music.Stop));
        s._musicBank = "";
    }
}
