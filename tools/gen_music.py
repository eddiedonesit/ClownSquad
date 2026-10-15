"""Clown Squad's soundtrack, composed and performed by this script.

Every song is original and generated from a short description: key, mode,
tempo, metre, a groove and a band. The composer writes real songs, not loops
of a few bars:

  intro | A | B | A | bridge | B | A        (the intro plays once; the rest loops)

A and B are 16 bars of two 8-bar phrases (question, answer) built from a
2-bar motif that is repeated, sequenced over the changing chords, varied and
then cadenced. The bridge brings a new motif, a new chord colour and a lighter
groove. Later passes add a counter-melody, pads, arpeggios and octave
doublings so the song grows each time round. The last bar lands on the
dominant and a drum fill, which leads straight back into A.

The band is synthesised: band-limited wavetables (calliope, brass, reeds,
accordion, strings, choir, kazoo), modal resonators (glockenspiel, marimba,
xylophone, music box, celesta, steel drum, vibes, piano), Karplus-Strong
plucks (ukulele, oud, harp, pizzicato, basses) and a synthesised drum kit, mixed
in stereo through a reverb. Pure Python, no dependencies. Songs render in
parallel, one process each.

Writes assets/audio/music_<name>.wav (stereo, 32 kHz) and
assets/audio/music_loops.json, the frame each song loops back to.

    python tools/gen_music.py            # every song
    python tools/gen_music.py world1 map # just these
"""

import cmath
import json
import math
import os
import random
import sys
import wave
from array import array
from multiprocessing import Pool

SR = 32000
TAU = 2.0 * math.pi
OUT = os.path.join("assets", "audio")


def mtof(m):
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


# =================================================================== sounds

TS = 4096
MASK = TS - 1
_TABLES = {}
_CACHE = {}


def spectrum(kind, f, K):
    """Harmonic amplitudes (k, a) of a timbre at fundamental f."""
    out = []
    for k in range(1, K + 1):
        if kind == "calliope":
            a = {1: 1.0, 2: 0.7, 3: 0.32, 4: 0.22, 5: 0.1, 6: 0.08}.get(k, 0.03 / k)
        elif kind == "saw_dark":
            a = math.exp(-k / 3.0) / k
        elif kind == "saw_mid":
            a = math.exp(-k / 7.0) / k
        elif kind == "saw_bright":
            a = math.exp(-k / 16.0) / k
        elif kind == "tuba_dark":
            a = math.exp(-k / 2.2) / k
        elif kind == "tuba_bright":
            a = math.exp(-k / 5.0) / k
        elif kind == "clarinet":
            a = (1.0 / k if k % 2 else 0.06 / k) * math.exp(-k / 11.0)
        elif kind == "reed":                      # oboe-ish: weak fundamental, nasal 2nd-5th
            a = (0.5, 1.0, 0.9, 0.75, 0.55, 0.35, 0.25, 0.18, 0.12)[k - 1] if k <= 9 else 0.1 / k
        elif kind == "pulse":                     # accordion reed
            a = abs(math.sin(math.pi * k * 0.28)) / k * math.exp(-k / 18.0)
        elif kind == "square":
            a = (1.0 / k if k % 2 else 0.0) * math.exp(-k / 20.0)
        elif kind == "flute":
            a = {1: 1.0, 2: 0.22, 3: 0.08, 4: 0.03}.get(k, 0.0)
        elif kind in ("choir", "kazoo"):
            fk = f * k
            if kind == "choir":                   # "aah": formants near 700, 1220, 2600 Hz
                fm = ((700, 130, 1.0), (1220, 90, 0.5), (2600, 160, 0.25))
                base = 1.0 / k ** 0.8
            else:                                 # buzzy comb with a nasal hump
                fm = ((520, 120, 1.0), (1500, 200, 0.8), (2700, 300, 0.4))
                base = 1.0 / k ** 0.35
            g = 0.08
            for fc, bw, amp in fm:
                g += amp / (1.0 + ((fk - fc) / bw) ** 2)
            a = base * g
        elif kind == "sine":
            a = 1.0 if k == 1 else 0.0
        else:
            raise ValueError(kind)
        if a > 1e-4:
            out.append((k, a))
    return out


def table(kind, f):
    K = min(64, max(1, int(SR * 0.45 / f)))
    key = (kind, K, round(f / 20) if kind in ("choir", "kazoo") else 0)
    t = _TABLES.get(key)
    if t is None:
        t = [0.0] * TS
        for k, a in spectrum(kind, f, K):
            w = TAU * k / TS
            for i in range(TS):
                t[i] += a * math.sin(w * i)
        m = max(abs(x) for x in t) or 1.0
        t = [x / m for x in t]
        _TABLES[key] = t
    return t


def lowpass(buf, fc):
    a = 1.0 - math.exp(-TAU * fc / SR)
    y = 0.0
    out = [0.0] * len(buf)
    for i, x in enumerate(buf):
        y += a * (x - y)
        out[i] = y
    return out


def highpass(buf, fc):
    lp = lowpass(buf, fc)
    return [x - l for x, l in zip(buf, lp)]


def noise(n, rnd):
    r = rnd.random
    return [r() * 2.0 - 1.0 for _ in range(n)]


def envelope(n, a, d, s, hold, r):
    """Attack a, decay d to level s, held until `hold` seconds, release r."""
    env = [0.0] * n
    na, nd, nh = max(1, int(a * SR)), max(1, int(d * SR)), int(hold * SR)
    level = 0.0
    rel_from = 0.0
    for i in range(n):
        if i >= nh:
            if i == nh:
                rel_from = level
            env[i] = rel_from * math.exp(-(i - nh) / (r * SR * 0.4))
            continue
        if i < na:
            level = i / na
        elif i < na + nd:
            level = 1.0 - (1.0 - s) * (i - na) / nd
        else:
            level = s
        env[i] = level
    return env


# Sustained wavetable voices.
# dark/bright: the tone fades from bright on the attack to dark (brass "blat").
WT = {
    "calliope": dict(dark="calliope", a=0.02, d=0.1, s=0.85, r=0.12, det=(-7, 6), vib=(6.2, 0.007, 0.0), breath=0.25),
    "calliope_eerie": dict(dark="calliope", a=0.04, d=0.2, s=0.8, r=0.3, det=(-18, 0, 15), vib=(4.5, 0.012, 0.0), breath=0.2),
    "trumpet": dict(dark="saw_mid", bright="saw_bright", a=0.03, d=0.15, s=0.8, r=0.08, det=(0,), vib=(5.5, 0.006, 0.25), blat=0.12),
    "horn": dict(dark="saw_dark", bright="saw_mid", a=0.06, d=0.2, s=0.85, r=0.15, det=(-4, 4), vib=(5.0, 0.004, 0.3), blat=0.2),
    "brass": dict(dark="saw_mid", bright="saw_bright", a=0.02, d=0.12, s=0.75, r=0.1, det=(-6, 5), vib=(5.5, 0.005, 0.3), blat=0.08),
    "trombone": dict(dark="saw_dark", bright="saw_mid", a=0.05, d=0.15, s=0.85, r=0.1, det=(0,), vib=(5.0, 0.006, 0.3), blat=0.15),
    "tuba": dict(dark="tuba_dark", bright="tuba_bright", a=0.015, d=0.12, s=0.6, r=0.06, det=(0,), vib=(0, 0, 0), blat=0.05),
    "clarinet": dict(dark="clarinet", a=0.03, d=0.1, s=0.9, r=0.08, det=(0,), vib=(5.0, 0.003, 0.35)),
    "oboe": dict(dark="reed", a=0.03, d=0.1, s=0.9, r=0.08, det=(0,), vib=(5.6, 0.007, 0.2)),
    "bassoon": dict(dark="reed", a=0.03, d=0.1, s=0.85, r=0.08, det=(0,), vib=(5.0, 0.004, 0.3)),
    "flute": dict(dark="flute", a=0.05, d=0.1, s=0.9, r=0.12, det=(0,), vib=(5.2, 0.006, 0.2), breath=0.35),
    "accordion": dict(dark="pulse", a=0.03, d=0.1, s=0.9, r=0.08, det=(-9, 9), vib=(0, 0, 0)),
    "strings": dict(dark="saw_mid", a=0.18, d=0.2, s=0.9, r=0.35, det=(-9, 0, 8), vib=(5.0, 0.004, 0.2)),
    "basstrings": dict(dark="saw_dark", bright="saw_mid", a=0.02, d=0.1, s=0.7, r=0.08, det=(-5, 5), vib=(0, 0, 0), blat=0.06),
    "choir": dict(dark="choir", a=0.25, d=0.2, s=0.9, r=0.5, det=(-12, 0, 11), vib=(5.0, 0.006, 0.0)),
    "square": dict(dark="square", a=0.01, d=0.1, s=0.8, r=0.08, det=(-8, 8), vib=(5.8, 0.005, 0.2)),
    "kazoo": dict(dark="kazoo", a=0.02, d=0.08, s=0.9, r=0.06, det=(0, 10), vib=(7.0, 0.018, 0.0)),
    "synthbass": dict(dark="saw_dark", bright="saw_mid", a=0.005, d=0.15, s=0.7, r=0.05, det=(-6, 6), vib=(0, 0, 0), blat=0.05, sub=0.6),
}

