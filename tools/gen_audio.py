"""Generate every sound effect and the music loop procedurally.

No samples and no downloads: each sound is synthesised from oscillators, noise
and filters, so it can be retuned in code and regenerated in seconds. The
palette is deliberately cartoon circus: honks, squeaks, bonks, boings, slide
whistles, a kazoo, a sad trombone, and a calliope march.

Sounds that play constantly get several variants (bonk_1, bonk_2...). The game
picks one at random and nudges its pitch, so a long fight does not turn into
the same sample on repeat.

Run: bash tools/refresh_sprites.sh   (generates, then lets Godot import)
"""
import math
import os
import random
import struct
import wave

SR = 44100
TAU = 2.0 * math.pi
OUT = os.path.join("assets", "audio")
rng = random.Random(1014)


# ----------------------------------------------------------- primitives

def N(sec):
    return max(1, int(sec * SR))


def silence(sec):
    return [0.0] * N(sec)


def osc(freq, sec, partials=((1, 1.0),), vib_rate=0.0, vib_depth=0.0):
    """Additive oscillator with a time-varying pitch. Partials above the
    Nyquist limit are skipped per sample, so sweeps never alias."""
    out = [0.0] * N(sec)
    ph = 0.0
    nyq = SR * 0.45
    for i in range(len(out)):
        t = i / SR
        f = freq(t)
        if vib_depth:
            f *= 1.0 + vib_depth * math.sin(TAU * vib_rate * t)
        ph += TAU * f / SR
        s = 0.0
        for k, a in partials:
            if k * f < nyq:
                s += a * math.sin(k * ph)
        out[i] = s
    return out


def saw(n):
    return tuple((k, 1.0 / k) for k in range(1, n + 1))


ORGAN = ((1, 1.0), (2, 0.55), (3, 0.3), (4, 0.18), (6, 0.08))


def noise(sec):
    return [rng.uniform(-1.0, 1.0) for _ in range(N(sec))]


class Biquad:
    """RBJ cookbook filter. Coefficients can be retargeted while running."""

    def __init__(self, kind, q):
        self.kind, self.q = kind, q
        self.x1 = self.x2 = self.y1 = self.y2 = 0.0

    def set(self, f):
        w = TAU * min(max(f, 20.0), SR * 0.45) / SR
        cw, sw = math.cos(w), math.sin(w)
        al = sw / (2.0 * self.q)
        if self.kind == "lp":
            b0, b1, b2 = (1 - cw) / 2, 1 - cw, (1 - cw) / 2
        elif self.kind == "hp":
            b0, b1, b2 = (1 + cw) / 2, -(1 + cw), (1 + cw) / 2
        else:
            b0, b1, b2 = al, 0.0, -al
        a0, a1, a2 = 1 + al, -2 * cw, 1 - al
        self.b0, self.b1, self.b2 = b0 / a0, b1 / a0, b2 / a0
        self.a1, self.a2 = a1 / a0, a2 / a0

    def run(self, x):
        y = self.b0 * x + self.b1 * self.x1 + self.b2 * self.x2 - self.a1 * self.y1 - self.a2 * self.y2
        self.x2, self.x1, self.y2, self.y1 = self.x1, x, self.y1, y
        return y


def filt(buf, kind, freq, q=0.707):
    f = freq if callable(freq) else (lambda t, v=freq: v)
    bq = Biquad(kind, q)
    out = [0.0] * len(buf)
    for i, x in enumerate(buf):
        if i % 32 == 0:
            bq.set(f(i / SR))
        out[i] = bq.run(x)
    return out


def shape(buf, env):
    return [x * env(i / SR) for i, x in enumerate(buf)]


def add(*bufs_and_gains):
    """add((buf, gain), ...) -> elementwise sum, padded to the longest."""
    n = max(len(b) for b, _ in bufs_and_gains)
    out = [0.0] * n
    for b, g in bufs_and_gains:
        for i, x in enumerate(b):
            out[i] += x * g
    return out


def place(dst, src, at, gain=1.0):
    o = N(at) if at > 0 else 0
    need = o + len(src)
    if need > len(dst):
        dst.extend([0.0] * (need - len(dst)))
    for i, x in enumerate(src):
        dst[o + i] += x * gain
    return dst


def finish(buf, peak=0.89):
    """De-click the edges, soft-limit, and normalise."""
    n = len(buf)
    fi, fo = min(n, N(0.002)), min(n, N(0.012))
    for i in range(fi):
        buf[i] *= i / fi
    for i in range(fo):
        buf[n - 1 - i] *= i / fo
    m = max(1e-9, max(abs(x) for x in buf))
    buf = [math.tanh(1.4 * x / m) for x in buf]
    m = max(1e-9, max(abs(x) for x in buf))
    return [x * peak / m for x in buf]


