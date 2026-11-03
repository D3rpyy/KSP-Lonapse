#!/usr/bin/env python3
"""Lonapse QA: checks a rendered build and writes a small review page for listening.

Run after build_lonapse.py, against a build folder (default audio/build):

    python3 -I audio/qa_lonapse.py [--build DIR] [--seed N] [--per-stratum N] [--jobs N]

It reads <build>/lonapse/phrases.tsv (columns by name, an old table without phase, cause, body, trait, persona or
topic still loads, the missing columns default to any)
and the clips <build>/Sounds/<folder>/<clip_key>_<m|f>.ogg, decodes every clip with ffmpeg and checks:

  errors (exit 1)
    - a clip the table needs is missing, or an OGG under Sounds/crew or Sounds/control is not in the table
    - duration out of bounds (callout 0.3 to 3.0 s, other 0.3 to 9 s, tuned on the v2 build)
    - seconds per letter outside 0.5 to 2.0 times the median of its voice and band group
      (texts of 10 letters or more, screams excepted)
    - peak above -1.0 dBFS, or clipped samples
    - internal silence longer than 0.8 s (1.0 s for panic)
    - gated RMS more than 2 dB from the build target
    - the same decoded PCM under two different clips
  warnings
    - the same checks with tighter limits (see LIMITS)

Writes:
    <build>/lonapse/qa_report.json      every clip with its measurements, errors and warnings
    <build>/lonapse/review/index.html   a stratified random sample with players and OK/Bad buttons
    <build>/lonapse/review/review.m3u   the same sample as a playlist

The sample holds --per-stratum clips per (source file, phase, band, role), plus --per-stratum clips per
(event, band, role, topic) for the plan v4 events (soi_enter, science, part_lost, power_low, power_ok,
signal_lost, signal_back, crew_lost) and per (trait, persona) for the tagged crew rows, so every new event and
every crew type gets clips on the page. It also holds every flagged clip and every clip whose decoded audio
changed since the previous qa_report.json. Verdicts live in the browser
(localStorage) and the Export button downloads them as a TSV.
"""

import argparse
import array
import hashlib
import html
import json
import math
import random
import statistics
import subprocess
import sys
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))  # -I drops the script folder from sys.path, build_lonapse.py lives next to this file
import build_lonapse as bl  # noqa: E402

SR = bl.SR
FRAME = bl.FRAME

# Hard limits give an error, soft limits a warning. Durations are in seconds, levels in dBFS.
LIMITS = {
    # v2 build data: crew callouts 0.34 to 0.92 s, Mission Control confirmations up to 2.73 s,
    # one-word replies down to 0.34 s, the longest line about 6 s.
    "dur_callout": (0.25, 3.0),   # countdown "tres" renders at 0.30 s
    "dur_callout_warn": (0.35, 2.5),
    "dur_other": (0.25, 9.0), "dur_other_warn": (0.45, 7.0),   # "Uy." renders at 0.28 s
    # Seconds per letter against the median of the voice and band group. Short texts carry a fixed
    # onset and the punctuation pauses, so under spl_min_letters only the wide warning range applies.
    # 2.5x: short replies with a sentence break ("Sí. Finjamos que no.") reach 2.2x and sound fine.
    "spl_ratio": (0.5, 2.5), "spl_ratio_warn": (0.65, 1.6),
    "spl_min_letters": 10, "spl_ratio_short_warn": (0.4, 3.0),
    "peak_max": -1.0, "peak_warn": bl.PEAK_CEILING_DB,
    "clip_level": 0.999,          # |sample| at or above this counts as clipped
    "silence_max": 0.8, "silence_max_panic": 1.0,
    "silence_warn": 0.55, "silence_warn_panic": 0.75,
    "silence_db": -45.0,          # a 10 ms frame under this (or 35 dB under the loudest frame) is a pause
    "silence_rel_db": 35.0,
    "rms_err": 2.0, "rms_warn": bl.RMS_TOL_DB,
    "min_group": 5,               # seconds per letter needs this many clips in a group, otherwise it is skipped
}


