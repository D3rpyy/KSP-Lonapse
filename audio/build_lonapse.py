#!/usr/bin/env python3
"""Lonapse clip builder (reversed spelling only).

Reads the source phrase tables audio/lonapse/phrases/*.tsv (in sorted order) and the body names in
audio/lonapse/bodies.tsv, validates them, expands {body} rows into one variant per body, computes lonapse_text
(each letter run reversed after NFC, see audio/lonapse/README.md), speaks that text with Piper
(neural TTS, one voice per role and gender, see VOICES and DELIVERY below), shifts it up into the
Kerbal register, and writes OGG Vorbis clips into the merged Sounds/ tree, in this folder layout:

    audio/build/Sounds/crew/<calm|tense|panic|relief|callout>/<clip_key>_<m|f>.ogg
    audio/build/Sounds/control/<calm|tense>/<clip_key>_m.ogg

Subtitle-only rows (role probe) have an empty spanish and get no clip. They are only in phrases.tsv,
the plugin shows their english and plays a probe beep for them.

This script owns Sounds/crew and Sounds/control. Sounds/beeps and most of Sounds/sfx belong to build_clips.py (the docking effects in Sounds/sfx belong to build_sfx.py).

Usage:
    python3 -I audio/build_lonapse.py [--clean] [--verify-only] [--validate-only] [--fetch-voices] [--jobs N]
                                      [--piper-python PATH] [--voice-m V] [--voice-f V] [--voice-mc V]
    python3 -I audio/build_lonapse.py --try KEY [KEY ...] --out DIR     render a few clips into DIR, with timing

Also written (under audio/build/lonapse/):
    phrases.tsv      the expanded source table plus lonapse_text, folder and body (installed next to the clips)
    manifest.json    per clip measurements, voices and tool versions
    cache/           rendered clips keyed by text, voice and delivery. Only clips whose key changed are rendered again.

Piper runs from a separate Python environment. Install it once:
    python3 -m venv audio/.venv && audio/.venv/bin/pip install piper-tts
Voice models live in audio/voices/ (not in git). --fetch-voices downloads them and checks the sha256.

Nothing is installed into the game. Run with -I so nothing from the current directory can be imported.
Piper output is not bit-reproducible between runs (the model samples noise), so a rebuild gives new
clips with the same text, voice and levels.
"""

import argparse
import array
import hashlib
import json
import math
import random
import re
import shutil
import os
import subprocess
import sys
import time
import unicodedata
import urllib.request
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SOURCE_DIR = ROOT / "lonapse" / "phrases"
BODIES_FILE = ROOT / "lonapse" / "bodies.tsv"
POOLS_FILE = ROOT / "lonapse" / "pools.tsv"   # experimental (procedural talk): subtitle slot values
BUILD = ROOT / "build" / "lonapse"
PHRASES_OUT = BUILD / "phrases.tsv"
POOLS_OUT = BUILD / "pools.tsv"
MANIFEST = BUILD / "manifest.json"
CACHE = BUILD / "cache"
# Bump when the render chain changes in a way the cache key does not see (filters, gasp model, trim rule).
RENDER_VERSION = 1
SOUNDS = ROOT / "build" / "Sounds"          # the merged tree that ships
OWNED = ("crew", "control")                 # top folders of SOUNDS this script owns
LEGACY_FLAT = BUILD / "Sounds"              # old flat output, removed on build
VOICES_DIR = ROOT / "voices"
DEFAULT_PIPER_PY = ROOT / ".venv" / "bin" / "python"

# Piper voices (rhasspy/piper-voices on Hugging Face). Licenses from each model card.
# Gender is an estimate from the median f0 of a test sentence (claude about 185 Hz, ald about 152 Hz,
# davefx about 104 Hz). Confirm by ear.
HF = "https://huggingface.co/rhasspy/piper-voices/resolve/main/"
VOICES = {
    "es_ES-davefx-medium": dict(path="es/es_ES/davefx/medium", license="CC0 (dataset), per model card",
                                onnx="6658b03b1a6c316ee4c265a9896abc1393353c2d9e1bca7d66c2c442e222a917",
                                json="0e0dda87c732f6f38771ff274a6380d9252f327dca77aa2963d5fbdf9ec54842"),
    "es_MX-claude-high": dict(path="es/es_MX/claude/high", license="Apache-2.0 (dataset), per model card",
                              onnx="3ef40a71ea63852cd8ab7e6fa7d2ecdcfa67a0b47c9c48e3f10e02ee02083ea0",
                              json="1afc81f703c0e4cb3b4d7c0dca096b8b54a98806807f0170cf5eb5557723c12d"),
    "es_MX-ald-medium": dict(path="es/es_MX/ald/medium", license="Unlicense (dataset), per model card",
                             onnx="019b3803293c93e34a206dd2e53a3889209a514e786fd7144f7b70196c579b63",
                             json="5a71498158e04afc8099bfd019c7e87c68eb9d042505a2b1a87e5c1ac2b1a61d"),
}

# Voice and delivery per speaker kind. Tune these by ear, none of them were listened to.
# pitch_st: shift up into the Kerbal register.
# (asetrate, so formants move up too, which gives the small, bright Kerbal sound). Piper renders slower by
# the same ratio, so the tempo after the shift is the natural one times the band tempo below.
DELIVERY = {
    "m":  dict(voice="es_ES-davefx-medium", pitch_st=5.0),   # male crew
    "f":  dict(voice="es_MX-claude-high", pitch_st=4.0),     # female crew
    "mc": dict(voice="es_MX-ald-medium", pitch_st=3.0),      # Mission Control (was 2.0, sounded flat)
}
# Delivery kinds that speak with a fixed voice like Mission Control (calm in a panic, see mc_panic)
MC_LIKE_KINDS = ("mc",)
# Mission Control outside a panic and the countdown, so it does not sound flat. Extra tempo and Piper
# variation (noise scale and noise width, None keeps the Piper default 0.667 and 0.8). The plugin adds the
# controllers on shift (DcMath.McPitch). Picked by ear from four test renders: +3 st, tempo 1.04 and
# noise 0.80 / 0.90 ("mild"). Stronger settings went toward the crew sound.
MC_LIVELY_TEMPO = 1.04
MC_LIVELY_NOISE_SCALE = 0.80
MC_LIVELY_NOISE_W = 0.90
# Per band (and for callouts): tempo factor (>1 faster) and extra pitch in semitones.
BAND_DELIVERY = {
    "calm":    dict(tempo=1.00, pitch_st=0.0),
    "none":    dict(tempo=1.00, pitch_st=0.0),
    # crew tense and panic: only a bit faster, no extra pitch. Panic was +3 and later +1 semitones, tense +0.7.
    # On top of the Kerbal shift that still sounded like chipmunks. The stress comes from the lines and the
    # delivery (pieces, gasps, cracks below).
    "tense":   dict(tempo=1.10, pitch_st=0.0),
    "panic":   dict(tempo=1.10, pitch_st=0.0),
    # Mission Control during a panic: a bit slower and calm, the contrast. The small lift keeps it from sounding
    # deeper than the calm delivery (the old 0.92 tempo plus the runtime pitch drop sounded too deep).
    "mc_panic": dict(tempo=0.95, pitch_st=0.3),
    "callout": dict(tempo=1.08, pitch_st=0.5),
    # Launch countdown (event countdown): the plain Mission Control register (no callout lift), a calm tempo just
    # quick enough that the longest number ("Oknis.", about 0.85 s) fits its 1 s slot.
    "countdown": dict(tempo=1.05, pitch_st=0.0),
}
PIPER_SR = 22050

# Launch countdown (event countdown). Every number has a 1 s slot (LaunchCountdown.step in C#), so the render must
# be short, clean and the same every time. Two fixes against muffled numbers:
# 1. Spoken text: espeak-ng splits a word where a small letter is followed by a capital and says the capital as a
#    separate word, mostly as a letter name. "eteiS" became "etei ese", "eveuN" "eveu ene", and "ohcO" became
#    "ok", a pause and a breathy "o" (1.4 s, cut by the next number). Piper gets the countdown text with only the
#    first letter as a capital ("Eteis", "Eveun", "Ohco"). lonapse_text in phrases.tsv stays ReverseText(spanish).
# 2. Lower Piper noise: steadier vowels, less breath, less variation between the numbers.
COUNTDOWN_EVENT = "countdown"
COUNTDOWN_NOISE_SCALE = 0.40
COUNTDOWN_NOISE_W = 0.50

SR = 44100
FRAME = 441                  # 10 ms at 44.1 kHz
SILENCE_DB = -50.0
RMS_TARGET_DB = -20.0
RMS_TOL_DB = 1.0
PEAK_CEILING_DB = -1.3       # contract: peak at or under -1.3 dBFS (checked on the decoded OGG)
PEAK_BUILD_DB = -1.9         # build-side cap, leaves room for the Vorbis round trip (decoded peaks overshoot by a few tenths)
FADE_S = 0.008
PAD_S = 0.015                # leading and trailing silence, the fade ramp adds frames under -50 dB, max 50 ms in total
MAX_SILENCE_S = 0.050
OGG_QUALITY = "3"            # libvorbis -q:a 3, mono (neural voices have more air and detail than espeak)

# Columns are found by name. The source files must have every SOURCE_COLUMNS column (any order) and may have the
# SOURCE_OPTIONAL columns (trait, persona, topic), which default to their value here when absent. The built
# table is written in BUILT_COLUMNS order, english and spanish_rt last. The C# loader (PhraseTable.Parse) also finds columns by name.
ENGLISH_COLUMN = "english"   # subtitle text
SOURCE_COLUMNS = ["clip_key", "event", "severity", "band", "role", "scene", "seq", "gender", "phase", "cause",
                  "spanish", ENGLISH_COLUMN]
# spanish_rt (runtime voice): one Spanish version per english version, split by |, said by Piper in game. An empty
# version is said by the recorded clip. Not part of render_key: it never changes a rendered clip.
SPANISH_RT_COLUMN = "spanish_rt"
SOURCE_OPTIONAL = {"trait": "any", "persona": "any", "topic": "any", "core": "any", "beep": "", SPANISH_RT_COLUMN: ""}
BUILT_COLUMNS = ["clip_key", "event", "severity", "band", "role", "scene", "seq", "gender", "phase", "cause",
                 "trait", "persona", "topic", "core", "beep", "spanish", "lonapse_text", "folder", "body",
                 ENGLISH_COLUMN, SPANISH_RT_COLUMN]
BODY_COLUMNS = ["key", "spanish", "english"]
PHASES = ["any", "prelaunch", "launch", "ascent", "orbit", "coast", "reentry", "descent", "landed", "blackout",
          "serious", "goofy"]
SERIOUS_PHASES = ["launch", "ascent", "reentry", "descent"]
GOOFY_PHASES = ["prelaunch", "orbit", "coast", "landed"]
CAUSES = ["any", "spin", "gforce", "q", "ground", "heat", "plasma", "fuel", "impulse", "engine_out"]
# 17 stock bodies (lowercase bodyName, as FlightSample.body gives them) plus generic for everything else
STOCK_BODIES = ["kerbol", "moho", "eve", "gilly", "kerbin", "mun", "minmus", "duna", "ike", "dres", "jool",
                "laythe", "vall", "tylo", "bop", "pol", "eeloo"]
