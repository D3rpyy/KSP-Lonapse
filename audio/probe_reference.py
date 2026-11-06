#!/usr/bin/env python3
"""Write tests/probe_reference.tsv: fingerprints of every probe beep from build_clips.py, for the C# port in
src/ProbeSynth.cs. The random timing jitter is replaced by its midpoint, so both sides are deterministic.

Usage: python3 -I audio/probe_reference.py
Run it again after changing the beep synth or audio/probe/cores.tsv in build_clips.py.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)   # -I leaves the script folder off sys.path

import build_clips as bc   # noqa: E402

POINTS = 48   # samples per clip in the fingerprint


class MidRandom:
    """Stand-in for random.Random in make_probe_beep: uniform(a, b) gives the midpoint."""

    def __init__(self, *args):
        pass

    def uniform(self, a, b):
        return (a + b) * 0.5


def main():
    bc.random = type("R", (), {"Random": MidRandom})
    out = os.path.join(HERE, "..", "tests", "probe_reference.tsv")
    # header first and every comment padded to the header width, so GitHub shows the file as a table
    header = ["core", "mood", "variant", "samples", "gated_rms_db"] + ["p%d" % k for k in range(POINTS)]
    pad = "\t" * (len(header) - 1)
    rows = ["\t".join(header),
            "# Probe beep fingerprints from audio/probe_reference.py (build_clips.py with midpoint jitter)." + pad,
            "# p0 to p%d: %d samples at i * (samples - 1) / %d" % (POINTS - 1, POINTS, POINTS - 1) + pad]
    bc.load_probe_cores()   # fail early on a bad cores.tsv
    for core in bc.PROBE_CORE_KEYS:
        for mood in bc.PROBE_MOODS:
            folder = "beeps/probe_%s_%s" % (core, mood)
            cfg = bc.FOLDERS[folder]
            for i in range(cfg["count"]):
                y = bc.normalize(bc.make_probe_beep(folder, i, 1), cfg["rms"], fades=False)
                n = len(y)
                pts = [y[k * (n - 1) // (POINTS - 1)] for k in range(POINTS)]
                rows.append("\t".join([core, mood, str(i), str(n), "%.6f" % bc.gated_rms_db(y)] + ["%.9f" % v for v in pts]))
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(rows) + "\n")
    print("Wrote %s (%d clips, cores from %s)" % (os.path.normpath(out), len(rows) - 3, bc.PROBE_CORES_TSV.name))


if __name__ == "__main__":
    main()
