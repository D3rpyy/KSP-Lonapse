// Lonapse phrase table: the reversed-text helpers and the TSV parser for phrases.tsv (plan v2 4.7 and 5.4, plan v3).
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
        public string Event;
        public string Severity;
        public string Band;
        public string Role;
        public string Scene;
        public int Seq;
        public string Gender;
        // Flight phase of the row (plan v3): any, a phase name, serious or goofy. "any" when the table has no column.
        public string Phase = "any";
        // Stress cause of the row (plan v3): any or a StressModel component name. "any" when the table has no column.
        public string Cause = "any";
        // Plan v4: the crew speaker must have this trait (any pilot engineer scientist tourist). "any" without column.
        public string Trait = "any";
        // Plan v4: the crew speaker persona (any badass brave nervous dim veteran). "any" without column.
        public string Persona = "any";
        // Plan v4: topic (science: experiment id, soi_enter: home, otherwise any). "any" without column.
        public string Topic = "any";
        // Plan v5: probe core of the scene (any or a ProbeCores key). "any" without column or when empty.
        public string Core = "any";
        // Plan v5: beep mood of a probe row (a ProbeCores.Moods value). Empty on every other row.
        public string Beep = "";
        // Body key of a {body} variant (kerbin, mun, ..., generic). Empty for a row without {body}.
        public string Body = "";
        // Experimental (scene variants): an alternative for the base row with the same scene and seq. Empty on the
        // base row. When the scene plays, one row per seq is picked from the base row and its alternatives.
        public string Alt = "";
        public bool IsAlt { get { return !string.IsNullOrEmpty(Alt); } }
        public string Spanish;
        public string LonapseText;
        // English subtitle text (optional column "english"), empty when the table has none
        public string English = "";
        // Sounds/ folder of the clip, e.g. "crew/calm" (from the built table, or PhraseTable.FolderFor)
        public string Folder;

        public bool HasBody { get { return !string.IsNullOrEmpty(Body); } }

        // Plan v5: a probe row has no voice clip, only a subtitle (and a beep at runtime)
        public bool SubtitleOnly { get { return PhraseTable.IsSubtitleOnlyRole(Role); } }

        // Subtitle text: English only (user request), the Lonapse text when a table has no english column
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
            // plan v4
            "soi_enter", "science", "part_lost", "power_low", "power_ok", "signal_lost", "signal_back", "crew_lost",
            // plan v5 (probe mode)
            "probe_chat", "probe_sleepy",
            // plan v6 (probe mode): the probe calls Mission Control first, stress per cause, "say again?" on a weak
            // signal, milestones, rover driving, data transmission
            "probe_hail", "probe_stress", "probe_say_again", "probe_milestone", "probe_rover", "probe_data",
            // 0.6.0: the probe and the crew on one vessel talk in the cabin
            ProbeCrewEvent };
        /// <summary>
        /// 0.6.0 (user request): a crewed vessel with a probe core. The probe beeps in the cabin and a crew member
        /// answers, or the other way around. Probe and crew rows, no MC rows, never in a serious phase. Not a probe
        /// mode event: it only plays with crew aboard. Same as PROBE_CREW_EVENT in audio/build_lonapse.py.
        /// </summary>
        public const string ProbeCrewEvent = "probe_crew";
        // Plan v5 and v6: events that only play in probe mode. Same as PROBE_EVENTS in audio/build_lonapse.py.
        public static readonly string[] ProbeEvents = {
            "probe_chat", "probe_sleepy", "probe_hail", "probe_stress", "probe_say_again", "probe_milestone", "probe_rover",
            "probe_data" };
        // Plan v5: existing events that also have probe scenes (marked by a core or a probe-mode role).
        // Same as PROBE_VARIANT_EVENTS in audio/build_lonapse.py.
        public static readonly string[] ProbeVariantEvents = {
            "science", "power_low", "power_ok", "soi_enter", "part_lost", "signal_lost", "signal_back",
            // plan v6: the probe reacts to the flight events and callouts too
            "touchdown", "splashdown", "docked", "burn_soon", "staging", "chute_semi", "chute_full", "throttle_full",
            "throttle_cut", "plan_node" };
        public static readonly string[] Severities = { "chatter", "callout", "warning" };
        public static readonly string[] Bands = { "calm", "tense", "panic", "none" };
        public static readonly string[] Roles = { "crew", "mc", "probe" };
        // Plan v5 roles. Subtitle-only roles only have english (spanish stays empty). Plan v6 removed the room roles
        // (flight, intern, coffee) and the diary: in probe mode only Mission Control and the probe talk.
        public const string RoleProbe = "probe";
        public const string RoleCrew = "crew";
        public static readonly string[] SubtitleOnlyRoles = { "probe" };
        // Roles that only appear in probe mode (every role except crew and mc)
        public static readonly string[] ProbeRoles = { "probe" };
        // Plan v6: a probe subtitle is short and the same form everywhere: "[beeps excitedly]", "[beeps very slowly]".
        static readonly Regex ProbeSubtitleRe = new Regex(@"^\[beeps( [a-z]+){1,3}\]$", RegexOptions.CultureInvariant);
        public static bool ProbeSubtitleOk(string english) { return english != null && ProbeSubtitleRe.IsMatch(english); }
        // Plan v5 column values: core any or a ProbeCores key, beep a ProbeCores mood
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

        /// <summary>Plan v5: a scene for probe mode. True when its core is not any, when a line has a probe-mode
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

        /// <summary>Plan v5: probe scenes (IsProbeScene) with this event whose phase matches and whose core fits.
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

        // Placeholder rules, same as validate() in audio/build_lonapse.py. Only {body} exists. {body} at most once per text, and for a voiced row in spanish and english or in
        // neither (a subtitle-only row has no spanish). Adds a description per problem to bad.
        static void CheckPlaceholders(PhraseEntry e, List<string> bad)
        {
            List<string> es = Placeholders(e.Spanish), en = Placeholders(e.English);
            List<string> unknown = new List<string>();
            foreach (string p in es) if (p != "body" && !unknown.Contains(p)) unknown.Add(p);
            foreach (string p in en) if (p != "body" && !unknown.Contains(p)) unknown.Add(p);
            if (unknown.Count > 0)
            {
                bad.Add("unknown placeholder(s) {" + string.Join("}, {", unknown.ToArray()) + "}, only {body} exists");
                return;
            }
            int bEs = es.FindAll(p => p == "body").Count, bEn = en.FindAll(p => p == "body").Count;
            if (!e.SubtitleOnly && (bEs > 0) != (bEn > 0)) bad.Add("{body} must be in both spanish and english, or in neither");
            else if (bEs > 1 || bEn > 1) bad.Add("{body} at most once per text");
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
            "serious", "goofy" };
        public static readonly string[] SeriousPhases = { "launch", "ascent", "reentry", "descent" };
        public static readonly string[] GoofyPhases = { "prelaunch", "orbit", "coast", "landed" };
        public static readonly string[] Causes = {
            "any", "spin", "gforce", "q", "ground", "heat", "plasma", "fuel", "impulse", "engine_out" };
        public const string GenericBody = "generic";
        // Plan v4, same values as TRAITS, PERSONAS and TOPICS_BY_EVENT in audio/build_lonapse.py
        public const string Any = "any";
        public static readonly string[] Traits = { "any", "pilot", "engineer", "scientist", "tourist" };
        public static readonly string[] Personas = { "any", "badass", "brave", "nervous", "dim", "veteran" };
        public static readonly string[] ScienceTopics = {
            "any", "crewreport", "evareport", "mysterygoo", "surfacesample", "mobilematerialslab",
            "temperaturescan", "barometerscan", "seismicscan", "gravityscan", "atmosphereanalysis",
            "asteroidsample", "cometsample", "infraredtelescope", "magnetometer", "roc" };
        public static readonly string[] SoiTopics = { "any", "home" };
        // Plan v6: probe staging (what the stage holds), milestones and rover moments
        public static readonly string[] StagingTopics = { "any", "separation", "ignition" };
        public static readonly string[] MilestoneTopics = { "any", "day1", "day10", "day100", "day365", "orbit", "landing" };
        public static readonly string[] RoverTopics = { "any", "fast", "air", "tilt", "idle" };
        // User test: a scene about solar panels (chatter, power_low, power_ok) only plays on a ship that has one.
        public const string SolarTopic = "solar";
        public static readonly string[] SolarTopics = { "any", SolarTopic };

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
        public const string AltColumn = "alt";
        // Optional columns. A column outside Columns and Optional is ignored.
        public static readonly string[] Optional = {
            PhaseColumn, CauseColumn, LonapseColumn, FolderColumn, BodyColumn, EnglishColumn,
            TraitColumn, PersonaColumn, TopicColumn, CoreColumn, BeepColumn, AltColumn };

        // Topics allowed for an event: ScienceTopics for science, SoiTopics for soi_enter, v6 StagingTopics,
        // MilestoneTopics and RoverTopics, only "any" otherwise.
        public static string[] TopicsFor(string ev)
        {
            if (ev == "science") return ScienceTopics;
            if (ev == "soi_enter") return SoiTopics;
            if (ev == "staging") return StagingTopics;
            if (ev == "probe_milestone") return MilestoneTopics;
            if (ev == "probe_rover") return RoverTopics;
            if (ev == "chatter" || ev == "power_low" || ev == "power_ok") return SolarTopics;
            return new string[] { Any };
        }

        // Explosion words (plan v4), only allowed in part_lost rows. Same rule as EXPLOSION_RE in build_lonapse.py:
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
        // Plan v5: the subtitle-only roles have no clip and no folder ("").
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
            // line number of every alt row, for DropBadAlts
            Dictionary<PhraseEntry, int> altLine = new Dictionary<PhraseEntry, int>();

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

                if (f.Length != width)
                {
                    errors.Add("line " + lineNo + ": expected " + width + " columns, got " + f.Length);
                    continue;
                }

                PhraseEntry e = new PhraseEntry();
                e.ClipKey = f[col["clip_key"]];
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
                if (col.ContainsKey(PhaseColumn)) e.Phase = f[col[PhaseColumn]];
                if (col.ContainsKey(CauseColumn)) e.Cause = f[col[CauseColumn]];
                if (col.ContainsKey(BodyColumn)) e.Body = f[col[BodyColumn]];
                if (col.ContainsKey(TraitColumn) && f[col[TraitColumn]].Length > 0) e.Trait = f[col[TraitColumn]];
                if (col.ContainsKey(PersonaColumn) && f[col[PersonaColumn]].Length > 0) e.Persona = f[col[PersonaColumn]];
                if (col.ContainsKey(TopicColumn) && f[col[TopicColumn]].Length > 0) e.Topic = f[col[TopicColumn]];
                if (col.ContainsKey(CoreColumn) && f[col[CoreColumn]].Length > 0) e.Core = f[col[CoreColumn]];
                if (col.ContainsKey(BeepColumn)) e.Beep = f[col[BeepColumn]];
                if (col.ContainsKey(AltColumn)) e.Alt = f[col[AltColumn]];

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
                // plan v5: core, beep and the probe-mode roles and events
                if (Array.IndexOf(Cores, e.Core) < 0) bad.Add("unknown core '" + e.Core + "'");
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
                CheckPlaceholders(e, bad);
                if (e.IsAlt)
                {
                    if (!IsAltKey(e.Alt)) bad.Add("alt '" + e.Alt + "' must match [a-z0-9]+");
                    if (e.HasBody || Placeholders(e.Spanish).Count > 0 || Placeholders(e.English).Count > 0)
                        bad.Add("an alt row cannot use {body}");
                }
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
                if (seq >= 1 && !string.IsNullOrEmpty(e.Scene) && !seenSeq.Add(e.Scene + "#" + seq + "#" + e.Body + "#" + e.Alt))
                    bad.Add("duplicate scene/seq " + e.Scene + "/" + seq + (e.HasBody ? " for body " + e.Body : "")
                            + (e.IsAlt ? " for alt " + e.Alt : ""));

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
                if (e.IsAlt) altLine[e] = lineNo;
            }

            DropBadAlts(table, altLine, errors);
            if (!anyHeader) errors.Add("no header line found");
            return table;
        }

        /// <summary>
        /// Scene variants: an alt row needs a base row (no alt) with the same scene and seq, the same role, and a
        /// gender that covers the base gender (both, or the same), so a speaker who can say the base line can say the
        /// alternative too. Same rule as validate() in audio/build_lonapse.py. Bad alt rows are removed.
        /// </summary>
        static void DropBadAlts(PhraseTable table, Dictionary<PhraseEntry, int> lines, List<string> errors)
        {
            if (lines.Count == 0) return;
            var bases = new Dictionary<string, PhraseEntry>();
            foreach (PhraseEntry e in table.entries)
            {
                string k = e.Scene + "#" + e.Seq;
                if (!e.IsAlt && !bases.ContainsKey(k)) bases[k] = e;
            }
            var drop = new List<PhraseEntry>();
            foreach (PhraseEntry e in table.entries)
            {
                if (!e.IsAlt) continue;
                PhraseEntry b;
                string why = null;
                if (!bases.TryGetValue(e.Scene + "#" + e.Seq, out b)) why = "alt row without a base row for scene/seq " + e.Scene + "/" + e.Seq;
                else if (b.Role != e.Role) why = "alt row role " + e.Role + " differs from the base row role " + b.Role;
                else if (e.Gender != "both" && e.Gender != b.Gender) why = "alt row gender " + e.Gender + " does not cover the base row gender " + b.Gender;
                if (why == null) continue;
                int no;
                lines.TryGetValue(e, out no);
                errors.Add("line " + no + " (" + e.ClipKey + "): " + why);
                drop.Add(e);
            }
            foreach (PhraseEntry e in drop)
            {
                table.entries.Remove(e);
                table.byKey.Remove(e.ClipKey);
            }
        }

        // Alt value of a scene variant row: [a-z0-9]+
        public static bool IsAltKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return false;
            return true;
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

        // ---- phase and cause matching (plan v3) ----
        // Same meaning as FlightPhase.Matches: "any" (or null or empty) on either side matches everything,
        // "serious" stands for launch, ascent, reentry and descent, "goofy" for prelaunch, orbit, coast and landed.
        // Kept here so PhraseTable compiles on its own in the offline tests.

        public static bool IsSeriousPhase(string p) { return p == "serious" || Array.IndexOf(SeriousPhases, p) >= 0; }
        public static bool IsGoofyPhase(string p) { return p == "goofy" || Array.IndexOf(GoofyPhases, p) >= 0; }

        public static bool PhaseMatches(string rowPhase, string current)
        {
            if (string.IsNullOrEmpty(rowPhase) || rowPhase == "any") return true;
            if (string.IsNullOrEmpty(current) || current == "any") return true;
            if (rowPhase == current) return true;
            if (rowPhase == "serious") return IsSeriousPhase(current);
            if (rowPhase == "goofy") return IsGoofyPhase(current);
            if (current == "serious") return IsSeriousPhase(rowPhase);
            if (current == "goofy") return IsGoofyPhase(rowPhase);
            return false;
        }

        public static bool CauseMatches(string rowCause, string current)
        {
            if (string.IsNullOrEmpty(rowCause) || rowCause == "any") return true;
            if (string.IsNullOrEmpty(current) || current == "any") return true;
            return rowCause == current;
        }

        // ---- trait, persona and topic matching (plan v4) ----
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

        // ---- scenes (plan v2, req 20) ----

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

        // Plan v4: the same plus trait, persona and topic (SceneMatches). Null trait, persona or topic does not filter.
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
        // Plan v4, "any" when the table has no column
        public readonly string Trait, Persona, Topic;
        // Plan v5: probe core of the scene, "any" when the table has no column
        public readonly string Core;
        // Every row of the scene, body variants included, ordered by seq and then body
        public readonly List<PhraseEntry> AllLines = new List<PhraseEntry>();
        // The generic lines: one row per seq (rows without a body, plus the generic variant)
        public readonly List<PhraseEntry> Lines = new List<PhraseEntry>();
        // Body keys this scene has variants for, empty for a scene without {body}
        public readonly List<string> Bodies = new List<string>();
        public int CrewLines { get; private set; }
        public int McLines { get; private set; }
        // Plan v5: lines with a probe-mode role (probe) and subtitle-only lines
        public int ProbeRoleLines { get; private set; }
        public int SubtitleOnlyLines { get; private set; }
        // Plan v5: see PhraseTable.IsProbeScene
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
            Phase = string.IsNullOrEmpty(phase) ? "any" : phase;
            Cause = string.IsNullOrEmpty(cause) ? "any" : cause;
            Trait = string.IsNullOrEmpty(trait) ? "any" : trait;
            Persona = string.IsNullOrEmpty(persona) ? "any" : persona;
            Topic = string.IsNullOrEmpty(topic) ? "any" : topic;
        }

        public bool IsIntercom { get { return McLines == 0 && ProbeRoleLines == 0 && CrewLines >= 2; } }
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
                    if (e.IsAlt) continue;   // alternatives: AltsFor, picked by PhraseClips.LinesFor with a chooser
                    if (!e.HasBody) { if (plain == null) plain = e; }
                    else if (e.Body == body) exact = e;
                    else if (e.Body == PhraseTable.GenericBody) generic = e;
                }
                PhraseEntry pick = plain ?? exact ?? generic;
                if (pick != null) r.Add(pick);
            }
            forBody[body] = r;
            return r;
        }

        /// <summary>Scene variants: the alternative rows for one seq, in alt order. Empty when there are none. Do not
        /// change the list.</summary>
        public List<PhraseEntry> AltsFor(int seq)
        {
            List<PhraseEntry> r;
            return alts.TryGetValue(seq, out r) ? r : NoAlts;
        }
        public bool HasAlts { get { return alts.Count > 0; } }
        /// <summary>How many different versions of the scene the alternatives give (the product per seq of 1 plus
        /// the alternatives of that seq). 1 for a scene without alternatives.</summary>
        public int Versions
        {
            get
            {
                int n = 1;
                foreach (PhraseEntry e in Lines) n *= 1 + AltsFor(e.Seq).Count;
                return n;
            }
        }
        readonly Dictionary<int, List<PhraseEntry>> alts = new Dictionary<int, List<PhraseEntry>>();
        static readonly List<PhraseEntry> NoAlts = new List<PhraseEntry>();

        internal void Seal()
        {
            AllLines.Sort((a, b) => a.Seq != b.Seq ? a.Seq.CompareTo(b.Seq)
                                  : a.Body != b.Body ? string.CompareOrdinal(a.Body, b.Body)
                                  : string.CompareOrdinal(a.Alt, b.Alt));
            Bodies.Clear();
            alts.Clear();
            foreach (PhraseEntry e in AllLines)
            {
                if (e.HasBody && !Bodies.Contains(e.Body)) Bodies.Add(e.Body);
                if (!e.IsAlt) continue;
                List<PhraseEntry> l;
                if (!alts.TryGetValue(e.Seq, out l)) alts[e.Seq] = l = new List<PhraseEntry>();
                l.Add(e);
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
