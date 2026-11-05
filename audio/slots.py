#!/usr/bin/env python3
"""Experimental (procedural talk): prints random fills of the scenes with subtitle slots or versions (|), so I can
read how they come out before I play them in game. Same fill rules as SubtitleSlots.Fill in C#: one value per slot
in a scene, {pool:2} another value, pool values with slots inside, a capital at the start of a sentence.

  python3 audio/slots.py               3 fills of every template scene
  python3 audio/slots.py tp_lost -n 8  8 fills of one scene
  python3 audio/slots.py --spanish     with the spanish under each line
  python3 audio/slots.py --phase landed --body kerbin   the versions for that situation ([landed], [kerbin])
  python3 audio/slots.py --has solar,legs              the vessel parts there are (default: a typical lander)
"""
import argparse
import random
import re
import sys

import build_lonapse as b

SLOT = re.compile(r"\{([a-z_]+)(?::([1-9]))?\}")
FAKE_FLIGHT = {"day": ["1", "3", "12", "47"], "here": ["Kerbin", "Mun", "Minmus", "Duna", "Jool"],
               "vessel": ["Kerbal X", "Lander II", "Station Alpha"], "alt_km": ["85", "120", "2.4", "640"]}
FAKE_CREW = ["Jebediah", "Bill", "Bob", "Valentina"]


def at_sentence_start(s, i, text_starts):
    k = i - 1
    while k >= 0 and (s[k].isspace() or s[k] in "\"'"):
        k -= 1
    return text_starts if k < 0 else s[k] in ".!?"


def cap(v):
    return v[:1].upper() + v[1:]


def pick_version(english, conditions, rng, pools):
    """Same rule as SubtitleSlots.PickVersion: the versions whose conditions all hold, else the plain ones, and of
    those only the ones whose pool slots have a usable value."""
    def ok(t):
        return all(n in FAKE_FLIGHT or n in ("self", "other") or usable(n, pools, conditions)
                   for n in b.slot_names(t))
    vs = [b.split_conditions(v) for v in english.split("|")]
    match = [t for c, t in vs if c and all(x in conditions for x in c) and ok(t)]
    plain = [t for c, t in vs if not c and ok(t)]
    return rng.choice(match or plain or ["(cannot be filled now, the scene is skipped)"])


def usable(pool, pools, conditions):
    """Values of a pool whose [conditions] hold, without the [ ] part."""
    out = []
    for raw in pools.get(pool, []):
        c, t = b.split_conditions(raw)
        if c is not None and all(x in conditions for x in c):
            out.append(t)
    return out


def fill_scene(lines, pools, rng, conditions, body):
    flight = {k: rng.choice(v) for k, v in FAKE_FLIGHT.items()}
    if body:
        flight["here"] = "the Mun" if body == "mun" else body.capitalize()
    a, bb = rng.sample(FAKE_CREW, 2)
    chosen, used = {}, {}

    def resolve(m, per, depth):
        name, n = m.group(1), m.group(2) or "1"
        if name in per:
            return per[name]
        if name in flight:
            return flight[name]
        key = name + ":" + n
        if key in chosen:
            return chosen[key]
        vals = usable(name, pools, conditions)
        if not vals or depth >= 6:
            return "..."
        taken = used.setdefault(name, [])
        free = [v for v in vals if v not in taken] or vals
        raw = rng.choice(free)
        taken.append(raw)
        v = expand(raw, False, per, depth + 1)
        chosen[key] = v
        return v

    def expand(src, text_starts, per, depth):
        out, at = [], 0
        for m in SLOT.finditer(src):
            out.append(src[at:m.start()])
            v = resolve(m, per, depth)
            out.append(cap(v) if at_sentence_start(src, m.start(), text_starts) else v)
            at = m.end()
        out.append(src[at:])
        return "".join(out)

    res = []
    for i, r in enumerate(lines):
        per = {"self": a if i % 2 == 0 else bb, "other": bb if i % 2 == 0 else a}
        version = pick_version(r["english"], conditions, rng, pools)
        who = r["role"] if r["role"] != "crew" else per["self"]
        res.append((who, expand(version, True, per, 0), r))
    return res


def main():
    ap = argparse.ArgumentParser(description="Print random fills of the subtitle templates.")
    ap.add_argument("scenes", nargs="*", help="scene names (default: every scene with slots or versions)")
    ap.add_argument("-n", type=int, default=3, help="fills per scene (default 3)")
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--spanish", action="store_true", help="also print the spanish of each line")
    ap.add_argument("--phase", default="orbit", help="flight phase for [conditions] (default orbit)")
    ap.add_argument("--body", default="kerbin", help="body key for [kerbin] and [away] (default kerbin)")
    ap.add_argument("--has", default="solar,antenna,legs,ladder,chute,engine,heatshield,goo,thermometer",
                    help="vessel parts for [solar], [goo] and so on, comma separated")
    args = ap.parse_args()
    rows, errors, _ = b.load_sources()
    good, verr = b.validate(rows)
    pools, perr = b.load_pools()
    errors += verr + perr + b.check_slots(good, pools)
    if errors:
        for e in errors:
            print("ERROR " + e)
        sys.exit(1)
    scenes = {}
    for r in good:
        scenes.setdefault(r["scene"], []).append(r)
    want = args.scenes or [s for s, ls in scenes.items()
                           if any("|" in r["english"] or b.slot_names(r["english"]) for r in ls)]
    rng = random.Random(args.seed)
    for sc in want:
        if sc not in scenes:
            print("no scene %s" % sc)
            continue
        lines = sorted(scenes[sc], key=lambda r: r["seq"])
        print("== %s" % sc)
        for _ in range(args.n):
            conds = [args.phase, "kerbin" if args.body == "kerbin" else "away"] + [x for x in args.has.split(",") if x]
            for who, text, r in fill_scene(lines, pools, rng, conds, args.body):
                print("  %-10s %s" % (who + ":", text))
                if args.spanish:
                    print("  %-10s (%s)" % ("", r["spanish"]))
            print()


if __name__ == "__main__":
    main()