def write(name, buf, peak=0.89):
    os.makedirs(OUT, exist_ok=True)
    data = finish(list(buf), peak)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, x)) * 32767)) for x in data))
    REPORT.append((name, data))


REPORT = []

NOTES = {"C": 0, "C#": 1, "D": 2, "D#": 3, "E": 4, "F": 5, "F#": 6,
         "G": 7, "G#": 8, "A": 9, "A#": 10, "B": 11}


def hz(note):
    name, octave = note[:-1], int(note[-1])
    return 440.0 * 2 ** ((NOTES[name] + 12 * (octave + 1) - 69) / 12.0)


# ------------------------------------------------------------- the kit

def honk(f0, sec=0.30):
    """Bike horn: two slightly detuned reeds through a nasal formant."""
    def f(t):
        rise = min(1.0, t / 0.03)
        sag = max(0.0, (t - sec * 0.7) / (sec * 0.3))
        return f0 * (0.9 + 0.1 * rise) * (1.0 - 0.06 * sag)
    reed = add((osc(f, sec, saw(28)), 1.0), (osc(lambda t: f(t) * 1.018, sec, saw(28)), 1.0))
    body = add((filt(reed, "bp", 1150, 1.4), 0.9), (filt(reed, "bp", 2600, 3.0), 0.5),
               (filt(noise(sec), "bp", 1600, 1.0), 0.05))
    return shape(body, lambda t: min(1.0, t / 0.012) * (1.0 if t < sec - 0.06 else max(0.0, (sec - t) / 0.06)))


def squeak(lo, hi, sec):
    """Rubber toy: a fast upward bend with a wobble, then a small sag."""
    def f(t):
        u = t / sec
        p = 1 - (1 - u / 0.45) ** 2 if u < 0.45 else 1 - 0.25 * (u - 0.45) / 0.55
        return lo + (hi - lo) * p
    s = add((osc(f, sec, ((1, 1.0), (2, 0.28), (3, 0.08)), 28, 0.025), 1.0),
            (filt(noise(sec), "hp", 3000), 0.03))
    return shape(s, lambda t: min(1.0, t / 0.008) * (1.0 - (t / sec) ** 3))


def bonk(scale=1.0):
    """Mallet on a head: a wood knock, a body thump, and a cartoon bwoing."""
    sec = 0.42
    knock = shape(filt(noise(0.05), "bp", 1900 * scale, 4.0), lambda t: math.exp(-t / 0.008))
    thump = shape(osc(lambda t: (60 + 110 * math.exp(-t / 0.04)) * scale, 0.25, ((1, 1.0), (2, 0.2))),
                  lambda t: math.exp(-t / 0.07))
    bwoing = shape(osc(lambda t: 300 * scale * (1 + 0.18 * math.exp(-t / 0.12) * math.sin(TAU * 17 * t)),
                       sec, ((1, 1.0), (2, 0.15))),
                   lambda t: min(1.0, t / 0.004) * math.exp(-t / 0.11))
    out = silence(sec)
    place(out, knock, 0, 1.3)
    place(out, thump, 0, 1.0)
    place(out, bwoing, 0.005, 0.45)
    return out


def slap(scale=1.0):
    crack = shape(filt(noise(0.06), "hp", 1200 * scale), lambda t: math.exp(-t / 0.012))
    thud = shape(osc(lambda t: (70 + 60 * math.exp(-t / 0.03)) * scale, 0.12), lambda t: math.exp(-t / 0.05))
    return add((crack, 1.0), (thud, 0.9))


def whoosh(lo=400, hi=2400, sec=0.2):
    s = filt(noise(sec), "bp", lambda t: lo * (hi / lo) ** (t / sec), 1.3)
    return shape(s, lambda t: math.sin(math.pi * min(1.0, t / sec)) ** 1.5)


def boing(base=260, sec=0.55):
    """A spring: pitch overshoots and rings back, decaying."""
    def f(t):
        return base * (1 + 0.55 * math.exp(-t / 0.12) * math.sin(TAU * 11 * t)) * (1 + 0.25 * min(1.0, t / 0.05))
    return shape(osc(f, sec, ((1, 1.0), (2, 0.3), (3, 0.1))),
                 lambda t: min(1.0, t / 0.003) * math.exp(-t / 0.18))