GENERIC_BODY = "generic"
# {body} scenes that make no sense at home ("you can't take Kerbin home"): no kerbin variant, so the game skips them
# at Kerbin (PhraseScene.PlaysAt, it does not fall back to generic there)
HOME_BODY = "kerbin"
NOT_AT_HOME = {"bo_souvenir", "bo_greetings", "bo_kids"}
BODY_SEP = "__"              # variant clip_key = <clip_key>__<body key>
PLACEHOLDER_RE = re.compile(r"\{([^{}]*)\}")
# Experimental (procedural talk): subtitle slots in the english only, filled in game (SubtitleSlots in C#).
# {pool} or {pool:2} from pools.tsv, a flight value, or (intercom only) a kerbal's first name.
SLOT_RE = re.compile(r"^([a-z_]+)(?::([1-9]))?$")
# The slot catalogue. Same lists as Slots.Numbers, Slots.Names and Slots.Persons in src/Slots.cs (the tests
# compare them). Every one of them is reserved: it cannot be a pool name.
NUMBER_SLOTS = ["alt_km", "pe_km", "ap_km", "speed", "fuel", "day", "sci", "funds",
                "inc_deg", "period_min", "t_ap_min", "t_pe_min", "ec", "mono", "parts", "crew", "node_dv", "node_min",
                "funds_total", "sci_total", "rep", "contracts", "dist_m", "close_ms", "height_m", "vspeed_ms",
                "at_home", "flight_no", "missions", "stars", "visits", "landings", "in_warp", "node_h", "soi_h",
                "sci_left", "sci_first", "con_sci", "con_rep", "sunrises", "eva_mins", "ship_m", "jet", "on_eva", "pair_m",
                "link", "apart", "a_eva", "b_eva", "has_solar", "has_rcs", "has_legs", "is_tourist",
                "temp_c", "press_kpa", "grav_g", "sci_flight", "exp_runs", "exp_count", "data_sent"]
NAME_SLOTS = ["here", "vessel", "body", "exp", "target", "next", "biome", "flag", "my_ship", "their_ship", "device", "contract", "exp_list", "probe"]
RUNTIME_PERSONS = ["other", "self", "kerbal"]
# On every flight, also without the runtime voice (Slots.Flight)
FLIGHT_SLOTS = ["day", "here", "vessel", "alt_km", "probe"]
# Numbers only the runtime voice can say: a version that uses one needs its spanish_rt (Slots.IsRuntimeNumber)
RUNTIME_NUMBERS = [n for n in NUMBER_SLOTS if n not in FLIGHT_SLOTS]
# In a subtitle template, intercom only (SubtitleSlots.PersonSlots)
PERSON_SLOTS = ["other", "self"]
RESERVED_SLOTS = set(NUMBER_SLOTS) | set(NAME_SLOTS) | set(RUNTIME_PERSONS)
POOL_COLUMNS = ["pool", "english"]
POOL_OPTIONAL = {"spanish": ""}   # runtime voice: the Spanish of the value, m: f: mp: fp: before it for the article
POOL_GENDERS = ["m", "f", "mp", "fp"]

# Panic crew delivery: broken pieces with a ragged pause in between (gasps off), Piper with more
# variation (noise), light drive, and now and then a voice crack. All random choices are seeded per clip.
PANIC_NOISE_SCALE = 0.90
PANIC_NOISE_W = 1.10
PANIC_DRIVE = 2.2            # tanh drive for the shouted sound
PANIC_CRACK_CHANCE = 0.35    # chance per piece that the end of the piece cracks upward
PANIC_CRACK_ST = 3.0
PANIC_GASP_CHANCE = 0.0      # chance of a gasp between two pieces (otherwise a short ragged pause). Was 0.7, the
                             # synthetic gasp sounded like a swoosh. The draw stays, so the pieces
                             # keep the same random choices as the approved test render.
# Speaker trait, speaker persona and topic. Same values as PhraseTable.Traits, Personas, ScienceTopics and
# SoiTopics in C#. topic is any on every event except science, soi_enter and staging, probe_milestone, probe_rover.
# tourist_pair (two tourists) and to_tourist (crew to a tourist) are for intercom scenes only. Same as Persona in C#.
TRAITS = ["any", "pilot", "engineer", "scientist", "tourist", "to_tourist", "tourist_pair"]
PERSONAS = ["any", "badass", "brave", "nervous", "dim", "veteran"]
TOPICS_BY_EVENT = {
    "science": ["any", "crewreport", "evareport", "mysterygoo", "surfacesample", "mobilematerialslab",
                "temperaturescan", "barometerscan", "seismicscan", "gravityscan", "atmosphereanalysis",
                "asteroidsample", "cometsample", "infraredtelescope", "magnetometer", "roc"],
    "soi_enter": ["any", "home"],
    # Probe staging, milestones and rover moments. Same as StagingTopics, MilestoneTopics, RoverTopics in C#.
    "staging": ["any", "separation", "ignition"],
    "probe_milestone": ["any", "day1", "day10", "day100", "day365", "orbit", "landing"],
    "probe_rover": ["any", "fast", "air", "tilt", "idle"],
    # probe_crew staging: a tourist calls the stage, the probe reacts. Same as PhraseTable.ProbeCrewTopics in C#.
    "probe_crew": ["any", "staging"],
    # Scenes about solar panels only play on a ship that has one. Chatter also has prechute (a chute still
    # packed, none open), chuteopen and heatshield. Same as PhraseTable.ChatterTopics in C#.
    "chatter": ["any", "solar", "prechute", "chuteopen", "heatshield"],
    "power_low": ["any", "solar"],
    "power_ok": ["any", "solar"],
}
# Explosion words are only allowed in part_lost rows. Matched at the start of a word, case-insensitive.
# "explor..." (explore) is no explosion. "blew" only counts as "blew up|apart|out|off" ("the intern blew away" is
# wind). "blast off" and "blastoff" are launch talk. Must match PhraseTable.HasExplosionWord in C#.
EXPLOSION_EVENT = "part_lost"
EXPLOSION_RE = re.compile(r"\b(?:explo(?!r)|boom|blew\s+(?:up|apart|out|off)\b|blast(?!\s*-?\s*off)|explot|estall)",
                          re.IGNORECASE)
LONAPSE_COLUMN = "lonapse_text"
FOLDER_COLUMN = "folder"
EVENTS = ["chatter", "staging", "chute_semi", "chute_full", "throttle_full", "throttle_cut",
          "dock_approach", "dock_close", "dock_hard", "docked", "touchdown", "splashdown", "relief",
          "plan_node", "burn_soon", "station_check", "eva_check", "eva_fall", "countdown",
          # game events
          "soi_enter", "science", "part_lost", "power_low", "power_ok", "signal_lost", "signal_back", "crew_lost",
          # probe mode
          "probe_chat", "probe_sleepy",
          # probe mode: the probe calls Mission Control first
          "probe_hail", "probe_stress", "probe_say_again", "probe_milestone", "probe_rover", "probe_data",
          # Probe and crew on one vessel, in the cabin
          "probe_crew"]
# Newer events: allowed, but not yet held to MIN_PER_EVENT. A shortfall is a warning.
NEW_EVENTS = ["soi_enter", "science", "part_lost", "power_low", "power_ok", "signal_lost", "signal_back", "crew_lost",
              "probe_chat", "probe_sleepy", "probe_hail", "probe_stress", "probe_say_again", "probe_milestone",
              "probe_rover", "probe_data", "probe_crew"]
# Optional events: no minimum at all, not even a warning.
OPTIONAL_EVENTS = []
# Same as PhraseTable.ProbeEvents and PhraseTable.ProbeVariantEvents in C#. Probe events only play in probe
# mode and have no crew rows. The variant events are existing events whose probe scenes carry a core or a probe-mode
# role (is_probe_scene).
PROBE_EVENTS = ["probe_chat", "probe_sleepy", "probe_hail", "probe_stress", "probe_say_again", "probe_milestone",
                "probe_rover", "probe_data"]
# A crewed vessel with a probe core. Probe and crew rows, no mc rows, never in a serious phase. Same as
# PhraseTable.ProbeCrewEvent in C#.
PROBE_CREW_EVENT = "probe_crew"
PROBE_VARIANT_EVENTS = ["science", "power_low", "power_ok", "soi_enter", "part_lost", "signal_lost", "signal_back",
                        "touchdown", "splashdown", "docked", "burn_soon", "staging", "chute_semi", "chute_full",
                        "throttle_full", "throttle_cut", "plan_node"]
# Core and beep columns. Same as PhraseTable.Cores (any plus ProbeCores.Keys) and ProbeCores.Moods in C#.
CORES = ["any", "stayputnik", "okto", "hecs", "rovemate", "snob", "qbe", "mk2drone", "mpo", "mtm", "generic"]
MOODS = ["happy", "offended", "curious", "scared", "sleepy", "proud", "bored"]
SEVERITIES = ["chatter", "callout", "warning"]
BANDS = ["calm", "tense", "panic", "none"]
ROLES = ["crew", "mc", "probe"]
# Rows without a clip (empty spanish, english required) and the roles that only exist in probe mode. In probe mode
# only Mission Control and the probe talk.
SUBTITLE_ONLY_ROLES = ["probe"]
PROBE_ROLES = ["probe"]
# A probe subtitle is short and the same form everywhere: "[beeps excitedly]". Same as
# PhraseTable.ProbeSubtitleOk in C#.
PROBE_SUBTITLE_RE = re.compile(r"^\[beeps( [a-z]+){1,3}\]$")
GENDERS = ["m", "f", "both"]
MIN_PER_EVENT = 2
MIN_CHATTER_PER_BAND = 3
CHATTER_BANDS = ["calm", "tense", "panic"]

KEY_RE = re.compile(r"^[a-z0-9_]+$")


class BuildError(Exception):
    pass


def log(msg=""):
    print(msg, flush=True)


# ---------------------------------------------------------------------------
# Lonapse text (must match PhraseTable.cs: Lonapse.ReverseText)
# ---------------------------------------------------------------------------
def is_mark(c):
    return unicodedata.category(c) in ("Mn", "Mc", "Me")


def is_letter_run(c):
    cat = unicodedata.category(c)
    return cat[0] == "L" or cat == "Nd" or is_mark(c)


def reverse_word(word):
    s = unicodedata.normalize("NFC", word)
    clusters = []
    for c in s:
        if not is_mark(c) or not clusters:
            clusters.append(c)
        else:
            clusters[-1] += c
    return "".join(reversed(clusters))


def reverse_text(text):
    s = unicodedata.normalize("NFC", text)
    out = []
    i = 0
    while i < len(s):
        if not is_letter_run(s[i]):
            out.append(s[i])
            i += 1
            continue
        j = i
        while j < len(s) and is_letter_run(s[j]):
            j += 1
        out.append(reverse_word(s[i:j]))
        i = j
    return "".join(out)