# Modal (struck) voices: partials (ratio, amp, decay seconds).
MODAL = {
    "glock": ((1.0, 1.0, 1.4), (2.76, 0.35, 0.3), (5.4, 0.15, 0.1)),
    "musicbox": ((1.0, 1.0, 1.6), (2.0, 0.25, 0.7), (4.2, 0.18, 0.25), (6.3, 0.08, 0.08)),
    "celesta": ((1.0, 1.0, 1.1), (2.0, 0.3, 0.45), (3.0, 0.12, 0.2), (4.0, 0.06, 0.1)),
    "marimba": ((1.0, 1.0, 0.55), (3.93, 0.28, 0.07), (9.4, 0.06, 0.02)),
    "xylo": ((1.0, 1.0, 0.3), (3.0, 0.45, 0.09), (6.0, 0.18, 0.03)),
    "steeldrum": ((1.0, 1.0, 0.7), (2.0, 0.7, 0.45), (3.0, 0.32, 0.25), (4.02, 0.16, 0.14), (5.03, 0.08, 0.08)),
    "vibes": ((1.0, 1.0, 1.8), (4.0, 0.18, 0.35), (10.0, 0.04, 0.06)),
    "bell": ((0.5, 0.4, 3.0), (1.0, 1.0, 2.2), (1.19, 0.35, 1.6), (1.5, 0.25, 1.2), (2.0, 0.4, 1.0), (2.74, 0.2, 0.5)),
    "piano": ((1.0, 1.0, 1.4), (2.0, 0.55, 0.9), (3.0, 0.3, 0.6), (4.0, 0.2, 0.45), (5.0, 0.12, 0.3), (6.0, 0.08, 0.25), (7.0, 0.05, 0.2)),
    "honky": ((1.0, 1.0, 1.2), (2.0, 0.6, 0.8), (3.0, 0.35, 0.55), (4.0, 0.22, 0.4), (5.0, 0.15, 0.3), (6.0, 0.1, 0.2)),
}

# Plucked strings: brightness (0-1), decay of the fundamental (s), how fast a stopped note dies.
PLUCK = {
    "uke": dict(bright=0.75, tau=0.7, mute=0.06),
    "banjo": dict(bright=0.95, tau=0.5, mute=0.05),
    "oud": dict(bright=0.6, tau=0.9, mute=0.08),
    "harp": dict(bright=0.45, tau=1.8, mute=0.5),
    "pizz": dict(bright=0.5, tau=0.25, mute=0.05),
    "upright": dict(bright=0.35, tau=0.9, mute=0.05, sub=0.5),
    "pluckbass": dict(bright=0.5, tau=0.6, mute=0.04, sub=0.4),
}


def render_wt(name, midi, dur, rnd):
    spec = WT[name]
    f = mtof(midi)
    rel = spec["r"]
    n = int((dur + rel * 3) * SR)
    out = [0.0] * n
    vr, vd, vdel = spec["vib"]
    blat = spec.get("blat", 0.0)
    has_bright = "bright" in spec
    for cents in spec["det"]:
        fv = f * 2.0 ** (cents / 1200.0)
        td = table(spec["dark"], fv)
        tb = table(spec["bright"], fv) if has_bright else td
        inc = fv * TS / SR
        ph = rnd.random() * TS
        vph = rnd.random() * TAU
        for b0 in range(0, n, 64):
            t = b0 / SR
            depth = vd * min(1.0, max(0.0, (t - vdel) / 0.3)) if vd else 0.0
            ii = inc * (1.0 + depth * math.sin(TAU * vr * t + vph))
            b1 = min(n, b0 + 64)
            if has_bright:
                br = 0.35 + 0.65 * (math.exp(-t / blat) if blat else 0.0)
                for i in range(b0, b1):
                    p = int(ph) & MASK
                    d = td[p]
                    out[i] += d + (tb[p] - d) * br
                    ph += ii
            else:
                for i in range(b0, b1):
                    out[i] += td[int(ph) & MASK]
                    ph += ii
    env = envelope(n, spec["a"], spec["d"], spec["s"], dur, rel)
    g = 1.0 / len(spec["det"])
    out = [x * e * g for x, e in zip(out, env)]
    if spec.get("sub"):
        inc = f * 0.5 * TS / SR
        sin = table("sine", f * 0.5)
        ph = 0.0
        sub = spec["sub"]
        for i in range(n):
            out[i] += sub * sin[int(ph) & MASK] * env[i]
            ph += inc
    if spec.get("breath"):                                # air at the mouth of the pipe
        m = int(0.09 * SR)
        nz = highpass(lowpass(noise(m, rnd), min(9000.0, f * 6)), f * 0.8)
        for i in range(min(m, n)):
            out[i] += nz[i] * spec["breath"] * (1.0 - i / m) ** 2
    return out


def render_modal(name, midi, dur, rnd):
    f = mtof(midi)
    parts = MODAL[name]
    keys = name in ("piano", "honky")
    scale = max(0.5, min(1.6, 300.0 / f)) if keys else 1.0
    long_tau = max(p[2] for p in parts) * scale
    n = int(min(dur + 0.3, long_tau * 4) * SR) if keys else int(min(4.5, long_tau * 4.5) * SR)
    n = max(n, int(0.05 * SR))
    out = [0.0] * n
    det = (0.0, 2.5) if name == "piano" else (0.0, 11.0) if name == "honky" else (0.0,)
    for cents in det:
        fd = f * 2.0 ** (cents / 1200.0)
        for ratio, amp, tau in parts:
            fp = fd * ratio
            if fp > SR * 0.45:
                continue
            tau *= scale
            z = cmath.exp(complex(-1.0 / (tau * SR), TAU * fp / SR))
            v = complex(0.0, amp / len(det))
            m = min(n, int(tau * 6 * SR))
            for i in range(m):
                out[i] += v.imag
                v *= z
    att = int((0.006 if name in ("steeldrum", "vibes") else 0.0015) * SR)
    for i in range(min(att, n)):
        out[i] *= i / att
    if name == "vibes":                                   # motor tremolo
        for i in range(n):
            out[i] *= 0.75 + 0.25 * math.cos(TAU * 5.0 * i / SR)
    if keys:                                              # the damper falls at note end
        s = int((dur + 0.02) * SR)
        for i in range(s, n):
            out[i] *= math.exp(-(i - s) / (0.08 * SR))
        hit = lowpass(noise(int(0.01 * SR), rnd), 3000)
        for i, x in enumerate(hit):
            out[i] += x * 0.15
    return out