def read_built_table(path):
    """Rows of the built phrases.tsv as dicts, columns found by name. Missing phase, cause, trait, persona and topic
    default to any, a missing body to empty."""
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines:
        raise SystemExit("ERROR %s is empty" % path)
    header = lines[0].split("\t")
    need = ["clip_key", "event", "severity", "band", "role", "gender", "spanish", "lonapse_text", "folder", "english"]
    missing = [c for c in need if c not in header]
    if missing:
        raise SystemExit("ERROR %s lacks the columns %s" % (path, " ".join(missing)))
    rows = []
    for n, ln in enumerate(lines[1:], start=2):
        if not ln.strip():
            continue
        f = ln.split("\t")
        if len(f) != len(header):
            raise SystemExit("ERROR %s:%d has %d columns, header has %d" % (path, n, len(f), len(header)))
        d = dict(zip(header, f))
        d.setdefault("body", "")
        for c in V4_TAGS + ("phase", "cause"):
            d[c] = d.get(c) or "any"
        rows.append(d)
    return rows, header


V4_TAGS = ("trait", "persona", "topic")
NEW_EVENTS = tuple(getattr(bl, "NEW_EVENTS", ["soi_enter", "science", "part_lost", "power_low", "power_ok",
                                               "signal_lost", "signal_back", "crew_lost"]))


def strata_keys(c):
    """Every stratum a clip belongs to. A clip counts once per stratum, so the strata overlap on purpose."""
    keys = [("file", c["source"], c["phase"], c["band"], c["role"])]
    if c["event"] in NEW_EVENTS:
        keys.append(("event", c["event"], c["band"], c["role"], c["topic"]))
    if c["trait"] != "any" or c["persona"] != "any":
        keys.append(("crew_type", c["trait"], c["persona"]))
    return keys


def source_files():
    """clip_key -> source file name, from audio/lonapse/phrases/*.tsv. Empty when the sources cannot be read."""
    out = {}
    for p in sorted(bl.SOURCE_DIR.glob("*.tsv")):
        try:
            rows, _ = bl.read_tsv(p, bl.SOURCE_COLUMNS, p.name)
        except (OSError, UnicodeDecodeError):
            continue
        for r in rows:
            out.setdefault(r["clip_key"], p.name)
    return out


def kind_of(row, g):
    return "mc" if row["role"] == "mc" else g


def letters(text):
    return sum(1 for c in text if c.isalpha())