# ---------------------------------------------------------------------------
# Table loading and validation
# ---------------------------------------------------------------------------
def read_tsv(path, want_columns, label, optional=None):
    """Reads one tab separated file. Columns are found by header name. Returns (rows, errors).
    Each row is a dict with the wanted columns plus "loc" ("phrases/launch.tsv:12"). Comment lines start with #.
    optional maps column name to default: the column may be in the header, and a row gets the default when it is not
    (or when the cell is empty). Default: SOURCE_OPTIONAL for want_columns SOURCE_COLUMNS, else none."""
    if optional is None:
        optional = SOURCE_OPTIONAL if want_columns is SOURCE_COLUMNS else {}
    errors = []
    rows = []
    header = None
    for lineno, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        line = raw.rstrip("\r")
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        loc = "%s:%d" % (label, lineno)
        fields = [f.strip() for f in line.split("\t")]
        if header is None:
            missing = [c for c in want_columns if c not in fields]
            extra = [c for c in fields if c not in want_columns and c not in optional]
            dup = sorted({c for c in fields if fields.count(c) > 1})
            if missing or extra or dup:
                errors.append("%s: header needs the columns %s%s (missing %s, unknown %s, twice %s)"
                              % (loc, " ".join(want_columns),
                                 " and may have %s" % " ".join(optional) if optional else "", missing, extra, dup))
                return rows, errors
            header = fields
            continue
        if len(fields) == len(header) - 1 and header[-1] in optional:
            fields.append("")   # editors drop the tab before an empty last cell
        if len(fields) != len(header):
            errors.append("%s: expected %d columns, got %d" % (loc, len(header), len(fields)))
            continue
        row = dict(zip(header, fields))
        for c, d in optional.items():
            if not row.get(c):
                row[c] = d
        row["loc"] = loc
        rows.append(row)
    if header is None:
        errors.append("%s: no header line found" % label)
    return rows, errors


def load_bodies(path=BODIES_FILE):
    """Body names for {body}. Returns ({key: (spanish, english)} in file order, errors)."""
    if not path.exists():
        raise BuildError("body table not found: %s" % path)
    rows, errors = read_tsv(path, BODY_COLUMNS, path.name)
    bodies = {}
    for r in rows:
        bad = []
        if not KEY_RE.match(r["key"]):
            bad.append("key must match [a-z0-9_]+")
        if r["key"] in bodies:
            bad.append("duplicate key")
        if not r["spanish"] or not r["english"]:
            bad.append("spanish and english must not be empty")
        if PLACEHOLDER_RE.search(r["spanish"] + r["english"]):
            bad.append("a body name cannot hold a {placeholder}")
        if bad:
            errors.append("%s (%s): %s" % (r["loc"], r["key"], "; ".join(bad)))
            continue
        bodies[r["key"]] = (r["spanish"], r["english"])
    for k in STOCK_BODIES + [GENERIC_BODY]:
        if k not in bodies and not any(k in e for e in errors):
            errors.append("%s: body '%s' is missing" % (path.name, k))
    return bodies, errors


def load_sources(src_dir=SOURCE_DIR):
    """Reads every phrases/*.tsv in sorted order. Returns (rows, errors). Rows keep "loc" and "file"."""
    files = sorted(src_dir.glob("*.tsv"))
    if not files:
        raise BuildError("no source tables in %s" % src_dir)
    rows, errors = [], []
    for p in files:
        r, e = read_tsv(p, SOURCE_COLUMNS, "phrases/" + p.name)
        for row in r:
            row["file"] = p.name
        rows += r
        errors += e
    return rows, errors, files


def validate(rows):
    """Checks every source row and the rules across all files. Returns (good rows, errors).
    Rows of a scene must share event, severity, band, phase, cause, trait, persona, topic and core, and the scene must live
    in one file. topic is any except on science and soi_enter. Explosion words only in part_lost.
    seq numbers of a scene run 1..n. {body} is the only placeholder, and it is in both spanish and english. Same rules as PhraseTable.Parse
    in C#."""
    errors = []
    good = []
    first_key = {}
    scene_file = {}
    scene_head = {}
    scene_seqs = {}
    for row in rows:
        bad = []
        key = row["clip_key"]
        loc = row["loc"]
        if not KEY_RE.match(key):
            bad.append("clip_key must match [a-z0-9_]+")
        elif BODY_SEP in key:
            bad.append("clip_key cannot hold '%s' (reserved for body variants)" % BODY_SEP)
        if key in first_key:
            bad.append("duplicate clip_key, first at %s" % first_key[key])
        if row["event"] not in EVENTS:
            bad.append("unknown event '%s'" % row["event"])
        if row["severity"] not in SEVERITIES:
            bad.append("unknown severity '%s'" % row["severity"])
        if row["band"] not in BANDS:
            bad.append("unknown band '%s'" % row["band"])
        if row["role"] not in ROLES:
            bad.append("unknown role '%s'" % row["role"])
        if row["gender"] not in GENDERS:
            bad.append("unknown gender '%s'" % row["gender"])
        if row["phase"] not in PHASES:
            bad.append("unknown phase '%s' (allowed: %s)" % (row["phase"], " ".join(PHASES)))
        if row["cause"] not in CAUSES:
            bad.append("unknown cause '%s' (allowed: %s)" % (row["cause"], " ".join(CAUSES)))
        if row["trait"] not in TRAITS:
            bad.append("unknown trait '%s' (allowed: %s)" % (row["trait"], " ".join(TRAITS)))
        if row["persona"] not in PERSONAS:
            bad.append("unknown persona '%s' (allowed: %s)" % (row["persona"], " ".join(PERSONAS)))
        topics = TOPICS_BY_EVENT.get(row["event"], ["any"])
        if row["topic"] not in topics:
            bad.append("topic '%s' not allowed for event '%s' (allowed: %s)" % (row["topic"], row["event"], " ".join(topics)))
        if row["event"] != EXPLOSION_EVENT:
            m = EXPLOSION_RE.search(row["spanish"]) or EXPLOSION_RE.search(row["english"])
            if m:
                bad.append("explosion word '%s' outside event %s" % (m.group(0), EXPLOSION_EVENT))
        if row["role"] == "mc" and row["gender"] == "f":
            bad.append("role mc uses the male voice only (gender m or both)")
        sub_only = row["role"] in SUBTITLE_ONLY_ROLES
        # Core, beep and the probe-mode roles and events (same rules as PhraseTable.Parse)
        if row["core"] not in CORES:
            bad.append("unknown core '%s' (allowed: %s)" % (row["core"], " ".join(CORES)))
        if row["role"] == "probe":
            if row["beep"] not in MOODS:
                bad.append("probe row needs a beep mood (%s), got '%s'" % (" ".join(MOODS), row["beep"]))
        elif row["beep"]:
            bad.append("beep is only allowed on probe rows")
        probe_ev = row["event"] in PROBE_EVENTS
        probe_ok = probe_ev or row["event"] in PROBE_VARIANT_EVENTS or row["event"] == PROBE_CREW_EVENT
        if row["event"] == PROBE_CREW_EVENT and row["role"] == "mc":
            bad.append("probe_crew is in the cabin, it cannot have mc rows")
        if row["core"] != "any" and not probe_ok:
            bad.append("core '%s' outside a probe event" % row["core"])
        if row["role"] in PROBE_ROLES and not probe_ok:
            bad.append("role %s outside a probe event" % row["role"])
        if probe_ev and row["role"] == "crew":
            bad.append("probe event '%s' cannot have crew rows" % row["event"])
        if row["role"] == "probe" and not PROBE_SUBTITLE_RE.match(row["english"]):
            bad.append("probe subtitle must look like [beeps excitedly] (beeps and 1 to 3 lowercase words), got '%s'"
                       % row["english"])
        if not KEY_RE.match(row["scene"]):
            bad.append("scene must match [a-z0-9_]+")
        try:
            seq = int(row["seq"])
            if seq < 1:
                raise ValueError
        except ValueError:
            seq = 0
            bad.append("seq must be an integer >= 1")
        if sub_only:
            if row["spanish"]:
                bad.append("role %s is subtitle only, spanish must be empty" % row["role"])
            if not row["english"]:
                bad.append("role %s is subtitle only and needs english" % row["role"])
        else:
            if not row["spanish"]:
                bad.append("spanish is empty")
            if not row["english"]:
                bad.append("english is empty")
        # placeholders: the spanish only knows {body}, the english also subtitle slots. Same rules as
        # PhraseTable.CheckPlaceholders in C#. Which slots exist is checked in check_slots.
        ph_es = PLACEHOLDER_RE.findall(row["spanish"])
        ph_en = PLACEHOLDER_RE.findall(row["english"])
        unknown_es = sorted({p for p in ph_es if p != "body"})
        unknown_en = sorted({p for p in ph_en if p != "body" and not is_slot(p)})
        if unknown_es:
            bad.append("spanish placeholder(s) %s, only {body} exists there" % ", ".join("{%s}" % p for p in unknown_es))
        elif unknown_en:
            bad.append("bad placeholder(s) %s, use {body} or a slot like {name} or {name:2}"
                       % ", ".join("{%s}" % p for p in unknown_en))
        elif "|" in row["english"] and version_problem(row["english"], "body" in ph_en):
            bad.append(version_problem(row["english"], "body" in ph_en))
        else:
            if not sub_only and (("body" in ph_es) != ("body" in ph_en)):
                bad.append("{body} must be in both spanish and english, or in neither")
            elif ph_es.count("body") > 1 or ph_en.count("body") > 1:
                bad.append("{body} at most once per text")
        sc = row["scene"]
        if sc in scene_file and scene_file[sc] != row["file"]:
            bad.append("scene '%s' is also in %s, a scene lives in one file" % (sc, scene_file[sc]))
        if seq >= 1 and seq in scene_seqs.get(sc, {}):
            bad.append("duplicate scene/seq %s/%d, first at %s" % (sc, seq, scene_seqs[sc][seq]))
        if not bad and sc in scene_head:
            head = scene_head[sc]
            diff = [c for c in ("event", "band", "severity", "phase", "cause", "trait", "persona", "topic", "core")
                    if head[c] != row[c]]
            if diff:
                bad.append("scene '%s' differs from its first row (%s) in %s" % (sc, head["loc"], ", ".join(diff)))
        first_key.setdefault(key, loc)
        if bad:
            errors.append("%s (%s): %s" % (loc, key, "; ".join(bad)))
            continue
        row["seq"] = seq
        row["has_body"] = "body" in ph_en
        scene_file.setdefault(sc, row["file"])
        scene_head.setdefault(sc, row)
        scene_seqs.setdefault(sc, {})[seq] = loc
        good.append(row)
    for sc, seqs in scene_seqs.items():
        if sorted(seqs) != list(range(1, len(seqs) + 1)):
            errors.append("%s: scene '%s' seq numbers %s, must run 1..%d" % (scene_head[sc]["loc"], sc,
                                                                            sorted(seqs), len(seqs)))
    # intercom (crew only, 2 or more lines) is always funny: never in a serious phase
    by_scene = {}
    for r in good:
        by_scene.setdefault(r["scene"], []).append(r)
    for sc, lines in by_scene.items():
        head = scene_head[sc]
        intercom = len(lines) >= 2 and all(r["role"] == "crew" for r in lines)
        if intercom and (head["phase"] == "serious" or head["phase"] in SERIOUS_PHASES):
            errors.append("%s: scene '%s' is an intercom scene in the serious phase '%s'" % (head["loc"], sc, head["phase"]))
        if head["trait"] in ("to_tourist", "tourist_pair") and not intercom:
            errors.append("%s: scene '%s' has trait %s, that is only for intercom scenes (crew only, 2 or more lines)"
                          % (head["loc"], sc, head["trait"]))
        if head["event"] == PROBE_CREW_EVENT:
            roles = {r["role"] for r in lines}
            if "probe" not in roles or "crew" not in roles:
                errors.append("%s: probe_crew scene '%s' needs a probe row and a crew row" % (head["loc"], sc))
            if head["phase"] == "serious" or head["phase"] in SERIOUS_PHASES:
                errors.append("%s: probe_crew scene '%s' is in the serious phase '%s'" % (head["loc"], sc, head["phase"]))
    return good, errors


