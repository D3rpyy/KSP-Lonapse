#!/usr/bin/env python3
"""Lonapse audio pipeline.

Synthesizes the beeps and static (no recorded material), checks the whole Sounds/ tree
(the Lonapse voices from build_lonapse.py included) and installs it into the game.

Usage:
    python3 -I audio/build_clips.py [--clean] [--install] [--seed N] [--verify-only]

Only the Python standard library and the ffmpeg/ffprobe binaries are used.
The game folder comes from $KSP_DIR or from the first line of .ksp_dir in the repo root.
"""

import argparse
import array
import hashlib
import json
import math
import os
import random
import re
import shutil
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent
WORK = ROOT / "work"
BUILD = ROOT / "build"
SOUNDS = BUILD / "Sounds"
MANIFEST = BUILD / "manifest.json"
KSP_DIR_FILE = ROOT.parent / ".ksp_dir"   # local, not in git: one line with the path of the KSP install

SR = 44100
FRAME = 441            # 10 ms analysis frame at 44.1 kHz

# ---------------------------------------------------------------------------
# Folder table: min count, built count, length window, loudness target, kind
# ---------------------------------------------------------------------------
FOLDERS = {
    "beeps/ping":       dict(min=3,  count=4,  lo=0.1,  hi=0.6,  rms=-22.0, kind="beep"),
    "beeps/descent":    dict(min=2,  count=3,  lo=0.05, hi=0.12, rms=-22.0, kind="beep"),
    "beeps/stage":      dict(min=2,  count=3,  lo=0.2,  hi=0.6,  rms=-22.0, kind="beep"),
    "beeps/landing":    dict(min=1,  count=2,  lo=0.4,  hi=1.2,  rms=-22.0, kind="beep"),
    "beeps/splashdown": dict(min=1,  count=2,  lo=0.4,  hi=1.2,  rms=-22.0, kind="beep"),
    "beeps/quindar_in": dict(min=1,  count=1,  lo=0.25, hi=0.25, rms=-22.0, kind="beep"),
    "beeps/quindar_out": dict(min=1, count=1,  lo=0.25, hi=0.25, rms=-22.0, kind="beep"),
    "static/burst":     dict(min=6,  count=8,  lo=0.2,  hi=1.5,  rms=-24.0, kind="static"),
    "static/loop":      dict(min=2,  count=3,  lo=4.0,  hi=8.0,  rms=-24.0, kind="loop"),
    "static/squelch":   dict(min=2,  count=3,  lo=0.12, hi=0.30, rms=-24.0, kind="static"),
    # v2 radio profiles (plan 4.6). Added at the end so the existing folders keep their RNG streams.
    # click_desk: station profile open click, 5 dB hotter than the other beeps because a 15 to 26 ms
    # click measures quiet at -22. headset_*: ship/EVA push-to-talk static (radio-band noise, no tone).
    # breath_loop: EVA helmet breathing bed, -30 dBFS on purpose so it stays under the voices.
    "beeps/click_desk":   dict(min=2, count=3, lo=0.015, hi=0.030, rms=-17.0, kind="beep"),
    "beeps/headset_open": dict(min=2, count=2, lo=0.150, hi=0.300, rms=-22.0, kind="beep"),
    "beeps/headset_close": dict(min=2, count=2, lo=0.150, hi=0.300, rms=-22.0, kind="beep"),
    "static/breath_loop": dict(min=1, count=2, lo=6.0, hi=8.0, rms=-30.0, kind="loop"),
    # v2 ideas round: roger beep at the end of a crew transmission. (The touchdown ambience was removed after a user test.)
    "beeps/roger":        dict(min=1, count=2, lo=0.08, hi=0.20, rms=-22.0, kind="beep"),
    # master_alarm: soft two-tone caution loop for panic moments. The plugin plays it far in the background.
    "beeps/master_alarm": dict(min=1, count=1, lo=2.0, hi=2.0, rms=-26.0, kind="loop"),
}

# Plan v5 probe mode: beeps per probe core and mood, timbre from audio/probe/cores.tsv. Same keys as ProbeCores.Keys and ProbeCores.Moods in src/ProbeCore.cs, folders as ProbeCores.Folders().
PROBE_CORES_TSV = ROOT / "probe" / "cores.tsv"
PROBE_CORE_KEYS = ["stayputnik", "okto", "hecs", "rovemate", "snob", "qbe", "mk2drone", "mpo", "mtm", "generic"]
PROBE_MOODS = ["happy", "offended", "curious", "scared", "sleepy", "proud", "bored"]
PROBE_WAVES = ["sine", "triangle", "square", "saw", "pulse", "fm"]
PROBE_RHYTHMS = ["even", "swing", "stutter", "lazy"]
for _core in PROBE_CORE_KEYS:
    for _mood in PROBE_MOODS:
        FOLDERS["beeps/probe_%s_%s" % (_core, _mood)] = dict(min=3, count=3, lo=0.08, hi=1.8, rms=-22.0, kind="beep")
# Plan v6: the data stream beep while an antenna sends science (ProbeCores.DataFolder). Fast even chirps.
FOLDERS["beeps/probe_data"] = dict(min=3, count=4, lo=0.3, hi=1.4, rms=-22.0, kind="beep")


PEAK_LIMIT_DB = -1.3       # limiter ceiling, contract requires <= -1 dBFS
RMS_TOL = 1.0              # +- dB
MAX_SILENCE = 0.050        # leading / trailing silence
SIZE_LIMIT = 200 * 1024 * 1024  # 40 MB, 80 MB in v4 (about 2500 clips), 110 MB in 0.6.0 (about 4600 clips plus the effects), 120 MB for the qbe, mk2drone, mpo and mtm beeps, 200 MB for more templates and subtitle versions
SILENCE_DB = -50.0
NAME_RE = re.compile(r"^[a-z0-9_]+_[0-9]{3}\.wav$")


def log(msg=""):
    print(msg, flush=True)


def die(msg):
    print("ERROR: " + msg, file=sys.stderr, flush=True)
    sys.exit(1)


def db(x):
    return 20.0 * math.log10(x) if x > 1e-12 else -240.0