def analyse(path_str):
    """Worker: decode one OGG and measure it. Returns a dict (or an error string under "fail")."""
    p = subprocess.run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error", "-i", path_str, "-vn",
                        "-ac", "1", "-ar", str(SR), "-f", "f32le", "-acodec", "pcm_f32le", "pipe:1"],
                       stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode != 0:
        return {"fail": p.stderr.decode("utf-8", "replace")[-300:]}
    raw = p.stdout[: len(p.stdout) // 4 * 4]
    x = array.array("f")
    x.frombytes(raw)
    if sys.byteorder != "little":
        x.byteswap()
    n = len(x)
    if n == 0:
        return {"fail": "no samples"}
    peak = max(abs(v) for v in x)
    clipped = sum(1 for v in x if abs(v) >= LIMITS["clip_level"])
    powers = []
    for i in range(0, n - FRAME + 1, FRAME):
        s = 0.0
        for v in x[i:i + FRAME]:
            s += v * v
        powers.append(s / FRAME)
    frames_db = [10.0 * math.log10(q) if q > 1e-24 else -240.0 for q in powers]
    mx = max(powers) if powers else 0.0
    gate = max(10 ** (bl.SILENCE_DB / 10.0), mx * 1e-3)
    sel = [q for q in powers if q >= gate]
    grms = 10.0 * math.log10(sum(sel) / len(sel)) if sel else -240.0
    # longest pause between the first and the last loud frame
    loud_db = max(frames_db) if frames_db else -240.0
    thr = max(LIMITS["silence_db"], loud_db - LIMITS["silence_rel_db"])
    active = [i for i, v in enumerate(frames_db) if v > thr]
    longest = run = 0
    if active:
        for v in frames_db[active[0]:active[-1] + 1]:
            run = run + 1 if v <= thr else 0
            longest = max(longest, run)
    return {"duration_s": round(n / float(SR), 3), "peak_dbfs": round(20 * math.log10(peak), 2) if peak > 0 else -240.0,
            "clipped": clipped, "gated_rms_dbfs": round(grms, 2), "max_pause_s": round(longest * FRAME / float(SR), 2),
            "pcm_sha256": hashlib.sha256(raw).hexdigest()}


def check_clip(c, med):
    """Fills c["errors"] and c["warnings"] from the measurements. med: median seconds per letter per group."""
    e, w = c["errors"], c["warnings"]
    m = c["meas"]
    d = m["duration_s"]
    if c["severity"] == "callout":
        lo, hi = LIMITS["dur_callout"]
        wlo, whi = LIMITS["dur_callout_warn"]
    else:
        lo, hi = LIMITS["dur_other"]
        wlo, whi = LIMITS["dur_other_warn"]
    if d < lo or d > hi:
        e.append("duration %.2f s outside %.1f..%.1f" % (d, lo, hi))
    elif d < wlo or d > whi:
        w.append("duration %.2f s near the limit (%.1f..%.1f)" % (d, wlo, whi))
    ref = med.get(c["group"])
    if ref and c["spl"] is not None:
        r = c["spl"] / ref
        c["spl_ratio"] = round(r, 2)
        lo, hi = LIMITS["spl_ratio"]
        wlo, whi = LIMITS["spl_ratio_warn"]
        if c["letters"] < LIMITS["spl_min_letters"] or c["clip_key"].startswith("scream"):
            lo, hi = -1.0, 1e9
            wlo, whi = LIMITS["spl_ratio_short_warn"]
        if r < lo or r > hi:
            e.append("%.3f s per letter is %.2fx the median of %s (allowed %.2f..%.2f)" % (c["spl"], r, c["group"], lo, hi))
        elif r < wlo or r > whi:
            w.append("%.3f s per letter is %.2fx the median of %s" % (c["spl"], r, c["group"]))
    if m["peak_dbfs"] > LIMITS["peak_max"]:
        e.append("peak %.2f dBFS above %.1f" % (m["peak_dbfs"], LIMITS["peak_max"]))
    elif m["peak_dbfs"] > LIMITS["peak_warn"]:
        w.append("peak %.2f dBFS above %.1f" % (m["peak_dbfs"], LIMITS["peak_warn"]))
    if m["clipped"]:
        e.append("%d clipped sample(s)" % m["clipped"])
    panic = c["band"] == "panic"
    smax = LIMITS["silence_max_panic"] if panic else LIMITS["silence_max"]
    swarn = LIMITS["silence_warn_panic"] if panic else LIMITS["silence_warn"]
    if m["max_pause_s"] > smax:
        e.append("internal silence %.2f s, max %.1f" % (m["max_pause_s"], smax))
    elif m["max_pause_s"] > swarn:
        w.append("internal silence %.2f s" % m["max_pause_s"])
    err = abs(m["gated_rms_dbfs"] - bl.RMS_TARGET_DB)
    if err > LIMITS["rms_err"]:
        e.append("gated RMS %.2f dBFS, target %.1f +-%.1f" % (m["gated_rms_dbfs"], bl.RMS_TARGET_DB, LIMITS["rms_err"]))
    elif err > LIMITS["rms_warn"]:
        w.append("gated RMS %.2f dBFS, %.1f dB from the target" % (m["gated_rms_dbfs"], err))


def pick_sample(clips, per, seed, changed):
    """Stratified sample: per clips per stratum (strata_keys: file x phase x band x role, new event x band x role x
    topic, trait x persona), plus every flagged and every changed clip. A stratum that already holds per clips
    picked for another stratum gets no extra ones."""
    rng = random.Random(seed)
    strata = {}
    for c in clips:
        if c.get("meas"):
            for k in strata_keys(c):
                strata.setdefault(k, []).append(c)
    chosen = {}
    for k in sorted(strata):
        group = sorted(strata[k], key=lambda c: c["file"])
        have = sum(1 for c in group if c["file"] in chosen)
        rest = [c for c in group if c["file"] not in chosen]
        for c in rng.sample(rest, max(0, min(per - have, len(rest)))):
            chosen[c["file"]] = "sample"
    for c in clips:
        if not c.get("meas"):
            continue
        if c["file"] in changed:
            chosen[c["file"]] = "changed"
        if c["errors"] or c["warnings"]:
            chosen[c["file"]] = "error" if c["errors"] else "warning"
    order = {"error": 0, "warning": 1, "changed": 2, "sample": 3}
    out = [dict(c, why=chosen[c["file"]]) for c in clips if c["file"] in chosen]
    out.sort(key=lambda c: (order[c["why"]], c["source"], c["event"], c["phase"], c["band"], c["role"], c["file"]))
    return out, len(strata)


PAGE_CSS = """
:root { --bg:#fafafa; --fg:#1d1d1f; --muted:#666; --card:#fff; --line:#ddd; --ok:#1f7a3a; --bad:#b3261e;
        --warn:#8a5a00; --chip:#eee; }
@media (prefers-color-scheme: dark) {
  :root { --bg:#16171a; --fg:#e8e8ea; --muted:#9a9aa2; --card:#1f2024; --line:#33343a; --ok:#5ccf7d;
          --bad:#ff7b72; --warn:#e3b341; --chip:#2a2b30; }
}
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--fg); font:15px/1.45 system-ui, sans-serif; }
header { position:sticky; top:0; background:var(--bg); border-bottom:1px solid var(--line); padding:12px 16px; z-index:1; }
h1 { font-size:18px; margin:0 0 4px; }
.bar { display:flex; gap:8px; flex-wrap:wrap; align-items:center; color:var(--muted); font-size:13px; }
main { max-width:900px; margin:0 auto; padding:12px 16px 60px; }
.card { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:10px 12px; margin:10px 0; }
.card.ok { border-left:4px solid var(--ok); } .card.bad { border-left:4px solid var(--bad); }
.key { font-family:ui-monospace, monospace; font-size:13px; word-break:break-all; }
.en { font-size:16px; margin:4px 0 2px; } .lo { color:var(--muted); font-style:italic; }
.chips { display:flex; gap:4px; flex-wrap:wrap; margin:6px 0; }
.chip { background:var(--chip); border-radius:10px; padding:1px 8px; font-size:12px; }
.flag { color:var(--warn); font-size:13px; } .flag.err { color:var(--bad); }
audio { width:100%; margin:6px 0; }
.row { display:flex; gap:8px; align-items:center; flex-wrap:wrap; }
button { font:inherit; padding:4px 12px; border-radius:6px; border:1px solid var(--line); background:var(--chip); color:var(--fg); cursor:pointer; }
button.on.okb { background:var(--ok); color:#fff; } button.on.badb { background:var(--bad); color:#fff; }
input.note { flex:1; min-width:160px; font:inherit; padding:4px 8px; border-radius:6px; border:1px solid var(--line); background:var(--bg); color:var(--fg); }
"""

PAGE_JS = """
const KEY = 'lonapse-review:' + document.body.dataset.build;
let state = {};
try { state = JSON.parse(localStorage.getItem(KEY) || '{}'); } catch (e) { state = {}; }
function save() { try { localStorage.setItem(KEY, JSON.stringify(state)); } catch (e) {} count(); }
function count() {
  const v = Object.values(state);
  document.getElementById('tally').textContent =
    v.filter(x => x.v === 'ok').length + ' OK, ' + v.filter(x => x.v === 'bad').length + ' bad, ' +
    (document.querySelectorAll('.card').length - v.filter(x => x.v).length) + ' open';
}
function paint(card) {
  const s = state[card.dataset.file] || {};
  card.classList.toggle('ok', s.v === 'ok'); card.classList.toggle('bad', s.v === 'bad');
  card.querySelector('.okb').classList.toggle('on', s.v === 'ok');
  card.querySelector('.badb').classList.toggle('on', s.v === 'bad');
  card.querySelector('.note').value = s.n || '';
}
document.querySelectorAll('.card').forEach(card => {
  const f = card.dataset.file;
  const set = (v) => { state[f] = Object.assign(state[f] || {}, {v: (state[f] || {}).v === v ? '' : v}); save(); paint(card); };
  card.querySelector('.okb').onclick = () => set('ok');
  card.querySelector('.badb').onclick = () => set('bad');
  card.querySelector('.note').oninput = (ev) => { state[f] = Object.assign(state[f] || {}, {n: ev.target.value}); save(); paint(card); };
  paint(card);
});
document.getElementById('export').onclick = () => {
  const clean = (t) => String(t || '').replace(/[\\t\\r\\n]+/g, ' ');
  const lines = ['file\\tclip_key\\tverdict\\tnote'];
  document.querySelectorAll('.card').forEach(card => {
    const s = state[card.dataset.file] || {};
    lines.push([card.dataset.file, card.dataset.key, s.v || '', clean(s.n)].join('\\t'));
  });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([lines.join('\\n') + '\\n'], {type: 'text/tab-separated-values'}));
  a.download = 'lonapse_verdicts.tsv'; a.click();
};
document.getElementById('clear').onclick = () => {
  if (confirm('Clear all verdicts on this page?')) { state = {}; save(); document.querySelectorAll('.card').forEach(paint); }
};
count();
"""


def write_review(review_dir, sample, build_id, seed, nstrata):
    review_dir.mkdir(parents=True, exist_ok=True)
    esc = html.escape
    cards = []
    m3u = ["#EXTM3U"]
    for c in sample:
        rel = "../../Sounds/" + c["file"]
        m = c["meas"]
        chips = [c["source"], "phase " + c["phase"], "band " + c["band"], c["role"], "cause " + c["cause"],
                 c["event"], "voice " + c["kind"], "%.2f s" % m["duration_s"], "%.1f dB RMS" % m["gated_rms_dbfs"],
                 c["why"]]
        if c["body"]:
            chips.insert(1, "body " + c["body"])
        for tag in V4_TAGS:
            if c[tag] != "any":
                chips.insert(1, "%s %s" % (tag, c[tag]))
        flags = "".join('<div class="flag err">error: %s</div>' % esc(x) for x in c["errors"])
        flags += "".join('<div class="flag">warning: %s</div>' % esc(x) for x in c["warnings"])
        cards.append(
            '<div class="card" data-file="%s" data-key="%s">\n'
            '  <div class="key">%s</div>\n  <div class="en">%s</div>\n  <div class="lo">%s</div>\n'
            '  <div class="chips">%s</div>\n%s'
            '  <audio controls preload="none" src="%s"></audio>\n'
            '  <div class="row"><button class="okb">OK</button><button class="badb">Bad</button>'
            '<input class="note" placeholder="note"></div>\n</div>'
            % (esc(c["file"], True), esc(c["clip_key"], True), esc(c["file"]), esc(c["english"]),
               esc(c["lonapse_text"]), "".join('<span class="chip">%s</span>' % esc(x) for x in chips), flags,
               esc(rel, True)))
        m3u.append("#EXTINF:%d,%s - %s" % (round(m["duration_s"]), c["clip_key"], c["english"]))
        m3u.append(rel)
    page = ("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">"
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
            "<title>Lonapse review</title><style>%s</style></head>\n<body data-build=\"%s\">\n"
            "<header><h1>Lonapse review</h1><div class=\"bar\"><span>%d clips (seed %d, %d strata, flagged first)</span>"
            "<span id=\"tally\"></span><button id=\"export\">Export TSV</button><button id=\"clear\">Clear</button>"
            "</div></header>\n<main>\n%s\n</main>\n<script>%s</script>\n</body></html>\n"
            % (PAGE_CSS, esc(build_id, True), len(sample), seed, nstrata, "\n".join(cards), PAGE_JS))
    (review_dir / "index.html").write_text(page, encoding="utf-8")
    (review_dir / "review.m3u").write_text("\n".join(m3u) + "\n", encoding="utf-8")


def main():
    ap = argparse.ArgumentParser(description="Check a rendered Lonapse build and write a review page.")
    ap.add_argument("--build", default=str(HERE / "build"), help="build folder with lonapse/ and Sounds/ (default audio/build)")
    ap.add_argument("--seed", type=int, default=1, help="seed of the random sample (default 1)")
    ap.add_argument("--per-stratum", type=int, default=2, help="clips per stratum: file x phase x band x role, new event x band x role x topic, trait x persona (default 2)")
    ap.add_argument("--jobs", type=int, default=8, help="parallel decode processes (default 8)")
    args = ap.parse_args()

    build = Path(args.build).resolve()
    table = build / "lonapse" / "phrases.tsv"
    sounds = build / "Sounds"
    report_path = build / "lonapse" / "qa_report.json"
    if not table.exists():
        print("ERROR no built table at %s, run build_lonapse.py first" % table)
        return 1
    rows, header = read_built_table(table)
    old_format = not all(c in header for c in ("phase", "cause", "body") + V4_TAGS)
    src_of = source_files()

    clips = []
    for r in rows:
        for _, g, kind in bl.clip_jobs([r]):
            base = r["clip_key"].split(bl.BODY_SEP)[0]
            n = letters(r["lonapse_text"])
            clips.append(dict(file="%s/%s_%s.ogg" % (r["folder"], r["clip_key"], g), clip_key=r["clip_key"], gender=g,
                              kind=kind, role=r["role"], event=r["event"], severity=r["severity"], band=r["band"],
                              phase=r["phase"], cause=r["cause"], body=r["body"], english=r["english"],
                              trait=r["trait"], persona=r["persona"], topic=r["topic"],
                              lonapse_text=r["lonapse_text"], source=src_of.get(base, "unknown"), letters=n,
                              group="%s/%s" % (kind, "callout" if r["severity"] == "callout" else r["band"]),
                              errors=[], warnings=[], meas=None, spl=None))
    expected = {c["file"] for c in clips}
    on_disk = set()
    for top in bl.OWNED:
        d = sounds / top
        if d.exists():
            on_disk |= {str(p.relative_to(sounds)) for p in d.rglob("*.ogg")}
    missing = sorted(expected - on_disk)
    orphans = sorted(on_disk - expected)
    global_errors = ["orphan OGG not in the table: %s" % f for f in orphans]

    todo = [c for c in clips if c["file"] in on_disk]
    with ProcessPoolExecutor(max_workers=max(1, args.jobs)) as pool:
        results = list(pool.map(analyse, [str(sounds / c["file"]) for c in todo], chunksize=4))
    for c, res in zip(todo, results):
        if "fail" in res:
            c["errors"].append("cannot decode: %s" % res["fail"].strip())
            continue
        c["meas"] = res
        if c["letters"]:
            c["spl"] = round(max(0.0, res["duration_s"] - 2 * bl.PAD_S) / c["letters"], 4)
    for c in clips:
        if c["file"] in missing:
            c["errors"].append("OGG file is missing")

    groups = {}
    for c in clips:
        if c["spl"] is not None:
            groups.setdefault(c["group"], []).append(c["spl"])
    med = {g: statistics.median(v) for g, v in groups.items() if len(v) >= LIMITS["min_group"]}
    for c in clips:
        if c["meas"]:
            check_clip(c, med)

    by_hash = {}
    for c in clips:
        if c["meas"]:
            by_hash.setdefault(c["meas"]["pcm_sha256"], []).append(c)
    for h, same in by_hash.items():
        if len(same) > 1:
            names = [c["file"] for c in same]
            for c in same:
                c["errors"].append("identical PCM as %s" % ", ".join(n for n in names if n != c["file"]))

    changed = set()
    if report_path.exists():
        try:
            prev = json.loads(report_path.read_text(encoding="utf-8"))
            prev_hash = {c["file"]: (c.get("meas") or {}).get("pcm_sha256") for c in prev.get("clips", [])}
            for c in clips:
                if c["meas"] and c["file"] in prev_hash and prev_hash[c["file"]] != c["meas"]["pcm_sha256"]:
                    changed.add(c["file"])
                elif c["meas"] and c["file"] not in prev_hash:
                    changed.add(c["file"])
        except (OSError, ValueError, KeyError, TypeError):
            print("WARNING previous qa_report.json could not be read, no change detection")

    sample, nstrata = pick_sample(clips, args.per_stratum, args.seed, changed)
    build_id = hashlib.sha256(str(build).encode("utf-8")).hexdigest()[:12]
    write_review(build / "lonapse" / "review", sample, build_id, args.seed, nstrata)

    n_err = sum(1 for c in clips if c["errors"]) + len(global_errors)
    n_warn = sum(1 for c in clips if c["warnings"] and not c["errors"])
    durs = [c["meas"]["duration_s"] for c in clips if c["meas"]]
    report = {
        "build": str(build), "table": str(table), "old_table_format": old_format, "seed": args.seed,
        "limits": LIMITS, "rms_target_dbfs": bl.RMS_TARGET_DB,
        "summary": {"rows": len(rows), "clips_expected": len(expected), "clips_on_disk": len(on_disk),
                    "missing": len(missing), "orphans": len(orphans), "clips_with_errors": n_err,
                    "clips_with_warnings_only": n_warn, "changed_since_last": len(changed),
                    "review_sample": len(sample), "total_audio_s": round(sum(durs), 1)},
        "spl_median": {g: round(v, 4) for g, v in sorted(med.items())},
        "global_errors": global_errors,
        "clips": clips,
    }
    report_path.write_text(json.dumps(report, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    print("Lonapse QA of %s%s" % (build, " (old table format: missing phase, cause, body, trait, persona or topic default)" if old_format else ""))
    print("Rows %d, clips expected %d, on disk %d, missing %d, orphans %d, audio %.1f min"
          % (len(rows), len(expected), len(on_disk), len(missing), len(orphans), sum(durs) / 60.0))
    print("Median seconds per letter: " + ", ".join("%s %.3f" % (g, v) for g, v in sorted(med.items())))
    for g in global_errors[:10]:
        print("ERROR " + g)
    shown = 0
    for c in clips:
        if c["errors"] and shown < 25:
            print("ERROR %s: %s" % (c["file"], "; ".join(c["errors"])))
            shown += 1
    if n_err > shown + len(global_errors[:10]):
        print("... more errors in the report")
    print("Clips with errors: %d, with warnings only: %d, changed since last run: %d"
          % (n_err, n_warn, len(changed)))
    print("Wrote %s" % report_path)
    print("Review page: %s (%d clips from %d strata)" % (build / "lonapse" / "review" / "index.html", len(sample), nstrata))
    return 1 if n_err else 0


if __name__ == "__main__":
    sys.exit(main())
