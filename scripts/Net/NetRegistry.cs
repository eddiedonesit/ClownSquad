namespace ClownSquad.Net;

using System.Collections.Generic;
using Godot;

/// <summary>
/// A number for every thing in a fight that a guest's screen would need to
/// draw: fighters, thrown and fired shots, telegraphs, hazards, loot. The host
/// numbers things as they enter the scene; snapshots and spawn messages refer
/// to them by number, and a guest keeps a puppet per number (ADR 0010).
///
/// Each tracked class forwards its enter/exit-tree notifications here with a
/// one-line <c>_Notification</c> override, so nothing has to remember to
/// register or unregister.
/// </summary>
public static class NetRegistry
{
    private static readonly Dictionary<int, Node> ById = new();
    private static readonly Dictionary<ulong, int> IdOf = new();
    private static int _next = 1;

    /// <summary>How many things are numbered right now.</summary>
    public static int Count => ById.Count;

    /// <summary>Forward a node's notification: numbered on entering the tree, forgotten on leaving it.</summary>
    public static void Notify(Node node, int what)
    {
        if (what == Node.NotificationEnterTree) Register(node);
        else if (what == Node.NotificationExitTree) Unregister(node);
    }

    public static int Register(Node node)
    {
        ulong key = node.GetInstanceId();
        if (IdOf.TryGetValue(key, out int id)) return id;
        id = _next++;
        IdOf[key] = id;
        ById[id] = node;
        return id;
    }

    public static void Unregister(Node node)
    {
        if (!IdOf.Remove(node.GetInstanceId(), out int id)) return;
        ById.Remove(id);
    }

    /// <summary>The number of a tracked node, or 0.</summary>
    public static int IdFor(Node node) => IdOf.TryGetValue(node.GetInstanceId(), out int id) ? id : 0;

    public static Node? Find(int id) => ById.TryGetValue(id, out Node? n) && GodotObject.IsInstanceValid(n) ? n : null;

    /// <summary>Start numbering again from 1, at the start of a fight.</summary>
    public static void Reset()
    {
        ById.Clear();
        IdOf.Clear();
        _next = 1;
    }
}