def slide(f_from, f_to, sec):
    """Slide whistle: a breathy near-sine glide with a little vibrato."""
    def f(t):
        u = min(1.0, t / sec)
        return f_from + (f_to - f_from) * (1 - (1 - u) ** 2)
    s = add((osc(f, sec, ((1, 1.0), (2, 0.06)), 6.5, 0.012), 1.0),
            (filt(noise(sec), "bp", 2500, 1.2), 0.04))
    return shape(s, lambda t: min(1.0, t / 0.02) * (1.0 if t < sec - 0.06 else max(0.0, (sec - t) / 0.06)))


def pop():
    tone = shape(osc(lambda t: 120 + 900 * math.exp(-t / 0.012), 0.07), lambda t: math.exp(-t / 0.02))
    click = shape(filt(noise(0.01), "hp", 4000), lambda t: math.exp(-t / 0.002))
    return add((tone, 1.0), (click, 0.5))


def trombone(notes, wobble_last=True):
    """Plunger-muted trombone. Each note's filter opens and closes (the wah),
    and the last note gets a widening vibrato: the universal sound of failure."""
    out = []
    for idx, (f0, dur) in enumerate(notes):
        last = idx == len(notes) - 1 and wobble_last
        def f(t, f0=f0, last=last):
            depth = 0.04 * min(1.0, t / 0.6) if last else 0.0
            return f0 * (1 + depth * math.sin(TAU * 5.5 * t))
        raw = osc(f, dur, saw(36))
        wah = filt(raw, "lp", lambda t, d=dur: 380 + 1800 * math.sin(math.pi * min(1.0, t / d)) ** 0.7, 2.4)
        env = shape(wah, lambda t, d=dur: min(1.0, t / 0.025) * (1.0 if t < d - 0.05 else max(0.0, (d - t) / 0.05)))
        place(out, env, len(out) / SR - (0.015 if out else 0.0))
    return out


def organ(note, sec, gain=1.0):
    f0 = hz(note) if isinstance(note, str) else note
    a = osc(lambda t: f0, sec, ORGAN, 5.5, 0.006)
    b = osc(lambda t: f0 * 1.004, sec, ORGAN, 5.1, 0.006)
    s = add((a, 1.0), (b, 0.6))
    rel = min(0.04, sec * 0.3)
    return shape(s, lambda t: gain * min(1.0, t / 0.008) * (1.0 if t < sec - rel else max(0.0, (sec - t) / rel)))


def cymbal(sec=0.6):
    return shape(filt(noise(sec), "hp", 5000), lambda t: math.exp(-t / (sec * 0.35)))


def bell(f0, sec=1.0):
    parts = ((1.0, 1.0, 1.2), (2.76, 0.5, 0.8), (5.40, 0.25, 0.5), (8.93, 0.12, 0.3))
    out = silence(sec)
    for ratio, amp, dec in parts:
        place(out, shape(osc(lambda t, r=ratio: f0 * r, sec), lambda t, d=dec: math.exp(-t / (d * 0.4))), 0, amp)
    return out


def kazoo(f_from, f_to, sec):
    """Kazoo: a buzzing reed through two vocal formants, pitch rising."""
    def f(t):
        u = min(1.0, t / sec)
        return f_from + (f_to - f_from) * u ** 1.5
    reed = osc(f, sec, saw(25))
    buzz = filt(noise(sec), "lp", 200)
    reed = [x * (1.0 + 2.5 * b) for x, b in zip(reed, buzz)]
    s = add((filt(reed, "bp", 750, 2.0), 1.0), (filt(reed, "bp", 2300, 3.0), 0.6))
    return shape(s, lambda t: min(1.0, t / 0.04) * (0.7 + 0.3 * t / sec))


# ---------------------------------------------------------------- sfx

for i, s in enumerate((1.0, 1.12, 0.9), 1):
    write(f"bonk_{i}", bonk(s))
for i, s in enumerate((1.0, 0.85), 1):
    write(f"slap_{i}", slap(s))
for i, (lo, hi) in enumerate(((380, 2200), (460, 2600), (340, 2000)), 1):
    write(f"whoosh_{i}", whoosh(lo, hi))
for i, (lo, hi, d) in enumerate(((900, 1700, 0.16), (1100, 2000, 0.13), (750, 1400, 0.20)), 1):
    write(f"squeak_{i}", squeak(lo, hi, d))
write("honk_1", honk(430))
write("honk_2", honk(395))
write("honk_low", honk(205, 0.38))
write("boing", boing(260))
write("boing_small", boing(420, 0.3))
write("slide_up", slide(480, 1450, 0.40))
write("slide_down", slide(1400, 300, 0.55))
write("pop", pop())
write("thump", add((shape(osc(lambda t: 50 + 70 * math.exp(-t / 0.03), 0.18), lambda t: math.exp(-t / 0.06)), 1.0),
                   (shape(filt(noise(0.08), "lp", 600), lambda t: math.exp(-t / 0.03)), 0.4)))