def render_pluck(name, midi, dur, rnd):
    spec = PLUCK[name]
    f = mtof(midi)
    n = int((dur + 0.3) * SR)
    P = SR / f
    pi = int(P - 0.5)
    frac = P - 0.5 - pi
    if frac < 0.1:
        pi -= 1
        frac += 1.0
    C = (1.0 - frac) / (1.0 + frac)
    g = math.exp(-1.0 / (spec["tau"] * f))
    init = lowpass(noise(pi, rnd), 300 + 9000 * spec["bright"] ** 2)
    m = sum(init) / len(init)
    buf = [x - m for x in init]
    out = [0.0] * n
    j = 0
    last = 0.0
    apx = apy = 0.0
    for i in range(n):
        x = buf[j]
        out[i] = x
        a = 0.5 * (x + last)
        last = x
        ap = C * a + apx - C * apy
        apx, apy = a, ap
        buf[j] = ap * g
        j += 1
        if j == pi:
            j = 0
    pk = max(1e-9, max(abs(x) for x in out[: min(n, 2000)]))
    out = [x / pk for x in out]
    if spec.get("sub"):                                   # body: a pure fundamental under the string
        z = cmath.exp(complex(-1.0 / (spec["tau"] * 1.3 * SR), TAU * f / SR))
        v = complex(0.0, spec["sub"])
        for i in range(n):
            out[i] += v.imag
            v *= z
    s = int(dur * SR)
    mute = spec["mute"]
    for i in range(s, n):
        out[i] *= math.exp(-(i - s) / (mute * SR))
    return out


def note_sample(inst, midi, dur):
    dur = max(0.05, round(dur * 40) / 40.0)
    key = (inst, midi, dur)
    s = _CACHE.get(key)
    if s is None:
        rnd = random.Random(f"{inst}{midi}{dur}")
        if inst in WT:
            s = render_wt(inst, midi, dur, rnd)
        elif inst in MODAL:
            s = render_modal(inst, midi, dur, rnd)
        else:
            s = render_pluck(inst, midi, dur, rnd)
        # Every instrument speaks at one loudness: the RMS of its first 0.2 s,
        # so a ringing celesta and a breathy flute sit where the mix says.
        head = s[: int(0.2 * SR)]
        rms = math.sqrt(sum(x * x for x in head) / max(1, len(head)))
        if rms > 1e-6:
            g = NOTE_RMS / rms
            s = [x * g for x in s]
        _CACHE[key] = s
    return s


NOTE_RMS = 0.45


# ------------------------------------------------------------------- drums

def ring(n, partials, out):
    for fr, amp, tau in partials:
        z = cmath.exp(complex(-1.0 / (tau * SR), TAU * fr / SR))
        v = complex(0.0, amp)
        for i in range(min(n, int(tau * 7 * SR))):
            out[i] += v.imag
            v *= z


def drum_sample(name, var=0):
    key = ("drum", name, var)
    s = _CACHE.get(key)
    if s is not None:
        return s
    rnd = random.Random(f"{name}{var}")
    N = lambda sec: int(sec * SR)
    if name == "kick":
        n = N(0.4)
        s = [0.0] * n
        ph = 0.0
        for i in range(n):
            t = i / SR
            ph += TAU * (46 + 95 * math.exp(-t / 0.035)) / SR
            s[i] = math.sin(ph) * math.exp(-t / 0.16)
        for i, x in enumerate(lowpass(noise(N(0.006), rnd), 4000)):
            s[i] += x * 0.4
    elif name == "snare":
        n = N(0.25)
        nz = highpass(noise(n, rnd), 1200)
        s = [0.7 * nz[i] * math.exp(-i / (0.06 * SR)) + 0.5 * math.sin(TAU * 185 * i / SR) * math.exp(-i / (0.03 * SR)) for i in range(n)]
    elif name == "rim":
        n = N(0.08)
        s = [math.sin(TAU * 1700 * i / SR) * math.exp(-i / (0.008 * SR)) + 0.4 * math.sin(TAU * 520 * i / SR) * math.exp(-i / (0.02 * SR)) for i in range(n)]
    elif name in ("hat", "ohat"):
        n = N(0.06 if name == "hat" else 0.45)
        nz = highpass(highpass(noise(n, rnd), 7000), 7000)
        tau = 0.018 if name == "hat" else 0.14
        s = [x * math.exp(-i / (tau * SR)) for i, x in enumerate(nz)]
    elif name in ("crash", "ride", "choke"):
        n = N({"crash": 2.2, "ride": 1.2, "choke": 0.25}[name])
        nz = highpass(noise(n, rnd), 4500 if name == "crash" else 6000)
        tau = {"crash": 0.7, "ride": 0.35, "choke": 0.06}[name]
        s = [x * math.exp(-i / (tau * SR)) for i, x in enumerate(nz)]
        if name != "choke":
            k = 1.6 if name == "ride" else 1.0
            rt = 0.5 if name == "ride" else 0.9
            ring(n, ((3120, 0.2 * k, rt), (4630, 0.15 * k, rt), (5410, 0.12 * k, rt), (7340, 0.1 * k, rt)), s)
    elif name in ("tomhi", "tomlo", "timp", "bongohi", "bongolo"):
        f0 = {"tomhi": 190, "tomlo": 120, "timp": 73 * 2 ** (var / 12.0), "bongohi": 420, "bongolo": 300}[name]
        tau = {"tomhi": 0.18, "tomlo": 0.22, "timp": 0.9, "bongohi": 0.08, "bongolo": 0.1}[name]
        bend = 0.5 if name.startswith("tom") else 0.12
        n = N(tau * 5)
        s = [0.0] * n
        ph = 0.0
        for i in range(n):
            t = i / SR
            ph += TAU * f0 * (1.0 + bend * math.exp(-t / 0.03)) / SR
            s[i] = math.sin(ph) * math.exp(-t / tau)
            if name == "timp":
                s[i] += 0.3 * math.sin(ph * 1.5) * math.exp(-t / (tau * 0.6)) + 0.2 * math.sin(ph * 2.0) * math.exp(-t / (tau * 0.5))
        for i, x in enumerate(lowpass(noise(N(0.02), rnd), 2500)):
            s[i] += x * 0.3
    elif name == "shaker":
        n = N(0.07)
        nz = highpass(noise(n, rnd), 5000)
        s = [x * math.sin(math.pi * i / n) ** 2 for i, x in enumerate(nz)]
    elif name == "tamb":
        n = N(0.18)
        nz = highpass(noise(n, rnd), 6000)
        s = [x * math.exp(-i / (0.05 * SR)) for i, x in enumerate(nz)]
        ring(n, ((5300, 0.3, 0.06), (6900, 0.3, 0.06), (8100, 0.3, 0.06)), s)
    elif name == "wood":
        n = N(0.1)
        s = [math.sin(TAU * 880 * i / SR) * math.exp(-i / (0.02 * SR)) + 0.5 * math.sin(TAU * 1390 * i / SR) * math.exp(-i / (0.012 * SR)) for i in range(n)]
    elif name == "clap":
        n = N(0.2)
        nz = highpass(lowpass(noise(n, rnd), 3500), 900)
        s = [0.0] * n
        for burst in (0.0, 0.011, 0.022):
            b = int(burst * SR)
            for i in range(b, n):
                s[i] += nz[i] * math.exp(-(i - b) / ((0.05 if burst == 0.022 else 0.006) * SR))
    elif name == "triangle":
        n = N(2.0)
        s = [0.0] * n
        ring(n, ((2400, 1.0, 0.9), (6100, 0.5, 0.9), (8700, 0.3, 0.9)), s)
    elif name == "swell":                                 # cymbal swell into a section
        n = N(1.6)
        nz = highpass(noise(n, rnd), 5000)
        s = [x * (i / n) ** 2.5 for i, x in enumerate(nz)]
    else:
        raise ValueError(name)
    pk = max(1e-9, max(abs(x) for x in s))
    s = [x / pk for x in s]
    _CACHE[key] = s
    return s


# =================================================================== mixing

class Mix:
    def __init__(self, seconds):
        n = int(seconds * SR)
        self.L = [0.0] * n
        self.R = [0.0] * n
        self.S = [0.0] * n          # reverb send (mono)

    def add(self, s, t, gain, pan=0.0, send=0.2):
        o = int(t * SR)
        if o < 0:
            s = s[-o:]
            o = 0
        e = min(len(self.L), o + len(s))
        if e <= o:
            return
        if e - o < len(s):
            s = s[: e - o]
        a = (pan + 1.0) * math.pi / 4.0
        gl, gr = gain * math.cos(a), gain * math.sin(a)
        L, R = self.L, self.R
        L[o:e] = [x + y * gl for x, y in zip(L[o:e], s)]
        R[o:e] = [x + y * gr for x, y in zip(R[o:e], s)]
        if send > 0:
            gs = gain * send
            S = self.S
            S[o:e] = [x + y * gs for x, y in zip(S[o:e], s)]