# Conditions a version can start with: "[landed] You're on the ground." Same as SubtitleSlots.Conditions in C#.
CONDITIONS = ["prelaunch", "launch", "ascent", "orbit", "coast", "reentry", "descent", "landed", "blackout",
              "kerbin", "away",
              # parts and experiments on the vessel
              "solar", "antenna", "legs", "ladder", "docking", "heatshield", "chute", "engine", "strut", "probe", "rcs",
              "goo", "sciencejr", "thermometer", "barometer", "seismic", "gravity", "magnetometer"]
COND_RE = re.compile(r"^\[([^\]]*)\]\s*(.*)$")


def split_conditions(version):
    """("[landed kerbin] text") gives (["landed", "kerbin"], "text"). (None, text) for a bad [ ] part."""
    v = version.strip()
    if not v.startswith("["):
        return [], v
    m = COND_RE.match(v)
    if not m:
        return None, v
    conds = m.group(1).split()
    if not conds or any(c not in CONDITIONS for c in conds):
        return None, m.group(2)
    return conds, m.group(2)


def version_problem(english, has_body):
    """Same rules as PhraseTable.CheckPlaceholders: non-empty versions, valid [conditions], one plain version."""
    if has_body:
        return "english versions (|) cannot use {body}"
    plain = False
    for v in english.split("|"):
        if not v.strip():
            return "english has an empty version between |"
        conds, text = split_conditions(v)
        if conds is None or not text:
            return "bad version '%s': use [condition] text, conditions are %s" % (v.strip(), " ".join(CONDITIONS))
        plain = plain or not conds
    return None if plain else "english versions need at least one version without [ ]"


def is_slot(p):
    m = SLOT_RE.match(p)
    return bool(m) and m.group(1) != "body"


def slot_names(text):
    """The slot names in an english text (without :n), {body} left out."""
    out = []
    for p in PLACEHOLDER_RE.findall(text):
        m = SLOT_RE.match(p)
        if m and m.group(1) != "body" and m.group(1) not in out:
            out.append(m.group(1))
    return out


def value_problem(val):
    """Same as SubtitleSlots.ValueProblem in C#: optional [conditions], non-empty, no |, only well-formed slots and
    no {body}."""
    if not val.strip():
        return "value is empty"
    conds, val = split_conditions(val)
    if conds is None:
        return "bad [conditions], they are " + " ".join(CONDITIONS)
    if not val:
        return "value is empty"
    if "|" in val:
        return "a value cannot hold |, give each version its own row"
    if "{" in PLACEHOLDER_RE.sub("", val) or "}" in PLACEHOLDER_RE.sub("", val):
        return "value has a bad slot, use {name} or {name:2}"
    for p in PLACEHOLDER_RE.findall(val):
        m = SLOT_RE.match(p)
        if not m:
            return "value has a bad slot {%s}, use {name} or {name:2}" % p
        if m.group(1) == "body":
            return "{body} cannot be used in a pool value"
    return None


def pool_reach(pools):
    """Per pool: every slot name its values can lead to, through other pools. Returns (reach, errors). A pool that
    leads back to itself is an error (the game stops at a depth of 6 and shows ...)."""
    errors = []
    direct = {k: set(n for v in vs for n in slot_names(v)) for k, vs in pools.items()}
    reach = {}

    def walk(k, stack):
        if k in reach:
            return reach[k]
        out = set()
        for n in direct.get(k, ()):
            out.add(n)
            if n in pools:
                if n in stack:
                    errors.append("pools: pool '%s' leads back to itself (%s)" % (n, " > ".join(stack + [n])))
                    continue
                out |= walk(n, stack + [n])
        reach[k] = out
        return out

    for k in pools:
        walk(k, [k])
    for k, names in direct.items():
        for n in names:
            if n not in pools and n not in FLIGHT_SLOTS and n not in PERSON_SLOTS and n not in RUNTIME_NUMBERS:
                errors.append("pools: pool '%s' uses slot {%s}, which has no pool" % (k, n))
    return reach, sorted(set(errors))


POOL_SPANISH = {}   # (pool, value without [conditions]) -> spanish cell as written, filled by load_pools


def load_pools(path=POOLS_FILE):
    """Subtitle slot values (experimental, procedural talk). Returns ({pool: [values]} in file order, errors).
    No file gives no pools. Same rules as SlotPools.Parse in C#. The spanish column goes to POOL_SPANISH."""
    POOL_SPANISH.clear()
    if not path.exists():
        return {}, []
    rows, errors = read_tsv(path, POOL_COLUMNS, path.name, POOL_OPTIONAL)
    pools = {}
    for r in rows:
        bad = []
        name, val = r["pool"], r["english"]
        if not re.match(r"^[a-z_]+$", name):
            bad.append("pool must match [a-z_]+")
        elif name in RESERVED_SLOTS:
            bad.append("'%s' is a reserved slot name" % name)
        why = value_problem(val)
        if why:
            bad.append(why)
        if bad:
            errors.append("%s (%s): %s" % (r["loc"], name, "; ".join(bad)))
            continue
        vals = pools.setdefault(name, [])
        if val in vals:
            errors.append("%s (%s): duplicate value '%s'" % (r["loc"], name, val))
        else:
            vals.append(val)
        es = r.get("spanish", "")
        if es:
            g, _, rest = es.partition(":")
            if rest and g in POOL_GENDERS and not rest.strip():
                errors.append("%s (%s): empty spanish after the gender" % (r["loc"], name))
            POOL_SPANISH[(name, split_conditions(val)[1])] = es
    return pools, errors


def check_slots(rows, pools):
    """Every english slot is a pool, a flight slot or (in an intercom scene) a person slot. A {pool:n} needs at
    least n values. Returns errors. Every pool should be used somewhere, an unused pool is a warning."""
    reach, errors = pool_reach(pools)
    used = set()
    by_scene = {}
    for r in rows:
        by_scene.setdefault(r["scene"], []).append(r)
    for sc, lines in by_scene.items():
        intercom = len(lines) >= 2 and all(r["role"] == "crew" for r in lines)
        for r in lines:
            for p in PLACEHOLDER_RE.findall(r["english"]):
                m = SLOT_RE.match(p)
                if not m or m.group(1) == "body":
                    continue
                name, n = m.group(1), int(m.group(2) or 1)
                if name in FLIGHT_SLOTS or name in RUNTIME_NUMBERS:
                    if m.group(2):
                        errors.append("%s (%s): {%s} is a flight value, it has no :n" % (r["loc"], r["clip_key"], p))
                elif name in PERSON_SLOTS:
                    if not intercom:
                        errors.append("%s (%s): {%s} only works in intercom scenes (crew only, 2 or more lines)"
                                      % (r["loc"], r["clip_key"], name))
                    elif m.group(2):
                        errors.append("%s (%s): {%s} has no :n" % (r["loc"], r["clip_key"], p))
                elif name not in pools:
                    errors.append("%s (%s): slot {%s} has no pool in %s" % (r["loc"], r["clip_key"], name,
                                                                          POOLS_FILE.name))
                else:
                    used.add(name)
                    used.update(reach.get(name, ()))
                    if n > len(pools[name]):
                        errors.append("%s (%s): {%s} needs %d values, pool %s has %d"
                                      % (r["loc"], r["clip_key"], p, n, name, len(pools[name])))
                    inner = [x for x in reach.get(name, ()) if x in PERSON_SLOTS]
                    if inner and not intercom:
                        errors.append("%s (%s): {%s} can give {%s}, which only works in intercom scenes"
                                      % (r["loc"], r["clip_key"], name, inner[0]))
    for name in pools:
        if name not in used:
            log("WARN pools: pool '%s' is not used by any phrase" % name)
    return errors


def write_pools(pools):
    BUILD.mkdir(parents=True, exist_ok=True)
    if not pools:
        if POOLS_OUT.exists():
            POOLS_OUT.unlink()
        return
    lines = ["\t".join(POOL_COLUMNS + list(POOL_OPTIONAL))]
    for name, vals in pools.items():
        for v in vals:
            lines.append(name + "\t" + v + "\t" + POOL_SPANISH.get((name, split_conditions(v)[1]), ""))
    POOLS_OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")


def cap_first(text, pos):
    """Upper-cases the letter at pos when it starts the text or a sentence (after . ! ? and spaces)."""
    if pos >= len(text) or not text[pos].islower():
        return text
    before = text[:pos].rstrip()
    if before and before[-1] not in ".!?¡¿":
        return text
    return text[:pos] + text[pos].upper() + text[pos + 1:]


def expand_bodies(rows, bodies):
    """Every row with {body} becomes one row per body: clip_key <key>__<body>, body column set.
    Rows without {body} get an empty body. lonapse_text is computed after the expansion."""
    out = []
    for r in rows:
        if not r["has_body"]:
            e = dict(r)
            e["body"] = ""
            e["lonapse_text"] = reverse_text(e["spanish"])
            out.append(e)
            continue
        for bk, (es, en) in bodies.items():
            if bk == HOME_BODY and r["scene"] in NOT_AT_HOME:
                continue
            e = dict(r)
            e["clip_key"] = r["clip_key"] + BODY_SEP + bk
            e["body"] = bk
            e["spanish"] = r["spanish"].replace("{body}", es)
            pos = r["english"].index("{body}")
            e["english"] = cap_first(r["english"].replace("{body}", en), pos)
            e["lonapse_text"] = reverse_text(e["spanish"])
            out.append(e)
    return out


def load_all():
    """Source rows, expanded rows, errors and the list of source files."""
    rows, errors, files = load_sources()
    bodies, berr = load_bodies()
    errors += berr
    good, verr = validate(rows)
    errors += verr
    pools, perr = load_pools()
    errors += perr + check_slots(good, pools) + check_spanish_rt(good)
    warnings = []
    errors += check_minimums(good, warnings)
    for w in warnings:
        log("WARN table: " + w)
    expanded = expand_bodies(good, bodies)
    return good, expanded, errors, files, bodies


ES_SLOT_RE = re.compile(r"\{(?:(?:el|un|del|al) )?([a-z_]+)(?::[1-9])?\}")


def check_spanish_rt(rows):
    """The spanish_rt column, same rules as PhraseTable.CheckSpanishRt in C#: one piece per english version (an
    empty piece: the recorded clip says it), each piece uses only slots of its english version, no [conditions],
    and a version with a runtime number ({fuel} ...) needs its piece."""
    errors = []
    for r in rows:
        rt = r.get(SPANISH_RT_COLUMN, "")
        en = [v.strip() for v in r["english"].split("|")]
        where = "%s (%s)" % (r["loc"], r["clip_key"])
        if not rt:
            for v in en:
                if any(n in RUNTIME_NUMBERS for n in slot_names(v)):
                    errors.append("%s: a version with a runtime number needs spanish_rt: '%s'" % (where, v))
                    break
            continue
        if r["role"] == "probe":
            errors.append("%s: spanish_rt on a subtitle-only row" % where)
            continue
        es = [v.strip() for v in rt.split("|")]
        if len(es) != len(en):
            errors.append("%s: spanish_rt has %d versions for %d english, split both by | in the same order"
                          % (where, len(es), len(en)))
            continue
        for k, (e, s) in enumerate(zip(en, es), start=1):
            text = split_conditions(e)[1] or ""
            have = set(slot_names(text))
            if not s:
                if have & set(RUNTIME_NUMBERS):
                    errors.append("%s: version %d has a runtime number and needs its spanish_rt" % (where, k))
                continue
            if s.startswith("["):
                errors.append("%s: spanish_rt version %d: leave out the [conditions]" % (where, k))
            for n in ES_SLOT_RE.findall(s):
                if n not in have:
                    errors.append("%s: spanish_rt version %d uses {%s}, its english does not" % (where, k, n))
            rest = ES_SLOT_RE.sub("", s)
            if "{" in rest or "}" in rest:
                errors.append("%s: spanish_rt version %d has a bad slot" % (where, k))
    return errors