write("clack", place(shape(filt(noise(0.03), "bp", 2600, 5.0), lambda t: math.exp(-t / 0.006)),
                     shape(filt(noise(0.03), "bp", 1700, 5.0), lambda t: math.exp(-t / 0.006)), 0.03, 0.8))
write("zip", add((shape(filt(noise(0.15), "bp", lambda t: 1500 * (6500 / 1500) ** (t / 0.15), 1.5),
                        lambda t: math.sin(math.pi * min(1.0, t / 0.15))), 1.0),
                 (shape(osc(lambda t: 500 + 1200 * t / 0.15, 0.15), lambda t: math.sin(math.pi * min(1.0, t / 0.15))), 0.4)))
write("whirr", shape(add((filt(noise(0.8), "lp", 1100), 1.0),
                         (osc(lambda t: 160, 0.8, ((1, 1.0), (2, 0.3))), 0.5)),
                     lambda t: (0.55 + 0.45 * math.sin(TAU * 15 * t)) * min(1.0, t / 0.05) * min(1.0, (0.8 - t) / 0.15)))
write("kazoo_rise", kazoo(196, 392, 0.95))
write("fwee", shape(osc(lambda t: 950 + 950 * min(1.0, t / 0.1), 0.3, ((1, 1.0), (2, 0.1)), 9, 0.015 * 0),
                    lambda t: min(1.0, t / 0.01) * (1.0 if t < 0.22 else max(0.0, (0.3 - t) / 0.08))))
write("ding", bell(1320))

slam = silence(1.0)
place(slam, shape(osc(lambda t: 26 + 44 * math.exp(-t / 0.1), 0.9), lambda t: math.exp(-t / 0.45)), 0, 1.0)
place(slam, shape(filt(noise(0.6), "lp", 350), lambda t: math.exp(-t / 0.35)), 0, 0.8)
place(slam, bonk(0.7), 0, 0.6)
place(slam, boing(140, 0.5), 0.08, 0.35)
place(slam, cymbal(0.6), 0, 0.15)
write("kaboom", slam)

# ------------------------------------------------------------- jingles

write("wah_short", trombone(((hz("A3"), 0.3), (hz("G3"), 0.9))))
write("sad_trombone", trombone(((hz("A#3"), 0.38), (hz("A3"), 0.38), (hz("G#3"), 0.38), (hz("G3"), 1.35))))

fan = silence(1.0)
for i, n in enumerate(("C5", "E5", "G5")):
    place(fan, organ(n, 0.12), i * 0.11)
place(fan, organ("C6", 0.55), 0.33)
place(fan, organ("E5", 0.55, 0.5), 0.33)
place(fan, shape(filt(noise(0.33), "hp", 2500), lambda t: 0.4 * (0.5 + 0.5 * math.sin(TAU * 30 * t))), 0, 0.3)
place(fan, cymbal(0.7), 0.33, 0.35)
write("fanfare", fan)

vic = silence(2.0)
tune = (("G4", 0.13), ("C5", 0.13), ("E5", 0.13), ("G5", 0.30), ("E5", 0.13), ("G5", 0.75))
at = 0.0
for n, d in tune:
    place(vic, organ(n, d), at)
    place(vic, organ(hz(n) * 2 ** (-8 / 12), d, 0.45), at)   # a sixth below
    at += d
place(vic, cymbal(0.9), 0.82, 0.4)
place(vic, kazoo(392, 523, 0.5), 0.82, 0.18)
write("victory", vic)

# ------------------------------------------------------------ progression

# Level up: a fast calliope run up the arpeggio, a bell on top, a kazoo cheer.
lvl = silence(1.4)
for i, n in enumerate(("C5", "E5", "G5", "C6", "E6")):
    place(lvl, organ(n, 0.09), i * 0.065)
place(lvl, organ("G6", 0.5, 0.7), 0.33)
place(lvl, organ("C6", 0.5, 0.45), 0.33)
place(lvl, bell(2093, 1.0), 0.33, 0.5)
place(lvl, kazoo(523, 784, 0.35), 0.36, 0.22)
place(lvl, cymbal(0.8), 0.33, 0.25)
write("level_up", lvl)

# Crit: a harder bonk with a bright ting riding on it.
crit = silence(0.5)
place(crit, bonk(1.25), 0, 0.8)
place(crit, bell(2637, 0.45), 0.01, 0.45)
write("crit", crit)

