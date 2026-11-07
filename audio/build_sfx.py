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
}


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def build(name, out_dir):
    src = SOURCES / EFFECTS[name]
    if not src.is_file():
        bc.die("missing source %s" % src)
    path = out_dir / name / ("sfx_%s_000.wav" % name)
    path.parent.mkdir(parents=True, exist_ok=True)
    bc.run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", str(src),
            "-af", BODY_LIFT + "," + LIMIT, "-ar", str(SR), "-c:a", "pcm_s16le", str(path)])
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
        return
    names = a.effects or list(EFFECTS)
    for n in names:
        if n not in EFFECTS:
            bc.die("unknown effect '%s', try --list" % n)
    for n in names:
        build(n, a.out)


if __name__ == "__main__":
    main()