# ---------------------------------------------------------------------------
# ffmpeg helpers
# ---------------------------------------------------------------------------
def run(cmd, data=None):
    p = subprocess.run(cmd, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode != 0:
        die("command failed: %s\n%s" % (" ".join(cmd), p.stderr.decode("utf-8", "replace")[-2000:]))
    return p


def ffmpeg_raw(args_in, af=None, data=None):
    """Run ffmpeg, return mono 44.1 kHz float samples from stdout."""
    cmd = ["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error"] + args_in
    if af:
        cmd += ["-af", af]
    cmd += ["-ac", "1", "-ar", str(SR), "-f", "s16le", "-acodec", "pcm_s16le", "pipe:1"]
    out = run(cmd, data).stdout
    a = array.array("h")
    a.frombytes(out[: len(out) // 2 * 2])
    if sys.byteorder != "little":
        a.byteswap()
    return [v / 32768.0 for v in a]


def to_s16_bytes(x):
    a = array.array("h", (max(-32768, min(32767, int(round(v * 32767.0)))) for v in x))
    if sys.byteorder != "little":
        a.byteswap()
    return a.tobytes()


def write_wav(path, x):
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(to_s16_bytes(x))


def read_wav(path):
    with wave.open(str(path), "rb") as w:
        n = w.getnframes()
        a = array.array("h")
        a.frombytes(w.readframes(n))
    if sys.byteorder != "little":
        a.byteswap()
    return [v / 32768.0 for v in a]


def process(x, af):
    """Pipe float samples through an ffmpeg filter chain."""
    return ffmpeg_raw(["-f", "s16le", "-ar", str(SR), "-ac", "1", "-i", "pipe:0"], af, to_s16_bytes(x))


def source_gen(src, dur):
    """Generate audio from an ffmpeg lavfi source expression."""
    return ffmpeg_raw(["-f", "lavfi", "-i", src, "-t", "%.6f" % dur])


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


F0_SR = 6300            # 44100 / 7: 10 ms = 63 samples
F0_N = 252              # 40 ms analysis window
F0_LAGS = range(15, 106)  # 60 .. 420 Hz
F0_AF = "lowpass=f=1000,lowpass=f=1000,aresample=%d:resampler=soxr" % F0_SR


def rms(x):
    if not x:
        return 0.0
    return math.sqrt(sum(v * v for v in x) / len(x))


def fade_edges(x, n_in, n_out):
    n_in = min(n_in, len(x))
    n_out = min(n_out, len(x))
    for k in range(n_in):
        x[k] *= 0.5 - 0.5 * math.cos(math.pi * k / n_in)
    L = len(x)
    for k in range(n_out):
        x[L - 1 - k] *= 0.5 - 0.5 * math.cos(math.pi * k / n_out)
    return x


def gated_rms_db(x, frame=FRAME):
    """RMS over non-silent 10 ms frames: frames above max(-50 dBFS, loudest - 30 dB)."""
    lv = []
    for i in range(0, len(x) - frame + 1, frame):
        lv.append(sum(v * v for v in x[i:i + frame]) / frame)
    if not lv:
        return db(rms(x))
    mx = max(lv)
    gate = max(10 ** (SILENCE_DB / 10), mx * 10 ** (-3.0))
    sel = [v for v in lv if v >= gate]
    return 10 * math.log10(sum(sel) / len(sel)) if sel else -240.0


def limit(x, ceiling_db=PEAK_LIMIT_DB):
    """Block-based lookahead peak limiter (64-sample blocks, fast release)."""
    t = 10 ** (ceiling_db / 20.0)
    B = 64
    nb = (len(x) + B - 1) // B
    req = []
    for b in range(nb):
        pk = max((abs(v) for v in x[b * B:(b + 1) * B]), default=0.0)
        req.append(1.0 if pk <= t else t / pk)
    env = [min(req[max(0, b - 1)], req[b], req[min(nb - 1, b + 1)]) for b in range(nb)]
    rel = 0.02
    for b in range(1, nb):
        env[b] = min(env[b], env[b - 1] + rel)
    for b in range(nb - 2, -1, -1):
        env[b] = min(env[b], env[b + 1] + rel)
    y = list(x)
    for b in range(nb):
        g0 = env[b]
        g1 = env[b + 1] if b + 1 < nb else env[b]
        g1 = min(g1, req[b])
        base = b * B
        for k in range(B):
            i = base + k
            if i >= len(y):
                break
            y[i] *= g0 + (g1 - g0) * k / B
    # safety clamp
    return [max(-t, min(t, v)) for v in y]


def normalize(x, target_db, fades=True):
    g_db = target_db - gated_rms_db(x)
    y = x
    for _ in range(8):
        g = 10 ** (g_db / 20.0)
        y = limit([v * g for v in x])
        err = target_db - gated_rms_db(y)
        if abs(err) < 0.25:
            break
        g_db += err
    if fades:
        nf = int(SR * 0.007)
        y = fade_edges(y, nf, nf)
    return y


# ---------------------------------------------------------------------------
# Beeps and static (ffmpeg sources: sine, aevalsrc, anoisesrc)
# ---------------------------------------------------------------------------
def tone(freq, dur, attack=0.005, decay=None):
    """Sine from ffmpeg's sine source with a raised-cosine attack and exp/cos release."""
    x = source_gen("sine=frequency=%.3f:sample_rate=%d" % (freq, SR), dur)
    n = len(x)
    na = max(1, int(attack * SR))
    for k in range(n):
        env = 1.0
        if k < na:
            env = 0.5 - 0.5 * math.cos(math.pi * k / na)
        t = k / SR
        if decay:
            env *= math.exp(-t / decay)
        x[k] *= env
    nr = min(n, int(0.006 * SR))
    for k in range(nr):
        x[n - 1 - k] *= 0.5 - 0.5 * math.cos(math.pi * k / nr)
    return x


def silence(dur):
    return [0.0] * int(round(dur * SR))


def out_root(folder):
    """Where a folder is written: everything this script builds goes to Sounds/."""
    return SOUNDS


def noise(seed, dur, color="white", af=None):
    x = ffmpeg_raw(["-f", "lavfi", "-i", "anoisesrc=sample_rate=%d:color=%s:seed=%d:amplitude=0.5" %
                    (SR, color, seed), "-t", "%.6f" % dur], af)
    return x


def make_beeps(folder, i, seed):
    rng = random.Random("%d:%s:%d" % (seed, folder, i))
    name = folder.split("/")[1]
    if name == "ping":
        nblip = [1, 2, 3, 2][i % 4]
        f = rng.uniform(850, 1350)
        x = []
        for b in range(nblip):
            ff = f * (1.0 if b == 0 else rng.choice([1.0, 1.0, 1.122, 0.891]))
            blen = rng.uniform(0.12, 0.16) if nblip == 1 else rng.uniform(0.045, 0.07)
            x += tone(ff, blen, attack=0.006, decay=0.08 if nblip == 1 else 0.05)
            if b < nblip - 1:
                x += silence(rng.uniform(0.06, 0.1))
        return x
    if name == "descent":
        return tone([1000.0, 960.0, 1045.0][i % 3], [0.07, 0.06, 0.08][i % 3], attack=0.005, decay=0.06)
    if name == "stage":
        hi, lo = [(1500.0, 1000.0), (1600.0, 1066.0), (1450.0, 970.0)][i % 3]
        return tone(hi, 0.12, attack=0.005) + silence(0.025) + tone(lo, 0.16, attack=0.005)
    if name == "landing":
        base = [880.0, 784.0][i % 2]
        notes = [base, base * 1.26, base * 1.498]
        x = []
        for k, nf in enumerate(notes):
            x += tone(nf, 0.16 if k < 2 else 0.26, attack=0.006, decay=0.35)
            if k < 2:
                x += silence(0.035)
        return x
    if name == "click_desk":
        # hard desk-mic click: band-passed noise with a fast exponential decay (v2, plan 4.6)
        dur = rng.uniform(0.015, 0.026)
        center = [1800.0, 2400.0, 3000.0][i % 3] * rng.uniform(0.92, 1.08)
        tau = rng.uniform(0.0025, 0.0045)
        nseed = 3000 + i + 37 * len(folder) + seed * 101
        x = noise(nseed, dur, "white", "bandpass=f=%.0f:width_type=q:w=1.0,highpass=f=300" % center)
        for k in range(len(x)):
            x[k] *= math.exp(-(k / SR) / tau)
        return fade_edges(x, int(0.005 * SR), int(0.006 * SR))
    if name in ("headset_open", "headset_close"):
        # push-to-talk static (v2 rev 3): radio-band noise only, no tone and no clicks.
        return radio_squelch(rng, 4000 + i + 37 * len(folder) + seed * 101, name == "headset_open")
    if name == "splashdown":
        # descending warbled sweep from ffmpeg aevalsrc (instantaneous phase integral)
        f0, f1, wr = [(1250.0, 700.0, 11.0), (1150.0, 640.0, 14.0)][i % 2]
        dur = 0.85
        k = (f1 - f0) / dur
        expr = "sin(2*PI*(%f*t+0.5*%f*t*t) + 6*sin(2*PI*%f*t))" % (f0, k, wr)
        x = source_gen("aevalsrc=%s:s=%d" % (expr.replace(",", "\\,"), SR), dur)
        return fade_edges(x, int(0.006 * SR), int(0.12 * SR))
    if name == "roger":
        # short NASA-style roger beep after the crew lets go of the key: one tone, or a quick rising pair
        if i % 2 == 0:
            return tone(1250.0, 0.11, attack=0.004, decay=0.05)
        return tone(1050.0, 0.05, attack=0.004, decay=0.03) + silence(0.012) + tone(1400.0, 0.065, attack=0.004, decay=0.04)
    if name == "probe_data":
        return make_probe_data(i, seed)
    if name.startswith("probe_"):
        return make_probe_beep(folder, i, seed)
    if name in ("quindar_in", "quindar_out"):
        f = 2525.0 if name == "quindar_in" else 2475.0
        x = source_gen("sine=frequency=%.1f:sample_rate=%d" % (f, SR), 0.25)
        x = x[: int(0.25 * SR)]
        return fade_edges(x, int(0.005 * SR), int(0.005 * SR))
    raise ValueError(folder)


# ---------------------------------------------------------------------------
# Plan v5: probe beeps (10 cores x 7 moods). Pure Python synthesis.
# ---------------------------------------------------------------------------
_PROBE_CORES = None


def load_probe_cores(path=PROBE_CORES_TSV):
    """audio/probe/cores.tsv -> {key: dict(base_hz, wave, rhythm, glide)}. Dies on a bad or missing core."""
    global _PROBE_CORES
    if _PROBE_CORES is not None:
        return _PROBE_CORES
    if not path.exists():
        die("missing %s" % path)
    cores, header, errors = {}, None, []
    for lineno, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        f = [c.strip() for c in raw.split("\t")]
        if header is None:
            header = f
            want = ["key", "base_hz", "wave", "rhythm", "glide"]
            if sorted(header) != sorted(want):
                die("%s:%d: header must be %s" % (path.name, lineno, " ".join(want)))
            continue
        if len(f) != len(header):
            errors.append("%s:%d: expected %d columns" % (path.name, lineno, len(header)))
            continue
        r = dict(zip(header, f))
        try:
            hz, gl = float(r["base_hz"]), float(r["glide"])
        except ValueError:
            errors.append("%s:%d: base_hz and glide must be numbers" % (path.name, lineno))
            continue
        if r["key"] not in PROBE_CORE_KEYS or r["key"] in cores:
            errors.append("%s:%d: key '%s' unknown or twice" % (path.name, lineno, r["key"]))
        if not 200.0 <= hz <= 3000.0:
            errors.append("%s:%d: base_hz %.0f outside 200..3000" % (path.name, lineno, hz))
        if r["wave"] not in PROBE_WAVES:
            errors.append("%s:%d: wave '%s' not in %s" % (path.name, lineno, r["wave"], " ".join(PROBE_WAVES)))
        if r["rhythm"] not in PROBE_RHYTHMS:
            errors.append("%s:%d: rhythm '%s' not in %s" % (path.name, lineno, r["rhythm"], " ".join(PROBE_RHYTHMS)))
        if not 0.0 <= gl <= 1.0:
            errors.append("%s:%d: glide %.2f outside 0..1" % (path.name, lineno, gl))
        cores[r["key"]] = dict(base_hz=hz, wave=r["wave"], rhythm=r["rhythm"], glide=gl)
    missing = [k for k in PROBE_CORE_KEYS if k not in cores]
    if missing:
        errors.append("%s: missing cores %s" % (path.name, " ".join(missing)))
    if errors:
        die("bad %s:\n  %s" % (path, "\n  ".join(errors)))
    _PROBE_CORES = cores
    return cores


def _wave_partials(wave, f):
    """(harmonic number, amplitude, cosine) partials of a band-limited wave at f Hz (harmonics under 9 kHz)."""
    nmax = max(1, int(9000.0 / max(f, 1.0)))
    if wave == "sine" or wave == "fm":
        return [(1, 1.0, False)]
    if wave == "triangle":
        return [(n, (8 / math.pi ** 2) * (1 if (n // 2) % 2 == 0 else -1) / (n * n), False)
                for n in range(1, min(nmax, 9) + 1, 2)]
    if wave == "square":
        return [(n, (4 / math.pi) / n, False) for n in range(1, min(nmax, 9) + 1, 2)]
    if wave == "saw":
        return [(n, (2 / math.pi) / n, False) for n in range(1, min(nmax, 10) + 1)]
    if wave == "pulse":   # 25 % duty pulse
        return [(n, (2 / (n * math.pi)) * math.sin(n * math.pi * 0.25), True) for n in range(1, min(nmax, 10) + 1)]
    raise ValueError(wave)


def synth_note(core, st0, st1, dur, amp=1.0, from_st=None, vibrato=0.0, buzz=0.0):
    """One note of a probe beep. Pitch glides linearly (in semitones) from st0 to st1 over the note. With from_st and
    a core glide the note slides in from the pitch of the note before. vibrato in semitones (6 Hz), buzz = depth of
    a 38 Hz amplitude buzz plus soft drive (offended)."""
    n = max(1, int(dur * SR))
    base = core["base_hz"]
    wave = core["wave"]
    partials = _wave_partials(wave, base * 2 ** (max(st0, st1) / 12.0))
    port = (from_st - st0) * core["glide"] if from_st is not None else 0.0
    tau = max(0.01, 0.3 * dur)
    na, nr = max(1, int(0.005 * SR)), max(1, min(n // 2, int(0.018 * SR)))
    out = [0.0] * n
    ph = 0.0
    for k in range(n):
        t = k / SR
        st = st0 + (st1 - st0) * (k / n) + port * math.exp(-t / tau)
        if vibrato:
            st += vibrato * math.sin(2 * math.pi * 6.0 * t)
        ph += 2 * math.pi * base * 2 ** (st / 12.0) / SR
        if wave == "fm":
            idx = 1.8 * math.exp(-t / 0.12) + 0.4
            v = math.sin(ph + idx * math.sin(2 * ph))
        else:
            v = 0.0
            for h, a, cosine in partials:
                v += a * (math.cos(h * ph) if cosine else math.sin(h * ph))
        if buzz:
            v = math.tanh(2.5 * v) / math.tanh(2.5) * (1.0 - buzz * (0.5 + 0.5 * math.sin(2 * math.pi * 38.0 * t)))
        env = 1.0
        if k < na:
            env = 0.5 - 0.5 * math.cos(math.pi * k / na)
        if k >= n - nr:
            env *= 0.5 - 0.5 * math.cos(math.pi * (n - 1 - k) / nr)
        out[k] = v * env * amp
    return out


# Mood shapes: notes (start st, end st, duration s, gap after s, amplitude). Offsets are from the core base pitch.
# variant i (0, 1, 2) changes transposition and note count, rng adds small timing jitter.
def probe_mood_notes(mood, i, rng):
    tr = [0.0, 2.0, -2.0][i % 3]
    if mood == "happy":       # rising chirps
        steps = [[0, 4, 7], [0, 4, 7, 12], [0, 5, 9]][i % 3]
        return [(tr + s, tr + s + 3, 0.065, 0.04, 1.0) for s in steps], {}
    if mood == "offended":    # low descending buzz
        lo = -12.0 + tr
        notes = [(lo, lo - 2, 0.16, 0.035, 1.0), (lo - 2, lo - 6, 0.30, 0.0, 1.0)]
        if i == 1:
            notes = [(lo + 2, lo, 0.10, 0.03, 0.9)] + notes
        return notes, dict(buzz=0.55)
    if mood == "curious":     # rising question
        notes = [(tr, tr, 0.08, 0.05, 0.9), (tr, tr + 7, 0.26, 0.0, 1.0)]
        if i == 2:
            notes = [(tr - 3, tr - 3, 0.06, 0.04, 0.8)] + notes
        return notes, {}
    if mood == "scared":      # fast trill
        count = [9, 11, 8][i % 3]
        up = [3.0, 2.0, 4.0][i % 3]
        return [(5 + tr + (up if k % 2 else 0), 5 + tr + (up if k % 2 else 0), 0.034, 0.010, 1.0)
                for k in range(count)], dict(no_rhythm=True)
    if mood == "sleepy":      # slow descending, fading
        b = -5.0 + tr
        notes = [(b, b - 1, 0.20, 0.07, 1.0), (b - 3, b - 4, 0.22, 0.08, 0.75), (b - 7, b - 10, 0.36, 0.0, 0.55)]
        if i == 1:
            notes = notes[1:]
        return notes, dict(vibrato=0.15)
    if mood == "proud":       # fanfare-ish triple: short, short, long
        top = [12, 12, 7][i % 3]
        mid = [5, 4, 4][i % 3]
        return [(tr, tr, 0.075, 0.035, 0.9), (tr + mid, tr + mid, 0.075, 0.035, 0.95),
                (tr + top, tr + top, 0.30, 0.0, 1.0)], dict(vibrato_last=0.25)
    if mood == "bored":       # flat
        b = -3.0 + tr
        n = [3, 2, 4][i % 3]
        return [(b, b, 0.15, 0.09, 0.8) for _ in range(n - 1)] + [(b, b - 1, 0.26, 0.0, 0.7)], {}
    raise ValueError(mood)


def apply_rhythm(notes, rhythm):
    """even: as is. swing: long short. stutter: every note played twice quickly. lazy: longer notes and gaps."""
    if rhythm == "swing":
        return [(a, b, d * (1.25 if k % 2 == 0 else 0.75), g, amp) for k, (a, b, d, g, amp) in enumerate(notes)]
    if rhythm == "stutter":
        out = []
        for a, b, d, g, amp in notes:
            mid = a + (b - a) * 0.5
            out.append((a, mid, d * 0.45, 0.014, amp))
            out.append((mid, b, d * 0.5, g, amp * 0.9))
        return out
    if rhythm == "lazy":
        return [(a, b, d * 1.2, g * 1.35, amp) for a, b, d, g, amp in notes]
    return notes


def make_probe_data(i, seed):
    """beeps/probe_data: a data stream, 10 to 16 short even chirps on 4 pitches, no glide (plan v6)."""
    rng = random.Random("%d:probe_data:%d" % (seed, i))
    core = dict(load_probe_cores()["generic"])
    core["glide"] = 0.0
    core["wave"] = "square" if i % 2 else "pulse"
    steps = [0, 3, 7, 12]
    x = []
    for k in range(rng.randint(10, 16)):
        st = rng.choice(steps)
        x += synth_note(core, st, st, rng.uniform(0.035, 0.05), 0.8)
        x += silence(0.018)
    return fade_edges(x, int(0.003 * SR), int(0.004 * SR))


def make_probe_beep(folder, i, seed):
    """beeps/probe_<core>_<mood>: one beep phrase in the timbre of the core (cores.tsv) and the shape of the mood."""
    name = folder.split("/")[1][len("probe_"):]
    core_key, mood = name.rsplit("_", 1)
    core = load_probe_cores()[core_key]
    rng = random.Random("%d:%s:%d" % (seed, folder, i))
    notes, opt = probe_mood_notes(mood, i, rng)
    if not opt.get("no_rhythm"):
        notes = apply_rhythm(notes, core["rhythm"])
    x = []
    prev = None
    for k, (a, b, d, g, amp) in enumerate(notes):
        d *= rng.uniform(0.92, 1.08)
        vib = opt.get("vibrato", 0.0)
        if k == len(notes) - 1:
            vib = max(vib, opt.get("vibrato_last", 0.0))
        x += synth_note(core, a, b, d, amp, from_st=prev, vibrato=vib, buzz=opt.get("buzz", 0.0))
        prev = b
        if k < len(notes) - 1 and g > 0:
            x += silence(g * rng.uniform(0.9, 1.1))
    return fade_edges(x, int(0.003 * SR), int(0.004 * SR))


def radio_squelch(rng, nseed, opening):
    """Push-to-talk static, the "kssh" of a handheld radio keying up or dropping out.

    Only noise, shaped like a radio channel: band-limited to about 350 Hz to 3.4 kHz (a little
    brighter on the close tail), a fast random flutter so it reads as a noisy carrier rather than
    a steady wash, and soft saturation for grit.
    open (key-up): comes up in about 4 ms, holds, and falls away over the last 60 ms.
    close (key-down): the squelch tail, starts at full level and decays over the whole clip.
    """
    dur = rng.uniform(0.20, 0.26) if opening else rng.uniform(0.16, 0.22)
    n = int(SR * dur)
    lp = 3400 if opening else 4200
    # two passes of each filter give a steeper, more "channel-like" band edge
    x = noise(nseed, dur + 0.03, "white",
              "highpass=f=350,highpass=f=350,lowpass=f=%d,lowpass=f=%d" % (lp, lp))[:n]
    # carrier flutter: random levels every 5 to 14 ms, linearly interpolated, depth about 35 %
    flutter = [1.0] * n
    pts = [(0, rng.uniform(0.65, 1.0))]
    while pts[-1][0] < n:
        pts.append((pts[-1][0] + int(SR * rng.uniform(0.005, 0.014)), rng.uniform(0.65, 1.0)))
    for (a, va), (b, vb) in zip(pts, pts[1:]):
        for k in range(a, min(b, n)):
            flutter[k] = va + (vb - va) * (k - a) / (b - a)
    rel = int(SR * 0.06)
    peak = max(abs(v) for v in x) or 1.0
    y = []
    for k in range(n):
        t = k / SR
        if opening:
            env = min(1.0, t / 0.004)
            if k > n - rel:
                env *= 0.5 + 0.5 * math.cos(math.pi * (k - (n - rel)) / rel)
        else:
            env = min(1.0, t / 0.002) * (1.0 - k / n) ** 1.4
        v = x[k] / peak * env * flutter[k]
        y.append(math.tanh(2.0 * v) / math.tanh(2.0))
    return fade_edges(y, int(0.005 * SR), int(0.008 * SR))


def gate_noise(x, rng, on=(0.004, 0.03), off=(0.0, 0.05), floor=0.08):
    """Amplitude gating: random on/off chunks with random levels plus sparse pops."""
    y = list(x)
    i = 0
    while i < len(y):
        n_on = int(SR * rng.uniform(*on))
        lvl = rng.uniform(0.35, 1.0)
        for k in range(i, min(len(y), i + n_on)):
            y[k] *= lvl
        i += n_on
        n_off = int(SR * rng.uniform(*off))
        for k in range(i, min(len(y), i + n_off)):
            y[k] *= floor
        i += n_off
    for _ in range(int(len(y) / SR * rng.uniform(15, 40))):
        p = rng.randrange(len(y))
        amp = rng.uniform(0.6, 1.4) * rng.choice([-1, 1])
        for k in range(min(30, len(y) - p)):
            y[p + k] += amp * math.exp(-k / 6.0) * (1 if k % 2 == 0 else -0.6)
    return y


def make_static(folder, i, seed):
    rng = random.Random("%d:%s:%d" % (seed, folder, i))
    nseed = 1000 + i + 37 * len(folder) + seed * 101
    name = folder.split("/")[1]
    if name == "burst":
        dur = rng.uniform(0.3, 1.3)
        center = rng.uniform(1500, 3200)
        x = noise(nseed, dur, rng.choice(["white", "pink"]),
                  "bandpass=f=%.0f:width_type=q:w=0.7,highpass=f=250" % center)
        x = gate_noise(x, rng)
        x = process(x, "lowpass=f=6500,highpass=f=200")
        return fade_edges(x, int(0.006 * SR), int(0.04 * SR))
    if name == "squelch":
        dur = [0.16, 0.22, 0.27][i % 3]
        x = noise(nseed, dur, "white", "highpass=f=900,lowpass=f=5500")
        n = len(x)
        for k in range(n):
            t = k / n
            x[k] *= (1.0 - t) ** 1.6
        # short downward "chirp" at the start like a receiver closing its squelch
        f0 = [2200.0, 1900.0, 2500.0][i % 3]
        ch = source_gen("aevalsrc=0.5*sin(2*PI*(%f*t-0.5*%f*t*t)):s=%d" % (f0, f0 * 4, SR), 0.06)
        for k, v in enumerate(ch):
            if k < n:
                x[k] += v * (1.0 - k / len(ch))
        return fade_edges(x, int(0.005 * SR), int(0.008 * SR))
    if name == "loop":
        L = int(SR * [6.0, 5.0, 7.0][i % 3])
        X = int(SR * 0.5)
        y = noise(nseed, (L + X) / SR + 0.05, ["pink", "white", "pink"][i % 3],
                  "highpass=f=250,lowpass=f=%d" % [4200, 3600, 4800][i % 3])
        # put the seam on the smallest natural sample step near the nominal length
        L = min(range(L - 60, L + 61), key=lambda n: abs(y[n] - y[n - 1]))
        y = y[:L + X]
        # slow, loop-periodic flutter: integer cycles per loop length
        cyc = [3, 5, 2][i % 3]
        ph = rng.uniform(0, 2 * math.pi)
        for k in range(len(y)):
            y[k] *= 1.0 + 0.12 * math.sin(2 * math.pi * cyc * k / L + ph)
        out = y[:L]
        for k in range(X):
            t = (k + 0.5) / X
            out[k] = y[k] * math.sin(t * math.pi / 2) + y[L + k] * math.cos(t * math.pi / 2)
        return out
    if name == "master_alarm":
        # Soft caution tone, exactly 2 s and periodic: 660 Hz and 880 Hz alternate every 0.5 s with smooth
        # crossfades, a slow 4 Hz tremolo and a faint second harmonic. Every part has a whole number of cycles
        # in 2 s, so the loop seam is clean without a crossfade. No noise and no filter, it should sound calm.
        L = int(SR * 2.0)
        y = [0.0] * L
        for k in range(L):
            t = k / SR
            # w: 1 on the high tone, 0 on the low tone, raised-cosine edges of 0.12 s, period 1 s
            u = t % 1.0
            if u < 0.5:
                w = 1.0 if u > 0.12 else 0.5 - 0.5 * math.cos(math.pi * u / 0.12)
            else:
                v = u - 0.5
                w = 0.0 if v > 0.12 else 0.5 + 0.5 * math.cos(math.pi * v / 0.12)
            hi = math.sin(2 * math.pi * 880 * t) + 0.12 * math.sin(2 * math.pi * 1760 * t)
            lo = math.sin(2 * math.pi * 660 * t) + 0.12 * math.sin(2 * math.pi * 1320 * t)
            trem = 0.8 + 0.2 * math.sin(2 * math.pi * 4.0 * t)
            y[k] = (w * hi + (1.0 - w) * lo) * trem
        return y
    if name == "breath_loop":
        # EVA helmet breathing (v2 rev 3): exactly 8 s, two breaths per loop.
        # Each breath: inhale (brighter, narrower band near 1.9 kHz, 1.0 to 1.3 s, a smooth bell),
        # a short hold, then exhale (darker and wider near 0.95 kHz, 1.4 to 1.8 s, quick onset and a
        # long tail). Under it a faint suit-fan hiss so the helmet never goes fully silent.
        # The mix gets a short double echo (6 and 11 ms) and a low-pass: a small closed space.
        # The breaths sit inside the loop and are silent at both ends, so only the fan hiss crosses
        # the seam, and that is crossfaded like static/loop.
        L = int(SR * 8.0)
        X = int(SR * 0.5)
        nseed = 3000 + i + 37 * len(folder) + seed * 101
        inh = noise(nseed, 8.1, "pink", "highpass=f=700,bandpass=f=1900:width_type=q:w=0.9")
        exh = noise(nseed + 1, 8.1, "pink", "highpass=f=200,bandpass=f=950:width_type=q:w=0.6")
        breath = [0.0] * L
        for b in range(2):
            start = b * 4.0 + rng.uniform(0.15, 0.35)
            t_in = rng.uniform(1.0, 1.3)
            hold = rng.uniform(0.12, 0.3)
            t_ex = rng.uniform(1.4, 1.8)
            g_in = rng.uniform(0.55, 0.7)
            g_ex = rng.uniform(0.9, 1.0)
            a = int(SR * start)
            m = int(SR * t_in)
            for k in range(m):
                u = k / m
                breath[a + k] += inh[a + k] * g_in * math.sin(math.pi * u) ** 2
            a2 = a + m + int(SR * hold)
            m2 = int(SR * t_ex)
            for k in range(m2):
                u = k / m2
                rise = math.sin(0.5 * math.pi * min(1.0, u / 0.18)) ** 2
                breath[a2 + k] += exh[a2 + k] * g_ex * rise * (1.0 - u) ** 1.3
        fan = noise(nseed + 2, (L + X) / SR + 0.05, "pink", "highpass=f=120,lowpass=f=1400")
        fan_loop = fan[:L]
        for k in range(X):
            t = (k + 0.5) / X
            fan_loop[k] = fan[k] * math.sin(t * math.pi / 2) + fan[L + k] * math.cos(t * math.pi / 2)
        pk_b = max(abs(v) for v in breath) or 1.0
        pk_f = max(abs(v) for v in fan_loop) or 1.0
        mix = [breath[k] / pk_b + 0.06 * fan_loop[k] / pk_f for k in range(L)]
        # Filter the loop's own last X samples first, then the loop, and cut. The start then sees the
        # same filter and echo history it gets when the loop wraps, so the seam stays clean.
        y = process(mix[L - X:] + mix, "aecho=0.85:0.7:6|11:0.22|0.12,lowpass=f=3000,highpass=f=120")
        return y[X:X + L]
    raise ValueError(folder)


# ---------------------------------------------------------------------------
# Verification (independent of the build path: ffprobe + ffmpeg astats/volumedetect)
# ---------------------------------------------------------------------------
def probe(path):
    p = run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
             "stream=codec_name,sample_rate,channels,bits_per_sample:format=duration,format_name",
             "-of", "json", str(path)])
    j = json.loads(p.stdout)
    st = j["streams"][0]
    return dict(codec=st.get("codec_name"), rate=int(st.get("sample_rate", 0)), channels=int(st.get("channels", 0)),
                duration=float(j["format"]["duration"]), fmt=j["format"].get("format_name"))


def ffmpeg_levels(path):
    """Peak via volumedetect, 10 ms frame RMS via astats."""
    p = run(["ffmpeg", "-hide_banner", "-nostdin", "-i", str(path), "-af", "volumedetect", "-f", "null", "-"])
    err = p.stderr.decode("utf-8", "replace")
    m = re.search(r"max_volume:\s*(-?[0-9.]+|-inf) dB", err)
    mv = re.search(r"mean_volume:\s*(-?[0-9.]+|-inf) dB", err)
    peak = float(m.group(1)) if m and m.group(1) != "-inf" else -240.0
    mean = float(mv.group(1)) if mv and mv.group(1) != "-inf" else -240.0
    txt = WORK / "verify_levels.txt"
    af = ("asetnsamples=n=%d:p=0,astats=metadata=1:reset=1:measure_perchannel=none:measure_overall=RMS_level,"
          "ametadata=print:key=lavfi.astats.Overall.RMS_level:file=%s") % (FRAME, str(txt).replace(":", "\\:"))
    run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error", "-i", str(path), "-af", af, "-f", "null", "-"])
    fr = []
    for line in txt.read_text().splitlines():
        if "RMS_level=" in line:
            try:
                v = float(line.split("=", 1)[1])
            except ValueError:
                v = -240.0
            fr.append(v if math.isfinite(v) else -240.0)
    return peak, mean, fr


def verify(manifest_entries):
    log("")
    log("Verification (ffprobe + ffmpeg volumedetect/astats)")
    failures = []
    WORK.mkdir(parents=True, exist_ok=True)
    by_folder = {}
    for e in manifest_entries:
        by_folder.setdefault(e["folder"], []).append(e)
    # files on disk must equal the manifest, names contiguous
    for folder, cfg in FOLDERS.items():
        d = out_root(folder) / folder
        files = sorted(p.name for p in d.glob("*")) if d.exists() else []
        base = folder.replace("/", "_")
        exp = ["%s_%03d.wav" % (base, k) for k in range(len(files))]
        if files != exp:
            failures.append("%s: file names not contiguous/regular: %s" % (folder, files[:5]))
        for f in files:
            if not NAME_RE.match(f):
                failures.append("%s: bad file name %s" % (folder, f))
    for top in ("beeps", "static"):
        for p in (SOUNDS / top).rglob("*"):
            if p.is_file() and p.suffix.lower() != ".wav":
                failures.append("non-wav file under Sounds/%s: %s" % (top, p))
    # size of what ships: the whole merged Sounds/ tree, Lonapse OGG included
    total_size = sum(p.stat().st_size for p in SOUNDS.rglob("*") if p.suffix.lower() in (".wav", ".ogg"))
    if total_size > SIZE_LIMIT:
        failures.append("Sounds/ is %.1f MB, limit %d MB" % (total_size / 1048576, SIZE_LIMIT // 1048576))

    for e in manifest_entries:
        path = BUILD / e["file"]
        cfg = FOLDERS[e["folder"]]
        pr = probe(path)
        tag = e["file"]
        if pr["codec"] != "pcm_s16le" or pr["rate"] != SR or pr["channels"] != 1 or pr["fmt"] != "wav":
            failures.append("%s: format %s" % (tag, pr))
        if not (cfg["lo"] - 0.002 <= pr["duration"] <= cfg["hi"] + 0.002):
            failures.append("%s: duration %.3f outside %.2f..%.2f" % (tag, pr["duration"], cfg["lo"], cfg["hi"]))
        peak, mean, fr = ffmpeg_levels(path)
        if peak > -1.0:
            failures.append("%s: peak %.1f dBFS > -1.0" % (tag, peak))
        lin = [10 ** (v / 10) for v in fr]
        mx = max(lin) if lin else 0
        gate = max(10 ** (SILENCE_DB / 10), mx * 1e-3)
        sel = [v for v in lin if v >= gate]
        grms = 10 * math.log10(sum(sel) / len(sel)) if sel else -240.0
        if abs(grms - cfg["rms"]) > RMS_TOL:
            failures.append("%s: gated RMS %.2f dBFS, target %.1f +-%.1f" % (tag, grms, cfg["rms"], RMS_TOL))
        # leading / trailing silence (10 ms frames under -50 dBFS)
        thr = SILENCE_DB
        lead = 0
        while lead < len(fr) and fr[lead] < thr:
            lead += 1
        trail = 0
        while trail < len(fr) and fr[len(fr) - 1 - trail] < thr:
            trail += 1
        if lead * 0.01 > MAX_SILENCE or trail * 0.01 > MAX_SILENCE:
            failures.append("%s: leading %.0f ms / trailing %.0f ms silence" % (tag, lead * 10, trail * 10))
        x = read_wav(path)
        if cfg["kind"] == "loop":
            # the wrap-around step must look like any other sample step of the noise
            dif = sorted(abs(x[k + 1] - x[k]) for k in range(len(x) - 1))
            p995 = dif[int(0.999 * (len(dif) - 1))]
            jump = abs(x[0] - x[-1])
            r0 = db(rms(x[:2205]))
            r1 = db(rms(x[-2205:]))
            if jump > p995 or abs(r0 - r1) > 3.0:
                failures.append("%s: loop seam jump %.4f (99.9%% step %.4f), edge rms %.1f/%.1f" %
                                (tag, jump, p995, r0, r1))
            e["loop_seam_jump"] = round(jump, 5)
        else:
            if abs(x[0]) > 0.003 or abs(x[-1]) > 0.003:
                failures.append("%s: no fade at edges (first %.4f last %.4f)" % (tag, x[0], x[-1]))
        e["verified_peak"] = peak
        e["verified_rms"] = round(grms, 2)
        e["verified_mean_volume"] = mean
    log("")
    log("  %-18s %5s %5s %8s %8s %8s %9s %9s %s" % ("folder", "min", "count", "min_s", "max_s", "total_s",
                                                  "rms_dB", "peak_dB", "status"))
    ok_all = True
    for folder, cfg in FOLDERS.items():
        es = by_folder.get(folder, [])
        n = len(es)
        if es:
            durs = [x["duration"] for x in es]
            rr = [x["verified_rms"] for x in es]
            pk = max(x["verified_peak"] for x in es)
            line = "  %-18s %5d %5d %8.3f %8.3f %8.1f %4.1f..%-4.1f %9.1f" % (
                folder, cfg["min"], n, min(durs), max(durs), sum(durs), min(rr), max(rr), pk)
        else:
            line = "  %-18s %5d %5d %8s %8s %8s %9s %9s" % (folder, cfg["min"], 0, "-", "-", "-", "-", "-")
        st = "OK" if n >= cfg["min"] else "UNDER MINIMUM"
        if n < cfg["min"]:
            ok_all = False
            failures.append("%s: %d clips, minimum %d" % (folder, n, cfg["min"]))
        log(line + " " + st)
    log("  total files: %d, total size: %.2f MB" % (len(manifest_entries), total_size / 1048576))
    if failures:
        log("")
        log("VERIFICATION FAILED (%d problems):" % len(failures))
        for f in failures[:80]:
            log("  - " + f)
        return False
    log("  all checks passed" if ok_all else "")
    return ok_all


# ---------------------------------------------------------------------------
# Install
# ---------------------------------------------------------------------------
def ksp_dir():
    """$KSP_DIR, else the first line of .ksp_dir in the repo root (a local file, not in git)."""
    d = os.environ.get("KSP_DIR", "").strip()
    if not d and KSP_DIR_FILE.is_file():
        rows = [r.strip() for r in KSP_DIR_FILE.read_text(encoding="utf-8").splitlines() if r.strip()]
        d = rows[0] if rows else ""
    if not d:
        die("no KSP install given: set KSP_DIR or write its path into %s" % KSP_DIR_FILE)
    return d


def install():
    ksp = Path(ksp_dir())
    if not (ksp / "GameData" / "Squad").is_dir():
        die("KSP_DIR %s does not look like a KSP install (no GameData/Squad)" % ksp)
    gd = ksp / "GameData" / "Lonapse"
    dst = gd / "Sounds"
    if not str(dst).endswith("/GameData/Lonapse/Sounds"):
        die("refusing to install into %s" % dst)
    if dst.is_symlink():
        die("refusing to install: %s is a symlink" % dst)
    if subprocess.run(["pgrep", "-f", "KSP_x64.exe"], stdout=subprocess.DEVNULL).returncode == 0:
        die("KSP is running. Close it before installing sounds.")
    # Sounds/ is the merged tree. Installing it without the Lonapse voices would ship a mod with no crew.
    if not any((SOUNDS / "crew").rglob("*.ogg")) or not any((SOUNDS / "control").rglob("*.ogg")):
        die("no Lonapse voices under %s/crew and /control. Run python3 -I audio/build_lonapse.py first." % SOUNDS)
    dst.mkdir(parents=True, exist_ok=True)
    want = set()
    copied = 0
    for p in sorted(q for q in SOUNDS.rglob("*") if q.is_file() and q.suffix.lower() in (".wav", ".ogg")):
        rel = p.relative_to(SOUNDS)
        want.add(rel)
        t = dst / rel
        t.parent.mkdir(parents=True, exist_ok=True)
        if not t.exists() or t.stat().st_size != p.stat().st_size or sha256_of(t) != sha256_of(p):
            shutil.copy2(p, t)
            copied += 1
    removed = 0
    # delete stale files, only inside dst
    for p in sorted(dst.rglob("*"), key=lambda q: len(q.parts), reverse=True):
        rel = p.relative_to(dst)
        if p.is_file() or p.is_symlink():
            if rel not in want:
                p.unlink()
                removed += 1
        elif p.is_dir() and not any(p.iterdir()):
            p.rmdir()
    n = sum(1 for q in dst.rglob("*") if q.suffix.lower() in (".wav", ".ogg"))
    log("")
    log("install: %s" % dst)
    log("install: %d files copied, %d stale files removed, %d sound files installed" % (copied, removed, n))

    # the Lonapse phrase table: the plugin maps clips to scenes and subtitles with it
    table = BUILD / "lonapse" / "phrases.tsv"
    if not table.exists():
        die("missing %s. Run python3 -I audio/build_lonapse.py first." % table)
    tdst = gd / "Phrases" / "phrases.tsv"
    tdst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(table, tdst)
    log("install: %s" % tdst)
    # experimental (procedural talk): the subtitle slot pools, next to the phrase table
    pools = BUILD / "lonapse" / "pools.tsv"
    pdst = gd / "Phrases" / "pools.tsv"
    if pools.exists():
        shutil.copy2(pools, pdst)
        log("install: %s" % pdst)
    elif pdst.exists():
        pdst.unlink()
        log("install: removed stale %s" % pdst)


# ---------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(description="Build Lonapse clips (audio contract).")
    ap.add_argument("--clean", action="store_true", help="remove audio/work/ and audio/build/ first")
    ap.add_argument("--install", action="store_true", help="sync audio/build/Sounds/ into GameData/Lonapse/Sounds/")
    ap.add_argument("--seed", type=int, default=1, help="random seed (default 1)")
    ap.add_argument("--verify-only", action="store_true", help="only verify the existing build")
    args = ap.parse_args()

    for tool in ("ffmpeg", "ffprobe"):
        if shutil.which(tool) is None:
            die("%s not found in PATH" % tool)

    if args.verify_only:
        if not MANIFEST.exists():
            die("no manifest, build first")
        entries = json.loads(MANIFEST.read_text())["files"]
        ok = verify(entries)
        sys.exit(0 if ok else 1)

    if args.clean:
        # Only what this script owns: the work cache, its own output folders and its manifest.
        # The Lonapse voices in Sounds/crew and Sounds/control are left alone.
        owned = [WORK, MANIFEST] + [SOUNDS / top for top in ("beeps", "static")]
        for d in owned:
            if d.is_dir():
                shutil.rmtree(d)
                log("clean: removed %s" % d)
            elif d.exists():
                d.unlink()
                log("clean: removed %s" % d)

    load_probe_cores()   # plan v5: fail early on a bad audio/probe/cores.tsv
    # Replace only the folders this script builds. Sounds/crew and Sounds/control (Lonapse) stay.
    for folder in FOLDERS:
        d = out_root(folder) / folder
        if d.exists():
            shutil.rmtree(d)
        d.mkdir(parents=True)
    entries = []
    log("")
    for folder, cfg in FOLDERS.items():
        log("build: %-18s %d clips" % (folder, cfg["count"]))
        base = folder.replace("/", "_")
        for i in range(cfg["count"]):
            if cfg["kind"] == "beep":
                y = normalize(make_beeps(folder, i, args.seed), cfg["rms"], fades=False)
            elif cfg["kind"] == "loop":
                y = normalize(make_static(folder, i, args.seed), cfg["rms"], fades=False)
            else:
                y = normalize(make_static(folder, i, args.seed), cfg["rms"], fades=False)
            path = out_root(folder) / folder / ("%s_%03d.wav" % (base, i))
            write_wav(path, y)
            e = dict(file=str(path.relative_to(BUILD)), folder=folder, duration=round(len(y) / SR, 4),
                     rms=round(gated_rms_db(y), 2), peak=round(db(max(abs(v) for v in y)), 2))
            entries.append(e)

    log("")
    ok = verify(entries)
    MANIFEST.write_text(json.dumps(dict(seed=args.seed, sample_rate=SR, files=entries), indent=1) + "\n")
    log("manifest: %s" % MANIFEST)
    if not ok:
        die("build does not meet the audio contract")
    if args.install:
        install()


if __name__ == "__main__":
    main()
