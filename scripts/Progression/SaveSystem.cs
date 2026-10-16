namespace ClownSquad.Progression;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

/// <summary>The whole save file. Versioned so later formats can migrate older ones.</summary>
public sealed class SaveData
{
    public int Version { get; set; } = SaveSystem.CurrentVersion;
    /// <summary>One character per player slot, by index.</summary>
    public List<CharacterRecord> Characters { get; set; } = new();
    /// <summary>World map progress, shared by the whole squad.</summary>
    public ClownSquad.Map.CampaignState Campaign { get; set; } = new();
}

/// <summary>
/// Reads and writes the save file (ADR 0006). One JSON file in the user data
/// folder, written atomically: the new save goes to a temporary file that then
/// replaces the old one, so a crash mid-write never leaves a half-written save.
/// A file that cannot be read is set aside rather than overwritten, so a bug
/// here can never silently destroy someone's characters.
/// </summary>
public static class SaveSystem
{
    // 2: gear (Equipped, Stash). 3: campaign progress and gold.
    // Older files load as-is: missing fields take their defaults.
    // 4: a look per character (which clown), and save slots.
    // 5: a weapon per character and a skill tree per weapon.
    public const int CurrentVersion = 5;
    /// <summary>Three save slots, chosen on the title screen.</summary>
    public const int Slots = 3;
    /// <summary>Before slots there was one file; it becomes slot 1 the first time slots are used.</summary>
    public const string LegacyPath = "user://clownsquad_save.json";

    public static string SlotPath(int slot) => $"user://clownsquad_slot{slot}.json";

    /// <summary>The slot being played. Kept across scene changes for the session.</summary>
    public static int ActiveSlot { get; set; } = 1;

    /// <summary>Point saves at the active slot (moving a pre-slot save into slot 1 first).</summary>
    public static void UseActiveSlot()
    {
        MigrateLegacy();
        Path = SlotPath(ActiveSlot);
    }

    private static void MigrateLegacy()
    {
        string legacy = ProjectSettings.GlobalizePath(LegacyPath), first = ProjectSettings.GlobalizePath(SlotPath(1));
        if (!File.Exists(legacy) || File.Exists(first)) return;
        File.Move(legacy, first);
        GD.Print($"Save: moved the old save into slot 1 ({first}).");
    }

    /// <summary>What a slot holds, for the load screen, without loading it. Null for an empty slot.</summary>
    public static SaveData? Peek(int slot)
    {
        MigrateLegacy();
        string file = ProjectSettings.GlobalizePath(SlotPath(slot));
        if (!File.Exists(file)) return null;
        try { return JsonSerializer.Deserialize<SaveData>(Migrate(File.ReadAllText(file)), Json); }
        catch (Exception e) when (e is JsonException or IOException) { return null; }
    }

    public static DateTime? SavedAt(int slot)
    {
        string file = ProjectSettings.GlobalizePath(SlotPath(slot));
        return File.Exists(file) ? File.GetLastWriteTime(file) : null;
    }

    /// <summary>Start the active slot over: an empty save, written straight away.</summary>
    public static void StartFresh()
    {
        UseActiveSlot();
        Data = new SaveData();
        Save();
    }

    // Enums are written by name, so the file is readable and survives reordering.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>
    /// Saves from before the seltzer bottle: the rubber chicken it replaced
    /// becomes the bottle, and each learned chicken skill the bottle's skill in
    /// the same place in the tree, so no point spent is lost.
    /// </summary>
    private static readonly (string from, string to)[] Renames =
    {
        ("\"Chicken\"", "\"Seltzer\""),
        ("\"feather_duster\"", "\"fizz_up\""), ("\"pecking_order\"", "\"double_squirt\""), ("\"frenzy\"", "\"quick_trigger\""),
        ("\"fowl_ball\"", "\"high_pressure\""), ("\"eggsplosive\"", "\"long_spray\""), ("\"rubber_bounce\"", "\"flood\""),
        ("\"squawk\"", "\"spritz\""), ("\"lucky_feather\"", "\"lucky_bubbles\""),
    };

