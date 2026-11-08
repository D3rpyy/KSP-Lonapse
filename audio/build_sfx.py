#!/usr/bin/env python3
"""Lonapse sound effects (sfx).

Builds the sound effects. This script is separate from build_clips.py so the existing beeps and
static keep their RNG streams and hashes. The effects come from the recordings in audio/sfx_sources/.

Usage:
    python3 -I audio/build_sfx.py [effect ...] [--list] [--out DIR]

Without effect names every effect is built. The files go to audio/build/Sounds/sfx/<effect>/ by
default, named sfx_<effect>_000.wav. Nothing is installed into the game by this script.

Only the Python standard library and the ffmpeg binary are used (helpers come from build_clips.py).
"""

import argparse
import math
import re
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))   # python3 -I does not put the script folder on the path

import build_clips as bc   # noqa: E402

SR = bc.SR
DEFAULT_OUT = bc.SOUNDS / "sfx"


# Effects made from recordings in audio/sfx_sources/ (the originals, never changed). They are not
# synthesized: the body of the sound is raised with an upward compressor, the loud hits stay about
# where they are, and a peak limiter keeps everything under -1.3 dBFS. Stereo is kept.
# compand points are input dB / output dB.
SOURCES = HERE / "sfx_sources"
BODY_LIFT = "compand=attacks=0.005:decays=0.3:points=-90/-90|-60/-52|-40/-22|-20/-10|-2/-2:soft-knee=6"
LIMIT = "alimiter=limit=0.86:level=disabled"      # 0.86 is -1.3 dBFS, the ceiling of build_clips.py

EFFECTS = {
    # name: source file in audio/sfx_sources/
    "airlock_dock":   "airlock_dock.wav",
    "airlock_undock": "airlock_undock.wav",
    "hatch_open":     "hatch_open.wav",
    "hatch_close":    "hatch_close.wav",
    "flag_plant":     "flag_plant.wav",
    "jetpack_on":     "jetpack_on.wav",
    "jetpack_off":    "jetpack_off.wav",
    "footstep":       "footstep.wav",         # synthesized when this file is missing, three steps
    "thud_heavy":     "thud_heavy.wav",       # synthesized when this file is missing
    "thud_light":     "thud_light.wav",       # same
    "jetpack_loop":   "jetpack_loop.wav",     # synthesized when this file is missing
}

# Effects that only get louder: one plain gain to a loudness target (LUFS), no body lift, so the recording keeps its
# own shape. The limiter catches the peaks above the ceiling. -16 LUFS is about the airlock.
GAIN_ONLY = {"flag_plant": -16.0, "jetpack_on": -20.0, "jetpack_off": -20.0, "jetpack_loop": -26.0}


# Effects that are synthesized because there is no recording: name -> list of (freq Hz, start s, length s, amp).
# The soi_change ping is two soft sine notes a fifth apart, like a telemetry handover blip.
SYNTH = {
    "soi_change": [(880.0, 0.00, 0.22, 0.50), (1318.5, 0.16, 0.45, 0.45)],
}


def build_loop_synth(name, out_dir):
    """The thrust loop of the jetpack: a soft gas hiss (band-passed noise) that repeats without a click.
    A recording in audio/sfx_sources/jetpack_loop.wav is used instead when that file exists."""
    dur, xf = 3.0, 0.25
    raw = bc.noise(7, dur + xf, "white", "highpass=f=900,lowpass=f=6500,lowpass=f=5000")
    n, m = int(round(dur * SR)), int(round(xf * SR))
    x = raw[:n]
    for k in range(m):      # equal-power crossfade of the extra tail into the head, so the loop point has no seam
        a = math.sin(0.5 * math.pi * k / m)
        b = math.cos(0.5 * math.pi * k / m)
        x[k] = x[k] * a + raw[n + k] * b
    x = bc.normalize(x, -30.0, fades=False)
    path = out_dir / name / ("sfx_%s_000.wav" % name)
    bc.write_wav(path, x)
    bc.log("synth   %-34s %.2f s  peak %.1f dBFS  (synthesized loop)" % (path.name, len(x) / SR, bc.db(max(abs(v) for v in x))))


# The ground thuds, synthesized (a recording in audio/sfx_sources/ is used instead when it exists): (start Hz, end Hz, pitch fall time s, body decay s, noise lowpass Hz,
# noise level, noise decay s, length s, peak dBFS). A sine that drops in pitch plus a short dull noise burst.
THUDS = {
    "thud_heavy": (95.0, 38.0, 0.10, 0.16, 350.0, 0.55, 0.05, 0.60, -2.0),
    "thud_light": (140.0, 70.0, 0.06, 0.08, 500.0, 0.40, 0.03, 0.30, -5.0),
}


def build_step_synth(name, out_dir):
    """The footsteps: a short dull tap (low-passed noise plus a small low thump), three variants so a walk does not
    repeat one sample. A recording in audio/sfx_sources/footstep.wav is used instead when that file exists."""
    import random
    rng = random.Random("footstep")
    for i in range(3):
        dur = 0.16
        n = int(round(dur * SR))
        lp = int(rng.uniform(900, 1500))
        noise = bc.noise(21 + i, dur, "white", "lowpass=f=%d,lowpass=f=%d" % (lp, lp))
        f0, f1 = rng.uniform(120, 160), rng.uniform(60, 80)
        tn, ta = rng.uniform(0.020, 0.032), rng.uniform(0.030, 0.045)
        x, ph = [], 0.0
        for k in range(n):
            t = k / SR
            ph += 2.0 * math.pi * (f1 + (f0 - f1) * math.exp(-t / 0.02)) / SR
            att = min(1.0, t / 0.002)
            x.append(att * (math.sin(ph) * math.exp(-t / ta) * 0.7 + (noise[k] if k < len(noise) else 0.0) * 1.6 * math.exp(-t / tn)))
        peak = max(abs(v) for v in x)
        x = [v * (10 ** (-3.0 / 20.0) / peak) for v in x]
        x = bc.fade_edges(x, 0, int(SR * 0.01))
        path = out_dir / name / ("sfx_%s_%03d.wav" % (name, i))
        bc.write_wav(path, x)
        bc.log("synth   %-34s %.2f s  peak %.1f dBFS  (synthesized step)" % (path.name, len(x) / SR, bc.db(max(abs(v) for v in x))))


