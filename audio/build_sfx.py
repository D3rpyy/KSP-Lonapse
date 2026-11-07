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
}

# Effects that only get louder: one plain gain to a loudness target (LUFS), no body lift, so the recording keeps its
# own shape. The limiter catches the peaks above the ceiling. -16 LUFS is about the airlock.
GAIN_ONLY = {"flag_plant": -16.0}


# Effects that are synthesized because there is no recording: name -> list of (freq Hz, start s, length s, amp).
# The soi_change ping is two soft sine notes a fifth apart, like a telemetry handover blip.
SYNTH = {
    "soi_change": [(880.0, 0.00, 0.22, 0.50), (1318.5, 0.16, 0.45, 0.45)],
}


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
