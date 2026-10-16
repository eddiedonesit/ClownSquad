namespace ClownSquad.Net;

using System.Collections.Generic;
using Godot;
using Godot.Collections;
using ClownSquad.Visuals;

/// <summary>
/// How a guest's screen copies what the host draws (ADR 0010, phase 2),
/// without the fight code knowing it is being watched.
///
/// Every numbered thing (<see cref="NetRegistry"/>: fighters, shots,
/// telegraphs, hazards, loot) is mirrored as the tree of 3D nodes under it:
/// sprites, meshes, labels and lamps, each with its properties as Godot
/// stores them. Meshes, materials and shaders built in code are sent as their
/// class and properties; sheets and files by path. That is a
/// <see cref="Describe">description</see>, sent once and again whenever the
/// tree changes shape (a swing trail appears, a fist is added). Each snapshot
/// then carries only what moves: transforms, visibility, the sprite's clip,
/// frame and flip, colours, and the shader values that carry tint and flash.
///
/// A guest only ever builds the node and resource classes listed here, never a
/// script, so a description cannot run code.
/// </summary>
public static class NetMirror
{
    private static readonly HashSet<string> Buildable = new()
    {
        "Node3D", "MeshInstance3D", "Sprite3D", "AnimatedSprite3D", "Label3D", "OmniLight3D", "SpotLight3D",
    };

    // Node properties that are not the picture, or that each snapshot sets anyway.
    private static readonly HashSet<string> SkipProps = new()
    {
        "name", "owner", "script", "scene_file_path", "unique_name_in_owner", "editor_description", "multiplayer",
        "transform", "position", "rotation", "rotation_degrees", "quaternion", "basis", "scale", "rotation_order",
        "rotation_edit_mode", "top_level", "visible", "physics_interpolation_mode", "auto_translate_mode",
    };

    /// <summary>The node class a guest builds for one of the host's nodes: itself if allowed, else a bare Node3D.</summary>
    public static string ClassFor(Node n)
    {
        string c = n.GetClass();
        return Buildable.Contains(c) ? c : "Node3D";
    }

    /// <summary>
    /// A numbered node's visual tree, depth first: each node with the index of
    /// its parent in the list. Numbered descendants are left out: they are
    /// mirrored as roots of their own.
    /// </summary>
    public static List<(Node3D node, int parent)> Walk(Node3D root)
    {
        var list = new List<(Node3D, int)> { (root, -1) };
        void Visit(Node n, int parent)
        {
            foreach (Node child in n.GetChildren())
            {
                if (child is not Node3D c3 || child is CollisionShape3D || NetRegistry.IdFor(child) != 0) continue;
                list.Add((c3, parent));
                Visit(child, list.Count - 1);
            }
        }
        Visit(root, 0);
        return list;
    }

    /// <summary>The tree's shape: changes when a node joins or leaves it.</summary>
    public static long Signature(List<(Node3D node, int parent)> nodes)
    {
        long h = nodes.Count;
        foreach ((Node3D n, int p) in nodes) h = h * 31 + (long)n.GetInstanceId() * 17 + p;
        return h;
    }

    // ------------------------------------------------------------ host side

    /// <summary>A tree, as the bytes a guest builds it from.</summary>
    public static byte[] Describe(List<(Node3D node, int parent)> nodes)
    {
        var arr = new Array();
        for (int i = 0; i < nodes.Count; i++)
        {
            (Node3D n, int p) = nodes[i];
            string cls = i == 0 ? "Node3D" : ClassFor(n);
            arr.Add(new Array { p, cls, cls == "Node3D" ? new Dictionary() : EncodeProps(n, 0) });
        }
        return GD.VarToBytes(arr);
    }

    private static Dictionary EncodeProps(GodotObject o, int depth)
    {
        var d = new Dictionary();
        foreach (Dictionary prop in o.GetPropertyList())
        {
            int usage = (int)prop["usage"];
            if ((usage & (int)PropertyUsageFlags.Storage) == 0) continue;
            string name = (string)prop["name"];
            if (SkipProps.Contains(name) || name.StartsWith("process") || name.StartsWith("metadata/")) continue;
            Variant v = o.Get(name);
            if (Encode(v, depth) is Variant enc) d[name] = enc;
        }
        return d;
    }