def check_minimums(rows, warnings=None):
    """Shortfalls per event and chatter band. A shortfall of a NEW_EVENTS event goes to warnings, not errors."""
    errors = []
    per_event = {}
    per_band = {}
    for r in rows:
        per_event[r["event"]] = per_event.get(r["event"], 0) + 1
        if r["event"] == "chatter":
            per_band[r["band"]] = per_band.get(r["band"], 0) + 1
    for ev in EVENTS:
        if ev in OPTIONAL_EVENTS:
            continue
        n = per_event.get(ev, 0)
        if n < MIN_PER_EVENT:
            msg = "event '%s': %d rows, minimum %d" % (ev, n, MIN_PER_EVENT)
            if ev in NEW_EVENTS:
                if warnings is not None:
                    warnings.append(msg + " (new v4 or v5 event, not enforced yet)")
            else:
                errors.append(msg)
    for band in CHATTER_BANDS:
        n = per_band.get(band, 0)
        if n < MIN_CHATTER_PER_BAND:
            errors.append("event 'chatter' band '%s': %d rows, minimum %d" % (band, n, MIN_CHATTER_PER_BAND))
    return errors


def summary(src_rows, rows, files):
    """Counts per file, per phase and per cause (source rows), and scenes and clips (expanded)."""
    lines = []
    per_file = {}
    for r in src_rows:
        per_file[r["file"]] = per_file.get(r["file"], 0) + 1
    lines.append("Rows per file: " + ", ".join("%s %d" % (p.name, per_file.get(p.name, 0)) for p in files))
    all_topics = sorted({t for v in TOPICS_BY_EVENT.values() for t in v} - {"any"})
    for col, values in (("phase", PHASES), ("cause", CAUSES), ("trait", TRAITS), ("persona", PERSONAS),
                        ("topic", ["any"] + all_topics), ("role", ROLES), ("core", CORES), ("beep", MOODS)):
        cnt = {}
        for r in src_rows:
            cnt[r[col]] = cnt.get(r[col], 0) + 1
        lines.append("Rows per %s: %s" % (col, ", ".join("%s %d" % (v, cnt[v]) for v in values if v in cnt) or "none"))
    scenes = {r["scene"] for r in src_rows}
    body_scenes = {r["scene"] for r in src_rows if r["has_body"]}
    by_scene = {}
    for r in src_rows:
        by_scene.setdefault(r["scene"], []).append(r)
    probe_scenes = sum(1 for sc in by_scene.values() if is_probe_scene(sc))
    sub_rows = sum(1 for r in src_rows if r["role"] in SUBTITLE_ONLY_ROLES)
    lines.append("Scenes: %d (%d with {body}, %d probe scenes). Source rows: %d (%d subtitle only). "
                 "Built rows after {body}: %d. Clips: %d"
                 % (len(scenes), len(body_scenes), probe_scenes, len(src_rows), sub_rows, len(rows),
                    len(clip_jobs(rows))))
    return lines


def is_probe_scene(lines):
    """Same as PhraseTable.IsProbeScene: the core is not any, a line has a probe-mode role, or the event is
    a probe-only event. lines are the rows of one scene."""
    if not lines:
        return False
    head = lines[0]
    return (head["core"] != "any" or head["event"] in PROBE_EVENTS
            or any(r["role"] in PROBE_ROLES for r in lines))


def folder_for(row):
    """Sounds/ folder of a row. Must match PhraseTable.FolderFor in C#.
    Subtitle-only role (probe): no clip, folder "".
    crew: callout severity -> crew/callout, relief event -> crew/relief, band calm/tense/panic -> crew/<band>,
    band none -> crew/calm. mc: band tense or panic -> control/tense, otherwise control/calm."""
    if row["role"] in SUBTITLE_ONLY_ROLES:
        return ""
    if row["role"] == "mc":
        return "control/tense" if row["band"] in ("tense", "panic") else "control/calm"
    if row["severity"] == "callout":
        return "crew/callout"
    if row["event"] == "relief":
        return "crew/relief"
    if row["band"] in ("calm", "tense", "panic"):
        return "crew/" + row["band"]
    return "crew/calm"


def clip_rel(row, g):
    """Path of one clip relative to SOUNDS."""
    return "%s/%s_%s.ogg" % (folder_for(row), row["clip_key"], g)


def clip_jobs(rows):
    """One job per output file: (row, gender suffix, delivery kind m|f|mc).
    Subtitle-only rows (probe) have no clip."""
    jobs = []
    for r in rows:
        if r["role"] in SUBTITLE_ONLY_ROLES:
            continue
        if r["role"] == "mc":
            jobs.append((r, "m", "mc"))
            continue
        if r["gender"] in ("m", "both"):
            jobs.append((r, "m", "m"))
        if r["gender"] in ("f", "both"):
            jobs.append((r, "f", "f"))
    return jobs