# Menus: a soft calliope tick to move, a coin-ish double ting to spend.
write("blip", organ("G5", 0.05, 0.6))
coin = silence(0.35)
place(coin, bell(1976, 0.3), 0, 0.7)
place(coin, bell(2637, 0.3), 0.06, 0.8)
write("coin", coin)


def crowd_cheer(sec=2.6, seed=5):
    """The stands going wild: a swelling roar, a scatter of rising "yay!"
    voices, and a patter of applause."""
    rnd = random.Random(seed)
    out = silence(sec)
    roar = shape(filt(filt(noise(sec), "bp", 850, 0.6), "lp", 2600),
                 lambda t: min(1.0, t / 0.3) * math.exp(-max(0.0, t - 0.6) / 0.9))
    place(out, roar, 0, 0.75)
    for _ in range(16):
        f0, d = rnd.uniform(210, 540), rnd.uniform(0.25, 0.6)
        v = osc(lambda t, f0=f0, d=d: f0 * (1.0 + 0.3 * min(1.0, t / d)), d, saw(12))
        v = filt(v, "bp", rnd.uniform(650, 1150), 2.2)
        place(out, shape(v, lambda t, d=d: math.sin(math.pi * min(1.0, t / d))), rnd.uniform(0.0, 0.7), 0.13)
    for _ in range(46):
        clap = shape(filt(noise(0.025), "bp", rnd.uniform(1100, 2600), 2.0), lambda t: math.exp(-t / 0.004))
        place(out, clap, rnd.uniform(0.15, sec - 0.2), rnd.uniform(0.2, 0.4))
    return out


write("crowd_cheer", crowd_cheer())


# ------------------------------------------------- the crowd, reacting
# A crowd is many voices on one vowel: each a sawtooth through two vowel
# formants, its own pitch and timing, all following the same contour.

VOWELS = {"oo": (350, 800), "ah": (750, 1200), "uh": (600, 1000), "ha": (800, 1300), "ee": (300, 2300)}


def crowd_vowel(sec, vowel, contour, n=14, lo=140, hi=330, seed=1, breath=0.25):
    rnd = random.Random(seed)
    f1, f2 = VOWELS[vowel]
    out = silence(sec + 0.3)
    for _ in range(n):
        f0 = rnd.uniform(lo, hi)
        d = sec * rnd.uniform(0.75, 1.0)
        start = rnd.uniform(0.0, sec - d + 0.05)
        v = osc(lambda t, f0=f0, d=d: f0 * contour(min(1.0, t / d)), d, saw(14), rnd.uniform(4.5, 6.5), 0.012)
        v = add((filt(v, "bp", f1 * rnd.uniform(0.9, 1.1), 2.2), 1.0), (filt(v, "bp", f2 * rnd.uniform(0.9, 1.1), 3.0), 0.5))
        place(out, shape(v, lambda t, d=d: math.sin(math.pi * min(1.0, t / d)) ** 0.7), start, 0.1)
    air = shape(filt(noise(sec), "bp", 1400, 0.7), lambda t: math.sin(math.pi * min(1.0, t / sec)))
    place(out, air, 0, breath)
    return out


# "Ooooooh": impressed, rising then settling. For a boss's entrance.
write("crowd_ooh", crowd_vowel(1.8, "oo", lambda u: 1.0 + 0.35 * math.sin(math.pi * u) - 0.1 * u, n=18, seed=11))
# A sharp gasp: everyone at once, up and cut off.
gasp = crowd_vowel(0.55, "ah", lambda u: 1.0 + 0.5 * u, n=16, lo=180, hi=420, seed=12, breath=0.6)
write("crowd_gasp", gasp)
# Booing: low, falling, a little ragged.
write("crowd_boo", crowd_vowel(1.6, "oo", lambda u: 1.0 - 0.25 * u + 0.04 * math.sin(30 * u), n=16, lo=95, hi=190, seed=13))


def crowd_laugh(sec=1.6, seed=14):
    """Laughter: overlapping 'ha-ha-ha' runs, each falling in pitch."""
    rnd = random.Random(seed)
    out = silence(sec + 0.2)
    for _ in range(12):
        at = rnd.uniform(0, 0.4)
        f0 = rnd.uniform(160, 380)
        for k in range(rnd.randint(3, 6)):
            d = 0.11
            v = osc(lambda t, f=f0 * (1 - 0.05 * k): f, d, saw(12))
            v = filt(v, "bp", 850, 2.0)
            place(out, shape(v, lambda t: math.sin(math.pi * min(1.0, t / d))), at + k * 0.17, 0.09)
    return out


write("crowd_laugh", crowd_laugh())


