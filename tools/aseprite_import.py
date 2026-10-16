"""Bring a sprite drawn in Aseprite into the game.

Aseprite (and the pixel-mcp plugin's export_spritesheet) writes a sheet PNG and
a JSON file describing its frames and tags. The game wants its own format: a
horizontal strip of equal frames, a .frames.json naming clips by frame index
with a baseline and frame rates, and a normal map (_n.png) for its lighting.
This converts one into the other:

  python tools/aseprite_import.py SHEET.png SHEET.json OUT_NAME
      [--scale 2] [--canvas 208x288] [--baseline 216] [--dir assets/sprites]

  --scale     nearest-neighbour upscale (draw at half size, 48x64, play at 2x:
              chunkier, more classic pixels, and a quarter of the drawing)
  --canvas    pad every frame to this size, feet centred on the baseline, so a
              redrawn character matches the frame size the game code expects
  --baseline  the row the feet stand on (default: the lowest opaque row seen)

Tags become clips (idle, walk, attack, hit...), named exactly as in Aseprite;
each clip's frame rate comes from its frames' durations. Trimmed sheets
(spriteSourceSize/sourceSize) are restored to full frames.

The normal map: there are no "parts" in a hand-drawn sprite, but its dark
outline already separates them, so each region enclosed by outline or
transparency is domed on its own, like gen_sprite_hd's parts, and the outline
itself faces outward for the rim light. Encoded the same way (OpenGL, +Y up).

Standard library only (a small PNG reader is included).
"""
import colorsys
import json
import math
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelpng import write_png


# ------------------------------------------------------------------ png in

def read_png(path):
    """RGBA rows from an 8-bit PNG (grey, RGB, palette, grey+alpha or RGBA; no interlace)."""
    data = open(path, "rb").read()
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"{path}: not a PNG")
    pos, idat, plte, trns = 8, b"", None, None
    w = h = depth = ctype = interlace = 0
    while pos < len(data):
        n, kind = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + n]
        pos += 12 + n
        if kind == b"IHDR":
            w, h, depth, ctype, _, _, interlace = struct.unpack(">IIBBBBB", body)
        elif kind == b"PLTE":
            plte = body
        elif kind == b"tRNS":
            trns = body
        elif kind == b"IDAT":
            idat += body
        elif kind == b"IEND":
            break
    if depth != 8 or interlace:
        raise ValueError(f"{path}: only 8-bit, non-interlaced PNGs (export again from Aseprite)")
    chans = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    raw = zlib.decompress(idat)
    stride = w * chans
    rows, prev = [], bytearray(stride)
    i = 0
    for _ in range(h):
        f = raw[i]
        line = bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        for x in range(stride):
            a = line[x - chans] if x >= chans else 0
            b = prev[x]
            c = prev[x - chans] if x >= chans else 0
            if f == 1:
                line[x] = (line[x] + a) & 255
            elif f == 2:
                line[x] = (line[x] + b) & 255
            elif f == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        prev = line
        row = []
        for x in range(w):
            px = line[x * chans:(x + 1) * chans]
            if ctype == 6:
                row.append(tuple(px))
            elif ctype == 2:
                row.append((px[0], px[1], px[2], 255))
            elif ctype == 0:
                row.append((px[0], px[0], px[0], 255))
            elif ctype == 4:
                row.append((px[0], px[0], px[0], px[1]))
            else:
                k = px[0]
                alpha = trns[k] if trns is not None and k < len(trns) else 255
                row.append((plte[k * 3], plte[k * 3 + 1], plte[k * 3 + 2], alpha))
        rows.append(row)
    return w, h, rows


# ------------------------------------------------------------- the frames

def frames_from_json(meta_path, w, h, rows):
    """Each frame as a full-size RGBA grid, in order, plus durations and tags."""
    meta = json.load(open(meta_path, encoding="utf-8"))
    raw = meta["frames"]
    items = raw if isinstance(raw, list) else [raw[k] for k in raw]       # array or hash export
    frames, durations = [], []
    for it in items:
        r = it["frame"]
        src = it.get("sourceSize", {"w": r["w"], "h": r["h"]})
        off = it.get("spriteSourceSize", {"x": 0, "y": 0})
        grid = [[(0, 0, 0, 0)] * src["w"] for _ in range(src["h"])]
        for y in range(r["h"]):
            for x in range(r["w"]):
                grid[off["y"] + y][off["x"] + x] = rows[r["y"] + y][r["x"] + x]
        frames.append(grid)
        durations.append(it.get("duration", 100))
    tags = meta.get("meta", {}).get("frameTags", [])
    return frames, durations, tags


