// Lonapse phrase table: the reversed-text helpers and the TSV parser for phrases.tsv.
// Pure C# (C# 4 syntax, no Unity). Compiled by tests/run.sh with plain Mono.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lonapse
{
    public sealed class PhraseEntry
    {
        public string ClipKey;
        public DialogueLine Dialogue;
        public string DialogueRole;
        public bool PreparedSpeech, IsReply;
        public string RuntimeWhen;
        public string Event;
        public string Severity;
        public string Band;
        public string Role;
        public string Scene;
        public int Seq;
        public string Gender;
        // Flight phase of the row: any, a phase name, serious or idle. "any" when the table has no column.
        public string Phase = "any";
        // Stress cause of the row: any or a StressModel component name. "any" when the table has no column.
        public string Cause = "any";
        // The crew speaker must have this trait (any pilot engineer scientist tourist). "any" without column.
        public string Trait = "any";
        // The crew speaker persona (any badass brave nervous dim veteran). "any" without column.
        public string Persona = "any";
        // Topic (science: experiment id, soi_enter: home, otherwise any). "any" without column.
        public string Topic = "any";
        // Probe core of the scene (any or a ProbeCores key). "any" without column or when empty.
        public string Core = "any";
        // Beep mood of a probe row (a ProbeCores.Moods value). Empty on every other row.
        public string Beep = "";
        // Body key of a {body} variant (kerbin, mun, ..., generic). Empty for a row without {body}.
        public string Body = "";
        public string Spanish;
        public string LonapseText;
        // English subtitle text (optional column "english"), empty when the table has none
        public string English = "";
        // Sounds/ folder of the clip, e.g. "crew/calm" (from the built table, or PhraseTable.FolderFor)
        public string Folder;
        // Runtime voice: the spanish_rt column, one Spanish version per english version (split by |, same order).
        // An empty version: the recorded clip says it. Null when the row has none.
        public List<string> SpanishRt;
        // Runtime voice: the Spanish of the filled subtitle (from SpanishRt), marked for RuntimeSpeech. Set by
        // SubtitleSlots.Fill, null when the recorded clip says the line.
        public string RuntimeSpanish;

        /// <summary>The spanish_rt of english version k, null or empty when the recorded clip says it.</summary>
        public string SpanishRtVersion(int k)
        {
            return SpanishRt != null && k >= 0 && k < SpanishRt.Count ? SpanishRt[k] : null;
        }

        public bool HasBody { get { return !string.IsNullOrEmpty(Body); } }

        // A probe row has no voice clip, only a subtitle (and a beep at runtime)
        public bool SubtitleOnly { get { return PhraseTable.IsSubtitleOnlyRole(Role); } }

        // Subtitle text: English only, the Lonapse text when a table has no english column
        public string SubtitleText
        {
            get { return !string.IsNullOrEmpty(English) ? English : LonapseText; }
        }

        // GameDatabase URL of one clip, gender "m" or "f": Lonapse/Sounds/crew/calm/chatter_calm_01_f
        public string ClipUrl(string gender)
        {
            return "Lonapse/Sounds/" + Folder + "/" + ClipKey + "_" + gender;
        }
    }

    public sealed class PhraseTable
    {
        public static readonly string[] Events = {
            "chatter", "staging", "chute_semi", "chute_full", "throttle_full", "throttle_cut",
            "dock_approach", "dock_close", "dock_hard", "docked", "touchdown", "splashdown", "relief",
            "plan_node", "burn_soon", "station_check", "eva_check", "eva_fall", "countdown",
            // game events
            "soi_enter", "science", "part_lost", "power_low", "power_ok", "signal_lost", "signal_back", "crew_lost",
            // probe mode
            "probe_chat", "probe_sleepy",
            // probe mode: the probe calls Mission Control first, stress per cause, "say again?" on a weak
            // signal, milestones, rover driving, data transmission
            "probe_hail", "probe_stress", "probe_say_again", "probe_milestone", "probe_rover", "probe_data",
            // The probe and the crew on one vessel talk in the cabin
            ProbeCrewEvent };
        /// <summary>
        /// A crewed vessel with a probe core. The probe beeps in the cabin and a crew member
        /// answers, or the other way around. Probe and crew rows, no MC rows, never in a serious phase. Not a probe
        /// mode event: it only plays with crew aboard. Same as PROBE_CREW_EVENT in audio/build_lonapse.py.
        /// </summary>
        public const string ProbeCrewEvent = "probe_crew";
        // Events that only play in probe mode. Same as PROBE_EVENTS in audio/build_lonapse.py.
        public static readonly string[] ProbeEvents = {
            "probe_chat", "probe_sleepy", "probe_hail", "probe_stress", "probe_say_again", "probe_milestone", "probe_rover",
            "probe_data" };
        // Existing events that also have probe scenes (marked by a core or a probe-mode role).
        // Same as PROBE_VARIANT_EVENTS in audio/build_lonapse.py.
        public static readonly string[] ProbeVariantEvents = {
            "science", "power_low", "power_ok", "soi_enter", "part_lost", "signal_lost", "signal_back",
            // The probe reacts to the flight events and callouts too
            "touchdown", "splashdown", "docked", "burn_soon", "staging", "chute_semi", "chute_full", "throttle_full",
            "throttle_cut", "plan_node" };
        public static readonly string[] Severities = { "chatter", "callout", "warning" };
        public static readonly string[] Bands = { "calm", "tense", "panic", "none" };
        public static readonly string[] Roles = { "crew", "mc", "probe" };
        // Roles. Subtitle-only roles only have english (spanish stays empty). In probe mode only Mission Control and
        // the probe talk.
        public const string RoleProbe = "probe";
        public const string RoleCrew = "crew";
        public static readonly string[] SubtitleOnlyRoles = { "probe" };
        // Roles that only appear in probe mode (every role except crew and mc)
        public static readonly string[] ProbeRoles = { "probe" };
        // A probe subtitle is short and the same form everywhere: "[beeps excitedly]", "[beeps very slowly]".
        static readonly Regex ProbeSubtitleRe = new Regex(@"^\[beeps( [a-z]+){1,3}\]$", RegexOptions.CultureInvariant);
        public static bool ProbeSubtitleOk(string english) { return english != null && ProbeSubtitleRe.IsMatch(english); }
        // Column values: core any or a ProbeCores key, beep a ProbeCores mood
        public static readonly string[] Cores = BuildCores();
        public static string[] Moods { get { return ProbeCores.Moods; } }

        static string[] BuildCores()
        {
            string[] r = new string[ProbeCores.Keys.Length + 1];
            r[0] = "any";
            Array.Copy(ProbeCores.Keys, 0, r, 1, ProbeCores.Keys.Length);
            return r;
        }

        public static bool IsSubtitleOnlyRole(string role) { return Array.IndexOf(SubtitleOnlyRoles, role) >= 0; }
        public static bool IsProbeRole(string role) { return Array.IndexOf(ProbeRoles, role) >= 0; }
        public static bool IsProbeEvent(string ev) { return Array.IndexOf(ProbeEvents, ev) >= 0; }
        public static bool IsProbeVariantEvent(string ev) { return Array.IndexOf(ProbeVariantEvents, ev) >= 0; }
        public static bool IsProbeCrewEvent(string ev) { return ev == ProbeCrewEvent; }

        /// <summary>The one clip gender a role has: "m" for mc, null for crew (both possible) and for the
        /// subtitle-only roles (no clip).</summary>
        public static string FixedGender(string role)
        {
            if (role == "mc") return "m";
            return null;
        }

        /// <summary>A scene for probe mode. True when its core is not any, when a line has a probe-mode
        /// role (probe), or when its event is a probe-only event. Crew pickers skip
        /// these scenes, probe mode uses only these.</summary>
        public static bool IsProbeScene(PhraseScene sc)
        {
            return sc != null && sc.IsProbe;
        }

        // Core matching: any (or null or empty) on either side matches. Exact: the row core equals the current core.
        public static bool CoreMatches(string rowCore, string current) { return AnyOrEqual(rowCore, current); }
        public static bool CoreMatchesExact(string rowCore, string current)
        {
            return (string.IsNullOrEmpty(rowCore) ? Any : rowCore) == (string.IsNullOrEmpty(current) ? Any : current);
        }

        /// <summary>Probe scenes (IsProbeScene) with this event whose phase matches and whose core fits.
        /// coreExact: only scenes with exactly this core (null core: only any scenes). Otherwise the core scenes and
        /// the any scenes. The caller can try exact first and fall back. Band is not filtered.</summary>
        public List<PhraseScene> ProbeScenesFor(string evt, string core, string phase, bool coreExact,
                                                Predicate<PhraseScene> filter)
        {
            List<PhraseScene> r = new List<PhraseScene>();
            foreach (PhraseScene sc in Scenes)
            {
                if (sc.Event != evt || !sc.IsProbe) continue;
                if (!PhaseMatches(sc.Phase, phase)) continue;
                if (coreExact ? !CoreMatchesExact(sc.Core, core) : !CoreMatches(sc.Core, core)) continue;
                if (filter != null && !filter(sc)) continue;
                r.Add(sc);
            }
            return r;
        }

        // ---- placeholders ----
        static readonly Regex PlaceholderRe = new Regex(@"\{([^{}]*)\}", RegexOptions.CultureInvariant);

        // Placeholder rules, same as validate() in audio/build_lonapse.py. The spanish only knows {body}: the audio is
        // fixed. The english may also hold subtitle slots ({thing}, {thing:2}, {day}, see SubtitleSlots), filled in
        // game. {body} at most once per text, and for a voiced row in spanish and english or in neither (a
        // subtitle-only row has no spanish). Whether a pool exists, and that {other} and {self} are only in
        // intercom scenes, is checked by the build script. Adds a description per problem to bad.
        /// <summary>
        /// The spanish_rt column: one piece per english version (split by |), an empty piece for a version the
        /// recorded clip says. A piece uses only slots of its english version ({el thing} counts as {thing}), and a
        /// version with a number the recording cannot say ({fuel} ...) needs one. Same rules as check_spanish_rt in
        /// audio/build_lonapse.py.
        /// </summary>
        static void CheckSpanishRt(PhraseEntry e, string rt, List<string> bad)
        {
            List<string> en = SubtitleSlots.Versions(e.English);
            if (string.IsNullOrEmpty(rt))
            {
                foreach (string v in en)
                    if (SubtitleSlots.HasRuntimeNumber(v)) { bad.Add("a version with a runtime number needs spanish_rt: '" + v + "'"); return; }
                return;
            }
            if (e.SubtitleOnly) { bad.Add("spanish_rt on a subtitle-only row"); return; }
            List<string> es = SubtitleSlots.Versions(rt);
            if (es.Count != en.Count) { bad.Add("spanish_rt has " + es.Count + " versions for " + en.Count + " english, split both by | in the same order"); return; }
            bool any = false;
            for (int k = 0; k < es.Count; k++)
            {
                string text;
                SubtitleSlots.SplitConditions(en[k], null, out text);
                if (es[k].Length == 0)
                {
                    if (SubtitleSlots.HasRuntimeNumber(text)) { bad.Add("version " + (k + 1) + " has a runtime number and needs its spanish_rt"); return; }
                    continue;
                }
                any = true;
                if (es[k].IndexOf('[') == 0) { bad.Add("spanish_rt version " + (k + 1) + ": leave out the [conditions]"); return; }
                var have = SubtitleSlots.Names(text);
                if (text.Contains("{body}")) have.Add("body");
                foreach (System.Text.RegularExpressions.Match m in SubtitleSlots.EsSlotRe.Matches(es[k]))
                    if (!have.Contains(m.Groups[2].Value)) { bad.Add("spanish_rt version " + (k + 1) + " uses {" + m.Groups[2].Value + "}, its english does not"); return; }
                string rest = SubtitleSlots.EsSlotRe.Replace(es[k], "");
                if (rest.IndexOf('{') >= 0 || rest.IndexOf('}') >= 0) { bad.Add("spanish_rt version " + (k + 1) + " has a bad slot"); return; }
            }
            if (any) e.SpanishRt = es;
        }

        static void CheckPlaceholders(PhraseEntry e, List<string> bad)
        {
            List<string> es = Placeholders(e.Spanish), en = Placeholders(e.English);
            List<string> unknown = new List<string>();
            foreach (string p in es) if (p != "body" && !unknown.Contains(p)) unknown.Add(p);
            if (unknown.Count > 0)
            {
                bad.Add("spanish placeholder(s) {" + string.Join("}, {", unknown.ToArray()) + "}, only {body} exists there");
                return;
            }
            foreach (string p in en) if (p != "body" && !IsSlot(p) && !unknown.Contains(p)) unknown.Add(p);
            if (unknown.Count > 0)
            {
                bad.Add("bad placeholder(s) {" + string.Join("}, {", unknown.ToArray()) + "}, use {body} or a slot like {name} or {name:2}");
                return;
            }
            if (e.English != null && e.English.IndexOf(SubtitleSlots.VersionSep) >= 0)
            {
                // versions split by |: each one non-empty, and not together with {body} (that is one row per body)
                bool plain = false;
                foreach (string v in SubtitleSlots.Versions(e.English))
                {
                    if (v.Length == 0) { bad.Add("english has an empty version between |"); return; }
                    var conds = new List<string>();
                    string text;
                    if (!SubtitleSlots.SplitConditions(v, conds, out text) || text.Length == 0)
                    {
                        bad.Add("bad version '" + v + "': use [condition] text, conditions are "
                                + string.Join(" ", SubtitleSlots.Conditions));
                        return;
                    }
                    if (conds.Count == 0) plain = true;
                }
                if (!plain) { bad.Add("english versions need at least one version without [ ]"); return; }
                if (en.Contains("body")) { bad.Add("english versions (|) cannot use {body}"); return; }
            }
            int bEs = es.FindAll(p => p == "body").Count, bEn = en.FindAll(p => p == "body").Count;
            if (!e.SubtitleOnly && (bEs > 0) != (bEn > 0)) bad.Add("{body} must be in both spanish and english, or in neither");
            else if (bEs > 1 || bEn > 1) bad.Add("{body} at most once per text");
        }

        // name or name:n with n 1..9
        static bool IsSlot(string p)
        {
            string n = SlotName(p);
            if (n == "body" || !SubtitleSlots.IsSlotName(n)) return false;
            if (n.Length == p.Length) return true;
            return p.Length == n.Length + 2 && p[n.Length] == ':' && p[n.Length + 1] >= '1' && p[n.Length + 1] <= '9';
        }

        static string SlotName(string p)
        {
            int c = p.IndexOf(':');
            return c < 0 ? p : p.Substring(0, c);
        }

        static List<string> Placeholders(string text)
        {
            List<string> r = new List<string>();
            if (string.IsNullOrEmpty(text)) return r;
            foreach (Match m in PlaceholderRe.Matches(text)) r.Add(m.Groups[1].Value);
            return r;
        }
        public static readonly string[] Genders = { "m", "f", "both" };
        // Same values as PHASES and CAUSES in audio/build_lonapse.py
        public static readonly string[] Phases = {
            "any", "prelaunch", "launch", "ascent", "orbit", "coast", "reentry", "descent", "landed", "blackout",
            "serious", "idle" };
        public static readonly string[] SeriousPhases = { "launch", "ascent", "reentry", "descent" };
        public static readonly string[] CalmPhases = { "prelaunch", "orbit", "coast", "landed" };
        public static readonly string[] Causes = {
            "any", "spin", "gforce", "q", "ground", "heat", "plasma", "fuel", "impulse", "engine_out" };
        public const string GenericBody = "generic";
        /// <summary>The home body: body scenes listed in NOT_AT_HOME (build_lonapse.py) have no variant for it.</summary>
        public const string HomeBody = "kerbin";
        // Same values as TRAITS, PERSONAS and TOPICS_BY_EVENT in audio/build_lonapse.py
        public const string Any = "any";
        public static readonly string[] Traits = { "any", "pilot", "engineer", "scientist", "tourist", "to_tourist", "tourist_pair" };
        public static readonly string[] Personas = { "any", "badass", "brave", "nervous", "dim", "veteran" };
        public static readonly string[] ScienceTopics = {
            "any", "crewreport", "evareport", "mysterygoo", "surfacesample", "mobilematerialslab",
            "temperaturescan", "barometerscan", "seismicscan", "gravityscan", "atmosphereanalysis",
            "asteroidsample", "cometsample", "infraredtelescope", "magnetometer", "roc" };
        public static readonly string[] SoiTopics = { "any", "home" };
        // Probe staging (what the stage holds), milestones and rover moments
        public static readonly string[] StagingTopics = { "any", "separation", "ignition" };
        public static readonly string[] MilestoneTopics = { "any", "day1", "day10", "day100", "day365", "orbit", "landing" };
        public static readonly string[] RoverTopics = { "any", "fast", "air", "tilt", "idle" };
        // probe_crew topic staging: a tourist calls the stage, the probe reacts (only tourists aboard, at a staging)
        public const string StagingJokeTopic = "staging";
        public static readonly string[] ProbeCrewTopics = { "any", StagingJokeTopic };
        // A scene about solar panels (chatter, power_low, power_ok) only plays on a ship that has one.
        // Chatter also has part topics: prechute (a chute is still packed and none is open), chuteopen (a chute
        // is open) and heatshield (the ship has a heat shield).
        public const string SolarTopic = "solar";
        public const string PreChuteTopic = "prechute";
        public const string ChuteOpenTopic = "chuteopen";
        public const string HeatShieldTopic = "heatshield";
        public static readonly string[] SolarTopics = { "any", SolarTopic };
        public static readonly string[] ChatterTopics = { "any", SolarTopic, PreChuteTopic, ChuteOpenTopic, HeatShieldTopic };

        /// <summary>A part topic only plays when the ship has that part in that state. Other topics always pass.</summary>
        public static bool PartTopicOk(string topic, bool hasSolar, bool hasShield, bool chuteStowed, bool chuteOpen)
        {
            if (topic == SolarTopic) return hasSolar;
            if (topic == HeatShieldTopic) return hasShield;
            if (topic == PreChuteTopic) return chuteStowed && !chuteOpen;
            if (topic == ChuteOpenTopic) return chuteOpen;
            return true;
        }

        /// <summary>
        /// Topic cycle for the Debug tab phrase test: "" (any) first, then every science topic and the soi_enter
        /// topics, each once and without "any".
        /// </summary>
        public static string[] TopicCycle()
        {
            var list = new List<string> { "" };
            foreach (string[] set in new[] { ScienceTopics, SoiTopics, StagingTopics, MilestoneTopics, RoverTopics })
                foreach (string t in set)
                    if (t != "any" && !list.Contains(t)) list.Add(t);
            return list.ToArray();
        }
        public const string ExplosionEvent = "part_lost";

        // Columns every table must have (found by name, any order). Kept under the old name Columns.
        public static readonly string[] Columns = {
            "clip_key", "event", "severity", "band", "role", "scene", "seq", "gender", "spanish" };
        public const string PhaseColumn = "phase";
        public const string CauseColumn = "cause";
        public const string LonapseColumn = "lonapse_text";
        public const string FolderColumn = "folder";
        public const string BodyColumn = "body";
        public const string EnglishColumn = "english";
        public const string TraitColumn = "trait";
        public const string PersonaColumn = "persona";
        public const string TopicColumn = "topic";
        public const string CoreColumn = "core";
        public const string BeepColumn = "beep";
        public const string SpanishRtColumn = "spanish_rt";
        // Optional columns. A column outside Columns and Optional is ignored.
        public static readonly string[] Optional = {
            PhaseColumn, CauseColumn, LonapseColumn, FolderColumn, BodyColumn, EnglishColumn,
            TraitColumn, PersonaColumn, TopicColumn, CoreColumn, BeepColumn, SpanishRtColumn, "runtime", "when", "dialogue" };

        // Topics allowed for an event: ScienceTopics for science, SoiTopics for soi_enter, StagingTopics,
        // MilestoneTopics and RoverTopics, only "any" otherwise.
        public static string[] TopicsFor(string ev)
        {
            if (ev == "science") return ScienceTopics;
            if (ev == "soi_enter") return SoiTopics;
            if (ev == "staging") return StagingTopics;
            if (ev == "probe_milestone") return MilestoneTopics;
            if (ev == "probe_rover") return RoverTopics;
            if (ev == ProbeCrewEvent) return ProbeCrewTopics;
            if (ev == "chatter") return ChatterTopics;
            if (ev == "power_low" || ev == "power_ok") return SolarTopics;
            return new string[] { Any };
        }

        // Explosion words, only allowed in part_lost rows. Same rule as EXPLOSION_RE in build_lonapse.py:
        // a word that starts with explo (not explor), boom, blast (not "blast off"), explot or estall, or "blew up",
        // "blew apart", "blew out", "blew off". Case-insensitive.
        static readonly Regex ExplosionRe = new Regex(
            @"\b(?:explo(?!r)|boom|blew\s+(?:up|apart|out|off)\b|blast(?!\s*-?\s*off)|explot|estall)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool HasExplosionWord(string text)
        {
            return !string.IsNullOrEmpty(text) && ExplosionRe.IsMatch(text);
        }

        // Sounds/ folder of a row. Must match folder_for in
        // audio/build_lonapse.py (the tests compare both on the built table).
        // The subtitle-only roles have no clip and no folder ("").
        public static string FolderFor(string role, string severity, string band, string ev)
        {
            if (IsSubtitleOnlyRole(role)) return "";
            if (role == "mc") return (band == "tense" || band == "panic") ? "control/tense" : "control/calm";
            if (severity == "callout") return "crew/callout";
            if (ev == "relief") return "crew/relief";
            if (band == "calm" || band == "tense" || band == "panic") return "crew/" + band;
            return "crew/calm";
        }

        readonly List<PhraseEntry> entries = new List<PhraseEntry>();
        readonly Dictionary<string, PhraseEntry> byKey = new Dictionary<string, PhraseEntry>();

        public IList<PhraseEntry> Entries { get { return entries; } }
        public int Count { get { return entries.Count; } }

        public PhraseEntry Get(string clipKey)
        {
            PhraseEntry e;
            if (clipKey != null && byKey.TryGetValue(clipKey, out e)) return e;
            return null;
        }

        // Rows with exactly this event, band and role, in table order. Gender is not filtered here.
        public List<PhraseEntry> Find(string ev, string band, string role)
        {
            List<PhraseEntry> result = new List<PhraseEntry>();
            foreach (PhraseEntry e in entries)
            {
                if (e.Event == ev && e.Band == band && e.Role == role) result.Add(e);
            }
            return result;
        }

        // Parses a source table or the generated phrases.tsv. Columns are found by header name: the Columns are
        // required, phase, cause, trait, persona, topic, lonapse_text, folder, body and english are optional, other
        // columns are ignored. Missing (or empty) phase, cause, trait, persona and topic give "any" (phase and cause:
        // missing column only), a missing body gives "". Without lonapse_text, LonapseText is
        // computed with LonapseText.ReverseText. Without folder, Folder is computed with FolderFor. A folder that
        // disagrees with FolderFor is an error. A later line that starts with clip_key is a new header, so
        // concatenated source files parse too (IsHeaderLine). Bad rows are skipped and described in errors (one line each).
        public static PhraseTable Parse(IList<string> lines, List<string> errors)
        {
            if (errors == null) errors = new List<string>();
            PhraseTable table = new PhraseTable();
            if (lines == null) { errors.Add("no input"); return table; }

            Dictionary<string, int> col = null;
            int width = 0;
            bool anyHeader = false;
            Dictionary<string, PhraseEntry> sceneHead = new Dictionary<string, PhraseEntry>();
            HashSet<string> seenSeq = new HashSet<string>();

            for (int i = 0; i < lines.Count; i++)
            {
                string raw = lines[i];
                if (raw == null) continue;
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0) continue;
                if (line.TrimStart().StartsWith("#")) continue;
                int lineNo = i + 1;

                string[] f = line.Split('\t');
                for (int k = 0; k < f.Length; k++) f[k] = f[k].Trim();

                if (col == null || IsHeaderLine(f))
                {
                    string why;
                    col = ReadHeader(f, out why);
                    if (col == null)
                    {
                        errors.Add("line " + lineNo + ": header must hold the columns " + string.Join("\\t", Columns) +
                                   " (optional: " + string.Join(", ", Optional) + ")" + why);
                        return table;
                    }
                    width = f.Length;
                    anyHeader = true;
                    continue;
                }

                // a missing last cell is an empty spanish_rt (editors drop a trailing tab)
                if (f.Length == width - 1 && col.ContainsKey(SpanishRtColumn) && col[SpanishRtColumn] == width - 1)
                {
                    var pad = new string[width];
                    f.CopyTo(pad, 0);
                    pad[width - 1] = "";
                    f = pad;
                }
                if (f.Length != width)
                {
                    errors.Add("line " + lineNo + ": expected " + width + " columns, got " + f.Length);
                    continue;
                }

                PhraseEntry e = new PhraseEntry();
                e.ClipKey = f[col["clip_key"]];
                if (col.ContainsKey("dialogue")) { e.Dialogue = DialogueTable.Find(f[col["dialogue"]]); e.DialogueRole = e.Dialogue != null ? e.Dialogue.Get("role") : null; }
                e.Event = f[col["event"]];
                e.Severity = f[col["severity"]];
                e.Band = f[col["band"]];
                e.Role = f[col["role"]];
                e.Scene = f[col["scene"]];
                e.Gender = f[col["gender"]];
                e.Spanish = f[col["spanish"]];
                string seqText = f[col["seq"]];
                bool hasEnglish = col.ContainsKey(EnglishColumn);
                if (hasEnglish) e.English = f[col[EnglishColumn]];
                if (col.ContainsKey(PhaseColumn)) e.Phase = FlightPhase.Normalize(f[col[PhaseColumn]]);
                if (col.ContainsKey(CauseColumn)) e.Cause = f[col[CauseColumn]];
                if (col.ContainsKey(BodyColumn)) e.Body = f[col[BodyColumn]];
                if (col.ContainsKey(TraitColumn) && f[col[TraitColumn]].Length > 0) e.Trait = f[col[TraitColumn]];
                if (col.ContainsKey(PersonaColumn) && f[col[PersonaColumn]].Length > 0) e.Persona = f[col[PersonaColumn]];
                if (col.ContainsKey(TopicColumn) && f[col[TopicColumn]].Length > 0) e.Topic = f[col[TopicColumn]];
                if (col.ContainsKey(CoreColumn) && f[col[CoreColumn]].Length > 0) e.Core = f[col[CoreColumn]];
                if (col.ContainsKey(BeepColumn)) e.Beep = f[col[BeepColumn]];
                string rt = col.ContainsKey(SpanishRtColumn) ? f[col[SpanishRtColumn]] : "";

                List<string> bad = new List<string>();
                if (!IsKey(e.ClipKey)) bad.Add("clip_key '" + e.ClipKey + "' must match [a-z0-9_]+");
                if (Array.IndexOf(Events, e.Event) < 0) bad.Add("unknown event '" + e.Event + "'");
                if (Array.IndexOf(Severities, e.Severity) < 0) bad.Add("unknown severity '" + e.Severity + "'");
                if (Array.IndexOf(Bands, e.Band) < 0) bad.Add("unknown band '" + e.Band + "'");
                if (Array.IndexOf(Roles, e.Role) < 0) bad.Add("unknown role '" + e.Role + "'");
                if (Array.IndexOf(Phases, e.Phase) < 0) bad.Add("unknown phase '" + e.Phase + "'");
                if (Array.IndexOf(Causes, e.Cause) < 0) bad.Add("unknown cause '" + e.Cause + "'");
                if (Array.IndexOf(Traits, e.Trait) < 0) bad.Add("unknown trait '" + e.Trait + "'");
                if (Array.IndexOf(Personas, e.Persona) < 0) bad.Add("unknown persona '" + e.Persona + "'");
                if (Array.IndexOf(TopicsFor(e.Event), e.Topic) < 0)
                    bad.Add("topic '" + e.Topic + "' not allowed for event '" + e.Event + "'");
                if (e.Event != ExplosionEvent && (HasExplosionWord(e.Spanish) || HasExplosionWord(e.English)))
                    bad.Add("explosion word outside event " + ExplosionEvent);
                if (e.Body.Length > 0 && !IsKey(e.Body)) bad.Add("body '" + e.Body + "' must match [a-z0-9_]+");
                if (!IsKey(e.Scene)) bad.Add("scene '" + e.Scene + "' must match [a-z0-9_]+");
                int seq;
                if (!int.TryParse(seqText, NumberStyles.None, CultureInfo.InvariantCulture, out seq) || seq < 1)
                    bad.Add("seq '" + seqText + "' must be an integer >= 1");
                if (Array.IndexOf(Genders, e.Gender) < 0) bad.Add("unknown gender '" + e.Gender + "'");
                if (e.Role == "mc" && e.Gender == "f") bad.Add("role mc uses the male voice only (gender m or both)");
                bool subOnly = IsSubtitleOnlyRole(e.Role);
                if (subOnly)
                {
                    if (e.Spanish.Length > 0) bad.Add("role " + e.Role + " is subtitle only, spanish must be empty");
                    if (e.English.Length == 0) bad.Add("role " + e.Role + " is subtitle only and needs english");
                }
                else
                {
                    if (e.Spanish.Length == 0) bad.Add("spanish is empty");
                    if (hasEnglish && e.English.Length == 0) bad.Add("english is empty");
                }
                // Core, beep and the probe-mode roles and events
                if (Array.IndexOf(Cores, e.Core) < 0 && !ProbeCores.IsLineset(e.Core))
                    bad.Add("unknown core '" + e.Core + "' (a stock core, or a lineset from a LONAPSE_PROBE_LINES node)");
                if (e.Role == RoleProbe)
                {
                    if (Array.IndexOf(Moods, e.Beep) < 0)
                        bad.Add("probe row needs a beep mood (" + string.Join(" ", Moods) + "), got '" + e.Beep + "'");
                }
                else if (e.Beep.Length > 0) bad.Add("beep is only allowed on probe rows");
                bool probeEv = IsProbeEvent(e.Event), probeOk = probeEv || IsProbeVariantEvent(e.Event) || IsProbeCrewEvent(e.Event);
                if (IsProbeCrewEvent(e.Event) && e.Role == "mc") bad.Add("probe_crew is in the cabin, it cannot have mc rows");
                if (e.Core != Any && !probeOk) bad.Add("core '" + e.Core + "' outside a probe event");
                if (IsProbeRole(e.Role) && !probeOk) bad.Add("role " + e.Role + " outside a probe event");
                if (probeEv && e.Role == "crew") bad.Add("probe event '" + e.Event + "' cannot have crew rows");
                if (e.Role == RoleProbe && !ProbeSubtitleOk(e.English))
                    bad.Add("probe subtitle must look like [beeps excitedly] (beeps and 1 to 3 lowercase words), got '" + e.English + "'");
                if (!col.ContainsKey("runtime")) CheckPlaceholders(e, bad);
                if (col.ContainsKey("when")) e.RuntimeWhen = f[col["when"]];
                if (bad.Count == 0) CheckSpanishRt(e, rt, bad);
                e.Seq = seq;

                if (col.ContainsKey(LonapseColumn))
                {
                    e.LonapseText = f[col[LonapseColumn]];
                    if (e.LonapseText.Length == 0 && !subOnly) bad.Add("lonapse_text is empty");
                }
                else if (e.Spanish.Length > 0)
                {
                    e.LonapseText = LonapseText.ReverseText(e.Spanish);
                }
                e.Folder = FolderFor(e.Role, e.Severity, e.Band, e.Event);
                if (col.ContainsKey(FolderColumn) && f[col[FolderColumn]] != e.Folder)
                    bad.Add("folder '" + f[col[FolderColumn]] + "' does not match the folder rule (" + e.Folder + ")");

                if (table.byKey.ContainsKey(e.ClipKey)) bad.Add("duplicate clip_key '" + e.ClipKey + "'");
                // body variants share scene and seq, one row per body
                if (seq >= 1 && !string.IsNullOrEmpty(e.Scene) && !seenSeq.Add(e.Scene + "#" + seq + "#" + e.Body))
                    bad.Add("duplicate scene/seq " + e.Scene + "/" + seq + (e.HasBody ? " for body " + e.Body : ""));

                PhraseEntry head;
                if (bad.Count == 0 && sceneHead.TryGetValue(e.Scene, out head))
                {
                    if (head.Event != e.Event || head.Band != e.Band || head.Severity != e.Severity
                        || head.Phase != e.Phase || head.Cause != e.Cause
                        || head.Trait != e.Trait || head.Persona != e.Persona || head.Topic != e.Topic
                        || head.Core != e.Core)
                        bad.Add("scene '" + e.Scene + "' mixes event, band, severity, phase, cause, trait, persona, topic or core with its first row");
                }

                if (bad.Count > 0)
                {
                    errors.Add("line " + lineNo + " (" + e.ClipKey + "): " + string.Join("; ", bad.ToArray()));
                    continue;
                }

                if (!sceneHead.ContainsKey(e.Scene)) sceneHead[e.Scene] = e;
                table.entries.Add(e);
                table.byKey[e.ClipKey] = e;
            }

            if (!anyHeader) errors.Add("no header line found");
            return table;
        }


        // A line that names the clip_key, scene and spanish columns is a header (a data row cannot hold all three).
        static bool IsHeaderLine(string[] f)
        {
            return Array.IndexOf(f, "clip_key") >= 0 && Array.IndexOf(f, "scene") >= 0 && Array.IndexOf(f, "spanish") >= 0;
        }

        // Column index by name, or null when a required column is missing or a known column is there twice.
        static Dictionary<string, int> ReadHeader(string[] f, out string why)
        {
            why = "";
            Dictionary<string, int> col = new Dictionary<string, int>();
            for (int i = 0; i < f.Length; i++)
            {
                bool known = Array.IndexOf(Columns, f[i]) >= 0 || Array.IndexOf(Optional, f[i]) >= 0;
                if (!known) continue;
                if (col.ContainsKey(f[i])) { why = ", '" + f[i] + "' is there twice"; return null; }
                col[f[i]] = i;
            }
            List<string> missing = new List<string>();
            foreach (string c in Columns) if (!col.ContainsKey(c)) missing.Add(c);
            if (missing.Count > 0) { why = ", missing " + string.Join(" ", missing.ToArray()); return null; }
            return col;
        }

        // [a-z0-9_]+
        public static bool IsKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        // ---- phase and cause matching ----
        // Same meaning as FlightPhase.Matches: "any" (or null or empty) on either side matches everything,
        // "serious" stands for launch, ascent, reentry and descent, "idle" for prelaunch, orbit, coast and landed.
        // Kept here so PhraseTable compiles on its own in the offline tests.

        public static bool IsSeriousPhase(string p) { return p == "serious" || Array.IndexOf(SeriousPhases, p) >= 0; }
        public static readonly string[] GoofyPhases = CalmPhases; // legacy API name
        public static bool IsGoofyPhase(string p) { return IsCalmPhase(p); }
        public static bool IsIdlePhase(string p) { return FlightPhase.Normalize(p) == FlightPhase.Idle || Array.IndexOf(CalmPhases, p) >= 0; }
        public static bool IsCalmPhase(string p) { return IsIdlePhase(p); }

        public static bool PhaseMatches(string rowPhase, string current)
        {
            rowPhase = FlightPhase.Normalize(rowPhase); current = FlightPhase.Normalize(current);
            if (string.IsNullOrEmpty(rowPhase) || rowPhase == "any") return true;
            if (string.IsNullOrEmpty(current) || current == "any") return true;
            if (rowPhase == current) return true;
            if (rowPhase == "serious") return IsSeriousPhase(current);
            if (rowPhase == FlightPhase.Idle) return IsIdlePhase(current);
            if (current == "serious") return IsSeriousPhase(rowPhase);
            if (current == FlightPhase.Idle) return IsIdlePhase(rowPhase);
            return false;
        }

        public static bool CauseMatches(string rowCause, string current)
        {
            if (string.IsNullOrEmpty(rowCause) || rowCause == "any") return true;
            if (string.IsNullOrEmpty(current) || current == "any") return true;
            return rowCause == current;
        }

        // ---- trait, persona and topic matching ----
        // "any" (or null or empty) on either side matches everything. Pass null to not filter.

        public static bool TraitMatches(string rowTrait, string current) { return AnyOrEqual(rowTrait, current); }
        public static bool PersonaMatches(string rowPersona, string current) { return AnyOrEqual(rowPersona, current); }

        // Topic, loose: a row with topic any fits every topic, a null/empty/any current topic fits every row.
        public static bool TopicMatches(string rowTopic, string current) { return AnyOrEqual(rowTopic, current); }

        // Topic, exact: the row topic equals the current topic. Null or empty counts as "any", so an any request
        // only takes any rows. The caller tries exact first and falls back to TopicMatches when that gives nothing.
        public static bool TopicMatchesExact(string rowTopic, string current)
        {
            string r = string.IsNullOrEmpty(rowTopic) ? Any : rowTopic;
            string c = string.IsNullOrEmpty(current) ? Any : current;
            return r == c;
        }

        static bool AnyOrEqual(string row, string current)
        {
            if (string.IsNullOrEmpty(row) || row == Any) return true;
            if (string.IsNullOrEmpty(current) || current == Any) return true;
            return row == current;
        }

        // Scene filter for trait, persona and topic. Null trait, persona or topic does not filter.
        // topicExact: TopicMatchesExact instead of TopicMatches (then a null topic only takes any scenes).
        public static bool SceneMatches(PhraseScene sc, string trait, string persona, string topic, bool topicExact)
        {
            if (sc == null) return false;
            if (!TraitMatches(sc.Trait, trait)) return false;
            if (!PersonaMatches(sc.Persona, persona)) return false;
            return topicExact ? TopicMatchesExact(sc.Topic, topic) : TopicMatches(sc.Topic, topic);
        }

        // ---- scenes ----

        // True when this row has a clip for the gender "m" or "f". Mission Control rows only exist as "m".
        public static bool HasGender(PhraseEntry e, string g)
        {
            if (e == null) return false;
            return e.Gender == "both" || e.Gender == g;
        }

        // Rows grouped by scene, in table order of the first row. A {body} scene is one scene: its variants
        // are in AllLines, and LinesFor(body) gives the lines for one body. Built once and cached.
        public List<PhraseScene> Scenes
        {
            get
            {
                if (scenes != null) return scenes;
                scenes = new List<PhraseScene>();
                Dictionary<string, PhraseScene> byId = new Dictionary<string, PhraseScene>();
                foreach (PhraseEntry e in entries)
                {
                    PhraseScene sc;
                    if (!byId.TryGetValue(e.Scene, out sc))
                    {
                        sc = new PhraseScene(e.Scene, e.Event, e.Band, e.Severity, e.Phase, e.Cause,
                                             e.Trait, e.Persona, e.Topic, e.Core);
                        byId[e.Scene] = sc;
                        scenes.Add(sc);
                    }
                    sc.AllLines.Add(e);
                }
                foreach (PhraseScene sc in scenes) sc.Seal();
                return scenes;
            }
        }
        List<PhraseScene> scenes;

        // Scenes with this event (and band, unless band is null) that pass the filter.
        public List<PhraseScene> ScenesFor(string ev, string band, Predicate<PhraseScene> filter)
        {
            return ScenesFor(ev, band, null, null, filter);
        }

        // Scenes with this event (and band, unless null) whose phase matches the current phase and whose cause
        // matches the current cause (PhaseMatches, CauseMatches). Null or empty phase or cause means any.
        public IList<PhraseScene> ScenesFor(string evt, string band, string phase, string cause)
        {
            return ScenesFor(evt, band, phase, cause, null);
        }

        // The same with an extra filter (for example PhraseClips.Playable).
        public List<PhraseScene> ScenesFor(string evt, string band, string phase, string cause, Predicate<PhraseScene> filter)
        {
            List<PhraseScene> r = new List<PhraseScene>();
            foreach (PhraseScene sc in Scenes)
            {
                if (sc.Event != evt) continue;
                if (band != null && sc.Band != band) continue;
                if (!PhaseMatches(sc.Phase, phase)) continue;
                if (!CauseMatches(sc.Cause, cause)) continue;
                if (filter != null && !filter(sc)) continue;
                r.Add(sc);
            }
            return r;
        }

        // The same plus trait, persona and topic (SceneMatches). Null trait, persona or topic does not filter.
        public List<PhraseScene> ScenesFor(string evt, string band, string phase, string cause,
                                           string trait, string persona, string topic, bool topicExact,
                                           Predicate<PhraseScene> filter)
        {
            List<PhraseScene> r = ScenesFor(evt, band, phase, cause, filter);
            r.RemoveAll(sc => !SceneMatches(sc, trait, persona, topic, topicExact));
            return r;
        }

        // Uniform pick that avoids `avoidId` (the last scene played) when there is another choice. Null when empty.
        public static PhraseScene Pick(IList<PhraseScene> list, Random rng, string avoidId)
        {
            if (list == null || list.Count == 0) return null;
            if (list.Count == 1) return list[0];
            int n = list.Count;
            int i = rng.Next(n);
            if (avoidId != null && list[i].Id == avoidId) i = (i + 1 + rng.Next(n - 1)) % n;
            return list[i];
        }
    }

    // One conversation: rows of one scene in seq order. A crew-only scene with two or more lines is an
    // intercom talk between crew members. Every other scene is a radio exchange with Mission Control.
    // A {body} scene holds one variant per body for its {body} lines. Lines is the generic version
    // (LinesFor("generic")), so Lines.Count, CrewLines and McLines count each seq once.
    public sealed class PhraseScene
    {
        public readonly string Id, Event, Band, Severity, Phase, Cause;
        // "any" when the table has no column
        public readonly string Trait, Persona, Topic;
        // Probe core of the scene, "any" when the table has no column
        public readonly string Core;
        // Every row of the scene, body variants included, ordered by seq and then body
        public readonly List<PhraseEntry> AllLines = new List<PhraseEntry>();
        // The generic lines: one row per seq (rows without a body, plus the generic variant)
        public readonly List<PhraseEntry> Lines = new List<PhraseEntry>();
        // Body keys this scene has variants for, empty for a scene without {body}
        public readonly List<string> Bodies = new List<string>();
        public int CrewLines { get; private set; }
        public int McLines { get; private set; }
        // Lines with a probe-mode role (probe) and subtitle-only lines
        public int ProbeRoleLines { get; private set; }
        public int SubtitleOnlyLines { get; private set; }
        // See PhraseTable.IsProbeScene
        public bool IsProbe { get; private set; }
        public bool FemaleOk { get; private set; }   // every crew line has an "f" clip
        public bool MaleOk { get; private set; }     // every crew line has an "m" clip
        readonly Dictionary<string, List<PhraseEntry>> forBody = new Dictionary<string, List<PhraseEntry>>();

        public PhraseScene(string id, string ev, string band, string severity)
            : this(id, ev, band, severity, "any", "any") { }

        public PhraseScene(string id, string ev, string band, string severity, string phase, string cause)
            : this(id, ev, band, severity, phase, cause, "any", "any", "any") { }

        public PhraseScene(string id, string ev, string band, string severity, string phase, string cause,
                           string trait, string persona, string topic)
            : this(id, ev, band, severity, phase, cause, trait, persona, topic, "any") { }

        public PhraseScene(string id, string ev, string band, string severity, string phase, string cause,
                           string trait, string persona, string topic, string core)
        {
            Core = string.IsNullOrEmpty(core) ? "any" : core;
            Id = id; Event = ev; Band = band; Severity = severity;
            Phase = string.IsNullOrEmpty(phase) ? "any" : FlightPhase.Normalize(phase);
            Cause = string.IsNullOrEmpty(cause) ? "any" : cause;
            Trait = string.IsNullOrEmpty(trait) ? "any" : trait;
            Persona = string.IsNullOrEmpty(persona) ? "any" : persona;
            Topic = string.IsNullOrEmpty(topic) ? "any" : topic;
        }

        public bool IsIntercom { get {
            if (McLines != 0 || ProbeRoleLines != 0) return false;
            if (Lines.Count > 0 && Lines[0].Dialogue != null)
            {
                string channel = Lines[0].Dialogue.Get("channel");
                if (channel == "intercom") return true;
                if (channel == "radio") return false;
            }
            return CrewLines >= 2;
        } }
        public bool StartsWithMc { get { return Lines.Count > 0 && Lines[0].Role == "mc"; } }
        public bool HasBody { get { return Bodies.Count > 0; } }
        public bool GenderOk(bool female) { return female ? FemaleOk : MaleOk; }

        // The lines for one body (lowercase bodyName, or "generic"): per seq the row without a body, else the
        // variant for this body, else the generic variant. A seq with none of these is left out. Null or empty
        // body means generic. The list is cached per body, do not change it.
        public List<PhraseEntry> LinesFor(string body)
        {
            if (string.IsNullOrEmpty(body)) body = PhraseTable.GenericBody;
            List<PhraseEntry> r;
            if (forBody.TryGetValue(body, out r)) return r;
            r = new List<PhraseEntry>();
            int i = 0;
            while (i < AllLines.Count)
            {
                int seq = AllLines[i].Seq;
                PhraseEntry plain = null, exact = null, generic = null;
                for (; i < AllLines.Count && AllLines[i].Seq == seq; i++)
                {
                    PhraseEntry e = AllLines[i];
                    if (!e.HasBody) { if (plain == null) plain = e; }
                    else if (e.Body == body) exact = e;
                    else if (e.Body == PhraseTable.GenericBody) generic = e;
                }
                // Kerbin is home: a scene without a Kerbin variant ("you can't take {body} home") does not fall back
                // to the generic planet there, the seq is left out and PlaysAt says no
                if (body == PhraseTable.HomeBody) generic = null;
                PhraseEntry pick = plain ?? exact ?? generic;
                if (pick != null) r.Add(pick);
            }
            forBody[body] = r;
            return r;
        }

        /// <summary>Every seq of the scene has a line for this body (see LinesFor). False for a body scene that has
        /// no variant for Kerbin, at Kerbin.</summary>
        public bool PlaysAt(string body)
        {
            if (!HasBody) return true;
            if (seqCount < 0)
            {
                var seqs = new HashSet<int>();
                foreach (PhraseEntry e in AllLines) seqs.Add(e.Seq);
                seqCount = seqs.Count;
            }
            return LinesFor(body).Count == seqCount;
        }
        int seqCount = -1;

        /// <summary>Experimental (procedural talk): a line of the scene has a subtitle slot like {thing}.</summary>
        public bool HasSlots
        {
            get
            {
                if (hasSlots < 0)
                {
                    hasSlots = 0;
                    foreach (PhraseEntry e in AllLines) if (e.Dialogue != null || SubtitleSlots.NeedsFill(e.English)) { hasSlots = 1; break; }
                }
                return hasSlots == 1;
            }
        }
        private int hasSlots = -1;

        internal void Seal()
        {
            AllLines.Sort((a, b) => a.Seq != b.Seq ? a.Seq.CompareTo(b.Seq)
                                  : string.CompareOrdinal(a.Body, b.Body));
            Bodies.Clear();
            foreach (PhraseEntry e in AllLines)
            {
                if (e.HasBody && !Bodies.Contains(e.Body)) Bodies.Add(e.Body);
            }
            forBody.Clear();
            Lines.Clear();
            Lines.AddRange(LinesFor(PhraseTable.GenericBody));
            CrewLines = 0; McLines = 0; ProbeRoleLines = 0; SubtitleOnlyLines = 0; FemaleOk = true; MaleOk = true;
            foreach (PhraseEntry e in Lines)
            {
                if (e.SubtitleOnly) SubtitleOnlyLines++;
                if (e.Role == "mc") { McLines++; continue; }
                if (e.Role != "crew") { ProbeRoleLines++; continue; }
                CrewLines++;
                if (!PhraseTable.HasGender(e, "f")) FemaleOk = false;
                if (!PhraseTable.HasGender(e, "m")) MaleOk = false;
            }
            IsProbe = Core != "any" || ProbeRoleLines > 0 || PhraseTable.IsProbeEvent(Event);
        }
    }

    // Lonapse: the reversed Spanish text. Default mode: reverse the letters of each word after NFC.
    // Punctuation, digits outside words and spaces stay where they are. A letter run is a run of
    // letters, digits and combining marks. A combining mark stays attached to the letter before it.
    public static class LonapseText
    {
        // NFC via string.Normalize. If the runtime cannot normalize (some Unity Mono builds lack the
        // tables), the hand-made Spanish composition is used instead.
        public static string Nfc(string s)
        {
            if (s == null) return null;
            try
            {
                return s.Normalize(NormalizationForm.FormC);
            }
            catch (Exception)
            {
                return ComposeSpanish(s);
            }
        }

        // Composes the marks that Spanish uses (acute, grave, diaeresis, tilde) with their base letter.
        // Only a fallback: the result matches NFC for these characters.
        public static string ComposeSpanish(string s)
        {
            if (s == null) return null;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i + 1 < s.Length)
                {
                    char m = s[i + 1];
                    char composed;
                    if (ComposeWith(c, m, out composed))
                    {
                        sb.Append(composed);
                        i++;
                        continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        static bool ComposeWith(char c, char mark, out char result)
        {
            result = c;
            if (mark == '́')
            {
                const string from = "aeiouAEIOU";
                const string to = "áéíóúÁÉÍÓÚ";
                int k = from.IndexOf(c);
                if (k >= 0) { result = to[k]; return true; }
                return false;
            }
            if (mark == '̈')
            {
                if (c == 'u') { result = 'ü'; return true; }
                if (c == 'U') { result = 'Ü'; return true; }
                return false;
            }
            if (mark == '̃')
            {
                if (c == 'n') { result = 'ñ'; return true; }
                if (c == 'N') { result = 'Ñ'; return true; }
                return false;
            }
            if (mark == '̀')
            {
                const string from = "aeiouAEIOU";
                const string to = "àèìòùÀÈÌÒÙ";
                int k = from.IndexOf(c);
                if (k >= 0) { result = to[k]; return true; }
                return false;
            }
            return false;
        }

        public static bool IsMark(char c)
        {
            UnicodeCategory cat = char.GetUnicodeCategory(c);
            return cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark
                || cat == UnicodeCategory.EnclosingMark;
        }

        static bool IsLetterRunChar(char c)
        {
            return char.IsLetterOrDigit(c) || IsMark(c);
        }

        // Reverses one word (or any string) by clusters: a base character plus its combining marks.
        // The input is NFC first.
        public static string ReverseWord(string word)
        {
            if (word == null) return null;
            string s = Nfc(word);
            List<string> clusters = new List<string>();
            StringBuilder cur = new StringBuilder();
            foreach (char c in s)
            {
                if (!IsMark(c) && cur.Length > 0)
                {
                    clusters.Add(cur.ToString());
                    cur.Length = 0;
                }
                cur.Append(c);
            }
            if (cur.Length > 0) clusters.Add(cur.ToString());

            StringBuilder outSb = new StringBuilder(s.Length);
            for (int i = clusters.Count - 1; i >= 0; i--) outSb.Append(clusters[i]);
            return outSb.ToString();
        }

        // NFC first, then every letter run is reversed in place. Other characters keep their place.
        // "Español" gives "loñapsE". "¿Qué tal?" gives "¿éuQ lat?".
        public static string ReverseText(string text)
        {
            if (text == null) return null;
            string s = Nfc(text);
            StringBuilder sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                if (!IsLetterRunChar(s[i]))
                {
                    sb.Append(s[i]);
                    i++;
                    continue;
                }
                int start = i;
                while (i < s.Length && IsLetterRunChar(s[i])) i++;
                sb.Append(ReverseWord(s.Substring(start, i - start)));
            }
            return sb.ToString();
        }

        // The subtitle for one clip: the Lonapse text, plus the Spanish source as a second line when asked.
        // Returns null when the clip key is not in the table.
        // Subtitle for a clip key: the English text, the Lonapse text when the table has no english column
        public static string SubtitleFor(PhraseTable table, string clipKey)
        {
            if (table == null) return null;
            PhraseEntry e = table.Get(clipKey);
            return e == null ? null : e.SubtitleText;
        }
    }
}
