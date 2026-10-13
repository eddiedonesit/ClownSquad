"""Render the main scene under several art-direction presets for comparison.

Usage: python tools/render_variants.py
Writes debug/variant_<name>.png for each preset. Non-destructive: the scene file
is restored afterwards.
"""
import re, os, shutil, subprocess, sys

SCENE = "scenes/Main.tscn"
GODOT = r"C:\Tools\Godot\4.7.2\Godot_v4.7.2-stable_mono_win64_console.exe"
PROJ = r"C:\Claude\ClownSquad"

# Row-wise Rx(-A): (1,0,0) (0,cosA,sinA) (0,-sinA,cosA); camera at distance d on that angle.
def cam(a_deg, d):
    import math
    a = math.radians(a_deg)
    c, s = math.cos(a), math.sin(a)
    return (f"Transform3D(1, 0, 0, 0, {c:.4f}, {s:.4f}, 0, {-s:.4f}, {c:.4f}, "
            f"0, {d*s:.3f}, {d*c:.3f})")

VARIANTS = {
    # Octopath HD-2D: heavy tilt-shift, hazy, distant camera
    "a_octopath": dict(
        transform=cam(35, 12.2), fov="25.0",
        dof_far="true", dof_near="true", dof_amount="0.3",
        dof_far_d="13.5", dof_near_d="8.5",
        fog="true", fog_density="0.045", fog_emission="Color(0.12, 0.1, 0.18, 1)",
        glow="0.8", amb_col="Color(0.35, 0.4, 0.6, 1)", amb_e="0.4",
        bg="Color(0.043, 0.047, 0.08, 1)", light_e="1.6",
    ),
    # Sea of Stars / Eastward: crisp, close, warm, no blur
    "b_crisp": dict(
        transform=cam(35, 9.0), fov="30.0",
        dof_far="false", dof_near="false", dof_amount="0.1",
        dof_far_d="20.0", dof_near_d="3.0",
        fog="true", fog_density="0.008", fog_emission="Color(0.2, 0.22, 0.3, 1)",
        glow="0.4", amb_col="Color(0.55, 0.6, 0.75, 1)", amb_e="0.7",
        bg="Color(0.09, 0.11, 0.16, 1)", light_e="1.8",
    ),
    # Chrono Trigger: flat, bright, near top-down, maximum readability
    "c_retro_flat": dict(
        transform=cam(45, 9.0), fov="22.0",
        dof_far="false", dof_near="false", dof_amount="0.1",
        dof_far_d="20.0", dof_near_d="3.0",
        fog="false", fog_density="0.0", fog_emission="Color(0.2, 0.2, 0.25, 1)",
        glow="0.2", amb_col="Color(0.75, 0.78, 0.85, 1)", amb_e="1.0",
        bg="Color(0.12, 0.14, 0.2, 1)", light_e="1.2",
    ),
    # Symphony of the Night / Blasphemous: moody, coloured light, atmospheric
    "d_moody": dict(
        transform=cam(32, 10.0), fov="26.0",
        dof_far="true", dof_near="false", dof_amount="0.15",
        dof_far_d="15.0", dof_near_d="3.0",
        fog="true", fog_density="0.09", fog_emission="Color(0.22, 0.09, 0.26, 1)",
        glow="1.3", amb_col="Color(0.25, 0.2, 0.45, 1)", amb_e="0.35",
        bg="Color(0.02, 0.015, 0.04, 1)", light_e="2.2",
    ),
}

def kv(s, key, val):
    return re.sub(rf'^{re.escape(key)} = .*$', f'{key} = {val}', s, count=1, flags=re.M)

def patch_camera(s, transform, fov):
    i = s.index('[node name="Camera3D"')
    j = s.index('[node name=', i + 10)
    blk = s[i:j]
    blk = re.sub(r'^transform = .*$', f'transform = {transform}', blk, count=1, flags=re.M)
    blk = re.sub(r'^fov = .*$', f'fov = {fov}', blk, count=1, flags=re.M)
    return s[:i] + blk + s[j:]

def attach_debug(s):
    s = s.replace('[ext_resource type="Texture2D"',
                  '[ext_resource type="Script" path="res://tools/DebugCapture.gd" id="0_dbg"]\n[ext_resource type="Texture2D"', 1)
    s = s.replace('[node name="Main" type="Node3D"]',
                  '[node name="Main" type="Node3D"]\nscript = ExtResource("0_dbg")', 1)
    return re.sub(r'load_steps=\d+', 'load_steps=9', s, count=1)

base = open(SCENE, encoding="utf-8").read()
backup = base
os.makedirs("debug", exist_ok=True)

try:
    for name, v in VARIANTS.items():
        s = base
        s = kv(s, "dof_blur_far_enabled", v["dof_far"])
        s = kv(s, "dof_blur_near_enabled", v["dof_near"])
        s = kv(s, "dof_blur_amount", v["dof_amount"])
        s = kv(s, "dof_blur_far_distance", v["dof_far_d"])
        s = kv(s, "dof_blur_near_distance", v["dof_near_d"])
        s = kv(s, "volumetric_fog_enabled", v["fog"])
        s = kv(s, "volumetric_fog_density", v["fog_density"])
        s = kv(s, "volumetric_fog_emission", v["fog_emission"])
        s = kv(s, "glow_intensity", v["glow"])
        s = kv(s, "ambient_light_color", v["amb_col"])
        s = kv(s, "ambient_light_energy", v["amb_e"])
        s = kv(s, "background_color", v["bg"])
        s = kv(s, "light_energy", v["light_e"])
        s = patch_camera(s, v["transform"], v["fov"])
        s = attach_debug(s)
        open(SCENE, "w", encoding="utf-8").write(s)
        subprocess.run([GODOT, "--path", PROJ], capture_output=True, timeout=180)
        out = f"debug/variant_{name}.png"
        if os.path.exists("debug/capture.png"):
            shutil.move("debug/capture.png", out)
            print(f"rendered {out}")
        else:
            print(f"FAILED {name}")
finally:
    open(SCENE, "w", encoding="utf-8").write(backup)
    print("scene restored")