def drumroll(sec=1.4):
    """A snare roll swelling to a cymbal crash: the introduction."""
    out = silence(sec + 0.9)
    t = 0.0
    while t < sec:
        u = t / sec
        hit = shape(filt(noise(0.05), "bp", 2400, 1.2), lambda x: math.exp(-x / 0.02))
        body = shape(osc(lambda x: 190, 0.04), lambda x: math.exp(-x / 0.015))
        place(out, add((hit, 0.8), (body, 0.4)), t, 0.25 + 0.6 * u)
        t += 0.032
    place(out, cymbal(0.9), sec, 0.9)
    place(out, shape(osc(lambda x: 55 + 40 * math.exp(-x / 0.04), 0.4), lambda x: math.exp(-x / 0.15)), sec, 0.9)
    return out


write("drumroll", drumroll())

# ------------------------------------------------- boss signature sounds

# Bertie: two deep belly drums and a low honk.
belly = silence(1.0)
for i in range(2):
    place(belly, shape(osc(lambda t: 48 + 70 * math.exp(-t / 0.05), 0.4, ((1, 1.0), (2, 0.4))), lambda t: math.exp(-t / 0.14)), i * 0.22, 1.0)
place(belly, honk(150, 0.45), 0.5, 0.6)
write("belly_drum", belly)

# Bobo: a big rubber ball bouncing to rest.
ball = silence(1.4)
gap, at = 0.32, 0.0
for i in range(5):
    place(ball, boing(200 + i * 25, 0.28), at, 0.9 - i * 0.14)
    place(ball, shape(filt(noise(0.03), "lp", 500), lambda t: math.exp(-t / 0.01)), at, 0.6)
    at += gap
    gap *= 0.7
write("ball_bounce", ball)

# The whip: a fast swish and a supersonic crack.
whip = silence(0.5)
place(whip, whoosh(600, 3800, 0.14), 0, 0.6)
place(whip, shape(filt(noise(0.05), "hp", 2500), lambda t: math.exp(-t / 0.004)), 0.14, 1.4)
place(whip, shape(osc(lambda t: 2400, 0.03), lambda t: math.exp(-t / 0.006)), 0.14, 0.4)
write("whip_crack", whip)


def roar(sec=1.3, base=95):
    """A big cat's roar: a growling low tone with rough breath, swelling and fading."""
    growl = osc(lambda t: base * (1 + 0.25 * math.sin(math.pi * min(1.0, t / sec))), sec, saw(40), 23, 0.05)
    rough = filt(noise(sec), "lp", 1400)
    body = [g * (0.6 + 0.8 * abs(r)) for g, r in zip(growl, rough)]
    body = add((filt(body, "bp", 500, 0.9), 1.0), (filt(body, "bp", 1100, 1.5), 0.6), (filt(noise(sec), "bp", 900, 0.6), 0.3))
    return shape(body, lambda t: min(1.0, t / 0.12) * math.sin(math.pi * min(1.0, t / sec)) ** 0.5)


write("tiger_roar", roar())

# The Jalopy: an "ah-OOGA" horn and a puttering engine.
ooga = silence(0.9)
horn = osc(lambda t: 160 + 220 * min(1.0, t / 0.35) ** 0.5, 0.7, saw(30), 7, 0.02)
horn = add((filt(horn, "bp", 900, 1.5), 1.0), (filt(horn, "bp", 2200, 2.5), 0.4))
place(ooga, shape(horn, lambda t: min(1.0, t / 0.02) * (1.0 if t < 0.6 else max(0.0, (0.7 - t) / 0.1))), 0, 1.0)
write("car_horn", ooga)
eng = silence(1.2)
for i in range(16):
    pop_ = shape(add((filt(noise(0.05), "lp", 700), 1.0), (osc(lambda t: 60, 0.05), 0.8)), lambda t: math.exp(-t / 0.018))
    place(eng, pop_, i * 0.07 + (0.015 if i % 3 == 0 else 0), 0.7 + 0.3 * (i % 2))
write("engine", eng)

# Stretch: a rubber band drawn out and let go.
twang = silence(0.9)
place(twang, shape(osc(lambda t: 160 + 520 * min(1.0, t / 0.45), 0.45, ((1, 1.0), (2, 0.5), (3, 0.2))), lambda t: 0.5 * min(1.0, t / 0.05)), 0, 0.7)
place(twang, boing(330, 0.45), 0.45, 0.9)
write("twang", twang)