# ---------------------------------------------------------------------------
# Audio helpers (ffmpeg and espeak-ng through pipes, no shell)
# ---------------------------------------------------------------------------
def run(cmd, data=None):
    p = subprocess.run(cmd, input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if p.returncode != 0:
        raise BuildError("command failed: %s\n%s" % (" ".join(cmd[:4]), p.stderr.decode("utf-8", "replace")[-800:]))
    return p


def db(x):
    return 20.0 * math.log10(x) if x > 1e-12 else -240.0


def gated_rms_db(x):
    """RMS over 10 ms frames above max(-50 dBFS, loudest frame - 30 dB), same rule as build_clips.py."""
    powers = []
    for i in range(0, len(x) - FRAME + 1, FRAME):
        powers.append(sum(v * v for v in x[i:i + FRAME]) / FRAME)
    if not powers:
        return -240.0
    gate = max(10 ** (SILENCE_DB / 10.0), max(powers) * 1e-3)
    sel = [p for p in powers if p >= gate]
    return 10.0 * math.log10(sum(sel) / len(sel)) if sel else -240.0


def decode_f32(data):
    """Decode any audio bytes to mono 44.1 kHz float samples."""
    out = run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error", "-i", "pipe:0",
               "-vn", "-ac", "1", "-ar", str(SR), "-f", "f32le", "-acodec", "pcm_f32le", "pipe:1"], data).stdout
    a = array.array("f")
    a.frombytes(out[: len(out) // 4 * 4])
    if sys.byteorder != "little":
        a.byteswap()
    return a


PIPER_PY = None  # set in main()


def is_countdown(row):
    return row.get("event") == COUNTDOWN_EVENT


def sentence_case(t):
    """All small letters, a capital only at the start of each sentence (after . ! ? and the opening marks).
    Experimental (LONAPSE_LOWERCASE=1, old name QUINDAR_LOWERCASE): espeak-ng reads a capital inside a word as a separate letter name."""
    out = []
    start = True
    for c in t.lower():
        if c.isalpha():
            out.append(c.upper() if start else c)
            start = False
        else:
            out.append(c)
            if c in ".!?\u00bf\u00a1":
                start = True
    return "".join(out)


def spoken_text(row):
    """The text Piper reads. lonapse_text, except for the countdown: there every word is folded to small letters and
    only the first letter of the line is a capital, so espeak-ng does not split "eteiS" into "etei" and "S"."""
    t = row["lonapse_text"]
    if not is_countdown(row):
        if os.environ.get("LONAPSE_LOWERCASE", os.environ.get("QUINDAR_LOWERCASE")) == "1":
            return sentence_case(t)
        return t
    t = t.lower()
    for i, c in enumerate(t):
        if c.isalpha():
            return t[:i] + c.upper() + t[i + 1:]
    return t


def delivery(row, kind):
    """(voice name, total pitch shift in semitones, tempo factor) for one clip."""
    d = DELIVERY[kind]
    if is_countdown(row):
        band = "countdown"
    elif row["severity"] == "callout":
        band = "callout"
    elif row["band"] == "panic" and kind in MC_LIKE_KINDS:
        band = "mc_panic"
    else:
        band = row["band"]
    b = BAND_DELIVERY[band]
    tempo = b["tempo"] * d.get("tempo", 1.0)
    if is_mc_lively(row, kind):
        tempo *= MC_LIVELY_TEMPO
    return d["voice"], d["pitch_st"] + b["pitch_st"], tempo


def is_mc_lively(row, kind):
    return kind in MC_LIKE_KINDS and row["band"] != "panic" and not is_countdown(row)


def mc_noise():
    """Piper noise flags for a lively MC line, empty when both stay at the Piper default."""
    out = []
    if MC_LIVELY_NOISE_SCALE is not None:
        out += ["--noise-scale", "%.2f" % MC_LIVELY_NOISE_SCALE]
    if MC_LIVELY_NOISE_W is not None:
        out += ["--noise-w-scale", "%.2f" % MC_LIVELY_NOISE_W]
    return out


def is_panic_crew(row, kind):
    return row["band"] == "panic" and kind in ("m", "f") and row["severity"] != "callout"


def piper_speech(text, kind, row, tempo_jitter=1.0, noisy=False):
    """Piper speech, shifted into the Kerbal register, trimmed of silence, as 44.1 kHz float samples."""
    voice, pitch_st, tempo = delivery(row, kind)
    tempo *= tempo_jitter
    ratio = 2.0 ** (pitch_st / 12.0)
    # asetrate speeds up by `ratio`, so render that much slower, and faster again by the band tempo
    length_scale = ratio / tempo
    # --output-raw: 16-bit mono PCM at the model rate on stdout (piper-tts 1.8 writes no WAV to a pipe)
    cmd = [str(PIPER_PY), "-m", "piper", "-m", str(VOICES_DIR / (voice + ".onnx")), "--output-raw",
           "--length-scale", "%.4f" % length_scale, "--sentence-silence", "0.12"]
    if noisy:
        cmd += ["--noise-scale", "%.2f" % PANIC_NOISE_SCALE, "--noise-w-scale", "%.2f" % PANIC_NOISE_W]
    elif is_countdown(row):
        cmd += ["--noise-scale", "%.2f" % COUNTDOWN_NOISE_SCALE, "--noise-w-scale", "%.2f" % COUNTDOWN_NOISE_W]
    elif is_mc_lively(row, kind):
        cmd += mc_noise()
    raw = run(cmd, text.encode("utf-8")).stdout
    if len(raw) < PIPER_SR // 10 * 2:
        raise BuildError("Piper returned %d bytes for '%s'" % (len(raw), text))
    af = ("asetrate=%d,aresample=%d,"
          "silenceremove=start_periods=1:start_threshold=-50dB:stop_periods=-1:stop_threshold=-50dB"
          % (int(round(PIPER_SR * ratio)), SR))
    out = run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error",
               "-f", "s16le", "-ar", str(PIPER_SR), "-ac", "1", "-i", "pipe:0", "-vn",
               "-af", af, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-acodec", "pcm_f32le", "pipe:1"], raw).stdout
    a = array.array("f")
    a.frombytes(out[: len(out) // 4 * 4])
    if sys.byteorder != "little":
        a.byteswap()
    return a


def clip_rng(row, g):
    seed = int(hashlib.sha256(("%s/%s" % (row["clip_key"], g)).encode("utf-8")).hexdigest()[:12], 16)
    return random.Random(seed)


PIECE_RE = re.compile(r"(?<=[.!?,\u2026])\s+")


def panic_pieces(text):
    """Splits a panic line at punctuation: "¡oN, on!" -> ["¡oN,", "on!"]. Pieces without letters join the previous."""
    out = []
    for p in PIECE_RE.split(text.strip()):
        if out and not any(c.isalpha() for c in p):
            out[-1] += " " + p
        elif p:
            out.append(p)
    return out or [text]


def gasp(rng, level):
    """A short inhale: band-limited noise that swells and stops abruptly (synthetic, no recording)."""
    n = int(SR * rng.uniform(0.16, 0.28))
    lp = hp = 0.0
    out = []
    for i in range(n):
        w = rng.uniform(-1.0, 1.0)
        lp += 0.35 * (w - lp)          # low pass about 3 kHz
        hp += 0.06 * (lp - hp)         # remove the low end, about 400 Hz
        t = i / float(n)
        env = (t ** 1.6) * (1.0 if t < 0.88 else (1.0 - t) / 0.12)
        out.append((lp - hp) * env)
    pk = max(abs(v) for v in out) or 1.0
    return [v / pk * level for v in out]


def crack(x, rng):
    """Voice crack: the last part of a piece jumps up (resampled, so it also gets a bit shorter)."""
    if len(x) < SR // 5:
        return x
    cut = int(len(x) * rng.uniform(0.55, 0.75))
    r = 2.0 ** (PANIC_CRACK_ST / 12.0)
    tail = x[cut:]
    m = int(len(tail) / r)
    up = []
    for i in range(m):
        pos = i * r
        j = int(pos)
        f = pos - j
        a = tail[j]
        b = tail[j + 1] if j + 1 < len(tail) else a
        up.append(a + (b - a) * f)
    xf = min(int(0.006 * SR), m, cut)
    head = list(x[:cut])
    for i in range(xf):  # short crossfade, no click at the jump
        g = i / float(xf)
        head[cut - xf + i] = head[cut - xf + i] * (1.0 - g) + up[i] * g
    return head + up[xf:]


def drive(x, amount):
    pk = max((abs(v) for v in x), default=0.0) or 1.0
    k = math.tanh(amount)
    return [math.tanh(amount * v / pk * 0.8) / k for v in x]


def ffmpeg_f32(x, af):
    """Runs float samples through an ffmpeg filter chain."""
    a = array.array("f", x)
    if sys.byteorder != "little":
        a.byteswap()
    out = run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error", "-f", "f32le", "-ar", str(SR), "-ac", "1",
               "-i", "pipe:0", "-af", af, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-acodec", "pcm_f32le", "pipe:1"],
              a.tobytes()).stdout
    y = array.array("f")
    y.frombytes(out[: len(out) // 4 * 4])
    if sys.byteorder != "little":
        y.byteswap()
    return list(y)


def scream_speech(text, kind, row, g):
    """A scream (clip keys scream_*): Piper says the vowel, then it is stretched with a shaky vibrato and driven."""
    rng = clip_rng(row, g)
    x = list(piper_speech(text, kind, row, noisy=True))
    stretch = rng.uniform(0.38, 0.5)   # Piper says "haaaaA" in about 0.3 s, a scream lasts about 0.7 s
    x = ffmpeg_f32(x, "atempo=%.3f,vibrato=f=%.1f:d=0.35" % (max(0.5, stretch), rng.uniform(5.5, 7.5))
                   + (",atempo=%.3f" % (stretch / 0.5) if stretch < 0.5 else ""))
    return drive(x, PANIC_DRIVE + 0.8)


def panic_speech(text, kind, row, g):
    """Panic crew line: pieces with random tempo, gasps or ragged pauses in between, cracks and light drive."""
    if row["clip_key"].startswith("scream"):
        return scream_speech(text, kind, row, g)
    rng = clip_rng(row, g)
    pieces = panic_pieces(text)
    out = []
    for n, piece in enumerate(pieces):
        x = list(piper_speech(piece, kind, row, tempo_jitter=rng.uniform(0.93, 1.12), noisy=True))
        if not x:
            continue
        if rng.random() < PANIC_CRACK_CHANCE:
            x = crack(x, rng)
        x = drive(x, PANIC_DRIVE)
        if out:
            if rng.random() < PANIC_GASP_CHANCE:
                out += [0.0] * int(SR * rng.uniform(0.03, 0.08))
                out += gasp(rng, rng.uniform(0.18, 0.30))
                out += [0.0] * int(SR * rng.uniform(0.02, 0.06))
            else:
                out += [0.0] * int(SR * rng.uniform(0.12, 0.35))
        out += x
    if not out:
        raise BuildError("panic line rendered empty: %s" % text)
    return out


def trim_tail(x):
    """Cuts the quiet decay at both ends, so the 30 ms pad is the only silence.

    The cut is relative to the loudest 10 ms frame (30 dB below it), because the gain step afterwards
    lifts a quiet tail above -50 dBFS. An absolute -45 dBFS cut left up to 150 ms of breath tail."""
    frames = [db(math.sqrt(sum(v * v for v in x[i:i + FRAME]) / FRAME)) for i in range(0, len(x) - FRAME + 1, FRAME)]
    cut = max(SILENCE_DB + 5.0, max(frames, default=SILENCE_DB) - 30.0)
    active = [i for i, v in enumerate(frames) if v > cut]
    if not active:
        raise BuildError("no speech above -45 dBFS after Piper")
    return x[active[0] * FRAME:(active[-1] + 1) * FRAME]


def limit(x, ceiling_db):
    """Block-based lookahead peak limiter (64-sample blocks, fast release), as in build_clips.py."""
    t = 10 ** (ceiling_db / 20.0)
    B = 64
    nb = (len(x) + B - 1) // B
    req = []
    for b_ in range(nb):
        pk = max((abs(v) for v in x[b_ * B:(b_ + 1) * B]), default=0.0)
        req.append(1.0 if pk <= t else t / pk)
    env = [min(req[max(0, k - 1)], req[k], req[min(nb - 1, k + 1)]) for k in range(nb)]
    rel = 0.02
    for k in range(1, nb):
        env[k] = min(env[k], env[k - 1] + rel)
    for k in range(nb - 2, -1, -1):
        env[k] = min(env[k], env[k + 1] + rel)
    y = list(x)
    for k in range(nb):
        g0 = env[k]
        g1 = min(env[k + 1] if k + 1 < nb else env[k], req[k])
        for j in range(B):
            i = k * B + j
            if i >= len(y):
                break
            y[i] *= g0 + (g1 - g0) * j / B
    return [max(-t, min(t, v)) for v in y]


def shape(samples):
    """Trim, fades, 30 ms silence at both ends, then gain to the RMS target. The peak limiter
    runs inside the gain loop, so the gated RMS lands on target and the peak stays under the cap."""
    x = trim_tail(list(samples))
    n = int(round(FADE_S * SR))
    for i in range(min(n, len(x))):
        g = i / float(n)
        x[i] *= g
        x[len(x) - 1 - i] *= g
    pad = [0.0] * int(round(PAD_S * SR))
    x = pad + x + pad
    g_db = RMS_TARGET_DB - gated_rms_db(x)
    y = x
    for _ in range(12):
        g = 10 ** (g_db / 20.0)
        y = limit([v * g for v in x], PEAK_BUILD_DB)
        err = RMS_TARGET_DB - gated_rms_db(y)
        if abs(err) < 0.2:
            break
        g_db += err
    return edge_trim(y)


def edge_trim(y, keep_frames=3):
    """After the gain step a clip that was turned down can have a tail under -50 dBFS that is longer than
    the pad. Cut leading and trailing frames at or under SILENCE_DB down to keep_frames (30 ms)."""
    nf = len(y) // FRAME
    if nf <= 2 * keep_frames:
        return y
    quiet = [db(math.sqrt(sum(v * v for v in y[i * FRAME:(i + 1) * FRAME]) / FRAME)) <= SILENCE_DB for i in range(nf)]
    lead = 0
    while lead < nf and quiet[lead]:
        lead += 1
    trail = 0
    while trail < nf and quiet[nf - 1 - trail]:
        trail += 1
    if lead >= nf:
        return y
    start = max(0, lead - keep_frames) * FRAME
    end = len(y) - max(0, trail - keep_frames) * FRAME
    return y[start:end]


def encode_ogg(x, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    a = array.array("f", x)
    if sys.byteorder != "little":
        a.byteswap()
    run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
         "-f", "f32le", "-ar", str(SR), "-ac", "1", "-i", "pipe:0",
         "-map_metadata", "-1", "-ac", "1", "-ar", str(SR),
         "-c:a", "libvorbis", "-q:a", OGG_QUALITY, "-f", "ogg", str(path)], a.tobytes())


def astats_levels(path, frame_file):
    """Overall peak from astats, and 10 ms frame RMS levels from astats (metadata printed to a file)."""
    p = run(["ffmpeg", "-hide_banner", "-nostdin", "-i", str(path), "-af",
             "astats=measure_perchannel=none:measure_overall=Peak_level+RMS_level", "-f", "null", "-"])
    m = re.search(r"Peak level dB:\s*(-?[0-9.]+|-inf)", p.stderr.decode("utf-8", "replace"))
    peak = float(m.group(1)) if m and m.group(1) != "-inf" else -240.0
    esc = str(frame_file).replace(":", "\\:")
    af = ("asetnsamples=n=%d:p=0,astats=metadata=1:reset=1:measure_perchannel=none:measure_overall=RMS_level,"
          "ametadata=print:key=lavfi.astats.Overall.RMS_level:file=%s") % (FRAME, esc)
    run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error", "-i", str(path), "-af", af,
         "-f", "null", "-"])
    frames = []
    for line in frame_file.read_text().splitlines():
        if "RMS_level=" in line:
            try:
                v = float(line.split("=", 1)[1])
            except ValueError:
                v = -240.0
            frames.append(v if math.isfinite(v) else -240.0)
    return peak, frames


def ffprobe_audio(path):
    p = run(["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries",
             "stream=codec_name,sample_rate,channels:format=duration", "-of", "json", str(path)])
    j = json.loads(p.stdout.decode("utf-8"))
    st = j["streams"][0]
    return {"codec": st.get("codec_name"), "rate": int(st.get("sample_rate", 0)),
            "channels": int(st.get("channels", 0)), "duration": float(j["format"]["duration"])}


def measure(path, frame_file):
    """Returns the measurements and the list of problems for one OGG file."""
    problems = []
    pr = ffprobe_audio(path)
    if pr["codec"] != "vorbis" or pr["rate"] != SR or pr["channels"] != 1:
        problems.append("format %s" % pr)
    peak, frames = astats_levels(path, frame_file)
    # gated RMS from the astats frame levels (same gate as the build)
    powers = [10 ** (v / 10.0) for v in frames]
    mx = max(powers) if powers else 0.0
    gate = max(10 ** (SILENCE_DB / 10.0), mx * 1e-3)
    sel = [p for p in powers if p >= gate]
    grms = 10.0 * math.log10(sum(sel) / len(sel)) if sel else -240.0
    silent = [v <= SILENCE_DB for v in frames]
    lead = 0
    while lead < len(silent) and silent[lead]:
        lead += 1
    trail = 0
    while trail < len(silent) and silent[len(silent) - 1 - trail]:
        trail += 1
    lead_s = lead * FRAME / float(SR)
    trail_s = trail * FRAME / float(SR)
    if peak > PEAK_CEILING_DB:
        problems.append("peak %.2f dBFS above %.1f" % (peak, PEAK_CEILING_DB))
    if abs(grms - RMS_TARGET_DB) > RMS_TOL_DB:
        problems.append("gated RMS %.2f dBFS, target %.1f +-%.1f" % (grms, RMS_TARGET_DB, RMS_TOL_DB))
    if lead_s > MAX_SILENCE_S + 1e-6 or trail_s > MAX_SILENCE_S + 1e-6:
        problems.append("leading %.0f ms / trailing %.0f ms silence, max 50" % (lead_s * 1000, trail_s * 1000))
    return {"duration_s": round(pr["duration"], 3), "gated_rms_dbfs": round(grms, 2),
            "peak_dbfs": round(peak, 2), "lead_silence_ms": round(lead_s * 1000),
            "trail_silence_ms": round(trail_s * 1000)}, problems


def pcm_hash(path):
    out = run(["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "error", "-i", str(path), "-vn",
               "-ac", "1", "-ar", str(SR), "-f", "s16le", "-acodec", "pcm_s16le", "pipe:1"]).stdout
    return hashlib.sha256(out).hexdigest()


def tool_version(cmd):
    p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    return p.stdout.decode("utf-8", "replace").splitlines()[0] if p.stdout else "unknown"


# ---------------------------------------------------------------------------
# Build and verify
# ---------------------------------------------------------------------------
def write_phrases(rows):
    BUILD.mkdir(parents=True, exist_ok=True)
    lines = ["\t".join(BUILT_COLUMNS)]
    for r in rows:
        vals = dict(r)
        vals["seq"] = str(r["seq"])
        vals["folder"] = folder_for(r)
        lines.append("\t".join(vals[c] for c in BUILT_COLUMNS))
    PHRASES_OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")


def owned_files(pattern="*"):
    """Files under the folders this script owns (Sounds/crew, Sounds/control)."""
    out = []
    for top in OWNED:
        d = SOUNDS / top
        if d.exists():
            out += [p for p in d.rglob(pattern) if p.is_file()]
    return out


def remove_empty_dirs():
    for top in OWNED:
        d = SOUNDS / top
        if d.exists():
            for sub in sorted(d.rglob("*"), key=lambda q: len(q.parts), reverse=True):
                if sub.is_dir() and not any(sub.iterdir()):
                    sub.rmdir()


def remove_owned():
    for p in owned_files("*.ogg"):
        p.unlink()
    remove_empty_dirs()
    if LEGACY_FLAT.exists():
        shutil.rmtree(LEGACY_FLAT)
        log("Removed old flat output %s" % LEGACY_FLAT)


# ---------------------------------------------------------------------------
# Render cache: one OGG plus one JSON record per render key, in audio/build/lonapse/cache/
# ---------------------------------------------------------------------------
def render_key(row, g, kind):
    """Everything that changes the rendered clip. The clip key and gender are part of it, so two rows with the same
    text still get their own render (Piper samples noise, and the panic chain is seeded per clip)."""
    voice, pitch, tempo = delivery(row, kind)
    k = dict(v=RENDER_VERSION, clip=row["clip_key"], g=g, kind=kind, text=spoken_text(row), voice=voice,
             onnx=VOICES.get(voice, {}).get("onnx", ""), pitch=round(pitch, 4), tempo=round(tempo, 4),
             sr=SR, rms=RMS_TARGET_DB, peak=PEAK_BUILD_DB, fade=FADE_S, pad=PAD_S, q=OGG_QUALITY)
    if is_panic_crew(row, kind):
        k["panic"] = [PANIC_NOISE_SCALE, PANIC_NOISE_W, PANIC_DRIVE, PANIC_CRACK_CHANCE, PANIC_CRACK_ST,
                      PANIC_GASP_CHANCE, row["clip_key"].startswith("scream")]
    if is_countdown(row):
        k["countdown"] = [COUNTDOWN_NOISE_SCALE, COUNTDOWN_NOISE_W]
    if is_mc_lively(row, kind) and mc_noise():
        k["mc_noise"] = [MC_LIVELY_NOISE_SCALE, MC_LIVELY_NOISE_W]
    return hashlib.sha256(json.dumps(k, sort_keys=True).encode("utf-8")).hexdigest()[:32]


def cache_get(key):
    """The cache record for a key, or None. The OGG must still match the recorded sha256."""
    ogg, rec = CACHE / (key + ".ogg"), CACHE / (key + ".json")
    if not ogg.exists() or not rec.exists():
        return None
    try:
        r = json.loads(rec.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    return r if r.get("file_sha256") == sha256_file(ogg) else None


def render_clip(row, g, kind, path):
    """Renders one clip to path. Returns the seconds it took."""
    t0 = time.monotonic()
    if is_panic_crew(row, kind):
        speech = panic_speech(row["lonapse_text"], kind, row, g)
    else:
        speech = piper_speech(spoken_text(row), kind, row)
    encode_ogg(shape(speech), path)
    return time.monotonic() - t0


def worker_init(piper_py, voices):
    """Process pool initializer: the worker processes do not see what main() set (forkserver start method)."""
    global PIPER_PY
    PIPER_PY = Path(piper_py)
    for k, v in voices.items():
        DELIVERY[k]["voice"] = v


def render_task(task):
    """Worker: render one clip into the cache, measure it, write the record. Returns the record."""
    row, g, kind, key = task
    CACHE.mkdir(parents=True, exist_ok=True)
    part = CACHE / (key + ".part.ogg")
    frames = CACHE / (key + ".frames.txt")
    secs = render_clip(row, g, kind, part)
    meas, probs = measure(part, frames)
    if frames.exists():
        frames.unlink()
    rec = dict(key=key, clip_key=row["clip_key"], gender=g, render_s=round(secs, 3), meas=meas, problems=probs,
               pcm_sha256=pcm_hash(part), file_sha256=sha256_file(part))
    part.replace(CACHE / (key + ".ogg"))
    tmp = CACHE / (key + ".json.part")
    tmp.write_text(json.dumps(rec, ensure_ascii=False) + "\n", encoding="utf-8")
    tmp.replace(CACHE / (key + ".json"))
    return rec


def try_task(task):
    """Worker for --try: render one clip into a scratch folder and measure it, nothing goes into the cache."""
    row, g, kind, path = task
    path.parent.mkdir(parents=True, exist_ok=True)
    secs = render_clip(row, g, kind, path)
    meas, probs = measure(path, path.with_suffix(".frames.txt"))
    path.with_suffix(".frames.txt").unlink()
    return path, secs, meas, probs


def run_pool(fn, tasks, njobs, label):
    """Runs tasks on a process pool. Returns (results in task order, errors). Logs progress with an estimate."""
    results = [None] * len(tasks)
    errors = []
    if not tasks:
        return results, errors
    voices = {k: DELIVERY[k]["voice"] for k in DELIVERY}
    t0 = time.monotonic()
    last = t0
    done = 0
    with ProcessPoolExecutor(max_workers=max(1, njobs), initializer=worker_init,
                             initargs=(str(PIPER_PY), voices)) as pool:
        futs = {pool.submit(fn, t): i for i, t in enumerate(tasks)}
        for f in as_completed(futs):
            i = futs[f]
            try:
                results[i] = f.result()
            except Exception as e:  # BuildError from ffmpeg or Piper, keep going and report at the end
                errors.append("%s: %s" % (tasks[i][0]["clip_key"], e))
            done += 1
            now = time.monotonic()
            if done == len(tasks) or now - last > 15:
                last = now
                rate = (now - t0) / done
                log("  %s %d / %d, %.1f s elapsed, about %.0f s left" % (label, done, len(tasks), now - t0,
                                                                         rate * (len(tasks) - done)))
    return results, errors


def build(rows, njobs):
    jobs = clip_jobs(rows)
    stray = [p for p in owned_files() if p.suffix.lower() != ".ogg"]
    if stray:
        raise BuildError("Sounds/crew or Sounds/control holds non-OGG files, move them first: %s"
                         % [str(p) for p in stray[:3]])
    if LEGACY_FLAT.exists():
        shutil.rmtree(LEGACY_FLAT)
        log("Removed old flat output %s" % LEGACY_FLAT)
    CACHE.mkdir(parents=True, exist_ok=True)
    keys = [render_key(r, g, kind) for r, g, kind in jobs]
    recs = {}
    tasks = []
    for (r, g, kind), key in zip(jobs, keys):
        rec = cache_get(key)
        if rec is not None:
            recs[key] = rec
        else:
            tasks.append((r, g, kind, key))
    log("Clips: %d from %d phrases, %d in the cache, %d to render with Piper (%d jobs)"
        % (len(jobs), len(rows), len(recs), len(tasks), njobs))
    t0 = time.monotonic()
    done, errors = run_pool(render_task, tasks, njobs, "rendered")
    if errors:
        for e in errors:
            log("ERROR render " + e)
        raise BuildError("%d clip(s) failed to render" % len(errors))
    for rec in done:
        recs[rec["key"]] = rec
    if tasks:
        secs = [rec["render_s"] for rec in done]
        log("Rendered %d clips in %.1f s wall (%.2f s per clip in a worker, median %.2f s)"
            % (len(tasks), time.monotonic() - t0, sum(secs) / len(secs), sorted(secs)[len(secs) // 2]))
    # sync Sounds/crew and Sounds/control with the table: copy changed clips, remove clips the table no longer has
    expected = {}
    for (r, g, kind), key in zip(jobs, keys):
        expected[clip_rel(r, g)] = key
    removed = 0
    for p in owned_files("*.ogg"):
        if str(p.relative_to(SOUNDS)) not in expected:
            p.unlink()
            removed += 1
    copied = 0
    for rel, key in expected.items():
        dst = SOUNDS / rel
        if dst.exists() and sha256_file(dst) == recs[key]["file_sha256"]:
            continue
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(CACHE / (key + ".ogg"), dst)
        copied += 1
    remove_empty_dirs()
    log("Sounds/: %d clip(s) copied from the cache, %d removed, %d unchanged"
        % (copied, removed, len(expected) - copied))
    write_phrases(rows)
    log("Wrote %s" % PHRASES_OUT)
    pools, _ = load_pools()
    write_pools(pools)
    if pools:
        log("Wrote %s" % POOLS_OUT)
    return jobs


def verify(rows, jobs, check_hash_against=None):
    """Checks every clip on disk. A clip whose bytes match its cache record reuses the measurements of the record
    (same bytes give the same measurements), every other clip is measured again with ffprobe and astats."""
    log("")
    log("Verification (ffprobe, ffmpeg astats, cached measurements for unchanged bytes)")
    problems = []
    expected = {clip_rel(r, g) for r, g, _ in jobs}
    on_disk = {str(p.relative_to(SOUNDS)) for p in owned_files("*.ogg")}
    if on_disk != expected:
        problems.append("files on disk differ from the table: missing %s, extra %s"
                        % (sorted(expected - on_disk)[:5], sorted(on_disk - expected)[:5]))
    for other in owned_files():
        if other.suffix.lower() != ".ogg":
            problems.append("non-ogg file under Sounds/%s" % other.relative_to(SOUNDS))
    old_hashes = {}
    if check_hash_against is not None and MANIFEST.exists():
        old = json.loads(MANIFEST.read_text(encoding="utf-8"))
        old_hashes = {c["file"]: c.get("pcm_sha256") for c in old.get("clips", [])}
    clips = []
    BUILD.mkdir(parents=True, exist_ok=True)
    frame_file = BUILD / "astats_frames.txt"
    total = 0
    fresh = 0
    for r, g, kind in jobs:
        fname = clip_rel(r, g)
        path = SOUNDS / fname
        voice = delivery(r, kind)[0]
        if not path.exists():
            continue
        rec = cache_get(render_key(r, g, kind))
        if rec is not None and rec["file_sha256"] == sha256_file(path):
            meas, probs, h = rec["meas"], list(rec["problems"]), rec["pcm_sha256"]
        else:
            meas, probs = measure(path, frame_file)
            h = pcm_hash(path)
            fresh += 1
        if fname in old_hashes and old_hashes[fname] and old_hashes[fname] != h:
            probs.append("decoded PCM hash differs from manifest")
        for p in probs:
            problems.append("%s: %s" % (fname, p))
        size = path.stat().st_size
        total += size
        clips.append(dict(file=fname, clip_key=r["clip_key"], gender=g, role=r["role"], voice=voice,
                          event=r["event"], band=r["band"], phase=r["phase"], cause=r["cause"], trait=r["trait"],
                          persona=r["persona"], topic=r["topic"], body=r["body"],
                          bytes=size, pcm_sha256=h, **meas))
    if frame_file.exists():
        frame_file.unlink()
    log("Clips verified: %d (%d measured again), total size %.3f MB" % (len(clips), fresh, total / 1048576.0))
    return clips, total, problems


def write_manifest(rows, clips, total):
    used = sorted({DELIVERY[k]["voice"] for k in DELIVERY})
    manifest = {
        "tts": "piper-tts " + tool_version([str(PIPER_PY), "-c",
                                            "import importlib.metadata as m; print(m.version('piper-tts'))"]),
        "voices": {v: dict(license=VOICES[v]["license"], onnx_sha256=VOICES[v]["onnx"]) for v in used},
        "delivery": DELIVERY,
        "band_delivery": BAND_DELIVERY,
        "render_version": RENDER_VERSION,
        "ffmpeg": tool_version(["ffmpeg", "-version"]),
        "codec": "OGG Vorbis, mono, 44.1 kHz, libvorbis -q:a %s" % OGG_QUALITY,
        "phrases": len(rows),
        "clip_count": len(clips),
        "total_bytes": total,
        "clips": clips,
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    log("Wrote %s" % MANIFEST)


def check_build_table(rows):
    """The generated phrases.tsv must match the expanded source (same keys, lonapse_text and folder rule)."""
    if not PHRASES_OUT.exists():
        return ["phrases.tsv is missing, run the build first"]
    problems = []
    lines = PHRASES_OUT.read_text(encoding="utf-8").splitlines()
    if not lines or lines[0].split("\t") != BUILT_COLUMNS:
        return ["phrases.tsv header is not %s, run the build again" % " ".join(BUILT_COLUMNS)]
    keys = []
    for ln in lines[1:]:
        f = ln.split("\t")
        if len(f) != len(BUILT_COLUMNS):
            problems.append("phrases.tsv row has %d columns" % len(f))
            continue
        d = dict(zip(BUILT_COLUMNS, f))
        keys.append(d["clip_key"])
        if d["lonapse_text"] != reverse_text(d["spanish"]):
            problems.append("lonapse_text of %s does not match ReverseText" % d["clip_key"])
        if d["folder"] != folder_for(d):
            problems.append("folder of %s does not match the folder rule" % d["clip_key"])
    want = [r["clip_key"] for r in rows]
    if keys != want:
        problems.append("phrases.tsv rows differ from the source tables (%d built, %d expected), run the build"
                        % (len(keys), len(want)))
    return problems


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def ensure_voices(fetch):
    """Every voice in DELIVERY must be in audio/voices with the expected sha256. --fetch-voices downloads."""
    VOICES_DIR.mkdir(parents=True, exist_ok=True)
    for v in sorted({DELIVERY[k]["voice"] for k in DELIVERY}):
        if v not in VOICES:
            raise BuildError("voice %s is not in the VOICES table (license unknown), add it there first" % v)
        info = VOICES[v]
        for ext, want in ((".onnx", info["onnx"]), (".onnx.json", info["json"])):
            p = VOICES_DIR / (v + ext)
            if not p.exists():
                if not fetch:
                    raise BuildError("missing %s, run with --fetch-voices" % p)
                url = HF + info["path"] + "/" + v + ext
                log("Downloading %s" % url)
                tmp = p.with_suffix(p.suffix + ".part")
                with urllib.request.urlopen(url, timeout=120) as resp, open(tmp, "wb") as fh:
                    shutil.copyfileobj(resp, fh)
                tmp.rename(p)
            got = sha256_file(p)
            if got != want:
                raise BuildError("sha256 mismatch for %s: %s, expected %s" % (p, got, want))
    log("Voices OK: %s" % ", ".join("%s=%s" % (k, DELIVERY[k]["voice"]) for k in DELIVERY))


def check_piper(fetch):
    if not PIPER_PY.exists():
        raise BuildError("no Python at %s. Install Piper: python3 -m venv audio/.venv && "
                         "audio/.venv/bin/pip install piper-tts" % PIPER_PY)
    probe_cmd = [str(PIPER_PY), "-c", "import piper"]
    if subprocess.run(probe_cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode != 0:
        raise BuildError("%s cannot import piper. Run: %s -m pip install piper-tts" % (PIPER_PY, PIPER_PY))
    ensure_voices(fetch)


def try_render(rows, keys, out, njobs):
    """--try: renders the clips of the given keys (a base key also selects its body variants) into out."""
    sel = [j for j in clip_jobs(rows)
           if any(j[0]["clip_key"] == k or j[0]["clip_key"].startswith(k + BODY_SEP) for k in keys)]
    if not sel:
        raise BuildError("no clips match %s" % " ".join(keys))
    out = Path(out).resolve()
    if out == SOUNDS.resolve() or SOUNDS.resolve() in out.parents or out == BUILD.resolve():
        raise BuildError("--out must not be inside audio/build/Sounds or audio/build/lonapse")
    tasks = [(r, g, kind, out / clip_rel(r, g)) for r, g, kind in sel]
    log("Rendering %d clip(s) into %s (%d jobs, cache not used)" % (len(tasks), out, njobs))
    t0 = time.monotonic()
    res, errors = run_pool(try_task, tasks, njobs, "rendered")
    wall = time.monotonic() - t0
    for e in errors:
        log("ERROR render " + e)
    for item in res:
        if item is None:
            continue
        path, secs, meas, probs = item
        log("  %-60s %.2f s  %.2f s audio  %s" % (path.relative_to(out), secs, meas["duration_s"],
                                                  "; ".join(probs) if probs else "levels OK"))
    ok = [x for x in res if x is not None]
    if ok:
        log("Wall %.1f s for %d clip(s), %.2f s per clip of wall time" % (wall, len(ok), wall / len(ok)))
    return 1 if errors else 0


def main():
    global PIPER_PY
    ap = argparse.ArgumentParser(description="Build the Lonapse (reversed Spanish) clips with Piper and ffmpeg.")
    ap.add_argument("--clean", action="store_true",
                    help="delete audio/build/lonapse (with the render cache) and the Lonapse clips in "
                         "Sounds/crew and Sounds/control first")
    ap.add_argument("--verify-only", action="store_true", help="validate the tables and re-check the existing build")
    ap.add_argument("--validate-only", action="store_true",
                    help="only check the source tables and bodies.tsv, render nothing, write nothing")
    ap.add_argument("--try", dest="try_keys", nargs="+", metavar="KEY",
                    help="render the clips of these clip keys into --out (no cache, no Sounds/), with timing")
    ap.add_argument("--out", help="scratch folder for --try")
    ap.add_argument("--jobs", type=int, default=max(1, min(8, (os.cpu_count() or 2) // 2)),
                    help="parallel render processes (default half the CPUs, at most 8)")
    ap.add_argument("--fetch-voices", action="store_true", help="download missing voice models into audio/voices")
    ap.add_argument("--piper-python", default=os.environ.get("LONAPSE_PIPER_PYTHON", os.environ.get("QUINDAR_PIPER_PYTHON", str(DEFAULT_PIPER_PY))),
                    help="Python that has piper-tts installed (default audio/.venv/bin/python, "
                         "or $LONAPSE_PIPER_PYTHON)")
    ap.add_argument("--voice-m", help="voice for male crew (default %s)" % DELIVERY["m"]["voice"])
    ap.add_argument("--voice-f", help="voice for female crew (default %s)" % DELIVERY["f"]["voice"])
    ap.add_argument("--voice-mc", help="voice for Mission Control (default %s)" % DELIVERY["mc"]["voice"])
    args = ap.parse_args()
    for k, v in (("m", args.voice_m), ("f", args.voice_f), ("mc", args.voice_mc)):
        if v:
            DELIVERY[k]["voice"] = v
    PIPER_PY = Path(args.piper_python)
    if args.try_keys and not args.out:
        ap.error("--try needs --out DIR")

    try:
        src_rows, rows, errors, files, bodies = load_all()
        log("Tables: %d source rows in %d files under %s, %d bodies in %s"
            % (len(src_rows), len(files), SOURCE_DIR.relative_to(ROOT.parent), len(bodies), BODIES_FILE.name))
        for line in summary(src_rows, rows, files):
            log(line)
        if errors:
            for e in errors:
                log("ERROR table: " + e)
            log("Table check FAILED with %d problem(s)" % len(errors))
            return 1
        log("Table check OK (columns by name, unique keys and scenes across files, allowed values, phase and cause, "
            "{body}, seq 1..n, no intercom in a serious phase, trait, persona and topic, explosion words only "
            "in part_lost, core, beep and probe roles, minimum counts per event and chatter band)")
        if args.validate_only:
            return 0

        if args.try_keys:
            check_piper(args.fetch_voices)
            return try_render(rows, args.try_keys, args.out, args.jobs)

        if not args.verify_only:
            check_piper(args.fetch_voices)
        if args.clean and not args.verify_only:
            if BUILD.exists():
                shutil.rmtree(BUILD)
                log("Removed %s" % BUILD)
            remove_owned()

        if args.verify_only:
            jobs = clip_jobs(rows)
            problems = check_build_table(rows)
            clips, total, vproblems = verify(rows, jobs, check_hash_against=True)
            problems += vproblems
        else:
            jobs = build(rows, args.jobs)
            clips, total, problems = verify(rows, jobs)
            if not problems:
                write_manifest(rows, clips, total)
                log("Total size of %s: %.3f MB" % (SOUNDS, total / 1048576.0))
        if problems:
            for p in problems:
                log("FAIL " + p)
            log("Verification FAILED with %d problem(s)" % len(problems))
            return 1
        log("Verification OK")
        return 0
    except BuildError as e:
        log("ERROR " + str(e))
        return 1


if __name__ == "__main__":
    sys.exit(main())