def comb(x, D, g):
    y = list(x)
    n = len(y)
    for s in range(D, n, D):
        e = min(n, s + D)
        y[s:e] = [a + g * b for a, b in zip(y[s:e], y[s - D:e - D])]
    return y


def allpass(x, D, g):
    n = len(x)
    y = [-g * v for v in x]
    for s in range(D, n, D):
        e = min(n, s + D)
        y[s:e] = [-g * a + b + g * c for a, b, c in zip(x[s:e], x[s - D:e - D], y[s - D:e - D])]
    return y


def reverb(send, size):
    """A Schroeder/Freeverb-style hall: parallel combs and series allpasses,
    a slightly different set per side for width."""
    src = highpass(lowpass(send, 5200), 180)
    k = SR / 44100.0
    combs = (1116, 1188, 1277, 1356, 1422, 1491)
    outs = []
    for spread in (0, 23):
        acc = [0.0] * len(src)
        for d in combs:
            c = comb(src, int((d + spread) * k), size)
            acc = [a + b for a, b in zip(acc, c)]
        acc = [a / len(combs) for a in acc]
        for d in (556, 441, 341, 225):
            acc = allpass(acc, int((d + spread) * k), 0.5)
        outs.append(lowpass(acc, 6500))
    return outs


# ================================================================== harmony

MODES = {
    "major": (0, 2, 4, 5, 7, 9, 11),
    "minor": (0, 2, 3, 5, 7, 8, 10),
    "harmonic": (0, 2, 3, 5, 7, 8, 11),
    "dorian": (0, 2, 3, 5, 7, 9, 10),
    "phrygdom": (0, 1, 4, 5, 7, 8, 10),
}
RAISE_V = ("minor", "dorian")      # the dominant borrows a leading tone


class Key:
    def __init__(self, tonic, mode):
        self.tonic, self.mode, self.scale = tonic, mode, MODES[mode]

    def midi(self, d, chord=None):
        o, i = divmod(d, 7)
        semi = self.scale[i]
        if chord is not None and self.mode in RAISE_V and chord[0] % 7 == 4 and i == 6:
            semi += 1
        return self.tonic + 12 * o + semi

    @staticmethod
    def tones(chord):
        r, seventh = chord
        t = [r % 7, (r + 2) % 7, (r + 4) % 7]
        if seventh:
            t.append((r + 6) % 7)
        return t


def snap(d, tones):
    """The chord tone (in scale degrees, any octave) nearest degree d."""
    best, bd = d, 99.0
    for t in tones:
        for k in range(-4, 5):
            c = t + 7 * k
            dd = abs(c - d) + (0.1 if c > d else 0.0)
            if dd < bd:
                best, bd = c, dd
    return best


PROGS = {
    "major": [
        [0, 5, 1, 4, 0, 5, 1, 4], [0, 3, 0, 4, 0, 3, 1, 4], [0, 0, 3, 3, 1, 4, 0, 4],
        [0, 4, 5, 2, 3, 0, 1, 4], [0, 2, 3, 0, 3, 4, 0, 4], [0, 5, 3, 4, 0, 5, 3, 4],
        [0, 0, 4, 4, 4, 4, 0, 4], [0, 3, 4, 0, 5, 1, 3, 4],
    ],
    "minor": [
        [0, 5, 2, 6, 0, 5, 3, 4], [0, 3, 0, 4, 0, 3, 4, 4], [0, 6, 5, 4, 0, 6, 5, 4],
        [0, 3, 6, 2, 5, 1, 4, 4], [0, 0, 5, 5, 3, 3, 4, 4], [0, 5, 0, 4, 3, 0, 1, 4],
    ],
    "phrygdom": [[0, 1, 0, 1, 6, 1, 0, 0], [0, 0, 1, 1, 0, 6, 1, 0], [3, 3, 1, 1, 0, 6, 1, 0]],
}
BRIDGE = {
    "major": [[3, 3, 0, 0, 3, 3, 1, 4], [5, 5, 3, 3, 1, 1, 4, 4], [3, 4, 2, 5, 1, 4, 0, 4]],
    "minor": [[5, 5, 2, 2, 3, 3, 4, 4], [3, 3, 0, 0, 5, 6, 4, 4], [2, 6, 5, 1, 3, 4, 0, 4]],
    "phrygdom": [[3, 3, 1, 1, 6, 6, 1, 1], [5, 5, 1, 1, 3, 3, 1, 0]],
}


def prog_family(mode):
    return "major" if mode == "major" else "phrygdom" if mode == "phrygdom" else "minor"


# =================================================================== melody

CELLS = {
    0: [[1.0], [1.0], [2.0], [1.5, 0.5], [0.5, 0.5]],
    1: [[1.0], [0.5, 0.5], [0.5, 0.5], [0.75, 0.25], [1.5, 0.5], [2.0], [1.0]],
    2: [[0.5, 0.5], [0.5, 0.5], [0.25, 0.25, 0.5], [0.5, 0.25, 0.25], [0.75, 0.25], [1.0], [0.25, 0.25, 0.25, 0.25]],
}


def make_motif(rnd, meter, density):
    """Two bars of rhythm and contour: [(start beat, beats, step | None for a rest)]."""
    beats = meter * 2
    end_long = beats - (2.0 if meter == 4 else 1.0)       # the motif ends on a long note
    notes = []
    t = 0.0
    while t < end_long - 1e-6:
        opts = [c for c in CELLS[density] if t + sum(c) <= end_long + 1e-6 and (sum(c) < 2 or abs(t % 2) < 1e-6)]
        if not opts:
            opts = [[end_long - t]]
        for d in rnd.choice(opts):
            notes.append([t, d])
            t += d
    notes.append([t, beats - t])
    rest_first = rnd.random() < 0.3 and len(notes) > 4   # a breath: the tune starts on the off-beat
    out = []
    last = 0
    for i, (t, d) in enumerate(notes):
        if i == 0:
            s = 0
        elif abs(last) >= 3:
            s = -1 if last > 0 else 1                     # after a leap, step back
        else:
            rising = i < len(notes) * 0.55
            s = rnd.choices([-2, -1, 0, 1, 2, 3, -3, 4, -4], [1.2, 3, 0.8, 3, 1.5, 0.7, 0.6, 0.25, 0.2])[0]
            if rising and s < 0 and rnd.random() < 0.35:
                s = -s
            if not rising and s > 0 and rnd.random() < 0.35:
                s = -s
        last = s
        out.append((t, d, None if (i == 0 and rest_first) else s))
    return out


def vary(rnd, motif):
    """Same rhythm, a new contour: the answer to a motif."""
    out = []
    for i, (t, d, s) in enumerate(motif):
        if s is None or i == 0:
            out.append((t, d, s))
        else:
            out.append((t, d, -s if rnd.random() < 0.6 else rnd.choice([-1, 1, 2, -2])))
    return out


DRUM_GAIN = {"kick": 0.9, "snare": 0.5, "rim": 0.35, "hat": 0.22, "ohat": 0.2, "crash": 0.35, "ride": 0.2, "choke": 0.25,
             "tomhi": 0.5, "tomlo": 0.55, "timp": 0.6, "bongohi": 0.35, "bongolo": 0.38, "shaker": 0.16, "tamb": 0.2,
             "wood": 0.3, "clap": 0.4, "triangle": 0.15, "swell": 0.3}
DRUM_PAN = {"hat": 0.35, "ohat": 0.35, "ride": -0.35, "tomhi": 0.25, "tomlo": -0.2, "bongohi": 0.4, "bongolo": 0.2,
            "shaker": -0.45, "tamb": 0.45, "wood": -0.3, "triangle": 0.5, "rim": -0.1, "choke": -0.2}