# The Fratelli: "ta-DA!" on the brass, arms up.
tada = silence(1.2)
place(tada, organ("G4", 0.14), 0, 0.9)
place(tada, organ("C5", 0.7), 0.16, 1.0)
place(tada, organ("E5", 0.7, 0.6), 0.16, 1.0)
place(tada, organ("G5", 0.7, 0.5), 0.16, 1.0)
place(tada, cymbal(0.8), 0.16, 0.4)
write("tada", tada)

# Pyro Pete: a roaring jet of flame.
flame = shape(add((filt(noise(1.2), "lp", lambda t: 400 + 1800 * min(1.0, t / 0.3)), 1.0), (filt(noise(1.2), "bp", 180, 0.8), 0.6)),
              lambda t: min(1.0, t / 0.15) * (1.0 if t < 0.8 else max(0.0, (1.2 - t) / 0.4)))
write("fire_roar", flame)


def glass_smash(seed=21):
    """A bottle shattering: a sharp crack and a shower of ringing shards."""
    rnd = random.Random(seed)
    out = silence(1.0)
    place(out, shape(filt(noise(0.06), "hp", 3000), lambda t: math.exp(-t / 0.01)), 0, 1.2)
    for _ in range(22):
        f0 = rnd.uniform(2600, 7500)
        tink = shape(osc(lambda t, f0=f0: f0, 0.2), lambda t: math.exp(-t / rnd.uniform(0.02, 0.06)))
        place(out, tink, rnd.uniform(0.0, 0.45) ** 1.5, rnd.uniform(0.1, 0.3))
    return out


write("glass_smash", glass_smash())

# The Totem Tots: five little giggles tumbling over each other.
gig = silence(1.2)
rg = random.Random(31)
for k in range(5):
    f0 = rg.uniform(520, 760)
    for j in range(4):
        v = osc(lambda t, f=f0 * (1 + 0.06 * j): f, 0.07, saw(10))
        v = filt(v, "bp", 1500, 2.0)
        place(gig, shape(v, lambda t: math.sin(math.pi * min(1.0, t / 0.07))), k * 0.13 + j * 0.09, 0.14)
write("giggle", gig)

# Iron Ivan: an effortful "HUP!" and a barbell's iron clang.
clang = silence(1.6)
heave = osc(lambda t: 110 * (1 + 0.2 * min(1.0, t / 0.2)), 0.3, saw(30))
heave = add((filt(heave, "bp", 600, 1.8), 1.0), (filt(heave, "bp", 1000, 2.5), 0.5))
place(clang, shape(heave, lambda t: math.sin(math.pi * min(1.0, t / 0.3))), 0, 0.8)
for ratio, amp in ((1.0, 1.0), (2.41, 0.5), (3.9, 0.3), (5.2, 0.2)):
    place(clang, shape(osc(lambda t, r=ratio: 260 * r, 1.2), lambda t: math.exp(-t / 0.4)), 0.32, amp * 0.5)
place(clang, shape(filt(noise(0.05), "hp", 1500), lambda t: math.exp(-t / 0.01)), 0.32, 0.8)
write("clang", clang)

# The rubber chicken: a strangled, wheezing squawk through a squeezed rubber throat.
def squawk(sec=0.42):
    def f(t):
        u = t / sec
        return 900 + 700 * math.sin(math.pi * min(1.0, u * 1.4)) + 120 * math.sin(t * 2 * math.pi * 23)
    reed = osc(f, sec, saw(18))
    wheeze = filt(noise(sec), "bp", 2200, 1.5)
    s = add((filt(reed, "bp", 1400, 2.2), 1.0), (filt(reed, "bp", 3100, 3.5), 0.6), (wheeze, 0.25))
    return shape(s, lambda t: min(1.0, t / 0.015) * max(0.0, 1.0 - (t / sec) ** 3))


write("squawk", squawk())


# A cream pie in the face: a wet, low slap with a squelchy tail.
def splat(sec=0.45):
    hit = shape(filt(noise(sec), "lp", 900), lambda t: math.exp(-t / 0.05))
    squelch = shape(filt(noise(sec), "bp", 420 + 0, 3.0), lambda t: min(1.0, t / 0.02) * math.exp(-t / 0.14))
    gloop = shape(osc(lambda t: 220 - 160 * min(1.0, t / 0.2), sec), lambda t: math.exp(-t / 0.09))
    return add((hit, 1.0), (squelch, 0.8), (gloop, 0.35))


write("splat", splat())

# The Ringmaster: a full calliope fanfare over a drum roll.
rm = silence(3.2)
place(rm, drumroll(1.2), 0, 0.7)
tune = (("C5", 0.16), ("C5", 0.16), ("C5", 0.16), ("G5", 0.5), ("E5", 0.16), ("G5", 0.16), ("C6", 0.9))
at = 1.2
for n, d in tune:
    place(rm, organ(n, d), at)
    place(rm, organ(hz(n) / 2, d, 0.5), at)
    at += d