def scaled(grid, k):
    if k == 1:
        return grid
    out = []
    for row in grid:
        wide = [p for p in row for _ in range(k)]
        out.extend([list(wide) for _ in range(k)])
    return out


def lowest_opaque(grid):
    for y in range(len(grid) - 1, -1, -1):
        if any(p[3] > 0 for p in grid[y]):
            return y
    return len(grid) - 1


def placed(grid, cw, ch, baseline, feet):
    """Pad to the canvas, centred, with the feet row on the baseline."""
    h, w = len(grid), len(grid[0])
    out = [[(0, 0, 0, 0)] * cw for _ in range(ch)]
    ox, oy = (cw - w) // 2, baseline - feet
    for y in range(h):
        for x in range(w):
            ty, tx = y + oy, x + ox
            if 0 <= ty < ch and 0 <= tx < cw:
                out[ty][tx] = grid[y][x]
    return out


# ------------------------------------------------------------- normal map

def is_ink(p):
    """Outline pixels: opaque and dark. They separate regions the way parts do."""
    return p[3] > 0 and (0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]) < 60


def normal_map(grid):
    h, w = len(grid), len(grid[0])

    def luma(p):
        return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]

    def hue(p):
        """Hue in degrees and saturation; shading ramps keep a hue, region changes don't."""
        h_, _, s_ = colorsys.rgb_to_hls(p[0] / 255, p[1] / 255, p[2] / 255)
        return h_ * 360, s_

    def seam(x, y):
        """Where one colour region meets another (hair against face, sleeve against
        glove), even without a drawn line: the lower-hue side of a big hue change
        between two coloured pixels. Tones of one colour never count."""
        p = grid[y][x]
        hp, sp = hue(p)
        for dx, dy in ((1, 0), (0, 1)):
            qx, qy = x + dx, y + dy
            if 0 <= qx < w and 0 <= qy < h and grid[qy][qx][3] > 0:
                hq, sq = hue(grid[qy][qx])
                diff = abs(hp - hq)
                diff = min(diff, 360 - diff)
                if min(sp, sq) > 0.25 and diff > 35:
                    return True
                if (sp > 0.25) != (sq > 0.25) and abs(sp - sq) > 0.35:     # white or grey against a colour
                    return True
        return False

    solid = [[grid[y][x][3] > 0 and not is_ink(grid[y][x]) and not seam(x, y) for x in range(w)] for y in range(h)]
    # Distance to the nearest boundary (transparency or ink), two passes, 8-way.
    INF = 1e9
    d = [[0.0 if not solid[y][x] else INF for x in range(w)] for y in range(h)]
    r2 = math.sqrt(2)
    for y in range(h):
        for x in range(w):
            if d[y][x]:
                best = d[y][x]
                for dx, dy, c in ((-1, 0, 1), (0, -1, 1), (-1, -1, r2), (1, -1, r2)):
                    qx, qy = x + dx, y + dy
                    best = min(best, (d[qy][qx] if 0 <= qx < w and 0 <= qy < h else 0.0) + c)
                d[y][x] = best
    for y in range(h - 1, -1, -1):
        for x in range(w - 1, -1, -1):
            if d[y][x]:
                best = d[y][x]
                for dx, dy, c in ((1, 0, 1), (0, 1, 1), (1, 1, r2), (-1, 1, r2)):
                    qx, qy = x + dx, y + dy
                    best = min(best, (d[qy][qx] if 0 <= qx < w and 0 <= qy < h else 0.0) + c)
                d[y][x] = best
    # A dome over each region: rounds over about 6 px, then flat on top.
    R = 6.0
    ht = [[(math.sqrt(1 - (1 - min(1.0, d[y][x] / R)) ** 2) * R if solid[y][x] else 0.0) for x in range(w)] for y in range(h)]
    # Smooth, as gen_sprite_hd does, so one-pixel steps do not show as creases.
    for _ in range(2):
        ht = [[(sum(ht[yy][xx] for yy in range(max(0, y - 1), min(h, y + 2)) for xx in range(max(0, x - 1), min(w, x + 2))) / 9.0
                if solid[y][x] else 0.0) for x in range(w)] for y in range(h)]
    out = []
    for y in range(h):
        row = []
        for x in range(w):
            p = grid[y][x]
            if p[3] == 0:
                row.append((128, 128, 255, 0))
                continue
            if solid[y][x]:
                def H(xx, yy):
                    return ht[yy][xx] if 0 <= xx < w and 0 <= yy < h else 0.0
                nx = (H(x - 1, y) - H(x + 1, y)) * 0.5
                ny = (H(x, y - 1) - H(x, y + 1)) * 0.5
                nz = 1.0
            elif not is_ink(p):
                # A seam between regions: the crease, facing the camera.
                nx, ny, nz = 0.0, 0.0, 1.0
            else:
                # Ink: face away from the figure (towards the empty side), for the rim light.
                def opaque(xx, yy):
                    return 1 if 0 <= xx < w and 0 <= yy < h and grid[yy][xx][3] > 0 else 0
                sx = opaque(x + 1, y) - opaque(x - 1, y)
                sy = opaque(x, y + 1) - opaque(x, y - 1)
                nx, ny, nz = -sx * 0.9, -sy * 0.9, 0.45
            l = math.sqrt(nx * nx + ny * ny + nz * nz) or 1.0
            # OpenGL convention: +Y up, so image-down flips (as pixelart_hd.shade).
            row.append((int((nx / l * 0.5 + 0.5) * 255), int((-ny / l * 0.5 + 0.5) * 255), int((nz / l * 0.5 + 0.5) * 255), 255))
        out.append(row)
    return out