    /// <summary>A value made safe to send: objects become a path or a class with its properties.</summary>
    private static Variant? Encode(Variant v, int depth)
    {
        switch (v.VariantType)
        {
            case Variant.Type.Object:
                return v.AsGodotObject() is Resource r ? EncodeResource(r, depth) : null;
            case Variant.Type.Array:
            {
                var outArr = new Array();
                foreach (Variant e in v.AsGodotArray()) outArr.Add(Encode(e, depth) ?? default);
                return outArr;
            }
            case Variant.Type.Dictionary:
            {
                var outD = new Dictionary();
                foreach (KeyValuePair<Variant, Variant> kv in v.AsGodotDictionary())
                    if (Encode(kv.Value, depth) is Variant ev) outD[kv.Key] = ev;
                return outD;
            }
            case Variant.Type.Callable:
            case Variant.Type.Signal:
            case Variant.Type.Rid:
                return null;
            default:
                return v;
        }
    }

    private static Variant? EncodeResource(Resource r, int depth)
    {
        if (r is SpriteFrames sf && FrameLibrary.SheetOf(sf) is string sheet) return new Dictionary { ["$frames"] = sheet };
        string path = r.ResourcePath;
        if (path.Length > 0 && !path.Contains("::")) return new Dictionary { ["$path"] = path };
        if (depth > 4) return null;
        return new Dictionary { ["$class"] = r.GetClass(), ["$props"] = EncodeProps(r, depth + 1) };
    }

    /// <summary>What moves on one node, each snapshot.</summary>
    public static Array State(Node3D n, bool root)
    {
        var a = new Array { root ? n.GlobalTransform : n.Transform, n.Visible };
        if (root) return a;
        switch (n)
        {
            case AnimatedSprite3D s: a.Add(s.Animation); a.Add(s.Frame); a.Add(s.FlipH); a.Add(s.Modulate); break;
            case Sprite3D s: a.Add(s.Frame); a.Add(s.FlipH); a.Add(s.Modulate); break;
            case Label3D l: a.Add(l.Text); a.Add(l.Modulate); break;
            case Light3D li: a.Add(li.LightEnergy); break;
        }
        if (n is GeometryInstance3D g && g.MaterialOverride is Material m) a.Add(MaterialState(m));
        return a;
    }

    private static readonly System.Collections.Generic.Dictionary<ulong, string[]> UniformNames = new();

    private static Variant MaterialState(Material m)
    {
        switch (m)
        {
            case ShaderMaterial sm when sm.Shader is Shader sh:
            {
                if (!UniformNames.TryGetValue(sh.GetInstanceId(), out string[]? names))
                {
                    var list = new List<string>();
                    foreach (Dictionary u in sh.GetShaderUniformList())
                        if ((int)u["type"] is not ((int)Variant.Type.Object or (int)Variant.Type.Nil)) list.Add((string)u["name"]);
                    names = list.ToArray();
                    UniformNames[sh.GetInstanceId()] = names;
                }
                var d = new Dictionary();
                foreach (string name in names) d[name] = sm.GetShaderParameter(name);
                return d;
            }
            case StandardMaterial3D std:
                return new Array { std.AlbedoColor, std.Emission };
            default:
                return default;
        }
    }

