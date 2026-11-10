// Experimental (runtime voice): lines that only exist at runtime, because they hold real values. The templates are in
// GameData/Lonapse/Phrases/Runtime/*.tsv (reports, docking, landing, science, contracts, chats). This file parses them, picks
// the ones that fit and fills them. Also the Spanish words for numbers and the spoken names of bodies and vessels.
// Pure: no Unity, no KSP, so the offline tests cover it. The director fills the values and sends the Spanish to Piper.
// In the Spanish a number becomes words, and those words are reversed like every other word (loñapsE).
// Names stay names (RuntimeSpeech.MarkName). The English subtitle shows the number in digits.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lonapse
{
    public sealed class RuntimeTemplate
    {
        public string Id, Kind;
        public DialogueLine Dialogue;
        public string Role;
        public string Scene;
        public int Sequence;
        public string ProbeBand, ProbePhase;
        public List<ProbeRuntimeLine> ProbeLines; // authored Runtime_Probe scene, null for legacy templates
        /// <summary>The versions of the line: { spanish, english } pairs, split by | in the file, in the same order.
        /// One is picked per play from the versions whose slots all have a value.</summary>
        public readonly List<string[]> Versions = new List<string[]>();
        /// <summary>Situations of the "when" column (orbit flight suborbital landed escape), empty: any.</summary>
        public readonly List<string> Situations = new List<string>();
        /// <summary>Checks of the "when" column: value key, operator (&lt; &lt;= &gt; &gt;= =), number.</summary>
        public readonly List<KeyValuePair<string, KeyValuePair<string, double>>> Checks =
            new List<KeyValuePair<string, KeyValuePair<string, double>>>();
        /// <summary>Chat only: the job kerbal A and kerbal B must have (a=pilot b=scientist in the when column).
        /// Null: any job.</summary>
        public string TraitA, TraitB;
        /// <summary>Reply only: the Mission Control lines it answers, ids or prefixes ending in * (to=fuel* in the
        /// when column). Empty: any report or warp line.</summary>
        public readonly List<string> To = new List<string>();
        /// <summary>Where the row is: "reports.tsv:12".</summary>
        public string Where = "";
        /// <summary>A version talks to one kerbal by name ({kerbal}): that kerbal answers more often.</summary>
        public bool NamesKerbal
        {
            get
            {
                foreach (string[] v in Versions)
                    if (v != null && v.Length > 0 && v[0] != null && v[0].Contains("{kerbal")) return true;
                return false;
            }
        }
        /// <summary>Event chats and science lines: the events it is about (on=flag on=surfacesample in the when
        /// column). A chat with on= only plays on that event, never as a normal chat, and may be one line long (a
        /// kerbal alone). A science line with on= only plays for that experiment. Empty: no event.</summary>
        public readonly List<string> On = new List<string>();

        /// <summary>Meta row of a mod event: the cooldown in seconds (-1: the default), the priority (low normal
        /// high, null: normal) and who talks (crew mc any, null: any).</summary>
        public double Cooldown = -1;
        public string Priority, Who;
        /// <summary>Registration row of a mod (experiment, device, contract): the KSP id it registers. An experiment
        /// id (plantGrowth, the part of a subject id before the @), a part name, or a contract class name. Its text
        /// holds the words for {exp}, {device} or {contract}, its on= the topic of its lines.</summary>
        public string RegId;

        /// <summary>An event chat: a chat with on=.</summary>
        public bool IsEvent { get { return Kind == "chat" && On.Count > 0; } }

        /// <summary>Science talk only: Mission Control speaks line 1 (who=mc in the when column), the kerbal line 2.</summary>
        public bool McFirst { get { return Kind == "science_talk" && Who == "mc"; } }

        /// <summary>A chat between two kerbals of different vessels nearby (a check on apart): it plays over the
        /// radio.</summary>
        public bool AboutApart
        {
            get
            {
                foreach (var c in Checks) if (c.Key == "apart") return true;
                return false;
            }
        }

        /// <summary>A chat for kerbals on EVA (a check on on_eva): it plays over the suit radio.</summary>
        public bool AboutEva
        {
            get
            {
                foreach (var c in Checks) if (c.Key == "on_eva") return true;
                return false;
            }
        }

        /// <summary>A line about time warp: kind warp, or a check on in_warp (a warp chat).</summary>
        public bool AboutWarp
        {
            get
            {
                if (Kind == "warp") return true;
                foreach (var c in Checks) if (c.Key == "in_warp") return true;
                return false;
            }
        }

        /// <summary>The first version (the tests and the log use it).</summary>
        public string Spanish { get { return Versions.Count > 0 ? Versions[0][0] : ""; } }
        public string English { get { return Versions.Count > 0 ? Versions[0][1] : ""; } }

        public RuntimeTemplate(string id, string kind, string spanish, string english)
        {
            Id = id; Kind = kind;
            string[] es = (spanish ?? "").Split(SubtitleSlots.VersionSep), en = (english ?? "").Split(SubtitleSlots.VersionSep);
            for (int i = 0; i < Math.Max(es.Length, en.Length); i++)
                Versions.Add(new[] { i < es.Length ? es[i].Trim() : "", i < en.Length ? en[i].Trim() : "" });
        }
    }

    public static class RuntimeLines
    {
        /// <summary>Legacy runtime IDs remain valid aliases of the prefix-free IDs.</summary>
        public static string CanonicalId(string id)
        {
            return DialogueIds.Resolve(id != null && id.StartsWith("rt_", StringComparison.Ordinal) ? id.Substring(3) : id);
        }

        /// <summary>Named routing/settings columns override their legacy inline spelling in when.</summary>
        public static List<string> ConditionTokens(string when, Func<string, string> get)
        {
            var tokens = new List<string>((when ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (string key in new[] { "on", "to", "who", "cooldown", "priority" })
            {
                string value = get(key);
                if (string.IsNullOrWhiteSpace(value)) continue;
                tokens.RemoveAll(token => token.StartsWith(key + "=", StringComparison.Ordinal));
                foreach (string item in value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) tokens.Add(key + "=" + item);
            }
            return tokens;
        }

        public static readonly string[] Kinds = { "report", "dock", "land", "science", "science_pts",
                                                  "contract_funds", "contract_done", "contract_failed", "chat", "warp", "reply", "sunrise", "eva", "flag",
                                                  "event", "meta", "experiment", "device", "contract", "undock", "dock_close", "dock_callout", "science_talk" };
        /// <summary>Kinds only other mods use (Lonapse itself ships no rows of them).</summary>
        public static readonly string[] ModKinds = { "event", "meta", "experiment", "device", "contract" };
        /// <summary>Registration rows: a mod names one of its own experiments, Breaking Ground parts or contract
        /// types (id=), gives it a topic (on=) and the words of its slot ({exp}, {device}, {contract}).</summary>
        public static readonly string[] RegKinds = { "experiment", "device", "contract" };
        public static bool IsRegKind(string kind) { return Array.IndexOf(RegKinds, kind) >= 0; }
        static bool IsContractKind(string kind) { return kind == "contract_funds" || kind == "contract_done" || kind == "contract_failed"; }
        public static readonly string[] Situations = { "orbit", "flight", "suborbital", "landed", "escape", "other" };
        /// <summary>The number slots (Slots.Numbers) and the name slots: names and persons of Slots.</summary>
        public static readonly string[] NumberSlots = Slots.Numbers;
        public static readonly string[] NameSlots = Concat(Slots.Names, Slots.Persons);
        /// <summary>Columns every runtime file must have, found by name. Other columns are ignored.</summary>
        public static readonly string[] Columns = { "id", "kind", "when", "spanish", "english" };

        static string[] Concat(string[] a, string[] b)
        {
            var r = new string[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }

        /// <summary>Career log types that count as a visit to their target body.</summary>
        static readonly string[] VisitTypes = { "Flyby", "Orbit", "Suborbit", "Land", "Flight", "Escape" };

        /// <summary>
        /// Crew history: from a kerbal's career log, entries { flight, type, target } of the flights
        /// before this one. missions: flights that ended with a recovery. flight_no: missions + 1, this flight.
        /// visits and landings: earlier flights that reached or landed on <paramref name="body"/>. stars: the
        /// experience level. Training entries do not count.
        /// </summary>
        public static void CrewHistory(IEnumerable<string[]> career, string body, int stars, Dictionary<string, double> nums)
        {
            if (nums == null) return;
            var recovered = new HashSet<string>();
            var visits = new HashSet<string>();
            var lands = new HashSet<string>();
            if (career != null)
                foreach (string[] e in career)
                {
                    if (e == null || e.Length < 3) continue;
                    string f = e[0] ?? "", type = e[1] ?? "", target = e[2] ?? "";
                    if (type.StartsWith("Training", StringComparison.Ordinal)) continue;
                    if (type == "Recover") recovered.Add(f);
                    bool here = !string.IsNullOrEmpty(body) && string.Equals(target, body, StringComparison.OrdinalIgnoreCase);
                    if (here && Array.IndexOf(VisitTypes, type) >= 0) visits.Add(f);
                    if (here && type == "Land") lands.Add(f);
                }
            nums["missions"] = recovered.Count;
            nums["flight_no"] = recovered.Count + 1;
            nums["visits"] = visits.Count;
            nums["landings"] = lands.Count;
            nums["stars"] = Math.Max(0, stars);
        }
        /// <summary>A legacy-compatible chat line: chat_&lt;name&gt;_&lt;seq&gt;, a science talk line st_&lt;name&gt;_&lt;seq&gt;. The
        /// lines of one name form one chat, seq from 1.</summary>
        static readonly Regex ChatId = new Regex(@"^((?:rt_)?chat_[a-z0-9_]+?)_([0-9]+)$");
        static readonly Regex TalkId = new Regex(@"^((?:rt_)?st_[a-z0-9_]+?)_([0-9]+)$");
        /// <summary>Kinds whose rows are lines of a group (a chat, a science talk).</summary>
        static bool IsGroupKind(string kind) { return kind == "chat" || kind == "science_talk"; }
        static Match GroupMatch(RuntimeTemplate t) { return (t.Kind == "science_talk" ? TalkId : ChatId).Match(t.Id); }
        static string GroupId(RuntimeTemplate t)
        {
            if (!string.IsNullOrEmpty(t.Scene)) return CanonicalId(t.Scene);
            Match match = GroupMatch(t);
            return match.Success ? CanonicalId(match.Groups[1].Value) : null;
        }

        static readonly Regex Check = new Regex(@"^([a-z][a-z0-9_]*)(<=|>=|<|>|=)(-?[0-9]+(?:\.[0-9]+)?)$");
        static readonly Regex TraitCheck = new Regex(@"^(a|b)=([a-z]+)$");
        static readonly Regex IdRe = new Regex(@"^[a-z0-9_]+$");
        static readonly Regex ToRe = new Regex(@"^to=([a-z0-9_]+\*?|\*)$");
        static readonly Regex OnRe = new Regex(@"^on=([a-z0-9_]+(?:\.[a-z0-9_]+)?)$");
        static readonly Regex MetaRe = new Regex(@"^(cooldown|priority|who)=([a-z0-9.]+)$");
        static readonly Regex RegIdRe = new Regex(@"^id=([A-Za-z0-9_.\-]+)$");

        /// <summary>The registration row of a kind (experiment, device, contract) for an id (case does not matter),
        /// null when none.</summary>
        public static RuntimeTemplate Registered(IList<RuntimeTemplate> all, string kind, string id)
        {
            if (all == null || string.IsNullOrEmpty(id)) return null;
            foreach (RuntimeTemplate t in all)
                if (t.Kind == kind && string.Equals(t.RegId, id, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        /// <summary>The registration row of a topic, null when the topic is not registered.</summary>
        public static RuntimeTemplate RegisteredTopic(IList<RuntimeTemplate> all, string topic)
        {
            if (all == null || topic == null) return null;
            foreach (RuntimeTemplate t in all) if (IsRegKind(t.Kind) && t.On.Contains(topic)) return t;
            return null;
        }

        /// <summary>The experiment row of a mod for an experiment id, null when none.</summary>
        public static RuntimeTemplate ModExperiment(IList<RuntimeTemplate> all, string experimentId)
        {
            return Registered(all, "experiment", experimentId);
        }

        /// <summary>The topic of a science result: the on= of a mod's experiment row for its experiment, else the
        /// stock topic (EventWatchers.ExperimentId: crewreport, roc, other ...).</summary>
        public static string ScienceTopic(IList<RuntimeTemplate> all, string subjectId)
        {
            RuntimeTemplate m = ModExperiment(all, ExperimentId(subjectId));
            return m != null ? m.On[0] : EventWatchers.ExperimentId(subjectId);
        }

        /// <summary>The topic of a deployed Breaking Ground part: the on= of a mod's device row for the part name,
        /// else deploy.</summary>
        public static string DeviceTopic(IList<RuntimeTemplate> all, string partName)
        {
            RuntimeTemplate m = Registered(all, "device", partName);
            return m != null ? m.On[0] : "deploy";
        }

        /// <summary>The topic of a contract type (its class name): the on= of a mod's contract row, null when none.</summary>
        public static string ContractTopic(IList<RuntimeTemplate> all, string contractType)
        {
            RuntimeTemplate m = Registered(all, "contract", contractType);
            return m != null ? m.On[0] : null;
        }

        /// <summary>The general event a topic belongs to, whose lines also fit it: science for an experiment (stock
        /// or a mod's experiment row), deploy for a deployed part (deploy or a mod's device row), else null.</summary>
        public static string GeneralOf(IList<RuntimeTemplate> all, string topic)
        {
            if (topic == null || topic == "flag" || topic == "undock" || topic == "dockclose" || topic == "dockcallout") return null;
            if (topic == "deploy") return "deploy";
            if (!IsModEvent(topic)) return "science";
            RuntimeTemplate m = RegisteredTopic(all, topic);
            return m == null ? null : m.Kind == "experiment" ? "science" : m.Kind == "device" ? "deploy" : null;
        }

        /// <summary>An event of another mod: a namespaced id, mod.event (examplemod.repair_done).</summary>
        public static bool IsModEvent(string ev) { return ev != null && ev.IndexOf('.') > 0; }

        /// <summary>The events of on=: flag (a flag planted), deploy (a Breaking Ground part deployed), science
        /// (any experiment) and the science topics of EventWatchers (evareport, surfacesample, roc ...).</summary>
        public static readonly string[] Events = { "flag", "undock", "dockclose", "dockcallout", "deploy", "science", "evareport", "surfacesample", "roc",
            "crewreport", "mysterygoo", "mobilematerialslab", "temperaturescan", "barometerscan", "seismicscan",
            "gravityscan", "atmosphereanalysis", "asteroidsample", "cometsample", "infraredtelescope", "magnetometer", "other" };

        /// <summary>A line with on= fits this event: one of its on= is the event, or it is on=science and the event
        /// is a science topic (not flag or deploy). A line without on= fits no event.</summary>
        public static bool OnFits(RuntimeTemplate t, string ev)
        {
            return OnFits(t, ev, GeneralOf(null, ev));
        }

        /// <summary>OnFits, where general is the general event of ev (GeneralOf): a line on=general fits it too, so
        /// on=science fits any experiment and on=deploy any deployed part, a mod's registered ones included.</summary>
        public static bool OnFits(RuntimeTemplate t, string ev, string general)
        {
            if (t == null || ev == null) return false;
            foreach (string o in t.On)
                if (o == ev || (general != null && o == general)) return true;
            return false;
        }

        /// <summary>Parses one runtime file (the tests, a single table).</summary>
        public static List<RuntimeTemplate> Parse(IList<string> lines, List<string> errors)
        {
            return ParseAll(new[] { new KeyValuePair<string, IList<string>>("runtime", lines) }, errors);
        }

        /// <summary>
        /// Parses category files as one set: logical ids are unique and a chat may not span files. Unified input
        /// uses DialogueTable; the kind-based reader remains for legacy mods. Bad rows are reported and skipped.
        /// </summary>
        public static List<RuntimeTemplate> ParseAll(IEnumerable<KeyValuePair<string, IList<string>>> files, List<string> errors)
        {
            var r = new List<RuntimeTemplate>();
            var seen = new HashSet<string>();
            foreach (var file in files)
            {
                if (DialogueTable.IsUnified(file.Value))
                {
                    var doc = DialogueTable.Parse(file.Value, file.Key, errors);
                    int start = r.Count;
                    ParseFile(file.Key, doc.TemplateRows(), errors, seen, r);
                    doc.Attach(r, start);
                }
                else ParseFile(file.Key, file.Value, errors, seen, r);
            }
            // registration rows (experiment, device, contract): each topic has one, each id one per kind. A science
            // or contract line with a mod topic needs the registration row of that topic (in any file).
            var topics = new Dictionary<string, string>();
            var regIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dropReg = new HashSet<RuntimeTemplate>();
            foreach (RuntimeTemplate t in r)
            {
                if (!IsRegKind(t.Kind)) continue;
                if (topics.ContainsKey(t.On[0]) || !regIds.Add(t.Kind + "|" + t.RegId))
                {
                    if (errors != null) errors.Add(t.Where + " (" + t.Id + "): the topic " + t.On[0] + " or the id " + t.RegId + " is already registered");
                    dropReg.Add(t);
                    continue;
                }
                topics[t.On[0]] = t.Kind;
            }
            foreach (RuntimeTemplate t in r)
            {
                bool sci = t.Kind == "science" || t.Kind == "science_pts" || t.Kind == "science_talk", con = IsContractKind(t.Kind);
                if (!sci && !con) continue;
                foreach (string o in t.On)
                {
                    string k;
                    if (!IsModEvent(o) || (topics.TryGetValue(o, out k) && (con ? k == "contract" : k != "contract"))) continue;
                    if (errors != null)
                        errors.Add(t.Where + " (" + t.Id + "): " + (con
                            ? "a contract line with on=" + o + " needs a contract row with on=" + o + " id=<contract type>"
                            : "a science line only plays after an experiment or a deployed part. on=" + o
                              + " needs an experiment or device row with on=" + o + " id=<id>, or use kind event for a mod event"));
                    dropReg.Add(t);
                    break;
                }
            }
            r.RemoveAll(t => dropReg.Contains(t));
            // chats: explicit scene/seq columns, or ids chat_<name>_<seq>, seq 1, 2, 3 ... without gaps, at least 2 lines, conditions on line 1 only.
            // Science talks the same with st_<name>_<seq>, and line 1 needs on= (the experiment).
            var groups = new Dictionary<string, List<RuntimeTemplate>>();
            var drop = new HashSet<RuntimeTemplate>();
            foreach (RuntimeTemplate t in r)
            {
                if (!IsGroupKind(t.Kind)) continue;
                string groupId = GroupId(t);
                if (groupId == null)
                {
                    if (errors != null) errors.Add(t.Where + " (" + t.Id + "): supply scene/seq columns or use chat_<name>_<seq> / st_<name>_<seq>");
                    drop.Add(t);
                    continue;
                }
                List<RuntimeTemplate> g;
                if (!groups.TryGetValue(t.Kind + ":" + groupId, out g)) groups[t.Kind + ":" + groupId] = g = new List<RuntimeTemplate>();
                g.Add(t);
            }
            foreach (var kv in groups)
            {
                kv.Value.Sort((x, y) => ChatSeq(x).CompareTo(ChatSeq(y)));
                string why = null;
                bool talk = kv.Value[0].Kind == "science_talk";
                if (talk && kv.Value.Count < 2) why = "a science talk needs at least 2 lines (the kerbal and Mission Control)";
                else if (talk && kv.Value[0].On.Count == 0) why = "line 1 of a science talk needs on= (the experiment, or on=science)";
                else if (kv.Value.Count < 2 && kv.Value[0].On.Count == 0) why = "a chat needs at least 2 lines (an event chat with on= may have 1)";
                string file0 = FileOf(kv.Value[0].Where);
                for (int i = 0; i < kv.Value.Count && why == null; i++)
                {
                    if (ChatSeq(kv.Value[i]) != i + 1) why = "seq must go 1, 2, 3 without gaps";
                    else if (FileOf(kv.Value[i].Where) != file0) why = "all lines of a chat go in one file";
                    else if (i > 0 && (kv.Value[i].Checks.Count > 0 || kv.Value[i].Situations.Count > 0 || kv.Value[i].On.Count > 0
                                       || kv.Value[i].TraitA != null || kv.Value[i].TraitB != null || kv.Value[i].Who != null)) why = "only line 1 has a when";
                }
                if (why != null) { if (errors != null) errors.Add((talk ? "runtime science talk " : "runtime chat ") + kv.Key + ": " + why); foreach (var t in kv.Value) drop.Add(t); }
            }
            r.RemoveAll(t => drop.Contains(t));
            return r;
        }

        static string FileOf(string where)
        {
            int c = (where ?? "").LastIndexOf(':');
            return c > 0 ? where.Substring(0, c) : where ?? "";
        }

        static void ParseFile(string name, IList<string> lines, List<string> errors, HashSet<string> seen, List<RuntimeTemplate> r)
        {
            Dictionary<string, int> col = null;
            int width = 0;
            if (lines == null) return;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = (lines[i] ?? "").TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                string[] f = line.Split('\t');
                for (int k = 0; k < f.Length; k++) f[k] = f[k].Trim();
                string where = name + ":" + (i + 1);
                if (col == null)
                {
                    col = new Dictionary<string, int>();
                    for (int k = 0; k < f.Length; k++) if (!col.ContainsKey(f[k])) col[f[k]] = k;
                    var missing = new List<string>();
                    foreach (string c in Columns) if (!col.ContainsKey(c)) missing.Add(c);
                    if (missing.Count > 0)
                    {
                        if (errors != null) errors.Add(where + ": header needs the columns " + string.Join(", ", Columns) + " (missing " + string.Join(" ", missing.ToArray()) + ")");
                        return;
                    }
                    width = f.Length;
                    continue;
                }
                int need = 0;
                foreach (string c in Columns) need = Math.Max(need, col[c] + 1);
                if (f.Length > width || f.Length < need)
                {
                    if (errors != null) errors.Add(where + ": expected " + width + " columns, got " + f.Length);
                    continue;
                }
                if (f.Length < width) { var pad = new string[width]; f.CopyTo(pad, 0); for (int k = f.Length; k < width; k++) pad[k] = ""; f = pad; }
                var bad = new List<string>();
                var t = new RuntimeTemplate(f[col["id"]], f[col["kind"]], f[col["spanish"]], f[col["english"]]);
                t.Where = where;
                if (col.ContainsKey("role")) t.Role = f[col["role"]];
                if (IsGroupKind(t.Kind))
                {
                    string scene = col.ContainsKey("scene") ? f[col["scene"]] : "";
                    string sequence = col.ContainsKey("seq") ? f[col["seq"]] : "";
                    if (scene.Length > 0 || sequence.Length > 0)
                    {
                        int seq;
                        if (!IdRe.IsMatch(scene) || !int.TryParse(sequence, out seq) || seq < 1)
                            bad.Add("scene and seq must name a valid group and a positive line number");
                        else { t.Scene = scene; t.Sequence = seq; }
                    }
                }
                if (!IdRe.IsMatch(t.Id)) bad.Add("id '" + t.Id + "' must match [a-z0-9_]+");
                else if (!seen.Add(CanonicalId(t.Id))) bad.Add("duplicate id " + t.Id);
                if (Array.IndexOf(Kinds, t.Kind) < 0) bad.Add("unknown kind '" + t.Kind + "'");
                foreach (string tok in ConditionTokens(f[col["when"]], key => col.ContainsKey(key) ? f[col[key]] : ""))
                {
                    if (tok == "any") continue;
                    if (Array.IndexOf(Situations, tok) >= 0) { t.Situations.Add(tok); continue; }
                    Match on = OnRe.Match(tok);
                    if (on.Success)
                    {
                        string ev = on.Groups[1].Value;
                        bool mod = IsModEvent(ev);
                        if (t.Kind != "chat" && t.Kind != "science" && t.Kind != "science_pts" && t.Kind != "science_talk" && t.Kind != "event" && t.Kind != "meta"
                            && !IsRegKind(t.Kind) && !IsContractKind(t.Kind))
                            bad.Add("'" + tok + "' only works in a chat, a science talk, a science or contract line, an event, a meta, an experiment, a device or a contract row");
                        else if ((t.Kind == "event" || t.Kind == "meta" || IsRegKind(t.Kind) || IsContractKind(t.Kind)) && !mod)
                            bad.Add("an event, meta, experiment, device, contract row or contract line needs a mod event mod.event, not '" + ev + "'");
                        else if (!mod && Array.IndexOf(Events, ev) < 0) bad.Add("unknown event '" + ev + "' (" + string.Join(" ", Events) + ", or a mod event mod.event)");
                        else t.On.Add(ev);
                        continue;
                    }
                    Match xi = RegIdRe.Match(tok);
                    if (xi.Success && IsRegKind(t.Kind))
                    {
                        string rid = xi.Groups[1].Value;
                        if (t.RegId != null) bad.Add("a " + t.Kind + " row has one id=");
                        else if (t.Kind == "experiment" && (Experiments.ContainsKey(rid) || Array.IndexOf(EventWatchers.KnownExperiments, rid.ToLowerInvariant()) >= 0))
                            bad.Add("id=" + rid + " is a stock experiment, Lonapse has its own words for it");
                        else if (t.Kind == "device" && Devices.ContainsKey(rid))
                            bad.Add("id=" + rid + " is a stock Breaking Ground part, Lonapse has its own words for it");
                        else t.RegId = rid;
                        continue;
                    }
                    Match mt = MetaRe.Match(tok);
                    if (mt.Success && t.Kind == "meta")
                    {
                        string mk = mt.Groups[1].Value, mv = mt.Groups[2].Value;
                        double cd;
                        if (mk == "cooldown" && double.TryParse(mv, NumberStyles.Float, CultureInfo.InvariantCulture, out cd) && cd >= 0) t.Cooldown = cd;
                        else if (mk == "priority" && (mv == "low" || mv == "normal" || mv == "high")) t.Priority = mv;
                        else if (mk == "who" && (mv == "crew" || mv == "mc" || mv == "any")) t.Who = mv;
                        else bad.Add("bad meta value '" + tok + "' (cooldown=<seconds>, priority=low|normal|high, who=crew|mc|any)");
                        continue;
                    }
                    if (mt.Success && t.Kind == "science_talk" && mt.Groups[1].Value == "who")
                    {
                        // who starts a science talk: crew (the default) or mc
                        string mv = mt.Groups[2].Value;
                        if (mv == "mc" || mv == "crew") t.Who = mv;
                        else bad.Add("bad '" + tok + "' (who=crew or who=mc in a science talk)");
                        continue;
                    }
                    Match to = ToRe.Match(tok);
                    if (to.Success)
                    {
                        if (t.Kind != "reply") bad.Add("'" + tok + "' only works in a reply");
                        else t.To.Add(to.Groups[1].Value);
                        continue;
                    }
                    Match tm = TraitCheck.Match(tok);
                    if (tm.Success)
                    {
                        string job = tm.Groups[2].Value;
                        if (t.Kind != "chat" && !(t.Kind == "science_talk" && tm.Groups[1].Value == "a"))
                            bad.Add("'" + tok + "' only works in a chat" + (t.Kind == "science_talk" ? " (a science talk has a=, the kerbal who did the experiment)" : ""));
                        else if (Array.IndexOf(Persona.TraitKeys, job) < 0 && Array.IndexOf(Persona.PersonaKeys, job) < 0)
                            bad.Add("unknown job or persona '" + job + "' (pilot engineer scientist tourist, badass brave nervous dim veteran)");
                        else if (tm.Groups[1].Value == "a") t.TraitA = t.TraitA == null ? job : t.TraitA + " " + job;
                        else t.TraitB = t.TraitB == null ? job : t.TraitB + " " + job;
                        continue;
                    }
                    Match m = Check.Match(tok);
                    if (m.Success && (Array.IndexOf(NumberSlots, m.Groups[1].Value) >= 0 || Slots.IsModSlot(m.Groups[1].Value)))
                    {
                        t.Checks.Add(new KeyValuePair<string, KeyValuePair<string, double>>(m.Groups[1].Value,
                            new KeyValuePair<string, double>(m.Groups[2].Value, double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))));
                        continue;
                    }
                    bad.Add("bad condition '" + tok + "'");
                }
                if (t.Kind == "meta")
                {
                    // settings of a mod event, no lines
                    if (t.On.Count != 1) bad.Add("a meta row needs exactly one on=mod.event");
                    t.Versions.Clear();
                    if (bad.Count > 0) { if (errors != null) errors.Add(where + " (" + t.Id + "): " + string.Join("; ", bad.ToArray())); continue; }
                    r.Add(t);
                    continue;
                }
                if (t.Kind == "event" && t.On.Count == 0) bad.Add("an event line needs on=mod.event");
                if (IsRegKind(t.Kind))
                {
                    string slot = t.Kind == "experiment" ? "{exp}" : "{" + t.Kind + "}";
                    if (t.On.Count != 1 || t.RegId == null) bad.Add("a " + t.Kind + " row needs one on=mod.topic and one id=<id>");
                    if (t.Versions.Count != 1) bad.Add("a " + t.Kind + " row has one version: the words for " + slot + ", no |");
                    else if (Slot.IsMatch(t.Versions[0][0]) || Slot.IsMatch(t.Versions[0][1])) bad.Add("the words of a " + t.Kind + " row have no slots");
                }
                int nEs = f[col["spanish"]].Split(SubtitleSlots.VersionSep).Length, nEn = f[col["english"]].Split(SubtitleSlots.VersionSep).Length;
                if (nEs != nEn) bad.Add(nEs + " spanish versions for " + nEn + " english, split both by | in the same order");
                for (int v = 0; v < t.Versions.Count && bad.Count == 0; v++)
                {
                    string es = t.Versions[v][0], en = t.Versions[v][1], vn = t.Versions.Count > 1 ? " (version " + (v + 1) + ")" : "";
                    if (es.Length == 0 || en.Length == 0) { bad.Add("spanish and english must not be empty" + vn); continue; }
                    foreach (string text in new[] { es, en })
                        foreach (Match m in Slot.Matches(text))
                            if (Array.IndexOf(NumberSlots, m.Groups[1].Value) < 0 && Array.IndexOf(NameSlots, m.Groups[1].Value) < 0
                                && !Slots.IsModSlot(m.Groups[1].Value))
                                bad.Add("unknown slot {" + m.Groups[1].Value + "}" + vn);
                    if (SlotSet(es) != SlotSet(en)) bad.Add("spanish and english must use the same slots" + vn);
                }
                if (bad.Count > 0) { if (errors != null) errors.Add(where + " (" + t.Id + "): " + string.Join("; ", bad.ToArray())); continue; }
                r.Add(t);
            }
            if (col == null && errors != null) errors.Add(name + ": no header");
        }

        static int ChatSeq(RuntimeTemplate t)
        {
            if (t.Sequence > 0) return t.Sequence;
            Match m = GroupMatch(t);
            return m.Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        }

        /// <summary>The chats of the templates: lists of lines in seq order. Line 1 holds the when.</summary>
        public static List<List<RuntimeTemplate>> Chats(IList<RuntimeTemplate> all)
        {
            return Groups(all, "chat");
        }

        /// <summary>The science talks of the templates: lists of lines in seq order. Line 1 holds the when.</summary>
        public static List<List<RuntimeTemplate>> Talks(IList<RuntimeTemplate> all)
        {
            return Groups(all, "science_talk");
        }

        /// <summary>A science talk for a batch: line 1 checks exp_count (exp_count>=2). Only batches use those, a
        /// single experiment never.</summary>
        public static bool ForBatch(List<RuntimeTemplate> talk)
        {
            if (talk == null || talk.Count == 0) return false;
            foreach (var c in talk[0].Checks) if (c.Key == "exp_count") return true;
            return false;
        }

        /// <summary>The checks of the template besides exp_count and data_sent: a talk that only fits now has one.
        /// Sent or kept is no rare moment, so a talk that checks only that is no special one.</summary>
        public static int SpecialChecks(RuntimeTemplate t)
        {
            int n = 0;
            if (t != null) foreach (var c in t.Checks) if (c.Key != "exp_count" && c.Key != "data_sent") n++;
            return n;
        }

        /// <summary>A list of words for {exp_list}: { "a, b y c", "a, b and c" }. More than max words: the first max
        /// and "y más", "and more". Spanish "y" is "e" before a word that starts with an i sound.</summary>
        public static string[] ListWords(IList<string> es, IList<string> en, int max)
        {
            int n = Math.Min(es.Count, en.Count);
            if (n == 0) return new[] { "", "" };
            bool more = n > max;
            if (more) n = max;
            var e = new List<string>();
            var g = new List<string>();
            for (int i = 0; i < n; i++) { e.Add(es[i]); g.Add(en[i]); }
            if (more) return new[] { string.Join(", ", e.ToArray()) + " y más", string.Join(", ", g.ToArray()) + " and more" };
            if (n == 1) return new[] { e[0], g[0] };
            string last = e[n - 1];
            string and = last.StartsWith("i", StringComparison.OrdinalIgnoreCase) || last.StartsWith("hi", StringComparison.OrdinalIgnoreCase)
                         ? " e " : " y ";
            return new[] { string.Join(", ", e.GetRange(0, n - 1).ToArray()) + and + last,
                           string.Join(", ", g.GetRange(0, n - 1).ToArray()) + (n > 2 ? ", and " : " and ") + g[n - 1] };
        }

        static List<List<RuntimeTemplate>> Groups(IList<RuntimeTemplate> all, string kind)
        {
            var map = new Dictionary<string, List<RuntimeTemplate>>();
            var order = new List<string>();
            if (all != null)
                foreach (RuntimeTemplate t in all)
                {
                    if (t.Kind != kind) continue;
                    string groupId = GroupId(t);
                    if (groupId == null) continue;
                    List<RuntimeTemplate> g;
                    if (!map.TryGetValue(groupId, out g)) { map[groupId] = g = new List<RuntimeTemplate>(); order.Add(groupId); }
                    g.Add(t);
                }
            var r = new List<List<RuntimeTemplate>>();
            foreach (string k in order) { map[k].Sort((x, y) => ChatSeq(x).CompareTo(ChatSeq(y))); r.Add(map[k]); }
            return r;
        }

        /// <summary>Line 1 of a chat fits (situation, checks and the jobs of A and B), and every line has a version
        /// that fills with the names of its speaker: line i is spoken by A when i is even. namesA and namesB hold self
        /// and other for each speaker. jobA and jobB are the trait keys of the two kerbals (Persona.trait). Null: the
        /// job is not checked.</summary>
        public static void ActorConditions(Dictionary<string, double> nums, string a, string b)
        {
            if (nums == null) return;
            foreach (string key in new[] { "pilot", "engineer", "scientist", "tourist", "badass", "brave", "nervous", "dim", "veteran" })
            {
                if (a == null) nums.Remove("job_a_" + key); else nums["job_a_" + key] = Array.IndexOf(a.Split(' '), key) >= 0 ? 1 : 0;
                if (b == null) nums.Remove("job_b_" + key); else nums["job_b_" + key] = Array.IndexOf(b.Split(' '), key) >= 0 ? 1 : 0;
            }
        }

        static Dictionary<string, double> SituationValues(Dictionary<string, double> nums, string situation)
        {
            var values = new Dictionary<string, double>(nums ?? new Dictionary<string, double>());
            foreach (string tag in Situations) values["tag_" + tag] = tag == situation ? 1 : 0;
            return values;
        }

        public static bool ChatFits(List<RuntimeTemplate> chat, string situation, Dictionary<string, double> nums,
                                    Dictionary<string, string[]> namesA, Dictionary<string, string[]> namesB,
                                    string jobA = null, string jobB = null)
        {
            if (chat == null || chat.Count == 0) return false;
            nums = SituationValues(nums, situation);
            ActorConditions(nums, jobA, jobB);
            if (!JobOk(chat[0].TraitA, jobA) || !JobOk(chat[0].TraitB, jobB)) return false;
            if (!Persona.ChatSideFits(chat[0].TraitA, jobA) || !Persona.ChatSideFits(chat[0].TraitB, jobB)) return false;
            if (!Holds(chat[0], situation, nums) || FillableVersions(chat[0], nums, chat[0].Role == "partner" ? namesB : namesA).Count == 0) return false;
            for (int i = 1; i < chat.Count; i++)
                if (FillableVersions(chat[i], nums, (chat[i].Role == "partner" || string.IsNullOrEmpty(chat[i].Role) && i % 2 != 0) ? namesB : namesA).Count == 0) return false;
            return true;
        }

        /// <summary>Line 1 of a science talk fits (situation, checks and the job of the kerbal), and every line has a
        /// version that fills with the names of its speaker: the kerbal (namesCrew) and Mission Control (namesMc) take
        /// turns, the kerbal first unless the talk is who=mc. job: the keys of the kerbal (Persona.ChatKeys), null: not
        /// checked.</summary>
        public static bool TalkFits(List<RuntimeTemplate> talk, string situation, Dictionary<string, double> nums,
                                    Dictionary<string, string[]> namesCrew, Dictionary<string, string[]> namesMc, string job = null)
        {
            if (talk == null || talk.Count == 0) return false;
            nums = SituationValues(nums, situation);
            ActorConditions(nums, job, null);
            if (!JobOk(talk[0].TraitA, job) || !Persona.ChatSideFits(talk[0].TraitA, job)) return false;
            if (!Holds(talk[0], situation, nums)) return false;
            for (int i = 0; i < talk.Count; i++)
                if (FillableVersions(talk[i], nums, TalkCrewLine(talk, i) ? namesCrew : namesMc).Count == 0) return false;
            return true;
        }

        /// <summary>Line i of a science talk is the kerbal's (else Mission Control's).</summary>
        public static bool TalkCrewLine(List<RuntimeTemplate> talk, int i)
        {
            if (talk != null && i < talk.Count && !string.IsNullOrEmpty(talk[i].Role)) return talk[i].Role != "mc";
            bool mcFirst = talk != null && talk.Count > 0 && talk[0].McFirst;
            return (i % 2 == 0) != mcFirst;
        }

        /// <summary>The text holds the name as a whole word (not inside another word), ignoring case.</summary>
        public static bool HasWord(string text, string name)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name)) return false;
            for (int i = text.IndexOf(name, StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = text.IndexOf(name, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                bool before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                int j = i + name.Length;
                bool after = j >= text.Length || !char.IsLetterOrDigit(text[j]);
                if (before && after) return true;
            }
            return false;
        }

        /// <summary>The jobs and personas a chat asks for (null: any, else keys split by spaces, all must hold) and the
        /// keys of the kerbal (null: not checked), like "pilot brave dim" (Persona.ChatKeys). A kerbal with an unknown
        /// job (any) and no flags only fits a chat that asks for none.</summary>
        public static bool JobOk(string want, string have)
        {
            if (want == null || have == null) return true;
            string[] keys = have.Split(' ');
            foreach (string w in want.Split(' '))
                if (w.Length > 0 && Array.IndexOf(keys, w) < 0) return false;
            return true;
        }

        static string SlotSet(string text)
        {
            var l = new List<string>();
            foreach (Match m in Slot.Matches(text ?? "")) if (!l.Contains(m.Groups[1].Value)) l.Add(m.Groups[1].Value);
            l.Sort(StringComparer.Ordinal);
            return string.Join(",", l.ToArray());
        }

        /// <summary>The templates of a kind that fit: the situation matches (or the template has none), every check
        /// holds and at least one version has a value for every slot.</summary>
        public static List<RuntimeTemplate> Fitting(IList<RuntimeTemplate> all, string kind, string situation,
                                                    Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            var r = new List<RuntimeTemplate>();
            if (all == null) return r;
            nums = SituationValues(nums, situation);
            foreach (RuntimeTemplate t in all)
                if (t.Kind == kind && Holds(t, situation, nums) && FillableVersions(t, nums, names).Count > 0) r.Add(t);
            return r;
        }

        /// <summary>The kind of a recorded Mission Control line (a calm chatter scene that ends with MC) for Answers.</summary>
        public const string RecordedKind = "recorded";

        /// <summary>A reply answers this Mission Control line: one of its to= ids or prefixes matches, or it has none
        /// and the line is a report, a warp or sunrise line, or a recorded calm MC line.</summary>
        public static bool Answers(RuntimeTemplate reply, string lineId, string lineKind)
        {
            if (reply == null || reply.Kind != "reply" || string.IsNullOrEmpty(lineId)) return false;
            if (reply.To.Count == 0) return lineKind == "report" || lineKind == "warp" || lineKind == "sunrise" || lineKind == "eva"
                                         || lineKind == RecordedKind;
            lineId = CanonicalId(lineId);
            foreach (string original in reply.To)
            {
                string p = CanonicalId(original);
                if (p.EndsWith("*", StringComparison.Ordinal) ? lineId.StartsWith(p.Substring(0, p.Length - 1), StringComparison.Ordinal)
                                                              : lineId == p) return true;
            }
            return false;
        }

        /// <summary>The replies that fit after a Mission Control line: they answer it (Answers), and their when and
        /// slots fit like any template (Fitting).</summary>
        public static List<RuntimeTemplate> Replies(IList<RuntimeTemplate> all, RuntimeTemplate line, string situation,
                                                    Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            var r = Fitting(all, "reply", situation, nums, names);
            if (line == null) { r.Clear(); return r; }
            r.RemoveAll(t => !Answers(t, line.Id, line.Kind));
            return r;
        }

        /// <summary>The template asks for a tourist (is_tourist=1 in the when column).</summary>
        public static bool ForTourist(RuntimeTemplate t)
        {
            foreach (var c in t.Checks)
                if (c.Key == "is_tourist" && c.Value.Key == "=" && c.Value.Value == 1) return true;
            return false;
        }

        /// <summary>The situation and the checks of the when column hold.</summary>
        internal static bool Holds(RuntimeTemplate t, string situation, Dictionary<string, double> nums)
        {
            if (t.Dialogue != null) foreach (string token in t.Dialogue.Common)
                if (Array.IndexOf(SubtitleSlots.Conditions, token) >= 0 && Array.IndexOf(Situations, token) < 0 && !DialogueTable.Holds(token, nums)) return false;
            if (t.Situations.Count > 0 && !t.Situations.Contains(situation ?? "")) return false;
            foreach (var c in t.Checks)
            {
                double v;
                if (nums == null || !nums.TryGetValue(c.Key, out v) || double.IsNaN(v)) return false;
                double rv = Rounded(c.Key, v), n = c.Value.Value;
                bool ok;
                switch (c.Value.Key)
                {
                    case "<": ok = rv < n; break;
                    case "<=": ok = rv <= n; break;
                    case ">": ok = rv > n; break;
                    case ">=": ok = rv >= n; break;
                    default: ok = rv == n; break;
                }
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>The indices of the versions whose slots all have a value now.</summary>
        public static List<int> FillableVersions(RuntimeTemplate t, Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            var r = new List<int>();
            if (t == null) return r;
            if (t.Dialogue != null) return t.Dialogue.Matching(nums, names);
            for (int v = 0; v < t.Versions.Count; v++)
            {
                string s, e;
                if (Fill(t, v, nums, names, out s, out e)) r.Add(v);
            }
            return r;
        }

        /// <summary>Stock experiment ids: Spanish words for "datos de ..." and the English for "the ... data".</summary>
        static readonly Dictionary<string, string[]> Experiments = new Dictionary<string, string[]> {
            { "crewReport", new[] { "informe de tripulación", "crew report" } },
            { "evaReport", new[] { "paseo espacial", "EVA report" } },
            { "surfaceSample", new[] { "muestra de suelo", "surface sample" } },
            { "mysteryGoo", new[] { "moco misterioso", "Mystery Goo" } },
            { "mobileMaterialsLab", new[] { "materiales", "materials" } },
            { "temperatureScan", new[] { "temperatura", "temperature" } },
            { "barometerScan", new[] { "presión", "pressure" } },
            { "seismicScan", new[] { "sismos", "seismic" } },
            { "gravityScan", new[] { "gravedad", "gravity" } },
            { "atmosphereAnalysis", new[] { "atmósfera", "atmosphere" } },
            { "asteroidSample", new[] { "asteroide", "asteroid sample" } },
            { "infraredTelescope", new[] { "telescopio", "telescope" } },
            { "magnetometer", new[] { "campo magnético", "magnetometer" } },
        };

        /// <summary>Breaking Ground parts by part name: { spanish, english } with the article, for {device}.</summary>
        static readonly Dictionary<string, string[]> Devices = new Dictionary<string, string[]> {
            { "DeployedCentralStation", new[] { "la estación central", "the central station" } },
            { "DeployedSeismicSensor", new[] { "el sismógrafo", "the seismometer" } },
            { "DeployedWeatherStn", new[] { "la estación del tiempo", "the weather station" } },
            { "DeployedIONExp", new[] { "el colector de iones", "the ion collector" } },
            { "DeployedGoExOb", new[] { "el observatorio de moco", "the goo observatory" } },
            { "DeployedSolarPanel", new[] { "el panel solar", "the solar panel" } },
            { "DeployedRTG", new[] { "el generador", "the generator" } },
            { "DeployedSatDish", new[] { "la antena", "the antenna" } },
        };

        /// <summary>The words for a deployed part: by part name, else by kind (station comms experiment power).</summary>
        public static string[] DeviceWords(string partName, string kind)
        {
            string[] w;
            if (partName != null && Devices.TryGetValue(partName, out w)) return w;
            switch (kind)
            {
                case "station": return new[] { "la estación central", "the central station" };
                case "comms": return new[] { "la antena", "the antenna" };
                case "experiment": return new[] { "el experimento", "the experiment" };
                case "power": return new[] { "el generador", "the generator" };
                default: return new[] { "el aparato", "the gadget" };
            }
        }

        /// <summary>Experiment id from a subject id ("temperatureScan@KerbinSrfLanded" gives "temperatureScan").</summary>
        public static string ExperimentId(string subjectId)
        {
            if (string.IsNullOrEmpty(subjectId)) return "";
            int at = subjectId.IndexOf('@');
            return at > 0 ? subjectId.Substring(0, at) : subjectId;
        }

        /// <summary>Spanish and English words of an experiment. Unknown (modded) ids: "los datos" style generic words.
        /// Comet samples (cometSample_short ...) share one entry.</summary>
        /// <summary>ExperimentWords, where a mod's experiment row (kind experiment) gives the words of its own id.</summary>
        public static string[] ExperimentWords(IList<RuntimeTemplate> all, string experimentId, string displayName = null)
        {
            RuntimeTemplate m = ModExperiment(all, experimentId);
            if (m != null && m.Versions.Count > 0) return new[] { m.Versions[0][0], m.Versions[0][1] };
            // A registry row can supply a translation; otherwise use the game's title for any unlisted experiment.
            string[] known;
            if (!string.IsNullOrEmpty(experimentId) && (Experiments.TryGetValue(experimentId, out known) || experimentId.StartsWith("cometSample", StringComparison.Ordinal))) return ExperimentWords(experimentId);
            displayName = CleanName(displayName);
            if (!string.IsNullOrEmpty(displayName) && !displayName.StartsWith("#")) return new[] { RuntimeSpeech.Respell(displayName), displayName };
            return ExperimentWords(experimentId);
        }

        /// <summary>DeviceWords, where a mod's device row gives the words of its own part.</summary>
        public static string[] DeviceWords(IList<RuntimeTemplate> all, string partName, string kind)
        {
            RuntimeTemplate m = Registered(all, "device", partName);
            if (m != null && m.Versions.Count > 0) return new[] { m.Versions[0][0], m.Versions[0][1] };
            return DeviceWords(partName, kind);
        }

        /// <summary>The words of a contract type for {contract}: a mod's contract row, null when none.</summary>
        public static string[] ContractWords(IList<RuntimeTemplate> all, string contractType)
        {
            RuntimeTemplate m = Registered(all, "contract", contractType);
            return m != null && m.Versions.Count > 0 ? new[] { m.Versions[0][0], m.Versions[0][1] } : null;
        }

        public static string[] ExperimentWords(string experimentId)
        {
            string[] w;
            if (!string.IsNullOrEmpty(experimentId))
            {
                if (Experiments.TryGetValue(experimentId, out w)) return w;
                if (experimentId.StartsWith("cometSample", StringComparison.Ordinal)) return new[] { "cometa", "comet sample" };
            }
            return new[] { "experimento", "experiment" };
        }

        // ---------------- numbers ----------------

        static readonly string[] Units = { "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve",
            "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve",
            "veinte", "veintiuno", "veintidós", "veintitrés", "veinticuatro", "veinticinco", "veintiséis", "veintisiete",
            "veintiocho", "veintinueve" };
        static readonly string[] Tens = { "", "", "", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa" };
        static readonly string[] Hundreds = { "", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos",
            "seiscientos", "setecientos", "ochocientos", "novecientos" };

        /// <summary>
        /// A whole number in Spanish words, 0 to 999 999 999: 21 "veintiuno", 101 "ciento uno", 2500 "dos mil
        /// quinientos". apocope: before a masculine noun "uno" becomes "un" (veintiún kilómetros, un millón).
        /// Negative numbers get "menos".
        /// </summary>
        public static string Number(long n, bool apocope)
        {
            if (n < 0) return "menos " + Number(-n, apocope);
            if (n == 0) return Units[0];
            if (n > 999999999) n = 999999999;
            var parts = new List<string>();
            long millions = n / 1000000, thousands = (n / 1000) % 1000, rest = n % 1000;
            if (millions > 0) parts.Add(millions == 1 ? "un millón" : Below1000(millions, true) + " millones");
            if (thousands > 0) parts.Add(thousands == 1 ? "mil" : Below1000(thousands, true) + " mil");
            if (rest > 0) parts.Add(Below1000(rest, apocope));
            return string.Join(" ", parts.ToArray());
        }

        static string Below1000(long n, bool apocope)
        {
            if (n == 100) return "cien";
            var sb = new StringBuilder();
            long h = n / 100, r = n % 100;
            if (h > 0) sb.Append(Hundreds[h]);
            if (r > 0)
            {
                if (sb.Length > 0) sb.Append(' ');
                string w;
                if (r < 30) w = Units[r];
                else w = Tens[r / 10] + (r % 10 > 0 ? " y " + Units[r % 10] : "");
                if (apocope)
                {
                    if (w == "uno") w = "un";
                    else if (w == "veintiuno") w = "veintiún";
                    else if (w.EndsWith(" y uno", StringComparison.Ordinal)) w = w.Substring(0, w.Length - 3) + "un";
                }
                sb.Append(w);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The value that is spoken and shown, rounded so it stays short ("15 000 000 km" was a long string
        /// of words). From a billion: one decimal below 10 billion. From a million: whole millions, one decimal below
        /// 10 million (2.5 million). From 10 000: whole thousands. Below that: one decimal below 10 for keys with decimals (km, min, deg, ms, science points),
        /// else whole numbers. Spoken words and subtitle digits use this same value.
        /// </summary>
        public static double Rounded(string key, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return v;
            double a = Math.Abs(v), sign = v < 0 ? -1 : 1;
            if (a >= 999500000)
            {
                double g = a / 1e9;
                g = g < 9.95 ? Math.Round(g, 1, MidpointRounding.AwayFromZero) : Math.Round(g, 0, MidpointRounding.AwayFromZero);
                return sign * g * 1e9;
            }
            if (a >= 999500)
            {
                double m = a / 1000000.0;
                m = m < 9.95 ? Math.Round(m, 1, MidpointRounding.AwayFromZero) : Math.Round(m, 0, MidpointRounding.AwayFromZero);
                return sign * m * 1000000.0;
            }
            if (a >= 10000) return sign * Math.Round(a / 1000.0, 0, MidpointRounding.AwayFromZero) * 1000.0;
            if (HasDecimals(key) && a < 10) return Math.Round(v, 1, MidpointRounding.AwayFromZero);
            return Math.Round(v, 0, MidpointRounding.AwayFromZero);
        }

        /// <summary>True when the rounded value is said in millions ("quince millones"): Spanish then needs "de"
        /// before the noun ("quince millones de kilómetros").</summary>
        public static bool InMillions(string key, double v)
        {
            return !double.IsNaN(v) && Math.Abs(Rounded(key, v)) >= 1000000.0;
        }

        static bool HasDecimals(string key) { return Slots.HasDecimals(key); }

        /// <summary>Subtitle digits: "1,234", "7.5", "15 million", "2.5 million".</summary>
        public static string Digits(string key, double v)
        {
            double r = Rounded(key, v);
            if (Math.Abs(r) >= 1e9) return (r / 1e9).ToString("#,0.#", CultureInfo.InvariantCulture) + " billion";
            if (Math.Abs(r) >= 1000000.0)
                return (r / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + " million";
            return r == Math.Floor(r) ? r.ToString("#,0", CultureInfo.InvariantCulture) : r.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>The spoken Spanish of a value: 12.5 gives "doce coma cinco", 15 million "quince millones", 2.5
        /// million "dos coma cinco millones", 1 million "un millón". The day is a count after "día", the other values
        /// come before a noun (apocope: "veintiún kilómetros").</summary>
        public static string Words(string key, double v)
        {
            double r = Rounded(key, v);
            string w;
            if (Math.Abs(r) >= 1e9)
            {
                // Spanish has no word for a billion: "mil millones"
                double g = Math.Abs(r) / 1e9;
                long whole = (long)Math.Floor(g);
                int tenth = (int)Math.Round((g - whole) * 10);
                w = (whole == 1 && tenth == 0 ? "" : Number(whole, true) + (tenth > 0 ? " coma " + Units[tenth] : "") + " ") + "mil millones";
            }
            else if (Math.Abs(r) >= 1000000.0)
            {
                double m = Math.Abs(r) / 1000000.0;
                long whole = (long)Math.Floor(m);
                int tenth = (int)Math.Round((m - whole) * 10);
                if (whole == 1 && tenth == 0) w = "un millón";
                else w = Number(whole, tenth == 0) + (tenth > 0 ? " coma " + Units[tenth] : "") + " millones";
            }
            else
            {
                bool apo = key != "day";
                long whole = (long)Math.Floor(Math.Abs(r));
                int tenth = (int)Math.Round((Math.Abs(r) - whole) * 10);
                w = Number(whole, apo && tenth == 0);
                if (tenth > 0) w += " coma " + Units[tenth];
            }
            return r < 0 ? "menos " + w : w;
        }

        // ---------------- names ----------------

        /// <summary>Spoken names of the stock bodies, as in bodies.tsv read forwards (the build reverses them back):
        /// Eve "Iv", Jool "Yul", the Mun "la Mun". The tests check them against that file.</summary>
        static readonly Dictionary<string, string> StockSpoken = new Dictionary<string, string> {
            { "kerbol", "Kérbol" }, { "moho", "Moho" }, { "eve", "Iv" }, { "gilly", "Guili" }, { "kerbin", "Kérbin" },
            { "mun", "la Mun" }, { "minmus", "Mínmus" }, { "duna", "Duna" }, { "ike", "Aik" }, { "dres", "Dres" },
            { "jool", "Yul" }, { "laythe", "Léiz" }, { "vall", "Val" }, { "tylo", "Tailo" }, { "bop", "Bop" },
            { "pol", "Pol" }, { "eeloo", "Ilu" },
        };

        /// <summary>Spoken Spanish of a body name: the bodies.tsv spelling for a stock body, else the name respelled for
        /// a Spanish voice. Already final: put it in a raw mark (RuntimeSpeech.MarkRaw), not respelled again.</summary>
        public static string BodySpoken(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return "";
            string s;
            if (StockSpoken.TryGetValue(bodyName.Trim().ToLowerInvariant(), out s)) return s;
            return RuntimeSpeech.Respell(CleanName(bodyName));
        }

        /// <summary>English body name for a subtitle: "the Mun", else the name as KSP gives it (without a ^N suffix).</summary>
        public static string BodyEnglish(string bodyName)
        {
            string n = CleanName(bodyName);
            return n == "Mun" ? "the Mun" : n;
        }

        // two letters or more: a single I, V or X is a letter ("Kerbal X")
        static readonly Regex Roman = new Regex(@"^(II|III|IV|VI|VII|VIII|IX)$");
        static readonly string[] RomanValues = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        /// <summary>A vessel name a voice can say: "Mun Lander II" gives "Mun Lander 2" (a digit is said in Spanish by
        /// the voice), " Debris" and brackets go. No length limit. Null for an empty name or one whose letters
        /// cannot be respelled for the Spanish voice.</summary>
        public static string VesselSpeakable(string vesselName)
        {
            return SpeakableName(vesselName, false);
        }

        static string SpeakableName(string name, bool limitLength)
        {
            string n = CleanName(name);
            if (string.IsNullOrEmpty(n) || !LatinOnly(RuntimeSpeech.Respell(n))) return null;
            n = Regex.Replace(n, @"\s*[\(\[].*?[\)\]]\s*", " ").Trim();
            if (n.EndsWith(" Debris", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 7).Trim();
            string[] words = n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || (limitLength && (words.Length > 4 || n.Length > 28))) return null;
            for (int i = 1; i < words.Length; i++)
                if (Roman.IsMatch(words[i])) words[i] = (Array.IndexOf(RomanValues, words[i]) + 1).ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            foreach (string w in words)
            {
                string clean = Regex.Replace(w, @"[^\p{L}\p{Nd}\u3007\uD800-\uDFFF\-']", "");
                if (clean.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(clean);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>A biome name a voice can say, as VesselSpeakable (Latin, Russian Cyrillic or supported Han).
        /// Limited to 4 words and 28 characters. Null for a localization tag left untranslated (#LOC_...).</summary>
        public static string BiomeSpeakable(string biomeName)
        {
            string n = CleanName(biomeName);
            if (string.IsNullOrEmpty(n) || n.StartsWith("#", StringComparison.Ordinal)) return null;
            return SpeakableName(n, true);
        }

        /// <summary>Every letter is Latin (Basic Latin up to Latin Extended-B, so accents pass).</summary>
        public static bool LatinOnly(string text)
        {
            foreach (char c in text ?? "")
                if (char.IsSurrogate(c) || (char.IsLetter(c) && c > '\u024F')) return false;
            return true;
        }

        /// <summary>KSP display names can end in a gender tag (^N, ^f). Trimmed, the tag removed.</summary>
        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string t = name.Trim();
            int caret = t.IndexOf('^');
            return caret > 0 ? t.Substring(0, caret).Trim() : t;
        }

        // ---------------- filling ----------------

        static readonly Regex Slot = new Regex(@"\{([a-z][a-z0-9_]*)\}");

        /// <summary>
        /// Fills a template. nums: numeric values by slot name. names: name slots by slot name, each { spoken, english }
        /// (the spoken one goes into a raw mark, it is final). False when a slot has no value.
        /// spanish: the Piper template (numbers in words, outside the marks, so they are reversed), english: the subtitle.
        /// </summary>
        public static bool Fill(RuntimeTemplate t, Dictionary<string, double> nums, Dictionary<string, string[]> names,
                                out string spanish, out string english)
        {
            return Fill(t, 0, nums, names, out spanish, out english);
        }

        /// <summary>Fill with one version of the template (an index into Versions).</summary>
        public static bool Fill(RuntimeTemplate t, int version, Dictionary<string, double> nums, Dictionary<string, string[]> names,
                                out string spanish, out string english)
        {
            spanish = null; english = null;
            if (t == null || version < 0 || version >= t.Versions.Count) return false;
            return FillText(t.Versions[version][0], t.Versions[version][1], nums, names, out spanish, out english);
        }

        /// <summary>Fills a Spanish and an English text with the same values (also used for recorded lines with
        /// number slots). False when a slot has no value.</summary>
        public static bool FillText(string es, string en, Dictionary<string, double> nums, Dictionary<string, string[]> names,
                                    out string spanish, out string english)
        {
            spanish = null; english = null;
            bool ok = true;
            spanish = Slot.Replace(Singular(es ?? "", nums, SpanishPlurals), m =>
            {
                string k = m.Groups[1].Value;
                double v;
                string[] nm;
                if (nums != null && nums.TryGetValue(k, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) return Words(k, v);
                if (names != null && names.TryGetValue(k, out nm) && nm != null && !string.IsNullOrEmpty(nm[0])) return RuntimeSpeech.MarkRaw(nm[0]);
                ok = false;
                return m.Value;
            });
            english = Slot.Replace(Singular(en ?? "", nums, EnglishPlurals), m =>
            {
                string k = m.Groups[1].Value;
                double v;
                string[] nm;
                if (nums != null && nums.TryGetValue(k, out v) && !double.IsNaN(v) && !double.IsInfinity(v)) return Digits(k, v);
                if (names != null && names.TryGetValue(k, out nm) && nm != null && nm.Length > 1 && !string.IsNullOrEmpty(nm[1])) return nm[1];
                ok = false;
                return m.Value;
            });
            if (!ok) { spanish = null; english = null; }
            return ok;
        }

        static readonly Dictionary<string, string> SpanishPlurals = new Dictionary<string, string> {
            { "kilómetros", "kilómetro" }, { "metros", "metro" }, { "minutos", "minuto" }, { "grados", "grado" },
            { "puntos", "punto" }, { "fondos", "fondo" }, { "piezas", "pieza" }, { "contratos", "contrato" }, { "kerbals", "kerbal" },
            { "vuelos", "vuelo" }, { "viajes", "viaje" }, { "aterrizajes", "aterrizaje" },
        };
        static readonly Dictionary<string, string> EnglishPlurals = new Dictionary<string, string> {
            { "meters", "meter" }, { "minutes", "minute" }, { "degrees", "degree" }, { "points", "point" }, { "funds", "fund" },
            { "parts", "part" }, { "contracts", "contract" }, { "kerbals", "kerbal" },
            { "flights", "flight" }, { "trips", "trip" }, { "landings", "landing" },
        };
        static readonly Regex SlotNoun = new Regex(@"\{([a-z][a-z0-9_]*)\} (\p{L}+)");

        /// <summary>A value that is 1 (after rounding) takes the singular noun right after its slot: "un kilómetro",
        /// "1 minute". In Spanish a value in millions gets "de" before its noun. Applied to the template before it is filled.</summary>
        public static string Singular(string template, Dictionary<string, double> nums, bool spanish)
        {
            return Singular(template, nums, spanish ? SpanishPlurals : EnglishPlurals);
        }

        static string Singular(string template, Dictionary<string, double> nums, Dictionary<string, string> plurals)
        {
            if (nums == null || string.IsNullOrEmpty(template)) return template;
            bool spanish = plurals == SpanishPlurals;
            return SlotNoun.Replace(template, m =>
            {
                double v;
                string k = m.Groups[1].Value, noun = m.Groups[2].Value, one;
                if (!nums.TryGetValue(k, out v) || double.IsNaN(v)) return m.Value;
                if (Math.Abs(Rounded(k, v)) == 1 && plurals.TryGetValue(noun, out one)) return "{" + k + "} " + one;
                // "quince millones de kilómetros", not before "por ciento" or a word that is already "de"
                if (spanish && InMillions(k, v) && noun != "de" && noun != "por") return "{" + k + "} de " + noun;
                return m.Value;
            });
        }
    }
}