E8 = [(b * 0.5, 0.7 if b % 2 == 0 else 0.45) for b in range(8)]
GROOVES = {
    "march": dict(kick=[(0, 1.0), (2, 0.9)], snare=[(1, 0.8), (3, 0.8), (3.5, 0.35), (1.75, 0.3)], choke=[(1, 0.6), (3, 0.6)],
                  light=("kick", "choke")),
    "polka": dict(kick=[(0, 1.0), (2, 0.9)], snare=[(1, 0.6), (3, 0.6)], hat=[(0.5, 0.6), (1.5, 0.6), (2.5, 0.6), (3.5, 0.6)],
                  light=("kick", "hat")),
    "gallop": dict(kick=[(0, 1.0), (1, 0.85), (2, 1.0), (3, 0.85)], snare=[(0.5, 0.7), (1.5, 0.7), (2.5, 0.7), (3.5, 0.7)],
                   hat=[(b * 0.25, 0.5 if b % 2 else 0.3) for b in range(16)], light=("kick", "hat")),
    "swing": dict(ride=[(0, 0.8), (1, 0.7), (1.5, 0.5), (2, 0.8), (3, 0.7), (3.5, 0.5)], hat=[(1, 0.6), (3, 0.6)],
                  kick=[(0, 0.45), (2, 0.35)], snare=[(3.5, 0.2), (1.5, 0.15)], light=("ride", "hat")),
    "calypso": dict(kick=[(0, 1.0), (1.5, 0.6), (2, 0.9), (3.5, 0.5)], rim=[(1, 0.7), (2.5, 0.5), (3, 0.7)],
                    shaker=[(b * 0.25, 0.8 if b % 2 == 0 else 0.45) for b in range(16)],
                    bongohi=[(0.5, 0.6), (1.75, 0.45), (2.5, 0.6), (3.75, 0.45)], bongolo=[(1.5, 0.6), (3.25, 0.5)],
                    light=("shaker", "bongohi", "bongolo"), fill="bongo"),
    "forest": dict(kick=[(0, 0.8), (2, 0.6)], wood=[(1, 0.6), (3, 0.6), (3.5, 0.35)],
                   hat=[(0.5, 0.35), (1.5, 0.35), (2.5, 0.35), (3.5, 0.35)], triangle=[(0, 0.8)],
                   light=("wood", "triangle", "hat"), fill="toms"),
    "waltz": dict(kick=[(0, 1.0)], choke=[(1, 0.5), (2, 0.5)], triangle=[(0, 0.9)], light=("triangle", "choke")),
    "drive": dict(kick=[(0, 1.0), (1.5, 0.6), (2, 0.9), (2.5, 0.5)], snare=[(1, 0.9), (3, 0.9)], hat=E8,
                  light=("kick", "hat"), fill="toms"),
    "rock": dict(kick=[(0, 1.0), (0.75, 0.6), (1.5, 0.8), (2, 1.0), (2.75, 0.6)], snare=[(1, 1.0), (3, 1.0)],
                 hat=[(b * 0.5, 0.65 if b % 2 == 0 else 0.4) for b in range(7)], ohat=[(3.5, 0.6)],
                 light=("kick", "hat"), fill="toms"),
    "desert": dict(kick=[(0, 1.0), (2, 0.7)], bongolo=[(0, 0.8), (1.5, 0.6), (2, 0.7)],
                   bongohi=[(0.75, 0.5), (1, 0.55), (2.5, 0.6), (3, 0.5), (3.5, 0.55)], tamb=E8, triangle=[(0, 0.6)],
                   light=("tamb", "bongolo", "bongohi"), fill="bongo"),
    "sparse": dict(kick=[(0, 0.8)], rim=[(2.5, 0.5)], hat=[(1, 0.3), (3, 0.3)], wood=[(3.75, 0.25)],
                   light=("rim", "hat")),
}