    /// <summary>Snapshots are squeezed before sending: most of one is transforms that barely change.</summary>
    public static byte[] Pack(byte[] raw)
    {
        using var output = new System.IO.MemoryStream();
        using (var z = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest)) z.Write(raw);
        return output.ToArray();
    }

    public static byte[] Unpack(byte[] packed)
    {
        using var input = new System.IO.MemoryStream(packed);
        using var z = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new System.IO.MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    // ----------------------------------------------------------- guest side

    /// <summary>Build a mirrored tree from a description; returns its nodes in the same order (the first is the root).</summary>
    public static List<Node3D> Build(byte[] bytes)
    {
        var nodes = new List<Node3D>();
        if (GD.BytesToVar(bytes).VariantType != Variant.Type.Array) return nodes;
        foreach (Variant e in GD.BytesToVar(bytes).AsGodotArray())
        {
            Array entry = e.AsGodotArray();
            int parent = (int)entry[0];
            string cls = (string)entry[1];
            Node3D n = Buildable.Contains(cls) && ClassDB.Instantiate(cls).AsGodotObject() is Node3D made ? made : new Node3D();
            foreach (KeyValuePair<Variant, Variant> kv in entry[2].AsGodotDictionary())
                n.Set((string)kv.Key, Decode(kv.Value));
            if (n is AnimatedSprite3D a) a.Stop();
            if (parent >= 0 && parent < nodes.Count) nodes[parent].AddChild(n);
            nodes.Add(n);
        }
        return nodes;
    }

    private static Variant Decode(Variant v)
    {
        switch (v.VariantType)
        {
            case Variant.Type.Dictionary:
            {
                Dictionary d = v.AsGodotDictionary();
                if (d.TryGetValue("$frames", out Variant sheet))
                {
                    string s = (string)sheet;
                    if (!s.StartsWith("res://")) return default;
                    return FrameLibrary.Load(s, ArtStyle.FramesFor(s));
                }
                if (d.TryGetValue("$path", out Variant path))
                {
                    string p = (string)path;
                    if (p.StartsWith("res://") && ResourceLoader.Exists(p) && ResourceLoader.Load(p) is Resource res) return res;
                    return default;
                }
                if (d.TryGetValue("$class", out Variant cls))
                {
                    string c = (string)cls;
                    if (!SafeResource(c) || ClassDB.Instantiate(c).AsGodotObject() is not Resource r) return default;
                    foreach (KeyValuePair<Variant, Variant> kv in d["$props"].AsGodotDictionary())
                        r.Set((string)kv.Key, Decode(kv.Value));
                    return r;
                }
                var outD = new Dictionary();
                foreach (KeyValuePair<Variant, Variant> kv in d) outD[kv.Key] = Decode(kv.Value);
                return outD;
            }
            case Variant.Type.Array:
            {
                var outArr = new Array();
                foreach (Variant e in v.AsGodotArray()) outArr.Add(Decode(e));
                return outArr;
            }
            default:
                return v;
        }
    }

    /// <summary>Resources a guest may build from a description: meshes, materials, shaders, textures. Never scripts or scenes.</summary>
    private static bool SafeResource(string c) =>
        ClassDB.ClassExists(c) && ClassDB.CanInstantiate(c) && ClassDB.IsParentClass(c, "Resource")
        && !ClassDB.IsParentClass(c, "Script") && !ClassDB.IsParentClass(c, "PackedScene") && !ClassDB.IsParentClass(c, "GDExtension");

    /// <summary>Apply one node's snapshot state on a guest (the root's transform is handled by the view, for smoothing).</summary>
    public static void Apply(Node3D n, Array a)
    {
        n.Transform = (Transform3D)a[0];
        n.Visible = (bool)a[1];
        int i = 2;
        switch (n)
        {
            case AnimatedSprite3D s:
                StringName anim = (StringName)a[i++];
                if (s.Animation != anim && s.SpriteFrames is SpriteFrames f && f.HasAnimation(anim)) s.Animation = anim;
                s.Frame = (int)a[i++]; s.FlipH = (bool)a[i++]; s.Modulate = (Color)a[i++];
                break;
            case Sprite3D s: s.Frame = (int)a[i++]; s.FlipH = (bool)a[i++]; s.Modulate = (Color)a[i++]; break;
            case Label3D l: l.Text = (string)a[i++]; l.Modulate = (Color)a[i++]; break;
            case Light3D li: li.LightEnergy = (float)a[i++]; break;
        }
        if (i < a.Count && n is GeometryInstance3D g && g.MaterialOverride is Material m)
        {
            Variant ms = a[i];
            if (m is ShaderMaterial sm && ms.VariantType == Variant.Type.Dictionary)
                foreach (KeyValuePair<Variant, Variant> kv in ms.AsGodotDictionary()) sm.SetShaderParameter((string)kv.Key, kv.Value);
            else if (m is StandardMaterial3D std && ms.VariantType == Variant.Type.Array && ms.AsGodotArray() is { Count: 2 } sa)
            {
                std.AlbedoColor = (Color)sa[0];
                std.Emission = (Color)sa[1];
            }
        }
    }
}