    private static string Migrate(string json)
    {
        if (!json.Contains("\"Chicken\"")) return json;
        foreach ((string from, string to) in Renames) json = json.Replace(from, to);
        return json;
    }

    /// <summary>A character as JSON, the save file's format: what a guest sends to join an online game.</summary>
    public static string CharacterToJson(CharacterRecord r) => JsonSerializer.Serialize(r, Json);

    /// <summary>A character from JSON, or null if it does not parse.</summary>
    public static CharacterRecord? CharacterFromJson(string json)
    {
        try { return JsonSerializer.Deserialize<CharacterRecord>(Migrate(json), Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Where saves go. Tests point this elsewhere so they never touch a real save.</summary>
    public static string Path { get; set; } = SlotPath(1);

    public static SaveData Data { get; private set; } = new();

    private static string Disk => ProjectSettings.GlobalizePath(Path);

    public static void Load()
    {
        string file = Disk;
        if (!File.Exists(file))
        {
            Data = new SaveData();
            GD.Print($"Save: none at {file}; starting fresh.");
            return;
        }

        try
        {
            SaveData? loaded = JsonSerializer.Deserialize<SaveData>(Migrate(File.ReadAllText(file)), Json);
            if (loaded is null) throw new InvalidDataException("empty save");
            if (loaded.Version > CurrentVersion)
                throw new InvalidDataException($"save version {loaded.Version} is newer than this build ({CurrentVersion})");
            loaded.Version = CurrentVersion;
            Data = loaded;
            GD.Print($"Save: loaded {Data.Characters.Count} character(s) from {file}.");
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or IOException)
        {
            string aside = file + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(file, aside);
            GD.PushError($"Save: could not read {file} ({e.Message}). Kept it as {aside} and started fresh.");
            Data = new SaveData();
        }
    }

    public static bool Save()
    {
        string file = Disk;
        string temp = file + ".tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(Data, Json));
            File.Move(temp, file, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"Save: could not write {file}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Put one character into a save file other than the one being played, and
    /// write it: how a guest keeps what they earned in someone else's online
    /// game. The rest of that save is left as it was.
    /// </summary>
    public static bool StoreCharacter(string path, int index, CharacterRecord record)
    {
        string file = ProjectSettings.GlobalizePath(path);
        SaveData data = new();
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<SaveData>(Migrate(File.ReadAllText(file)), Json) is SaveData on) data = on;
        }
        catch (Exception e) when (e is JsonException or IOException) { return false; }
        while (data.Characters.Count <= index) data.Characters.Add(new CharacterRecord());
        data.Characters[index] = record;
        data.Version = CurrentVersion;
        string temp = file + ".tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(data, Json));
            File.Move(temp, file, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GD.PushError($"Save: could not write {file}: {e.Message}");
            return false;
        }
    }

    /// <summary>The character in a player slot, created on first use.</summary>
    public static CharacterRecord CharacterFor(int slot, string defaultName)
    {
        while (Data.Characters.Count <= slot)
            Data.Characters.Add(new CharacterRecord { Name = Data.Characters.Count == slot ? defaultName : "" });
        CharacterRecord r = Data.Characters[slot];
        if (r.Name.Length == 0) r.Name = defaultName;
        if (r.Look.Length == 0)
        {
            // Older saves predate looks: match the look to the name if it is a clown's, else by slot.
            Roster.Clown match = Roster.DefaultFor(slot);
            foreach (Roster.Clown c in Roster.All) if (c.Name == r.Name) match = c;
            r.Look = match.Id;
        }
        return r;
    }

    /// <summary>Delete the save at the current path. Tests only.</summary>
    public static void DeleteForTests()
    {
        if (Path == LegacyPath || Path.StartsWith("user://clownsquad_slot")) throw new InvalidOperationException("refusing to delete a real save");
        // Probes run side by side on one scratch file: another may be writing it
        // this instant. Starting fresh in memory is what matters.
        try { if (File.Exists(Disk)) File.Delete(Disk); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { GD.Print($"Save: {Disk} is busy; starting fresh anyway."); }
        Data = new SaveData();
    }
}