class Song:
    def __init__(self, spec):
        self.spec = spec
        self.rnd = random.Random(spec["seed"])
        self.key = Key(spec["tonic"], spec["mode"])
        self.meter = spec.get("meter", 4)
        self.beat = 60.0 / spec["bpm"]
        self.swing = spec.get("swing", 0.5)
        self.events = []        # (time, instrument | ("drum", name, var), midi, seconds, gain, pan, send)
        self.bars = []          # (degree, seventh) per bar
        self.sections = []

    def time(self, bar, beat):
        b = math.floor(beat)
        f = beat - b
        s = self.swing
        if s != 0.5 and f > 1e-6:
            f = f * (s / 0.5) if f < 0.5 else s + (f - 0.5) * ((1 - s) / 0.5)
        return (bar * self.meter + b + f) * self.beat

    # -------------------------------------------------------- planning
    def plan(self):
        fam = prog_family(self.key.mode)
        rnd = self.rnd
        bar_sec = self.meter * self.beat
        L = 8 if bar_sec > 2.2 else 16
        progA = rnd.choice(PROGS[fam])
        progB = rnd.choice([p for p in PROGS[fam] if p != progA])
        progC = rnd.choice(BRIDGE[fam])
        dens = self.spec.get("density", 1)
        m1 = make_motif(rnd, self.meter, dens)
        m2 = make_motif(rnd, self.meter, max(0, min(2, dens + rnd.choice([-1, 1]))))
        m3 = make_motif(rnd, self.meter, max(0, dens - 1))
        intro = [0, 1, 0, 1] if fam == "phrygdom" else [0, 3, 0, 4]
        form = [
            dict(name="intro", bars=4, prog=intro, motif=None, lead=None, inten=1, arp=True),
            dict(name="A", bars=L, prog=progA, motif=m1, lead="lead", inten=2),
            dict(name="B", bars=L, prog=progB, motif=m2, lead="lead", inten=2, counter=True, lift=2),
            dict(name="A2", bars=8, prog=progA, motif=m1, lead="lead2", inten=2, arp=True, counter=True),
            dict(name="C", bars=8, prog=progC, motif=m3, lead="lead2", inten=1, pad=True, arp=True, lift=1),
            dict(name="B2", bars=L, prog=progB, motif=m2, lead="lead", double=True, inten=3, counter=True, pad=True, lift=2),
            dict(name="A3", bars=L, prog=progA, motif=m1, lead="lead", double=True, inten=3, arp=True, pad=True, last=True),
        ]
        bar = 0
        for sec in form:
            n = sec["bars"]
            chords = []
            for i in range(n):
                deg = sec["prog"][i % len(sec["prog"])]
                in_phrase = i % 8
                answer = sec["name"] not in ("intro", "C") and (n == 8 or i >= 8)
                if answer and in_phrase == 6:
                    deg = 0 if sec.get("last") and fam != "phrygdom" else (1 if fam == "phrygdom" else 4)
                if answer and in_phrase == 7:
                    deg = 0
                    if sec.get("last"):                    # the end turns back to the top of the loop
                        deg = 1 if fam == "phrygdom" else 4
                if not answer and in_phrase == 7 and sec["name"] != "intro":
                    deg = 1 if fam == "phrygdom" else 4
                seventh = deg == 4 and self.spec.get("sevenths", True) and fam != "phrygdom"
                chords.append((deg, seventh))
            sec["start"] = bar
            sec["chords"] = chords
            self.bars.extend(chords)
            bar += n
        self.sections = form
        self.total_bars = bar
        self.loop_bar = form[1]["start"]

    # ------------------------------------------------------- writing
    def emit(self, part, bar, beat, midi, dur_beats, vel):
        inst, gain, pan, send, artic = self.spec["parts"][part]
        t = self.time(bar, beat) + self.rnd.uniform(-0.004, 0.006)
        self.events.append((t, inst, midi, dur_beats * self.beat * artic, gain * vel, pan, send))

    def drum(self, name, bar, beat, vel, var=0, pan=None, send=0.12):
        t = self.time(bar, beat) + self.rnd.uniform(-0.003, 0.003)
        gain = vel * self.spec.get("drum_gain", 1.0) * DRUM_GAIN.get(name, 0.5)
        self.events.append((t, ("drum", name, var), None, 0, gain, DRUM_PAN.get(name, 0.0) if pan is None else pan, send))

    def write_melody(self, sec):
        if sec["motif"] is None:
            return
        key, meter, rnd = self.key, self.meter, self.rnd
        lo, hi = self.spec.get("range", (2, 13))
        lift = sec.get("lift", 0)
        motif = sec["motif"]
        answer = vary(rnd, motif)
        prev = 4 + lift
        first_d = first_root = 0
        phrases = sec["bars"] // 8
        for p in range(phrases):
            final = p == phrases - 1
            for c, cell in enumerate((motif, motif, answer, motif)):
                bar0 = sec["start"] + p * 8 + c * 2
                ch0 = self.bars[bar0]
                tones0 = key.tones(ch0)
                if c == 0:
                    d = snap(4 + lift, tones0)
                    first_d, first_root = d, ch0[0]
                elif c == 1:                               # the motif again, moved with the harmony
                    shift = (ch0[0] - first_root) % 7
                    if shift > 3:
                        shift -= 7
                    d = snap(first_d + shift, tones0)
                elif c == 2:                               # the answer climbs
                    d = snap(prev + 2, tones0)
                else:
                    d = snap(first_d, tones0)
                for i, (t, dur, step) in enumerate(cell):
                    bar = bar0 + int(t // meter)
                    beat = t - (bar - bar0) * meter
                    ch = self.bars[bar]
                    tones = key.tones(ch)
                    if step is None:
                        continue
                    if i > 0:
                        d += step
                    on_beat = abs(beat - round(beat)) < 1e-6
                    strong = on_beat and (int(round(beat)) % (2 if meter == 4 else 3) == 0 or dur >= 1)
                    last_note = i == len(cell) - 1
                    if last_note and c == 3 and final and not sec.get("last"):
                        d = snap(d, tones[:2] if ch[0] == 0 else tones)   # home: the root or the third
                    elif strong or last_note:
                        d = snap(d, tones)
                    while d > hi:
                        d = snap(d - 3, tones) if (strong or last_note) else d - 2
                    while d < lo:
                        d = snap(d + 3, tones) if (strong or last_note) else d + 2
                    m = key.midi(d, ch)
                    vel = 1.0 if strong else 0.82
                    part = sec["lead"]
                    if self.spec.get("graces") and dur >= 1 and beat >= 0.25 and rnd.random() < 0.3:
                        self.emit(part, bar, beat - 0.12, m - 1, 0.1, 0.6)
                    self.emit(part, bar, beat, m, dur, vel)
                    if sec.get("double"):
                        other = "lead2" if part == "lead" else "lead"
                        up = 12 if self.spec.get("double_up", True) else -12
                        self.emit(other, bar, beat, m + up, dur, vel * 0.5)
                prev = d

    def voicing(self, chord, prev, lo=55, hi=72, n=3):
        key = self.key
        tones = [key.midi(t, chord) % 12 for t in Key.tones(chord)]
        if n == 3 and len(tones) == 4:
            tones = [tones[0], tones[1], tones[3]]           # root, third, seventh
        cands = [m for m in range(lo, hi + 1) if m % 12 in tones]
        targets = sorted(prev) if prev else [(lo + hi) / 2 - 4, (lo + hi) / 2, (lo + hi) / 2 + 4]
        out, used = [], set()
        for t in targets[:n]:
            free = [c for c in cands if c % 12 not in used]
            if not free:
                break
            best = min(free, key=lambda c: abs(c - t))
            out.append(best)
            used.add(best % 12)
        return sorted(out)

    def write_comp(self, sec):
        style = self.spec["comp"]
        meter = self.meter
        prev = None
        for i, ch in enumerate(sec["chords"]):
            bar = sec["start"] + i
            v = self.voicing(ch, prev)
            prev = v
            hits = []
            if style == "pah":
                hits = [(1, 0.4, 0.8), (3, 0.4, 0.8)] if meter == 4 else [(1, 0.5, 0.8), (2, 0.5, 0.8)]
                if self.spec["bass"] == "gallop":
                    hits = [(0.5, 0.3, 0.8), (1.5, 0.3, 0.8), (2.5, 0.3, 0.8), (3.5, 0.3, 0.8)]
            elif style == "stride":
                hits = [(1, 0.5, 0.75), (3, 0.5, 0.75)]
            elif style == "waltz":
                hits = [(1, 0.6, 0.7), (2, 0.6, 0.6)]
            elif style == "stabs":
                hits = [(0, 0.3, 0.9), (1.5, 0.25, 0.8), (2.5, 0.3, 0.85), (3.5, 0.2, 0.7)] if meter == 4 else [(0, 0.3, 0.9), (2, 0.3, 0.8)]
            elif style == "pad":
                hits = [(0, meter, 0.7)]
            elif style == "strum":
                for k, b in enumerate((0, 1, 1.5, 2.5, 3, 3.5) if meter == 4 else (0, 1, 2)):
                    order = v if k % 2 == 0 else list(reversed(v))
                    for j, m in enumerate(order):
                        self.emit("comp", bar, b + j * 0.035, m, 0.45, 0.85 if k % 2 == 0 else 0.6)
            for b, d, vel in hits:
                for m in v:
                    self.emit("comp", bar, b, m, d, vel)

    def write_arp(self, sec):
        meter = self.meter
        step = self.spec.get("arp_step", 0.5)
        prev = None
        for i, ch in enumerate(sec["chords"]):
            bar = sec["start"] + i
            v = self.voicing(ch, prev, 62, 79, 3)
            prev = v
            seq = v + [v[0] + 12]
            pattern = seq + list(reversed(seq[1:-1]))
            k = 0
            b = 0.0
            while b < meter - 1e-6:
                self.emit("arp", bar, b, pattern[k % len(pattern)], step * 1.5, 0.9 if k % 2 == 0 else 0.7)
                k += 1
                b += step

    def write_pad(self, sec):
        prev = None
        for i, ch in enumerate(sec["chords"]):
            v = self.voicing(ch, prev, 50, 70, 3)
            prev = v
            for m in v:
                self.emit("pad", sec["start"] + i, 0, m, self.meter, 0.65)

    def write_counter(self, sec):
        key = self.key
        d = 9
        for i, ch in enumerate(sec["chords"]):
            tones = Key.tones(ch)
            guide = [tones[1], tones[3] if len(tones) > 3 else tones[2]]
            d = max(5, min(12, snap(d, guide)))
            bar = sec["start"] + i
            if self.spec.get("counter_style") == "answer" and i % 2 == 1:
                for k, b in enumerate((self.meter - 1.5, self.meter - 1, self.meter - 0.5)):
                    self.emit("counter", bar, b, key.midi(d + k - 1, ch), 0.5, 0.7)
            else:
                self.emit("counter", bar, 0, key.midi(d, ch), self.meter, 0.75)

    def write_bass(self, sec):
        style = self.spec["bass"]
        key, meter = self.key, self.meter
        n = len(sec["chords"])
        for i, ch in enumerate(sec["chords"]):
            bar = sec["start"] + i
            root = key.midi(ch[0], ch) % 12
            r = 36 + root if root <= 9 else 24 + root          # E1..A2 roots, low and round
            fifth = r + 7 if r + 7 <= 50 else r - 5
            third = r + (key.midi(ch[0] + 2, ch) - key.midi(ch[0], ch)) % 12
            nb = bar + 1 if bar + 1 < self.total_bars else self.loop_bar
            nxt = self.bars[nb]
            nr = r - root + key.midi(nxt[0], nxt) % 12
            if nr - r > 6:
                nr -= 12
            if r - nr > 6:
                nr += 12
            if style == "oompah":
                pat = [(0, r, 0.9), (2, fifth, 0.9)] if meter == 4 else [(0, r, 0.9)]
                if i % 4 == 3 and meter == 4 and nr != r:
                    pat = [(0, r, 0.9), (2, fifth, 0.45), (3, nr + (1 if nr < r else -1), 0.45)]
            elif style == "gallop":
                pat = [(0, r, 0.45), (1, fifth, 0.45), (2, r, 0.45), (3, fifth, 0.45)]
            elif style == "waltz":
                pat = [(0, r if i % 2 == 0 else fifth, 0.95)]
            elif style == "walking":
                approach = nr + (1 if self.rnd.random() < 0.5 else -1)
                pat = [(0, r, 0.95), (1, third, 0.95), (2, fifth, 0.95), (3, approach, 0.95)]
            elif style == "calypso":
                pat = [(0, r, 1.4), (1.5, r, 0.45), (2, fifth, 1.4), (3.5, r + 12, 0.45)]
            elif style == "drive":
                pat = [(b * 0.5, fifth if (b == 7 and i % 2) else r, 0.42) for b in range(meter * 2)]
            elif style == "habanera":
                pat = []
                for h in range(0, meter, 2):
                    pat += [(h, r, 0.7), (h + 0.75, fifth, 0.2), (h + 1, third + 12, 0.45), (h + 1.5, fifth, 0.45)]
            elif style == "two":
                pat = [(0, r, 1.6), (2, fifth, 1.6)] if meter == 4 else [(0, r, 2.5)]
            else:   # pedal
                pat = [(0, r, meter)]
            for b, m, d in pat:
                self.emit("bass", bar, b, m, d, 0.95 if b == 0 else 0.8)

    def write_drums(self, sec):
        inten = sec["inten"]
        meter = self.meter
        groove = GROOVES[self.spec["drums"]]
        light = groove.get("light", ())
        n = len(sec["chords"])
        for i in range(n):
            bar = sec["start"] + i
            fill = inten >= 2 and (i % 8 == 7 or i == n - 1)
            if i == 0 and inten >= 2:
                self.drum("crash", bar, 0, 0.9, pan=-0.3, send=0.25)
            elif inten == 3 and i % 4 == 0:
                self.drum("crash", bar, 0, 0.55, pan=0.3, send=0.25)
            for name, hits in groove.items():
                if name in ("light", "fill"):
                    continue
                if inten == 1 and name not in light:
                    continue
                if name == "triangle" and i % 4 != 0:
                    continue
                for b, vel in hits:
                    if b >= meter or (fill and b >= meter - 1):
                        continue
                    v = vel * (0.75 if inten == 1 else 1.0) * self.rnd.uniform(0.9, 1.05)
                    self.drum(name, bar, b, v, var=self.rnd.randint(0, 2))
            if fill:
                kind = groove.get("fill", "snare")
                steps = 4 if meter == 4 else 3
                for k in range(steps):
                    if kind == "toms":
                        name = ("tomhi", "tomhi", "tomlo", "tomlo")[k]
                    elif kind == "bongo":
                        name = ("bongohi", "bongohi", "bongolo", "bongolo")[k]
                    else:
                        name = "snare"
                    self.drum(name, bar, meter - 1 + k / steps, 0.55 + 0.4 * k / steps, var=k % 3)
            if self.spec.get("timpani") and inten >= 2 and i % 4 == 0:
                ch = sec["chords"][i]
                semis = (self.key.midi(ch[0], ch) - 34) % 12
                self.drum("timp", bar, 0, 0.8, var=semis - 12 if semis > 6 else semis, send=0.3)
        if self.spec.get("drips") and sec["name"] in ("intro", "A2", "C"):     # water in the dark
            for i in range(n):
                if self.rnd.random() < 0.6:
                    b = self.rnd.choice([0.5, 1.5, 2.25, 3.25][:meter])
                    ch = sec["chords"][i]
                    m = self.key.midi(self.rnd.choice([7, 9, 11, 14]), ch) + 12
                    self.events.append((self.time(sec["start"] + i, b), "bell", m, 0.5, 0.12, self.rnd.uniform(-0.8, 0.8), 0.8))

    def compose(self):
        self.plan()
        for sec in self.sections:
            self.write_melody(sec)
            self.write_bass(sec)
            self.write_comp(sec)
            if sec.get("arp") and "arp" in self.spec["parts"]:
                self.write_arp(sec)
            if sec.get("pad") and "pad" in self.spec["parts"]:
                self.write_pad(sec)
            if sec.get("counter") and "counter" in self.spec["parts"]:
                self.write_counter(sec)
            self.write_drums(sec)
            if sec["name"] == "C":                         # a cymbal swell back into the tune
                beats = 1.6 / self.beat
                self.drum("swell", sec["start"] + sec["bars"], -beats, 0.5, send=0.4)

    def render(self):
        loop_end = self.total_bars * self.meter * self.beat
        loop_start = self.loop_bar * self.meter * self.beat
        mix = Mix(loop_end + 5.0)
        for t, inst, midi, dur, gain, pan, send in self.events:
            s = drum_sample(inst[1], inst[2]) if isinstance(inst, tuple) else note_sample(inst, midi, dur)
            mix.add(s, t, gain, pan, send)
        wl, wr = reverb(mix.S, self.spec.get("room", 0.8))
        wet = self.spec.get("wet", 0.35) * 3.0
        L = [a + b * wet for a, b in zip(mix.L, wl)]
        R = [a + b * wet for a, b in zip(mix.R, wr)]
        n0, n1 = int(loop_start * SR), int(loop_end * SR)
        for ch in (L, R):                                  # the tail rings on over the loop point
            for i in range(n1, len(ch)):
                j = n0 + (i - n1)
                if j < n1:
                    ch[j] += ch[i]
            del ch[n1:]
        el = math.sqrt(sum(x * x for x in L[::7]))         # centre the image: equal channel loudness
        er = math.sqrt(sum(x * x for x in R[::7]))
        if el > 0 and er > 0:
            bal = math.sqrt(er / el)
            L = [x * bal for x in L]
            R = [x / bal for x in R]
        pk = max(1e-9, max(abs(x) for x in L), max(abs(x) for x in R))
        drive = 1.5                                        # bus glue: a soft saturating limiter
        norm = math.tanh(drive)
        L = [math.tanh(drive * x / pk) / norm for x in L]
        R = [math.tanh(drive * x / pk) / norm for x in R]
        return L, R, n0


# =================================================================== songs
# parts: role -> (instrument, gain, pan, reverb send, articulation)

SONGS = {
    "title": dict(seed=101, tonic=60, mode="major", bpm=126, drums="march", bass="oompah", comp="pah", timpani=True, graces=True,
                  parts=dict(lead=("trumpet", 0.55, 0.0, 0.25, 0.9), lead2=("calliope", 0.4, -0.1, 0.25, 0.9),
                             comp=("horn", 0.16, 0.25, 0.3, 0.8), bass=("tuba", 0.55, 0.0, 0.1, 0.85),
                             arp=("glock", 0.22, 0.45, 0.35, 1.0), pad=("strings", 0.16, -0.35, 0.5, 1.0),
                             counter=("strings", 0.22, -0.4, 0.4, 1.0))),
    "map": dict(seed=207, tonic=55, mode="major", bpm=118, drums="polka", bass="oompah", comp="pah", density=1, graces=True,
                range=(4, 14), parts=dict(lead=("accordion", 0.4, 0.1, 0.2, 0.9), lead2=("flute", 0.45, -0.2, 0.3, 0.92),
                                          comp=("accordion", 0.12, -0.3, 0.2, 0.7), bass=("tuba", 0.5, 0.0, 0.1, 0.8),
                                          arp=("glock", 0.2, 0.5, 0.35, 1.0), counter=("clarinet", 0.25, -0.35, 0.3, 0.95),
                                          pad=("strings", 0.12, 0.3, 0.5, 1.0))),
    "world1": dict(seed=311, tonic=60, mode="major", bpm=132, drums="march", bass="oompah", comp="pah", density=1, graces=True,
                   parts=dict(lead=("calliope", 0.45, 0.0, 0.25, 0.88), lead2=("trumpet", 0.4, 0.15, 0.25, 0.9),
                              comp=("calliope", 0.13, -0.3, 0.2, 0.7), bass=("tuba", 0.55, 0.0, 0.1, 0.85),
                              arp=("glock", 0.2, 0.45, 0.35, 1.0), counter=("trombone", 0.25, -0.35, 0.25, 0.95),
                              pad=("strings", 0.12, 0.3, 0.5, 1.0))),
    "world2": dict(seed=419, tonic=65, mode="major", bpm=108, swing=0.58, drums="calypso", bass="calypso", comp="strum", density=1,
                   counter_style="answer",
                   parts=dict(lead=("steeldrum", 0.42, 0.1, 0.25, 1.0), lead2=("marimba", 0.45, -0.15, 0.25, 1.0),
                              comp=("uke", 0.13, -0.3, 0.2, 1.0), bass=("pluckbass", 0.55, 0.0, 0.08, 1.0),
                              arp=("marimba", 0.18, 0.45, 0.3, 1.0), counter=("flute", 0.22, -0.4, 0.35, 0.95),
                              pad=("strings", 0.1, 0.3, 0.5, 1.0))),
    "world3": dict(seed=523, tonic=62, mode="dorian", bpm=100, swing=0.66, drums="forest", bass="two", comp="pah", density=1,
                   range=(1, 11), double_up=False,
                   parts=dict(lead=("clarinet", 0.45, 0.05, 0.3, 0.85), lead2=("bassoon", 0.42, -0.1, 0.3, 0.85),
                              comp=("pizz", 0.2, -0.3, 0.3, 1.0), bass=("upright", 0.6, 0.0, 0.1, 1.0),
                              arp=("celesta", 0.18, 0.45, 0.45, 1.0), counter=("flute", 0.22, 0.35, 0.4, 0.95),
                              pad=("strings", 0.12, -0.3, 0.55, 1.0))),
    "world4": dict(seed=631, tonic=55, mode="major", bpm=152, swing=0.64, drums="swing", bass="walking", comp="stride", density=2,
                   range=(4, 15), counter_style="answer",
                   parts=dict(lead=("honky", 0.42, 0.05, 0.2, 1.0), lead2=("clarinet", 0.4, -0.15, 0.25, 0.9),
                              comp=("honky", 0.12, 0.2, 0.2, 1.0), bass=("upright", 0.6, 0.0, 0.08, 1.0),
                              arp=("vibes", 0.15, 0.45, 0.35, 1.0), counter=("trombone", 0.25, -0.35, 0.25, 0.95),
                              pad=("horn", 0.1, 0.3, 0.4, 1.0))),
    "world5": dict(seed=743, tonic=64, mode="harmonic", bpm=156, meter=3, drums="waltz", bass="waltz", comp="waltz", density=1,
                   room=0.84, wet=0.45, range=(4, 15),
                   parts=dict(lead=("celesta", 0.45, 0.0, 0.45, 1.0), lead2=("strings", 0.34, 0.15, 0.45, 1.0),
                              comp=("accordion", 0.1, 0.3, 0.3, 0.8), bass=("upright", 0.55, 0.0, 0.15, 1.0),
                              arp=("musicbox", 0.08, 0.2, 0.5, 1.0), counter=("choir", 0.1, 0.35, 0.6, 1.0),
                              pad=("choir", 0.07, 0.2, 0.6, 1.0))),
    "world6": dict(seed=853, tonic=62, mode="major", bpm=124, drums="drive", bass="drive", comp="strum", timpani=True, density=1,
                   room=0.83,
                   parts=dict(lead=("trumpet", 0.45, 0.0, 0.3, 0.92), lead2=("strings", 0.38, -0.2, 0.4, 1.0),
                              comp=("harp", 0.16, 0.35, 0.35, 1.0), bass=("basstrings", 0.42, 0.0, 0.15, 0.95),
                              arp=("glock", 0.16, 0.45, 0.4, 1.0), counter=("horn", 0.24, -0.35, 0.35, 1.0),
                              pad=("strings", 0.14, 0.3, 0.5, 1.0))),
    "world7": dict(seed=967, tonic=57, mode="phrygdom", bpm=112, drums="desert", bass="habanera", comp="strum", density=2,
                   graces=True, range=(0, 11),
                   parts=dict(lead=("oboe", 0.42, 0.05, 0.3, 0.9), lead2=("oud", 0.45, -0.15, 0.3, 1.0),
                              comp=("oud", 0.12, -0.35, 0.25, 1.0), bass=("pluckbass", 0.55, 0.0, 0.1, 1.0),
                              arp=("oud", 0.13, 0.45, 0.3, 1.0), counter=("flute", 0.2, 0.4, 0.4, 0.95),
                              pad=("strings", 0.1, 0.3, 0.5, 1.0))),
    "world8": dict(seed=1069, tonic=60, mode="minor", bpm=88, drums="sparse", bass="pedal", comp="pad", density=1, drips=True,
                   room=0.86, wet=0.55,
                   parts=dict(lead=("calliope_eerie", 0.38, 0.0, 0.55, 0.95), lead2=("vibes", 0.4, -0.2, 0.55, 1.0),
                              comp=("strings", 0.1, 0.3, 0.6, 1.0), bass=("synthbass", 0.4, 0.0, 0.15, 1.0),
                              arp=("musicbox", 0.07, 0.4, 0.6, 1.0), counter=("choir", 0.09, -0.35, 0.65, 1.0),
                              pad=("choir", 0.07, 0.3, 0.65, 1.0))),
    "world9": dict(seed=1171, tonic=52, mode="minor", bpm=144, drums="rock", bass="drive", comp="stabs", timpani=True, density=1,
                   range=(4, 15),
                   parts=dict(lead=("brass", 0.42, 0.0, 0.25, 0.92), lead2=("square", 0.3, 0.15, 0.25, 0.9),
                              comp=("brass", 0.12, -0.25, 0.25, 1.0), bass=("synthbass", 0.45, 0.0, 0.08, 1.0),
                              arp=("pizz", 0.2, 0.45, 0.25, 1.0), counter=("strings", 0.24, -0.4, 0.4, 1.0),
                              pad=("choir", 0.15, 0.35, 0.5, 1.0))),
    "world10": dict(seed=1283, tonic=58, mode="major", bpm=138, drums="drive", bass="oompah", comp="pah", timpani=True, density=1,
                    graces=True, room=0.83,
                    parts=dict(lead=("calliope", 0.4, -0.05, 0.3, 0.9), lead2=("trumpet", 0.42, 0.1, 0.3, 0.92),
                               comp=("brass", 0.12, 0.3, 0.3, 0.8), bass=("tuba", 0.55, 0.0, 0.1, 0.85),
                               arp=("glock", 0.18, 0.45, 0.4, 1.0), counter=("strings", 0.26, -0.4, 0.45, 1.0),
                               pad=("choir", 0.14, 0.3, 0.55, 1.0))),
    "boss": dict(seed=1409, tonic=50, mode="harmonic", bpm=168, drums="gallop", bass="gallop", comp="stabs", timpani=True, density=2,
                 range=(4, 15),
                 parts=dict(lead=("brass", 0.42, 0.0, 0.25, 0.9), lead2=("square", 0.3, 0.15, 0.25, 0.9),
                            comp=("brass", 0.12, -0.3, 0.25, 1.0), bass=("synthbass", 0.45, 0.0, 0.08, 1.0),
                            arp=("xylo", 0.18, 0.45, 0.25, 1.0), counter=("strings", 0.26, -0.4, 0.4, 1.0),
                            pad=("choir", 0.15, 0.35, 0.5, 1.0))),
    "bonus": dict(seed=1511, tonic=57, mode="major", bpm=176, drums="gallop", bass="gallop", comp="pah", density=2, graces=True,
                  range=(4, 15),
                  parts=dict(lead=("xylo", 0.48, 0.05, 0.2, 1.0), lead2=("kazoo", 0.3, -0.1, 0.2, 0.9),
                             comp=("calliope", 0.11, -0.3, 0.2, 0.7), bass=("tuba", 0.5, 0.0, 0.1, 0.8),
                             arp=("glock", 0.16, 0.45, 0.3, 1.0), counter=("trombone", 0.22, -0.35, 0.25, 0.95),
                             pad=("strings", 0.1, 0.3, 0.5, 1.0))),
}


def write_wav(path, L, R):
    data = array("h", bytes(4 * len(L)))
    data[0::2] = array("h", [int(max(-1.0, min(1.0, x * 0.89)) * 32767) for x in L])
    data[1::2] = array("h", [int(max(-1.0, min(1.0, x * 0.89)) * 32767) for x in R])
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


def build(name):
    song = Song(SONGS[name])
    song.compose()
    L, R, loop = song.render()
    os.makedirs(OUT, exist_ok=True)
    write_wav(os.path.join(OUT, f"music_{name}.wav"), L, R)
    return name, loop, len(L) / SR, song.total_bars, len(song.events)


def main():
    names = sys.argv[1:] or list(SONGS)
    for n in names:
        if n not in SONGS:
            sys.exit(f"no song called {n}; songs: {' '.join(SONGS)}")
    with Pool(min(len(names), os.cpu_count() or 4)) as pool:
        results = pool.map(build, names)
    path = os.path.join(OUT, "music_loops.json")
    loops = {}
    if os.path.exists(path):
        with open(path) as f:
            loops = json.load(f)
    for name, loop, secs, bars, events in results:
        loops[f"music_{name}"] = loop
        print(f"music_{name:<8} {secs:6.1f} s  {bars:3d} bars  {events:5d} notes  loops to {loop / SR:5.1f} s")
    with open(path, "w") as f:
        json.dump(loops, f, indent=1, sort_keys=True)


if __name__ == "__main__":
    main()
