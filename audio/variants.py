#!/usr/bin/env python3
"""Experimental (scene variants): prints every version of the scenes that have alt rows, so I can read them all
before rendering. A scene with 3 options on seq 2 and 4 on seq 3 prints 12 versions.

Usage:
    python3 audio/variants.py                  every scene with alt rows, English
    python3 audio/variants.py SCENE [SCENE..]  only these scenes
    python3 audio/variants.py --spanish        the Spanish under each English line
    python3 audio/variants.py --max 50         at most 50 versions per scene (default 200)

Reads the source tables in audio/lonapse/phrases the same way build_lonapse.py does. Table errors are printed and
the script stops with exit code 1.
"""
import argparse
import itertools
import sys

import build_lonapse as b


def scenes_with_alts(rows):
    """{scene: {seq: [base row, alt rows in alt order]}} for the scenes that have at least one alt row."""
    by_scene = {}
    for r in rows:
        by_scene.setdefault(r["scene"], {}).setdefault(r["seq"], []).append(r)
    out = {}
    for sc, seqs in by_scene.items():
        if not any(r["alt"] for opts in seqs.values() for r in opts):
            continue
        out[sc] = {seq: sorted(opts, key=lambda r: (r["alt"] != "", r["alt"])) for seq, opts in seqs.items()}
    return out


def main():
    ap = argparse.ArgumentParser(description="Print every version of the scenes with alt rows.")
    ap.add_argument("scenes", nargs="*", help="scene names (default: every scene with alt rows)")
    ap.add_argument("--spanish", action="store_true", help="also print the Spanish line")
    ap.add_argument("--max", type=int, default=200, help="most versions printed per scene (default 200)")
    args = ap.parse_args()

    rows, errors, _ = b.load_sources()
    good, verr = b.validate(rows)
    errors += verr
    if errors:
        for e in errors:
            print("ERROR table: " + e)
        return 1

    found = scenes_with_alts(good)
    names = args.scenes or sorted(found)
    missing = [n for n in names if n not in found]
    for n in missing:
        print("No alt rows in scene '%s'" % n)
    total = 0
    for sc in names:
        if sc not in found:
            continue
        seqs = found[sc]
        order = sorted(seqs)
        count = 1
        for s in order:
            count *= len(seqs[s])
        total += count
        print("=== %s: %d versions (%s)" % (sc, count, " x ".join(str(len(seqs[s])) for s in order)))
        for i, combo in enumerate(itertools.product(*(seqs[s] for s in order))):
            if i >= args.max:
                print("  ... %d more" % (count - args.max))
                break
            print("  [%d]" % (i + 1))
            for r in combo:
                tag = r["alt"] or "-"
                print("    %-4s %s" % (tag, r["english"]))
                if args.spanish:
                    print("         %s" % r["spanish"])
        print()
    if names and not missing:
        print("%d scenes, %d versions in total" % (len([n for n in names if n in found]), total))
    elif not found:
        print("No scene has alt rows yet")
    return 0


if __name__ == "__main__":
    sys.exit(main())