# ------------------------------------------------------------------- main

def strip_rows(grids, fw, fh):
    rows = []
    for y in range(fh):
        r = bytearray()
        for g in grids:
            for p in g[y]:
                r.extend(p)
        rows.append(r)
    return rows


def main(argv):
    if len(argv) < 4:
        print(__doc__)
        return 1
    sheet, meta_path, name = argv[1], argv[2], argv[3]
    opts = dict(zip(argv[4::2], argv[5::2]))
    k = int(opts.get("--scale", 1))
    out_dir = opts.get("--dir", os.path.join("assets", "sprites"))

    w, h, rows = read_png(sheet)
    frames, durations, tags = frames_from_json(meta_path, w, h, rows)
    frames = [scaled(f, k) for f in frames]
    feet = max(lowest_opaque(f) for f in frames)
    if "--canvas" in opts:
        cw, ch = (int(v) for v in opts["--canvas"].lower().split("x"))
        baseline = int(opts.get("--baseline", ch - (len(frames[0]) - feet)))
        frames = [placed(f, cw, ch, baseline, feet) for f in frames]
    else:
        baseline = int(opts.get("--baseline", feet))
    fh, fw = len(frames[0]), len(frames[0][0])

    clips, fps = {}, {}
    for t in tags:
        idx = list(range(t["from"], t["to"] + 1))
        if t.get("direction") == "reverse":
            idx.reverse()
        elif t.get("direction") == "pingpong":
            idx = idx + idx[-2:0:-1]
        clips[t["name"]] = idx
        ms = sum(durations[i] for i in range(t["from"], t["to"] + 1)) / (t["to"] - t["from"] + 1)
        fps[t["name"]] = round(1000.0 / max(1.0, ms), 2)
    if not clips:
        clips["idle"] = list(range(len(frames)))

    os.makedirs(out_dir, exist_ok=True)
    base = os.path.join(out_dir, name)
    write_png(base + ".png", fw * len(frames), fh, strip_rows(frames, fw, fh))
    write_png(base + "_n.png", fw * len(frames), fh, strip_rows([normal_map(f) for f in frames], fw, fh))
    with open(base + ".frames.json", "w") as f:
        json.dump({"frame_width": fw, "frame_height": fh, "baseline": baseline, "count": len(frames),
                   "clips": clips, "fps": fps, "source": "aseprite"}, f, indent=2)
    print(f"wrote {base}.png (+ normal map, frames.json): {len(frames)} frames of {fw}x{fh}, "
          f"baseline {baseline}, clips: {', '.join(f'{c}({len(v)})' for c, v in clips.items())}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
