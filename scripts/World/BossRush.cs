namespace ClownSquad.World;

using Godot;

/// <summary>
/// Boss rush: all ten bosses back to back in the circus ring, worlds 1 to 10,
/// a short breather and half a heal between them, against the clock. The
/// squad plays as level-30 clowns on a throwaway save, so a run never touches
/// a campaign, and the best time is kept with the settings. Started from the
/// title screen; the fight scene reads Active and sets itself up.
/// </summary>
public static class BossRush
{
    public const int Level = 30;
    public const int Bosses = 10;

    /// <summary>True while a rush is being played.</summary>
    public static bool Active => Session.Mode == PlayMode.BossRush;

    public static void Start(SceneTree tree)
    {
        Session.BeginBossRush();
        tree.ChangeSceneToFile(ClownSquad.Map.Campaign.FightScene);
    }

    /// <summary>Back to the title, out of rush mode and off the rush save.</summary>
    public static void Leave(SceneTree tree)
    {
        Session.BeginCampaign();
        tree.ChangeSceneToFile(ClownSquad.Map.Campaign.TitleScene);
    }

    public static string Clock(double secs) => $"{(int)secs / 60}:{(int)secs % 60:00}.{(int)(secs * 10) % 10}";
}