def build_thud_synth(name, out_dir):
    f0, f1, tf, ta, lp, nl, tn, dur, pk = THUDS[name]
    n = int(round(dur * SR))
    noise = bc.noise(11 if name == "thud_heavy" else 12, dur, "white", "lowpass=f=%d,lowpass=f=%d" % (lp, lp))
    x, ph = [], 0.0
    for k in range(n):
        t = k / SR
        f = f1 + (f0 - f1) * math.exp(-t / tf)
        ph += 2.0 * math.pi * f / SR
        att = min(1.0, t / 0.003)
        body = math.sin(ph) * math.exp(-t / ta) * att
        x.append(body + (noise[k] if k < len(noise) else 0.0) * nl * 2.0 * math.exp(-t / tn) * att)
    peak = max(abs(v) for v in x)
    x = [v * (10 ** (pk / 20.0) / peak) for v in x]
    x = bc.fade_edges(x, 0, int(SR * 0.01))
    path = out_dir / name / ("sfx_%s_000.wav" % name)
    bc.write_wav(path, x)
    bc.log("synth   %-34s %.2f s  peak %.1f dBFS  (synthesized thud)" % (path.name, len(x) / SR, bc.db(max(abs(v) for v in x))))


def build_synth(name, out_dir):
    notes = SYNTH[name]
    total = max(s + d for _, s, d, _ in notes) + 0.05
    x = [0.0] * int(round(total * SR))
    for f, start, dur, amp in notes:
        t = bc.tone(f, dur, attack=0.008, decay=dur / 3.0)
        i0 = int(round(start * SR))
        for k, v in enumerate(t):
            x[i0 + k] += v * amp
    peak = max(abs(v) for v in x)
    x = [v * (10 ** (-5.0 / 20.0) / peak) for v in x]      # peak at -5 dBFS, a ping should sit just under the airlock hits
    x = bc.limit(x)
    path = out_dir / name / ("sfx_%s_000.wav" % name)
    bc.write_wav(path, x)
    bc.log("synth   %-34s %.2f s  peak %.1f dBFS" % (path.name, len(x) / SR, bc.db(max(abs(v) for v in x))))


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def source_lufs(src):
    """The integrated loudness of a file in LUFS (ffmpeg ebur128)."""
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostdin", "-i", str(src), "-af", "ebur128", "-f", "null", "-"],
                       capture_output=True, text=True)
    m = re.findall(r"I:\s+(-?[0-9.]+) LUFS", r.stderr)
    if not m:
        bc.die("no loudness for %s" % src)
    return float(m[-1])


def build(name, out_dir):
    src = SOURCES / EFFECTS[name]
    if not src.is_file() and name == "footstep":
        return build_step_synth(name, out_dir)
    if not src.is_file() and name in THUDS:
        return build_thud_synth(name, out_dir)
    if not src.is_file() and name == "jetpack_loop":
        return build_loop_synth(name, out_dir)
    if not src.is_file():
        bc.die("missing source %s" % src)
    path = out_dir / name / ("sfx_%s_000.wav" % name)
    path.parent.mkdir(parents=True, exist_ok=True)
    af = BODY_LIFT + "," + LIMIT
    if name in GAIN_ONLY:
        af = "volume=%.2fdB,%s" % (GAIN_ONLY[name] - source_lufs(src), LIMIT)
    bc.run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", str(src),
            "-af", af, "-ar", str(SR), "-c:a", "pcm_s16le", str(path)])
    for label, f in (("source", src), ("output", path)):
        x = bc.ffmpeg_raw(["-i", str(f)])      # mono downmix, only for the numbers
        w = SR // 10
        lv = sorted(bc.db(math.sqrt(sum(v * v for v in x[i:i + w]) / w)) for i in range(0, len(x) - w, w))
        lv = [v for v in lv if v > -60]
        bc.log("%-7s %-34s %.2f s  body %.1f dB  peak %.1f dBFS" %
               (label, f.name, len(x) / SR, lv[len(lv) // 2], bc.db(max(abs(v) for v in x))))


def main():
    ap = argparse.ArgumentParser(description="Build the Lonapse sound effects.")
    ap.add_argument("effects", nargs="*", help="effect names, default all")
    ap.add_argument("--list", action="store_true", help="list the effects and stop")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT, help="output folder, default audio/build/Sounds/sfx")
    a = ap.parse_args()
    if a.list:
        for n, f in EFFECTS.items():
            bc.log("%s  from %s" % (n, f))
        for n in list(SYNTH):
            bc.log("%s  synthesized" % n)
        return
    names = a.effects or (list(EFFECTS) + list(SYNTH))
    for n in names:
        if n not in EFFECTS and n not in SYNTH:
            bc.die("unknown effect '%s', try --list" % n)
    for n in names:
        if n in SYNTH:
            build_synth(n, a.out)
        else:
            build(n, a.out)


if __name__ == "__main__":
    main()