place(rm, cymbal(1.0), 1.2 + 1.3, 0.5)
write("ringmaster_fanfare", rm)

# --------------------------------------------------------------- music

def march():
    """An original oom-pah circus march for calliope, tuba and drums. Rendered
    one loop long, with the tails of the last notes folded back onto the start
    so it loops without a seam."""
    bpm = 136
    beat = 60.0 / bpm
    bars = [
        [("E5", .5), ("D#5", .25), ("E5", .25), ("C5", .5), ("G4", .5)],
        [("A4", .5), ("B4", .5), ("C5", .5), ("E5", .5)],
        [("G5", .5), ("F#5", .25), ("G5", .25), ("E5", .5), ("C5", .5)],
        [("D5", 1.5), (None, .5)],
        [("F5", .5), ("E5", .25), ("F5", .25), ("D5", .5), ("B4", .5)],
        [("G4", .5), ("A4", .5), ("B4", .5), ("D5", .5)],
        [("F5", .5), ("E5", .5), ("D5", .5), ("B4", .5)],
        [("C5", 1.0), ("G4", .5), (None, .5)],
        [("C6", .5), ("B5", .25), ("C6", .25), ("G5", .5), ("E5", .5)],
        [("F5", .5), ("A5", .5), ("G5", .5), ("F5", .5)],
        [("E5", .5), ("D#5", .25), ("E5", .25), ("G5", .5), ("C5", .5)],
        [("D5", 1.5), (None, .5)],
        [("F5", .25), ("F#5", .25), ("G5", .5), ("A5", .5), ("B5", .5)],
        [("C6", .5), ("B5", .25), ("A5", .25), ("G5", .5), ("F5", .5)],
        [("E5", .5), ("D5", .5), ("B4", .5), ("D5", .5)],
        [("C5", 1.0), (None, 1.0)],
    ]
    chords = ["C", "Am", "C", "G", "G7", "G", "G7", "C", "C", "F", "C", "G", "G7", "C", "G7", "C"]
    triads = {"C": ("C", "E", "G"), "Am": ("A", "C", "E"), "G": ("G", "B", "D"),
              "G7": ("G", "B", "F"), "F": ("F", "A", "C")}
    roots = {"C": ("C3", "G2"), "Am": ("A2", "E2"), "G": ("G2", "D3"), "G7": ("G2", "D3"), "F": ("F2", "C3")}

    loop = len(bars) * 2 * beat
    out = [0.0] * (N(loop) + N(1.0))

    for b, notes in enumerate(bars):
        t0 = b * 2 * beat
        at = t0
        for note, beats in notes:
            d = beats * beat
            if note:
                place(out, organ(note, d * 0.86), at, 0.55)
            at += d
        ch = chords[b]
        for k, off in enumerate((0.0, 1.0)):                      # oom: tuba on the beats
            bass = hz(roots[ch][k])
            tuba = shape(filt(osc(lambda t, f=bass: f, 0.26, ((1, 1.0), (2, 0.6), (3, 0.4), (4, 0.25), (5, 0.15))), "lp", 700),
                         lambda t: min(1.0, t / 0.01) * math.exp(-t / 0.18))
            place(out, tuba, t0 + off * beat, 0.6)
            kick = shape(osc(lambda t: 50 + 60 * math.exp(-t / 0.02), 0.15), lambda t: math.exp(-t / 0.05))
            place(out, kick, t0 + off * beat, 0.35)
        for off in (0.5, 1.5):                                    # pah: chords and snare off the beat
            for name in triads[ch]:
                place(out, organ(name + "4", 0.11, 0.18), t0 + off * beat)
            snare = shape(filt(noise(0.12), "hp", 1800), lambda t: math.exp(-t / 0.035))
            place(out, snare, t0 + off * beat, 0.22)

    n = N(loop)
    for i in range(n, len(out)):                                  # fold the tail onto the head
        out[i - n] += out[i]
    return out[:n]


write("music_march", march(), peak=0.7)

# ------------------------------------------------------------- report

print("{:<14} {:>6} {:>8} {:>8}".format("sound", "secs", "peak dB", "rms dB"))
for name, d in REPORT:
    peak = max(abs(x) for x in d)
    rms = math.sqrt(sum(x * x for x in d) / len(d))
    print("{:<14} {:>6.2f} {:>8.1f} {:>8.1f}".format(
        name, len(d) / SR, 20 * math.log10(max(peak, 1e-9)), 20 * math.log10(max(rms, 1e-9))))
