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
    // 2: gear (Equipped, Stash). Version 1 files load as-is, with no gear.
    public const int CurrentVersion = 2;
    public const string DefaultPath = "user://clownsquad_save.json";

    // Enums are written by name, so the file is readable and survives reordering.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Where saves go. Tests point this elsewhere so they never touch a real save.</summary>
    public static string Path { get; set; } = DefaultPath;

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
            SaveData? loaded = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(file), Json);
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

    /// <summary>The character in a player slot, created on first use.</summary>
    public static CharacterRecord CharacterFor(int slot, string defaultName)
    {
        while (Data.Characters.Count <= slot)
            Data.Characters.Add(new CharacterRecord { Name = Data.Characters.Count == slot ? defaultName : "" });
        CharacterRecord r = Data.Characters[slot];
        if (r.Name.Length == 0) r.Name = defaultName;
        return r;
    }

    /// <summary>Delete the save at the current path. Tests only.</summary>
    public static void DeleteForTests()
    {
        if (Path == DefaultPath) throw new InvalidOperationException("refusing to delete the real save");
        if (File.Exists(Disk)) File.Delete(Disk);
        Data = new SaveData();
    }
}
