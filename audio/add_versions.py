#!/usr/bin/env python3
"""Experimental (procedural talk): adds subtitle versions to existing rows of the phrase tables.

The input has one line per row: "<scene> <seq> <version> | <version> ...". The versions are added after the english
of the row with that scene and seq, split by |. The spanish stays, so no audio changes. Rows with
{body}, probe rows and rows that already have versions are skipped, so running it twice changes nothing.

  python3 audio/add_versions.py versions.txt            show what would change
  python3 audio/add_versions.py versions.txt --write    write the tables
"""
import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PHRASES = ROOT / "lonapse" / "phrases"


def load_versions(path):
    out, errors = {}, []
    for n, line in enumerate(Path(path).read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        parts = line.split(" ", 2)
        if len(parts) < 3 or not parts[1].isdigit():
            errors.append("%s:%d: expected '<scene> <seq> <versions>'" % (path, n))
            continue
        vs = [v.strip() for v in parts[2].split("|") if v.strip()]
        out[(parts[0], int(parts[1]))] = vs
    return out, errors


def main():
    ap = argparse.ArgumentParser(description="Add subtitle versions to existing phrase rows.")
    ap.add_argument("versions", nargs="+", help="files with '<scene> <seq> <version> | <version>' lines")
    ap.add_argument("--write", action="store_true", help="write the changed tables (default: only show)")
    args = ap.parse_args()
    want, errors = {}, []
    for p in args.versions:
        v, e = load_versions(p)
        want.update(v)
        errors += e
    if errors:
        print("\n".join(errors))
        sys.exit(1)
    done, skipped = set(), []
    for f in sorted(PHRASES.glob("*.tsv")):
        lines = f.read_text(encoding="utf-8").split("\n")
        header = None
        changed = 0
        for i, line in enumerate(lines):
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            cols = line.split("\t")
            if header is None:
                header = {c: k for k, c in enumerate(cols)}
                continue
            sc, seq = cols[header["scene"]], cols[header["seq"]]
            key = (sc, int(seq)) if seq.isdigit() else None
            if key not in want:
                continue
            en = cols[header["english"]]
            done.add(key)
            if "|" in en or "{body}" in en or cols[header["role"]] == "probe":
                skipped.append("%s %s: %s" % (sc, seq, "has versions" if "|" in en else "{body} or probe row"))
                continue
            cols[header["english"]] = " | ".join([en] + want[key])
            lines[i] = "\t".join(cols)
            changed += 1
        if changed:
            print("%s: %d row(s)" % (f.name, changed))
            if args.write:
                f.write_text("\n".join(lines), encoding="utf-8")
    missing = sorted(set(want) - done)
    for m in missing:
        print("not found: %s %d" % m)
    for s in skipped:
        print("skipped: " + s)
    if not args.write:
        print("(dry run, add --write to change the tables)")


if __name__ == "__main__":
    main()
