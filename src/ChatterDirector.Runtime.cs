// Experimental (runtime voice): lines spoken at runtime by Piper. With the setting "Runtime voice" on:
//   names    some Mission Control lines end with the name of a kerbal aboard or the vessel, some intercom lines with
//            the partner's name
//   bodies   a {body} line on a modded planet says that planet's name instead of "the planet"
//   new rows rows without a clip (GameData/Lonapse/Phrases/Extra/*.tsv) are spoken at runtime
//   reports  now and then Mission Control reads out real values (Phrases/Runtime): orbit, speed, fuel, power, the next
//            burn, funds and more. Also once per descent (height, speed) and after a docking approach (distance)
//   science  after a science reaction, the experiment, the body and the science points
//   contract a completed contract with its funds, a failed one
// A line that has a clip falls back to it when Piper is not done in time. A line without a clip waits longer and is
// skipped when it still has no sound. Reports, science and contract lines are queued only once their clip is ready.
// Also the Debug tab test: a Mission Control report right now.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        int runtimeAsked, runtimeUsed, runtimeLate, runtimeFailed, runtimeOnly;
        KerbalVoice lastCrewVoice;
        float lastCrewVoiceClock = -999f;
        float nextReport = -1f;
        bool reportPending;
        readonly Queue<string> recentRuntime = new Queue<string>();
        readonly HashSet<string> warmed = new HashSet<string>();

        /// <summary>Share of Mission Control lines that get a name, and of intercom lines that get the partner's.</summary>
        const double RuntimeNameChance = 0.35, RuntimeIntercomNameChance = 0.3;
        /// <summary>With crew aboard, the share of MC names that is the vessel instead of a kerbal.</summary>
        const double RuntimeVesselShare = 0.25;
        /// <summary>After a name was said (an added name, or a line with {other}, {vessel} ...), the time in s in
        /// which lines prefer to say none. People do not name each other in every sentence.</summary>
        const float NameGap = 30f;
        float lastNameClock = -999f;
        bool NamesRecent { get { return clock - lastNameClock < NameGap; } }
        void NameSaid() { lastNameClock = clock; }

        /// <summary>One of the fillable versions of a runtime line. With <paramref name="fewNames"/> one without a
        /// name wins when there is one. A picked version with a name starts the name gap.</summary>
        int PickRuntimeVersion(RuntimeTemplate t, List<int> vs, bool fewNames)
        {
            if (vs == null || vs.Count == 0) return -1;
            if (fewNames)
            {
                List<int> bare = vs.FindAll(i => !SubtitleSlots.NamesSomeone(t.Versions[i][1]));
                if (bare.Count > 0) vs = bare;
            }
            int v = vs[rng.Next(vs.Count)];
            if (SubtitleSlots.NamesSomeone(t.Versions[v][1])) NameSaid();
            return v;
        }
        /// <summary>How long a line may hold its turn for Piper, in s: one with a clip to fall back on, and one without.</summary>
        const float RuntimeMaxWait = 1.0f, RuntimeMaxWaitRequired = 4.0f;
        /// <summary>The kerbal who spoke in the last minute is the one Mission Control answers.</summary>
        const float RuntimeAnswerWindow = 60f;
        /// <summary>Reports: the first after 2 to 5 minutes of flight, then every 4 to 9 minutes, only when calm.</summary>
        const float ReportFirstMin = 120f, ReportFirstMax = 300f, ReportMin = 240f, ReportMax = 540f, ReportMaxStress = 0.35f;
        /// <summary>The share of science reactions that get the numbers line after them.</summary>
        const double RuntimeScienceChance = 0.6;

        /// <summary>A line on its way: Piper renders it while it waits in the queue.</summary>
        public sealed class RuntimeLine
        {
            public PhraseEntry entry;  // the copy that is spoken: its own clip key, its English and Spanish
            public string kind;        // mc, m or f
            public bool required;      // the row has no clip: nothing to fall back on
            public UnityEngine.AudioClip clip;
            public bool failed;
            public float waitFrom = -1f;
        }

        /// <summary>The setting is on and Mission Control's voice is installed.</summary>
        bool RuntimeOn { get { return host.settings.runtimeVoice && RuntimeVoice.Missing("mc") == null; } }

        /// <summary>Counters for the Debug tab.</summary>
        public string RuntimeStats
        {
            get
            {
                return runtimeAsked + " asked, " + runtimeUsed + " played, " + runtimeLate + " too late, " + runtimeFailed
                       + " failed, " + runtimeOnly + " runtime-only lines";
            }
        }

        RuntimeVoice Voice { get { if (host.runtimeVoice == null) host.runtimeVoice = new RuntimeVoice(); return host.runtimeVoice; } }

        /// <summary>The Piper voice of a kerbal: m or f, like the recorded clips. The kerbal's own pitch, timbre and
        /// tremolo come from the playback, the same for recorded and runtime lines.</summary>
        static string KindFor(KerbalVoice sp) { return sp != null && sp.isFemale ? "f" : "m"; }

        /// <summary>For PhraseClips.Playable: a line without a clip can still play when Piper can speak it.</summary>
        bool RuntimeCanSpeak(PhraseEntry e)
        {
            if (e == null || e.SubtitleOnly || string.IsNullOrEmpty(e.Spanish) || !RuntimeOn) return false;
            return e.Role == "mc" || RuntimeVoice.Missing("m") == null || RuntimeVoice.Missing("f") == null;
        }

        // ---------------- the current body and vessel ----------------

        Vessel ActiveVessel { get { try { return FlightGlobals.ActiveVessel; } catch (Exception) { return null; } } }

        string BodyName
        {
            get
            {
                Vessel v = ActiveVessel;
                return v != null && v.mainBody != null ? v.mainBody.bodyName : null;
            }
        }

        static string[] BodyNames(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return null;
            return new[] { RuntimeLines.BodySpoken(bodyName), RuntimeLines.BodyEnglish(bodyName) };
        }

        /// <summary>Spoken and English vessel name, null when it is too long to say.</summary>
        string[] VesselNames() { return NamesOfVessel(ActiveVessel); }

        /// <summary>{vessel} on EVA: the ship nearby, not the kerbal (user report: "Stay close to" the kerbal's own name).</summary>
        string[] ShipNames()
        {
            double d;
            return NamesOfVessel(ShipNear(ActiveVessel, out d));
        }

        static string[] NamesOfVessel(Vessel v)
        {
            string full = v != null ? Names.Of(v) : null;   // stock craft are named #autoLOC_...
            string say = full != null && !full.StartsWith("#", StringComparison.Ordinal) ? RuntimeLines.VesselSpeakable(full) : null;
            if (say == null) return null;
            return new[] { RuntimeSpeech.Respell(say), RuntimeLines.CleanName(full) };
        }

        // ---------------- lines from the table ----------------

        /// <summary>
        /// Plans the runtime version of a table line, or null when the line plays as it is. speaker: the kerbal for a
        /// crew line (null for MC). name: { spoken, english } to add at the end, or null.
        /// </summary>
        RuntimeLine PlanLine(PhraseEntry e, KerbalVoice speaker, string[] name, KerbalVoice partner = null)
        {
            if (e == null || e.SubtitleOnly || string.IsNullOrEmpty(e.Spanish) || !RuntimeOn) return null;
            if (e.ClipKey != null && e.ClipKey.StartsWith("rt_", StringComparison.Ordinal)) return null; // already runtime
            bool mc = e.Role == "mc";
            if (!mc && speaker == null) return null;
            // the delivery of the line's band, as in the build (tense and panic faster and higher, MC calm in a panic)
            string kind = RuntimeDelivery.KindFor(mc ? "mc" : KindFor(speaker), e.Band, e.Severity, e.Event);
            if (kind == null || RuntimeVoice.Missing(kind) != null) return null;
            bool required = PhraseClips.Clip(e, !mc && speaker.isFemale) == null;
            // a subtitle template with its runtime Spanish (spanish_rt): the voice says what the subtitle shows
            string spanish = e.RuntimeSpanish ?? e.Spanish, english = e.English ?? "";
            bool changed = e.RuntimeSpanish != null;
            if (spanish.IndexOf("{body}", StringComparison.Ordinal) >= 0 || english.IndexOf("{body}", StringComparison.Ordinal) >= 0)
            {
                // a new row with {body}: the body name goes in at runtime
                string[] b = BodyNames(BodyName) ?? new[] { "el planeta", "the planet" };
                spanish = spanish.Replace("{body}", RuntimeSpeech.MarkRaw(b[0]));
                english = english.Replace("{body}", b[1]);
                changed = true;
            }
            else if (e.Body == FlightPhase.GenericBody && FlightPhase.BodyKey(BodyName) == FlightPhase.GenericBody)
            {
                // a modded planet: its own name instead of "the planet"
                string[] b = BodyNames(BodyName);
                string s2, e2;
                if (b != null && RuntimeSpeech.SwapBody(spanish, english, RuntimeSpeech.MarkRaw(b[0]), b[1], out s2, out e2))
                {
                    spanish = s2; english = e2; changed = true;
                }
            }
            // a subtitle slot ({vessel}, {other}) already shows a name the recorded Spanish does not say: say it too.
            // Not with the runtime Spanish of the row (spanish_rt): that already says what the subtitle shows.
            string[] shown = e.RuntimeSpanish == null ? SubtitleName(english, spanish, partner) : null;
            if (e.RuntimeSpanish != null) name = null;   // its slots already hold the names it needs
            if (shown != null)
            {
                string s4 = RuntimeSpeech.AddName(spanish, RuntimeSpeech.MarkRaw(shown[0]));
                if (s4 != null) { spanish = s4; changed = true; name = null; }
            }
            if (name != null)
            {
                string s3 = RuntimeSpeech.AddName(spanish, RuntimeSpeech.MarkRaw(name[0]));
                string e3 = RuntimeSpeech.AddName(english, name[1]);
                if (s3 != null && e3 != null) { spanish = s3; english = e3; changed = true; }
            }
            if (!changed && !required) return null;
            var n = CopyEntry(e, e.ClipKey + "_rt" + (++runtimeAsked));
            n.Spanish = RuntimeSpeech.Unmarked(spanish);
            n.LonapseText = RuntimeSpeech.Spoken(spanish);
            n.English = english;
            var rt = new RuntimeLine { entry = n, kind = kind, required = required };
            Voice.Request(kind, spanish, r =>
            {
                if (r.clip == null) { rt.failed = true; runtimeFailed++; return; }
                PhraseClips.SetRuntimeClip(n, r.clip);
                rt.clip = r.clip;
                Log.Info("Runtime voice: " + n.ClipKey + " \"" + r.spoken + "\" ready in " + r.queueToReadyMs.ToString("0")
                         + " ms (Piper " + r.piperMs.ToString("0") + " ms)");
            });
            return rt;
        }

        static PhraseEntry CopyEntry(PhraseEntry e, string key)
        {
            return new PhraseEntry
            {
                ClipKey = key, Event = e.Event, Severity = e.Severity, Band = e.Band, Role = e.Role, Scene = e.Scene,
                Seq = e.Seq, Gender = e.Gender, Phase = e.Phase, Cause = e.Cause, Trait = e.Trait, Persona = e.Persona,
                Topic = e.Topic, Core = e.Core, Beep = e.Beep, Body = e.Body, Spanish = e.Spanish,
                LonapseText = e.LonapseText, English = e.English, Folder = e.Folder,
            };
        }

        /// <summary>The vessel name or the partner's first name when the subtitle shows it as a whole word and the
        /// Spanish does not: { spoken, english }. Null otherwise.</summary>
        string[] SubtitleName(string english, string spanish, KerbalVoice partner)
        {
            if (string.IsNullOrEmpty(english)) return null;
            var cands = new List<string[]>();
            string[] vn = VesselNames();
            if (vn != null)
            {
                Vessel v = ActiveVessel;
                string raw = v != null ? Names.Of(v) : null;
                cands.Add(vn);
                if (!string.IsNullOrEmpty(raw) && raw != vn[1]) cands.Add(new[] { vn[0], raw });
            }
            string[] pn = FirstNames(partner);
            if (pn != null) cands.Add(pn);
            foreach (string[] c in cands)
                if (RuntimeLines.HasWord(english, c[1]) && !RuntimeLines.HasWord(spanish ?? "", c[1])) return c;
            return null;
        }

        /// <summary>An MC line: sometimes with a name at the end. The kerbal MC answers (the scene's speaker, or who
        /// spoke in the last minute), else the vessel or "team". One kerbal picked at random only when alone aboard:
        /// with more crew that sounds like MC talks to one and ignores the rest. Without crew (a probe), the vessel.
        /// No name when one was said a moment ago.</summary>
        RuntimeLine PlanMc(PhraseEntry e, KerbalVoice to = null)
        {
            if (e == null || !RuntimeOn) return null;
            string[] name = null;
            if (!NamesRecent && rng.NextDouble() < RuntimeNameChance)
            {
                int crew = host.roster.voices.Count;
                // user report: MC named one kerbal and another answered. The scene's own crew speaker wins
                // (the lines are queued before anyone speaks, so lastCrewVoice is the one before)
                KerbalVoice k = to != null && host.roster.voices.Contains(to) ? to
                                : lastCrewVoice != null && clock - lastCrewVoiceClock < RuntimeAnswerWindow
                                && host.roster.voices.Contains(lastCrewVoice) ? lastCrewVoice
                                : crew == 1 ? host.roster.voices[0] : null;
                // on EVA the vessel name is the name of the kerbal in control: never that
                bool evaGroup = ActiveVessel != null && ActiveVessel.isEVA;
                if (crew == 0 || (!evaGroup && rng.NextDouble() < RuntimeVesselShare)) name = VesselNames();
                if (name == null && k != null)
                {
                    string first = RuntimeLines.CleanName(SubtitleSlots.FirstName(k.displayName));
                    if (!string.IsNullOrEmpty(first)) name = new[] { RuntimeSpeech.Respell(first), first };
                }
                if (name == null && crew > 1) name = !evaGroup && rng.NextDouble() < 0.5 ? VesselNames() : TeamNames();
            }
            RuntimeLine r = PlanLine(e, null, name);
            if (name != null && r != null) NameSaid();
            return r;
        }

        /// <summary>The whole crew as a name for the end of an MC line: "Good work, team."</summary>
        static string[] TeamNames()
        {
            return new[] { LonapseText.ReverseText("equipo"), "team" };
        }

        /// <summary>An intercom line: sometimes with the partner's first name at the end, not when a name was said a
        /// moment ago.</summary>
        RuntimeLine PlanIntercom(PhraseEntry e, KerbalVoice speaker, KerbalVoice partner)
        {
            if (e == null || !RuntimeOn) return null;
            string[] name = null;
            if (partner != null && !NamesRecent && rng.NextDouble() < RuntimeIntercomNameChance)
            {
                string first = RuntimeLines.CleanName(SubtitleSlots.FirstName(partner.displayName));
                if (!string.IsNullOrEmpty(first)) name = new[] { RuntimeSpeech.Respell(first), first };
            }
            RuntimeLine r = PlanLine(e, speaker, name, partner);
            if (name != null && r != null) NameSaid();
            return r;
        }

        const int TurnWait = 0, TurnGo = 1, TurnSkip = 2;

        /// <summary>
        /// At the turn of a line with a runtime plan. TurnWait: Piper is not done and the wait is still short.
        /// TurnGo with the runtime clip and entry, or with nulls to play the table line. TurnSkip: a line without a
        /// clip that got no sound in time. speaker: the kerbal who speaks now (null for MC).
        /// </summary>
        int RuntimeTurn(Utterance u, KerbalVoice speaker, out UnityEngine.AudioClip clip, out PhraseEntry shown)
        {
            clip = null; shown = null;
            RuntimeLine rt = u.rt;
            if (rt == null) return TurnGo;
            // a different kerbal took the line (the planned one fainted): the other voice only when there is no clip
            if (speaker != null && RuntimeDelivery.BaseKind(rt.kind) != KindFor(speaker) && !rt.required) { u.rt = null; return TurnGo; }
            if (rt.clip != null) { clip = rt.clip; shown = rt.entry; runtimeUsed++; return TurnGo; }
            if (!rt.failed)
            {
                if (rt.waitFrom < 0f) rt.waitFrom = clock;
                if (clock - rt.waitFrom < (rt.required ? RuntimeMaxWaitRequired : RuntimeMaxWait)) return TurnWait;
                runtimeLate++;
                Log.Info("Runtime voice: " + rt.entry.ClipKey + " too late" + (rt.required ? ", skipped (no clip)" : ", the table line plays"));
            }
            u.rt = null;
            return rt.required ? TurnSkip : TurnGo;
        }

        /// <summary>Called when a crew line starts: the kerbal Mission Control answers next.</summary>
        void NoteCrewSpeaker(KerbalVoice sp)
        {
            lastCrewVoice = sp;
            lastCrewVoiceClock = clock;
        }

        // ---------------- runtime-only lines ----------------

        /// <summary>Per frame: starts Piper for the voices aboard (once each) and times the reports.</summary>
        void RuntimeTick()
        {
            PhraseClips.runtimeCanSpeak = RuntimeCanSpeak;
            if (!RuntimeOn) return;
            WarmOnce("mc");
            for (int i = 0; i < host.roster.voices.Count; i++)
                if (host.roster.voices[i] != null) WarmOnce(KindFor(host.roster.voices[i]));
            if (!host.settings.runtimeReports) return;
            RuntimeLandCheck();
            SunriseTick();
            if (nextReport < 0f) nextReport = clock + U(ReportFirstMin, ReportFirstMax);
            if (clock < nextReport || reportPending) return;
            if (count > 0 || host.stress.stress > ReportMaxStress || !host.signal.connected || radioSilent || mcBlackout
                || !host.settings.radioEnabled)
            {
                nextReport = clock + 20f;
                return;
            }
            nextReport = clock + U(ReportMin, ReportMax);
            QueueReport();
        }

        void WarmOnce(string kind)
        {
            if (warmed.Contains(kind) || RuntimeVoice.Missing(kind) != null) return;
            warmed.Add(kind);
            Voice.Warm(kind);
        }

        /// <summary>Situation key of a vessel for the templates, null on the pad.</summary>
        static string SituationOf(Vessel v)
        {
            switch (v.situation)
            {
                case Vessel.Situations.ORBITING: return "orbit";
                case Vessel.Situations.FLYING: return "flight";
                case Vessel.Situations.SUB_ORBITAL: return "suborbital";
                case Vessel.Situations.LANDED:
                case Vessel.Situations.SPLASHED: return "landed";
                case Vessel.Situations.ESCAPING: return "escape";
                case Vessel.Situations.PRELAUNCH: return null;
                default: return "other";
            }
        }

        /// <summary>Every value a template can use, as far as KSP gives it here. A value KSP does not have (no
        /// node, sandbox funds, no fuel tanks) is left out, so the templates that need it do not fit.</summary>
        void CollectValues(Vessel v, string sit, Dictionary<string, double> nums, Dictionary<string, string[]> names, bool log = true)
        {
            nums["alt_km"] = v.altitude / 1000.0;
            nums["speed"] = sit == "orbit" || sit == "escape" ? v.obt_speed : v.srfSpeed;
            Orbit o = v.orbit;
            if (o != null && (sit == "orbit" || sit == "suborbital" || sit == "escape"))
            {
                nums["pe_km"] = o.PeA / 1000.0;
                if (o.ApA > 0) nums["ap_km"] = o.ApA / 1000.0;
                nums["inc_deg"] = o.inclination;
                if (sit == "orbit") { nums["period_min"] = o.period / 60.0; nums["t_pe_min"] = o.timeToPe / 60.0; }
                if (o.ApA > 0 && o.timeToAp > 0) nums["t_ap_min"] = o.timeToAp / 60.0;
            }
            // not on the pad: "fuel left" means something was used (user report: a fuel line before any engine fired)
            bool pad = false;
            try { pad = v.situation == Vessel.Situations.PRELAUNCH; } catch (Exception) { }
            bool eva = v.isEVA;
            if (!eva)
            {
                if (!pad) Put(nums, "fuel", StageFuelPercent(v, log));
                Put(nums, "ec", ResourcePercent(v, "ElectricCharge"));
                Put(nums, "mono", ResourcePercent(v, "MonoPropellant"));
                try { nums["parts"] = v.parts.Count; nums["crew"] = v.GetCrewCount(); } catch (Exception) { }
            }
            else
            {
                // a kerbal on EVA: the jetpack, the ship nearby and the time outside. Not the vessel values: the
                // kerbal is a vessel of one part (user report: "one kerbal aboard" answered by a kerbal outside)
                double jet = ResourcePercent(v, "EVA Propellant");
                if (double.IsNaN(jet)) jet = ResourcePercent(v, "MonoPropellant");
                Put(nums, "jet", jet);
                double sd;
                if (ShipNear(v, out sd) != null) nums["ship_m"] = Math.Round(sd);
                double es = EvaSeconds();
                if (!double.IsInfinity(es)) nums["eva_mins"] = Math.Floor(es / 60.0);
                nums["on_eva"] = 1;
            }
            try
            {
                if (v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes.Count > 0)
                {
                    ManeuverNode n = v.patchedConicSolver.maneuverNodes[0];
                    double dt = n.UT - Planetarium.GetUniversalTime();
                    if (dt > 0) { nums["node_min"] = dt / 60.0; nums["node_h"] = dt / 3600.0; nums["node_dv"] = n.DeltaV.magnitude; }
                }
            }
            catch (Exception) { }
            try { if (Funding.Instance != null) nums["funds_total"] = Funding.Instance.Funds; } catch (Exception) { }
            try { if (ResearchAndDevelopment.Instance != null) nums["sci_total"] = ResearchAndDevelopment.Instance.Science; } catch (Exception) { }
            try { if (Reputation.Instance != null) nums["rep"] = Reputation.CurrentRep; } catch (Exception) { }
            try
            {
                if (Contracts.ContractSystem.Instance != null)
                {
                    int active = 0;
                    foreach (Contracts.Contract c in Contracts.ContractSystem.Instance.Contracts)
                        if (c != null && c.ContractState == Contracts.Contract.State.Active) active++;
                    nums["contracts"] = active;
                }
            }
            catch (Exception) { }
            string day;
            double dd;
            if (BaseFlightValues().TryGetValue("day", out day) && double.TryParse(day, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out dd)) nums["day"] = dd;
            string[] b = BodyNames(v.mainBody != null ? v.mainBody.bodyName : null);
            if (b != null) { names["body"] = b; names["here"] = b; }
            string[] vn = v.isEVA ? ShipNames() : VesselNames();
            if (vn != null) names["vessel"] = vn;
            try { if (v.mainBody != null) nums["at_home"] = v.mainBody == FlightGlobals.GetHomeBody() ? 1 : 0; } catch (Exception) { }
            // the biome under the vessel, said as a name (Kerbin's "Water" reads badly in a sentence, left out)
            try
            {
                CelestialBody mb = v.mainBody;
                if (mb != null && mb.BiomeMap != null && sit != "escape")
                {
                    string bn = ScienceUtil.GetExperimentBiomeLocalized(mb, v.latitude, v.longitude);
                    string say = bn != "Water" ? RuntimeLines.BiomeSpeakable(bn) : null;
                    if (say != null) names["biome"] = new[] { RuntimeSpeech.Respell(say), RuntimeLines.CleanName(bn) };
                }
            }
            catch (Exception) { }
            if (sunrises > 0 && v.id == sunVessel) nums["sunrises"] = sunrises;
            // time warp chatter (only for checks, the rate is never said), and the time to the next body when the
            // orbit leaves this one
            if (host.warpChatter) nums["in_warp"] = 1;
            try
            {
                if (o != null && (o.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER
                                  || o.patchEndTransition == Orbit.PatchTransitionType.ESCAPE))
                {
                    double left = o.EndUT - Planetarium.GetUniversalTime();
                    CelestialBody nb = o.nextPatch != null ? o.nextPatch.referenceBody : null;
                    if (left > 0) nums["soi_h"] = left / 3600.0;
                    string[] nn = nb != null ? BodyNames(nb.bodyName) : null;
                    if (left > 0 && nn != null) names["next"] = nn;
                }
            }
            catch (Exception) { }
        }

        static void Put(Dictionary<string, double> d, string k, double v) { if (!double.IsNaN(v)) d[k] = v; }

        Dictionary<string, double> rtNumbers;
        float rtNumbersAt = -100f;
        Guid rtNumbersId;
        /// <summary>How often the numbers for recorded lines are worked out, in s. CanFill asks every frame and
        /// CollectValues walks every part.</summary>
        const float RuntimeNumbersEvery = 2f;

        /// <summary>The numbers of CollectValues for recorded lines with a number version ({fuel}, {ec} ...), null
        /// with the runtime voice off or without a vessel. Without crew history: that belongs to one kerbal.</summary>
        Dictionary<string, double> RuntimeNumbers()
        {
            if (!RuntimeOn) return null;
            Vessel v = ActiveVessel;
            if (v == null) return null;
            if (rtNumbers != null && v.id == rtNumbersId && clock - rtNumbersAt < RuntimeNumbersEvery) return rtNumbers;
            var nums = new Dictionary<string, double>();
            try { CollectValues(v, SituationOf(v) ?? "other", nums, new Dictionary<string, string[]>(), false); }
            catch (Exception e) { Log.Debug("Runtime voice numbers: " + e.Message); }
            rtNumbers = nums;
            rtNumbersAt = clock;
            rtNumbersId = v.id;
            return nums;
        }

        /// <summary>The crew history of a kerbal aboard (RuntimeLines.CrewHistory) into nums. Nothing for an unknown
        /// kerbal: templates that need these values then do not fit.</summary>
        void CrewValues(KerbalVoice k, Vessel v, Dictionary<string, double> nums)
        {
            if (k == null || v == null) return;
            try
            {
                ProtoCrewMember pcm = HighLogic.CurrentGame != null && HighLogic.CurrentGame.CrewRoster != null
                                      ? HighLogic.CurrentGame.CrewRoster[k.name] : null;
                if (pcm == null) return;
                var entries = new List<string[]>();
                if (pcm.careerLog != null)
                    foreach (FlightLog.Entry e in pcm.careerLog.Entries)
                        entries.Add(new[] { e.flight.ToString(System.Globalization.CultureInfo.InvariantCulture), e.type, e.target });
                RuntimeLines.CrewHistory(entries, v.mainBody != null ? v.mainBody.bodyName : null, pcm.experienceLevel, nums);
            }
            catch (Exception e) { Log.Debug("Runtime voice crew history: " + e.Message); }
        }

        /// <summary>
        /// Liquid fuel left in percent for the stage that burns: the tanks that feed a running engine (its crossfeed
        /// set), like the stage view of the KSP resource panel (user report: 73 % was said with 530 of 1125 in the
        /// stage, the full upper stage tanks were counted too). No running engine: the tanks of the next stage with a
        /// liquid engine, the one that fires next. No liquid engine at all: NaN, so no fuel line plays (user report:
        /// "92 % fuel left" before any engine had fired, the old fallback counted every tank of the vessel).
        /// </summary>
        static double StageFuelPercent(Vessel v, bool log)
        {
            var burning = new List<Part>();
            var waiting = new List<Part>();
            try
            {
                foreach (Part p in v.parts)
                    foreach (PartModule m in p.Modules)
                    {
                        var e = m as ModuleEngines;
                        if (e == null || e.flameout) continue;
                        bool liquid = false;
                        foreach (Propellant pr in e.propellants) if (pr.name == "LiquidFuel") liquid = true;
                        if (!liquid) continue;
                        if (e.EngineIgnited) burning.Add(p);
                        else if (p.inverseStage < v.currentStage) waiting.Add(p);
                    }
            }
            catch (Exception) { burning.Clear(); waiting.Clear(); }
            string what = "the burning stage";
            List<Part> engines = burning;
            if (engines.Count == 0 && waiting.Count > 0)
            {
                // the next stage to fire: the highest inverseStage below the current stage
                int next = int.MinValue;
                foreach (Part p in waiting) if (p.inverseStage > next) next = p.inverseStage;
                engines = waiting.FindAll(p => p.inverseStage == next);
                what = "the next stage (stage " + next + ", no engine running)";
            }
            var feed = new HashSet<Part>();
            try
            {
                foreach (Part p in engines)
                {
                    if (p.crossfeedPartSet != null) foreach (Part q in p.crossfeedPartSet.GetParts()) feed.Add(q);
                    else feed.Add(p);
                }
            }
            catch (Exception) { feed.Clear(); }
            double have = 0, max = 0;
            foreach (Part p in feed)
                foreach (PartResource r in p.Resources)
                    if (r.resourceName == "LiquidFuel") { have += r.amount; max += r.maxAmount; }
            if (max > 0.01)
            {
                if (log) Log.Info("Runtime voice: fuel " + have.ToString("0") + " / " + max.ToString("0") + " in " + what + " (" + feed.Count + " parts)");
                return 100.0 * have / max;
            }
            if (log) Log.Info("Runtime voice: no fuel value (no liquid engine with tanks)");
            return double.NaN;
        }

        /// <summary>A resource left in percent, NaN when the vessel has none of it.</summary>
        static double ResourcePercent(Vessel v, string resource)
        {
            double have = 0, max = 0;
            try
            {
                foreach (Part p in v.parts)
                    foreach (PartResource r in p.Resources)
                        if (r.resourceName == resource) { have += r.amount; max += r.maxAmount; }
            }
            catch (Exception) { return double.NaN; }
            return max > 0.01 ? 100.0 * have / max : double.NaN;
        }

        /// <summary>Picks a template of a kind that fits now, not one of the last few, and queues it. False when none fits.</summary>
        bool QueueKind(string kind, string sit, Dictionary<string, double> nums, Dictionary<string, string[]> names, float delay, bool report,
                       double replyChance = ReplyChance)
        {
            List<RuntimeTemplate> fit = RuntimeLines.Fitting(PhraseClips.runtimeTemplates, kind, sit, nums, names);
            List<RuntimeTemplate> fresh = fit.FindAll(t => !recentRuntime.Contains(t.Id));
            if (fresh.Count > 0) fit = fresh;
            else if (kind == "report" || kind == "warp" || kind == "eva") return false;   // a report can wait, an answer to an event may repeat
            if (fit.Count == 0) return false;
            QueueRuntimeOnly(fit[rng.Next(fit.Count)], nums, names, delay, report, replyChance);
            return true;
        }

        /// <summary>A report that fits the vessel now, not one of the last few. False when none fits.</summary>
        bool QueueReport()
        {
            Vessel v = ActiveVessel;
            return QueueReport(v != null && v.isEVA ? "eva" : "report");
        }

        /// <summary>A report of a kind: report, or eva on EVA (the ship reports do not fit a kerbal outside).</summary>
        bool QueueReport(string kind, double replyChance = ReplyChance)
        {
            Vessel v = ActiveVessel;
            if (v == null) return false;
            string sit = SituationOf(v);
            if (sit == null) return false;   // on the pad there is nothing to report yet
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            CollectValues(v, sit, nums, names);
            // the kerbal Mission Control talks to in a report with {kerbal}: the one who spoke last, else one aboard
            if (host.roster.voices.Count > 0)
            {
                KerbalVoice k = lastCrewVoice != null && clock - lastCrewVoiceClock < RuntimeAnswerWindow
                                && host.roster.voices.Contains(lastCrewVoice) ? lastCrewVoice
                                : host.roster.voices[rng.Next(host.roster.voices.Count)];
                string[] kn = FirstNames(k);
                if (kn != null) { names["kerbal"] = kn; CrewValues(k, v, nums); reportKerbal = k; }
            }
            try { return QueueKind(kind, sit, nums, names, 0.3f, true, replyChance); }
            finally { reportKerbal = null; }
        }

        // ---------------- EVA ----------------

        readonly Dictionary<string, double> evaStartUT = new Dictionary<string, double>();
        const double EvaStartWindow = 90.0, NearShipRange = 100.0, ShipRange = 500.0;
        /// <summary>Scenes about going out (only in the first EvaStartWindow s) and about the hatch or the ship (only
        /// within NearShipRange of it). User report: "Leaving the hatch" far from the ship.</summary>
        static readonly string[] EvaStartScenes = { "eva_check_03", "ev_eva_08" };
        static readonly string[] NearShipScenes = { "ev_eva_03", "ev_eva_04" };

        /// <summary>A kerbal went out now (onCrewOnEva).</summary>
        public void NoteEvaStart(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            try { evaStartUT[name] = Planetarium.GetUniversalTime(); } catch (Exception) { }
        }

        /// <summary>Seconds the active EVA kerbal has been outside. Infinity when not on EVA or not seen going out
        /// (a loaded save): then it counts as a long time.</summary>
        double EvaSeconds()
        {
            Vessel v = ActiveVessel;
            if (v == null || !v.isEVA) return double.PositiveInfinity;
            try
            {
                foreach (ProtoCrewMember pcm in v.GetVesselCrew())
                {
                    double t;
                    if (pcm != null && evaStartUT.TryGetValue(pcm.name, out t)) return Math.Max(0, Planetarium.GetUniversalTime() - t);
                }
            }
            catch (Exception) { }
            return double.PositiveInfinity;
        }

        /// <summary>The ship of an EVA kerbal: the nearest loaded vessel within ShipRange that is not a kerbal, a flag,
        /// debris or an asteroid. Null when there is none.</summary>
        static Vessel ShipNear(Vessel v, out double dist)
        {
            dist = double.PositiveInfinity;
            Vessel best = null;
            if (v == null) return null;
            try
            {
                List<Vessel> loaded = FlightGlobals.VesselsLoaded;
                for (int i = 0; i < loaded.Count; i++)
                {
                    Vessel o = loaded[i];
                    if (o == null || ReferenceEquals(o, v) || o.isEVA) continue;
                    if (o.vesselType == VesselType.Flag || o.vesselType == VesselType.Debris || o.vesselType == VesselType.SpaceObject
                        || o.vesselType == VesselType.DeployedScienceController || o.vesselType == VesselType.DeployedSciencePart) continue;
                    double d = Vector3d.Distance(o.GetWorldPos3D(), v.GetWorldPos3D());
                    if (d < dist && d <= ShipRange) { dist = d; best = o; }
                }
            }
            catch (Exception) { best = null; dist = double.PositiveInfinity; }
            return best;
        }

        /// <summary>The scene fits the EVA now: the going-out lines only at the start, the hatch lines near the ship.</summary>
        bool EvaSceneOk(PhraseScene sc)
        {
            if (sc == null) return false;
            if (Array.IndexOf(EvaStartScenes, sc.Id) >= 0 && EvaSeconds() > EvaStartWindow) return false;
            if (Array.IndexOf(NearShipScenes, sc.Id) >= 0)
            {
                double d;
                if (ShipNear(ActiveVessel, out d) == null || d > NearShipRange) return false;
            }
            return true;
        }

        /// <summary>A runtime EVA line (kind eva) instead of a table scene, for variety. False when none fits.</summary>
        bool QueueEvaLine()
        {
            if (!RuntimeOn || !host.settings.runtimeReports || reportPending || radioSilent || mcBlackout
                || !host.signal.connected || !host.settings.radioEnabled) return false;
            return QueueReport("eva");
        }

        /// <summary>A flag stands (afterFlagPlanted): Mission Control says something about it, and the kerbal who
        /// planted it nearly always answers. {flag} is the flag's name when the voice can say it.</summary>
        public void RuntimeFlag(string placedBy, string flagName)
        {
            if (!RuntimeOn || !host.settings.runtimeReports || radioSilent || mcBlackout || !host.signal.connected
                || !host.settings.radioEnabled) return;
            Vessel v = ActiveVessel;
            if (v == null) return;
            string sit = SituationOf(v) ?? "landed";
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            CollectValues(v, sit, nums, names);
            string say = !string.IsNullOrEmpty(flagName) && !flagName.StartsWith("#", StringComparison.Ordinal)
                         ? RuntimeLines.VesselSpeakable(flagName) : null;
            if (say != null) names["flag"] = new[] { RuntimeSpeech.Respell(say), RuntimeLines.CleanName(flagName) };
            KerbalVoice k = placedBy != null ? host.roster.voices.Find(x => x != null && x.name == placedBy) : null;
            if (k == null) k = host.roster.Own;
            string[] kn = k != null ? FirstNames(k) : null;
            if (kn != null) { names["kerbal"] = kn; reportKerbal = k; }
            try
            {
                bool ok = QueueKind("flag", sit, nums, names, 0.6f, false, FlagReplyChance);
                Log.Info("Runtime voice: flag line " + (ok ? "queued" : "none fits"));
            }
            finally { reportKerbal = null; }
        }

        const double FlagReplyChance = 0.9;

        /// <summary>Share of radio turns in time warp that are a warp line (kind warp), when one fits.</summary>
        const double WarpLineChance = 0.75;
        /// <summary>Share of intercom chats in time warp that are a runtime chat, so a warp chat can come up.</summary>
        const double WarpChatChance = 0.85;

        /// <summary>Clip keys of lines about the warp (warp lines, warp chats and their replies) queued in a real
        /// warp. When the warp has ended by their turn they are dropped, so nobody talks about the warp after it.</summary>
        readonly HashSet<string> warpOnlyKeys = new HashSet<string>();

        void MarkWarpOnly(string clipKey)
        {
            if (clipKey == null) return;
            if (warpOnlyKeys.Count > 200) warpOnlyKeys.Clear();
            warpOnlyKeys.Add(clipKey);
        }

        /// <summary>The line is about a warp that has ended: skip it.</summary>
        bool WarpOver(PhraseEntry e)
        {
            return e != null && !host.warpChatter && e.ClipKey != null && warpOnlyKeys.Contains(e.ClipKey);
        }

        /// <summary>Time warp chatter: a warp line from Mission Control (time to the node or the next body).
        /// False when the runtime voice is off, the radio is cut or none fits: the usual exchange plays then.</summary>
        bool QueueWarpLine()
        {
            if (!RuntimeOn || !host.settings.runtimeReports || reportPending || radioSilent || mcBlackout
                || !host.signal.connected || !host.settings.radioEnabled) return false;
            Vessel v = ActiveVessel;
            if (v == null) return false;
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            string sit = SituationOf(v) ?? "other";
            CollectValues(v, sit, nums, names);
            return QueueKind("warp", sit, nums, names, U(0f, 0.3f), true);
        }

        /// <summary>After the docking approach call: distance and closing speed to the target.</summary>
        public void RuntimeDock()
        {
            if (!RuntimeOn || !host.settings.runtimeReports) return;
            Vessel v = ActiveVessel;
            if (v == null) return;
            try
            {
                ITargetable t = v.targetObject;
                if (t == null || t.GetTransform() == null) return;
                Vector3d to = (Vector3d)t.GetTransform().position - (Vector3d)v.transform.position;
                double dist = to.magnitude;
                Vector3d rel = v.obt_velocity - t.GetObtVelocity();
                double closing = dist > 0.1 ? Vector3d.Dot(rel, to / dist) : 0;
                var nums = new Dictionary<string, double> { { "dist_m", dist }, { "close_ms", closing } };
                var names = new Dictionary<string, string[]>();
                Vessel tv = t.GetVessel();
                string tn = tv != null ? Names.Of(tv) : null;
                string say = tn != null && !tn.StartsWith("#", StringComparison.Ordinal) ? RuntimeLines.VesselSpeakable(tn) : null;
                if (say != null) names["target"] = new[] { RuntimeSpeech.Respell(say), RuntimeLines.CleanName(tn) };
                string[] own = VesselNames();
                if (own != null) names["vessel"] = own;
                QueueKind("dock", SituationOf(v), nums, names, ResponseDelay(host.stress.stress) + 0.5f, false);
            }
            catch (Exception e) { Log.Debug("Runtime voice dock: " + e.Message); }
        }

        /// <summary>Share of intercom chats that are a runtime chat (real values), when one fits.</summary>
        const double RuntimeChatChance = 0.25;
        bool runtimeChatPending;

        static string[] FirstNames(KerbalVoice k)
        {
            string first = k != null ? RuntimeLines.CleanName(SubtitleSlots.FirstName(k.displayName)) : null;
            return string.IsNullOrEmpty(first) ? null : new[] { RuntimeSpeech.Respell(first), first };
        }

        /// <summary>
        /// Sometimes an intercom chat from Phrases/Runtime instead of the table: every line rendered in the voice of its
        /// kerbal, queued as one chat once all lines are ready. True when one was started (the caller queues nothing).
        /// </summary>
        bool TryRuntimeChat(KerbalVoice a, KerbalVoice b, bool eva = false)
        {
            double chance = eva ? 1.0 : host.warpChatter ? WarpChatChance : RuntimeChatChance;
            if (!RuntimeOn || runtimeChatPending || a == null || b == null || (!debugForce && rng.NextDouble() >= chance)) return false;
            if (RuntimeVoice.Missing(KindFor(a)) != null || RuntimeVoice.Missing(KindFor(b)) != null) return false;
            Vessel v = ActiveVessel;
            if (v == null) return false;
            string sit = SituationOf(v) ?? "other";
            var nums = new Dictionary<string, double>();
            var common = new Dictionary<string, string[]>();
            CollectValues(v, sit, nums, common);
            string[] na = FirstNames(a), nb = FirstNames(b);
            if (na == null || nb == null) return false;
            var namesA = new Dictionary<string, string[]>(common) { { "self", na }, { "other", nb } };
            var namesB = new Dictionary<string, string[]>(common) { { "self", nb }, { "other", na } };
            // a chat with jobs (a=pilot b=scientist) also fits with A and B swapped: then B starts it
            string jobA = a.persona.ChatKeys, jobB = b.persona.ChatKeys;
            // the crew history values (flight_no, visits ...) are those of the kerbal who starts the chat
            var numsA = new Dictionary<string, double>(nums);
            var numsB = new Dictionary<string, double>(nums);
            CrewValues(a, v, numsA);
            CrewValues(b, v, numsB);
            if (eva)
            {
                // on EVA the jetpack and the ship distance are the speaker's own, pair_m the meters between the two
                EvaValues(a, b, numsA);
                EvaValues(b, a, numsB);
            }
            var fit = new List<KeyValuePair<List<RuntimeTemplate>, bool>>();
            foreach (List<RuntimeTemplate> c in RuntimeLines.Chats(PhraseClips.runtimeTemplates))
            {
                if (recentRuntime.Contains(c[0].Id)) continue;
                if (RuntimeLines.ChatFits(c, sit, numsA, namesA, namesB, jobA, jobB)) fit.Add(new KeyValuePair<List<RuntimeTemplate>, bool>(c, false));
                else if (RuntimeLines.ChatFits(c, sit, numsB, namesB, namesA, jobB, jobA)) fit.Add(new KeyValuePair<List<RuntimeTemplate>, bool>(c, true));
            }
            // on EVA only the suit radio chats, inside never those
            fit.RemoveAll(c => c.Key[0].AboutEva != eva);
            // in time warp a chat about the warp when one fits
            if (host.warpChatter && !eva)
            {
                var warpFit = fit.FindAll(c => c.Key[0].AboutWarp);
                if (warpFit.Count > 0) fit = warpFit;
            }
            if (fit.Count == 0) return false;
            var pick = fit[rng.Next(fit.Count)];
            List<RuntimeTemplate> chat = pick.Key;
            if (pick.Value)
            {
                KerbalVoice k = a; a = b; b = k;
                var n = namesA; namesA = namesB; namesB = n;
            }
            nums = pick.Value ? numsB : numsA;
            recentRuntime.Enqueue(chat[0].Id);
            while (recentRuntime.Count > 6) recentRuntime.Dequeue();

            var entries = new List<PhraseEntry>();
            var spanish = new List<string>();
            for (int i = 0; i < chat.Count; i++)
            {
                string sp, en;
                Dictionary<string, string[]> who = i % 2 == 0 ? namesA : namesB;
                List<int> vs = RuntimeLines.FillableVersions(chat[i], nums, who);
                // one name per chat when the lines allow it: a line with a name starts the gap, the rest prefer none
                int vi = PickRuntimeVersion(chat[i], vs, NamesRecent);
                if (vi < 0 || !RuntimeLines.Fill(chat[i], vi, nums, who, out sp, out en)) return false;
                var e = new PhraseEntry
                {
                    ClipKey = chat[i].Id + "_" + (++runtimeAsked), Event = "chatter", Severity = "chatter", Band = "calm", Role = "crew",
                    Scene = chat[0].Id, Seq = i + 1, Gender = "both", Phase = "any", Cause = "any",
                    Spanish = RuntimeSpeech.Unmarked(sp), LonapseText = RuntimeSpeech.Spoken(sp), English = en,
                };
                e.Folder = PhraseTable.FolderFor(e.Role, e.Severity, e.Band, e.Event);
                if (chat[0].AboutWarp && host.warpChatter) MarkWarpOnly(e.ClipKey);
                entries.Add(e);
                spanish.Add(sp);
            }
            runtimeChatPending = true;
            int left = entries.Count;
            bool failed = false;
            var clips = new UnityEngine.AudioClip[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                int idx = i;
                Voice.Request(KindFor(i % 2 == 0 ? a : b), spanish[i], r =>
                {
                    if (r.clip == null) failed = true;
                    clips[idx] = r.clip;
                    if (--left > 0) return;
                    runtimeChatPending = false;
                    bool ok = !failed && CanChat(a) && CanChat(b) && !Somber
                              && (eva ? host.settings.radioEnabled && !radioSilent && host.evaMode : host.settings.intercomEnabled);
                    if (ok)
                    {
                        for (int k = 0; k < entries.Count; k++) PhraseClips.SetRuntimeClip(entries[k], clips[k]);
                        ok = eva ? QueueRadioChat(a, b, entries) : QueueChatTurns(a, b, entries, entries.Count, null, false);
                    }
                    if (!ok)
                    {
                        if (failed) runtimeFailed++;
                        foreach (var c in clips) if (c != null) UnityEngine.Object.Destroy(c);
                        Log.Info("Runtime voice: chat " + chat[0].Id + " dropped (" + (failed ? "Piper failed" : "the chat cannot start now") + ")");
                        return;
                    }
                    runtimeOnly++;
                    Log.Info("Runtime voice: chat " + chat[0].Id + " (" + a.displayName + ", " + b.displayName + ") ready in "
                             + r.queueToReadyMs.ToString("0") + " ms");
                });
            }
            return true;
        }

        /// <summary>An EVA chat over the suit radio: each line keyed like a crew radio line (click, line, beep,
        /// squelch), the two kerbals in turn. False when the queue has no room.</summary>
        bool QueueRadioChat(KerbalVoice a, KerbalVoice b, List<PhraseEntry> entries)
        {
            if (count + 4 * entries.Count > Capacity) return false;
            for (int i = 0; i < entries.Count; i++)
                EnqueueCrew(entries[i].Folder, i == 0 ? U(0.2f, 0.6f) : ResponseDelay(host.stress.stress), 0f, i % 2 == 0 ? a : b, entries[i]);
            return true;
        }

        /// <summary>The loaded EVA vessel of a kerbal (the active one or a nearby one), null when not on EVA.</summary>
        static Vessel EvaVesselOf(KerbalVoice k)
        {
            if (k == null) return null;
            try
            {
                List<Vessel> loaded = FlightGlobals.VesselsLoaded;
                for (int i = 0; i < loaded.Count; i++)
                {
                    Vessel o = loaded[i];
                    if (o == null || !o.isEVA) continue;
                    foreach (ProtoCrewMember pcm in o.GetVesselCrew()) if (pcm != null && pcm.name == k.name) return o;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>EVA values of kerbal k for a suit radio chat: its own jetpack and ship distance, and pair_m to
        /// the other kerbal.</summary>
        void EvaValues(KerbalVoice k, KerbalVoice other, Dictionary<string, double> nums)
        {
            Vessel kv = EvaVesselOf(k), ov = EvaVesselOf(other);
            if (kv == null) return;
            double jet = ResourcePercent(kv, "EVA Propellant");
            if (double.IsNaN(jet)) jet = ResourcePercent(kv, "MonoPropellant");
            nums.Remove("jet");
            Put(nums, "jet", jet);
            double sd;
            nums.Remove("ship_m");
            if (ShipNear(kv, out sd) != null) nums["ship_m"] = Math.Round(sd);
            try { if (ov != null) nums["pair_m"] = Math.Round(Vector3d.Distance(kv.GetWorldPos3D(), ov.GetWorldPos3D())); }
            catch (Exception) { }
        }

        /// <summary>Two kerbals of an EVA group talk over the suit radio (a runtime EVA chat). False when there are
        /// not two who can talk, or no chat fits.</summary>
        bool TryEvaChat()
        {
            if (!host.evaMode || host.roster.SpeakableCount() < 2) return false;
            KerbalVoice a = PickRested();
            if (!CanChat(a)) return false;
            var others = host.roster.voices.FindAll(k => !ReferenceEquals(k, a) && CanChat(k));
            if (others.Count == 0) return false;
            return TryRuntimeChat(a, others[rng.Next(others.Count)], true);
        }

        bool landSaid;

        /// <summary>Once per descent: low over the ground and coming down, Mission Control reads height and speed.
        /// Re-armed above 5 km or after landing.</summary>
        void RuntimeLandCheck()
        {
            Vessel v = ActiveVessel;
            if (v == null) return;
            double h = v.heightFromTerrain;
            if (v.LandedOrSplashed || h > 5000) { landSaid = false; return; }
            if (landSaid || h <= 50 || h > 1500 || v.verticalSpeed > -2) return;
            if (v.situation != Vessel.Situations.FLYING && v.situation != Vessel.Situations.SUB_ORBITAL) return;
            landSaid = true;
            if (rng.NextDouble() >= 0.6 || count + 7 > Capacity || radioSilent || mcBlackout || !host.signal.connected) return;
            var nums = new Dictionary<string, double> { { "height_m", h }, { "vspeed_ms", -v.verticalSpeed } };
            var names = new Dictionary<string, string[]>();
            string[] b = BodyNames(v.mainBody != null ? v.mainBody.bodyName : null);
            if (b != null) names["body"] = b;
            string[] own = VesselNames();
            if (own != null) names["vessel"] = own;
            QueueKind("land", SituationOf(v), nums, names, 0.2f, false);
        }

        /// <summary>From GameEventHub after a science reaction was sent: sometimes Mission Control follows up with the
        /// experiment, the body and the science points.</summary>
        public void RuntimeScience(string subjectId, float dataAmount, string bodyName)
        {
            if (!RuntimeOn || !host.settings.runtimeScience || rng.NextDouble() >= RuntimeScienceChance) return;
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            FlightValues(nums, names);
            double pts = double.NaN, left = double.NaN;
            bool first = false;
            try
            {
                if (ResearchAndDevelopment.Instance != null)
                {
                    ScienceSubject sub = ResearchAndDevelopment.GetSubjectByID(subjectId);
                    if (sub != null)
                    {
                        float mul = HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
                        pts = ResearchAndDevelopment.GetScienceValue(dataAmount, sub, 1f) * mul;
                        first = sub.science < 0.01f;
                        left = (sub.scienceCap - sub.science) * mul - pts;
                    }
                }
            }
            catch (Exception) { pts = left = double.NaN; }
            if (!double.IsNaN(pts) && pts >= 0.05) nums["sci"] = pts;
            if (!double.IsNaN(left)) nums["sci_left"] = Math.Max(0, left);
            if (!double.IsNaN(pts)) nums["sci_first"] = first ? 1 : 0;
            string[] w = RuntimeLines.ExperimentWords(RuntimeLines.ExperimentId(subjectId));
            // the experiment words are Spanish: reversed like the rest, so they go in a raw mark already reversed
            names["exp"] = new[] { LonapseText.ReverseText(w[0]), w[1] };
            string[] b = BodyNames(bodyName);
            if (b != null) { names["body"] = b; names["here"] = b; }
            // after the reaction scene: it is queued first, this waits behind it
            float delay = ResponseDelay(host.stress.stress) + 0.8f;
            if (!(nums.ContainsKey("sci") && QueueKind("science_pts", null, nums, names, delay, false)))
                QueueKind("science", null, nums, names, delay, false);
        }

        /// <summary>From GameEventHub: a contract was completed or failed. funds, sci and rep are the rewards of a
        /// completed contract, or the funds and reputation a failed one costs (positive numbers).</summary>
        public void RuntimeContract(bool completed, double funds, double sci, double rep)
        {
            if (!RuntimeOn || !host.settings.runtimeScience) return;
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            FlightValues(nums, names);
            if (funds >= 1) nums["funds"] = funds;
            if (completed && sci >= 0.05) nums["con_sci"] = sci;
            if (rep >= 0.5) nums["con_rep"] = rep;
            string kind = !completed ? "contract_failed" : nums.ContainsKey("funds") ? "contract_funds" : "contract_done";
            if (!QueueKind(kind, null, nums, names, ResponseDelay(host.stress.stress), false) && kind == "contract_funds")
                QueueKind("contract_done", null, nums, names, ResponseDelay(host.stress.stress), false);
        }

        /// <summary>The values of the active vessel for a science or contract line: the totals, the day, the
        /// vessel and the body. Nothing without a vessel. Quiet: no fuel log line.</summary>
        void FlightValues(Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            Vessel v = ActiveVessel;
            if (v == null) return;
            try { CollectValues(v, SituationOf(v) ?? "other", nums, names, false); }
            catch (Exception e) { Log.Debug("Runtime voice values: " + e.Message); }
        }

        /// <summary>
        /// Renders a runtime-only MC line and queues it once its clip is ready. Nothing plays when Piper fails, or
        /// when the radio is busy or cut by then. report: the report timer waits for it.
        /// </summary>
        void QueueRuntimeOnly(RuntimeTemplate t, Dictionary<string, double> nums, Dictionary<string, string[]> names,
                              float delay, bool report, double replyChance = ReplyChance)
        {
            string spanish, english;
            List<int> vs = RuntimeLines.FillableVersions(t, nums, names);
            int vi = PickRuntimeVersion(t, vs, NamesRecent);
            if (vi < 0 || !RuntimeLines.Fill(t, vi, nums, names, out spanish, out english)) return;
            recentRuntime.Enqueue(t.Id);
            while (recentRuntime.Count > 4) recentRuntime.Dequeue();
            var e = new PhraseEntry
            {
                ClipKey = t.Id + "_" + (++runtimeAsked), Event = "chatter", Severity = "chatter", Band = "calm", Role = "mc",
                Scene = t.Id, Seq = 1, Gender = "m", Phase = "any", Cause = "any",
                Spanish = RuntimeSpeech.Unmarked(spanish), LonapseText = RuntimeSpeech.Spoken(spanish), English = english,
            };
            e.Folder = PhraseTable.FolderFor(e.Role, e.Severity, e.Band, e.Event);
            if (report) reportPending = true;
            bool warpOnly = t.AboutWarp && host.warpChatter;
            if (warpOnly) MarkWarpOnly(e.ClipKey);
            // a crew reply, rendered at the same time: it plays right after the MC line, or not at all
            Reply rp = PlanReply(t, nums, names, replyChance);
            if (rp != null && warpOnly) MarkWarpOnly(rp.entry.ClipKey);
            bool mcDone = false, mcQueued = false;
            Action reply = () =>
            {
                if (rp == null || !mcDone || !rp.done) return;
                Reply x = rp;
                rp = null;
                if (x.clip == null) return;
                if (!mcQueued || !CanChat(x.speaker) || count + 4 > Capacity || radioSilent || !host.signal.connected || !host.settings.radioEnabled)
                {
                    UnityEngine.Object.Destroy(x.clip);
                    return;
                }
                PhraseClips.SetRuntimeClip(x.entry, x.clip);
                runtimeOnly++;
                EnqueueCrew(x.entry.Folder, ResponseDelay(host.stress.stress), 0f, x.speaker, x.entry);
                Log.Info("Runtime voice: reply " + x.entry.ClipKey + " (" + x.speaker.displayName + ") to " + t.Id);
            };
            if (rp != null)
                Voice.Request(KindFor(rp.speaker), rp.spanish, r =>
                {
                    if (rp == null) { if (r.clip != null) UnityEngine.Object.Destroy(r.clip); return; }
                    rp.clip = r.clip;
                    rp.done = true;
                    if (r.clip == null) runtimeFailed++;
                    reply();
                });
            Voice.Request("mc", spanish, r =>
            {
                if (report) reportPending = false;
                mcDone = true;
                if (r.clip == null) { runtimeFailed++; reply(); return; }
                if (count + 7 > Capacity || radioSilent || mcBlackout || !host.signal.connected || !host.settings.radioEnabled)
                {
                    UnityEngine.Object.Destroy(r.clip);
                    Log.Info("Runtime voice: " + e.ClipKey + " dropped, the radio is busy or cut");
                    reply();
                    return;
                }
                PhraseClips.SetRuntimeClip(e, r.clip);
                runtimeOnly++;
                EnqueueMc(e.Folder, delay, e);
                mcQueued = true;
                Log.Info("Runtime voice: " + e.ClipKey + " \"" + r.spoken + "\" ready in " + r.queueToReadyMs.ToString("0")
                         + " ms (Piper " + r.piperMs.ToString("0") + " ms), " + r.clip.length.ToString("0.00") + " s");
                reply();
            });
        }

        /// <summary>Share of Mission Control runtime lines the crew answers, when a reply fits.</summary>
        const double ReplyChance = 0.45;
        /// <summary>The kerbal a report talks to ({kerbal}), set while QueueReport queues it: that kerbal answers.</summary>
        KerbalVoice reportKerbal;
        readonly Queue<string> recentReplies = new Queue<string>();

        sealed class Reply
        {
            public KerbalVoice speaker;
            public PhraseEntry entry;
            public string spanish;
            public UnityEngine.AudioClip clip;
            public bool done;
        }

        /// <summary>
        /// Sometimes a crew reply to a Mission Control runtime line (kind reply): a kerbal aboard, the one the report
        /// talked to when there is one. Null when there is no crew, the stress is up, the dice say no or none fits.
        /// </summary>
        Reply PlanReply(RuntimeTemplate line, Dictionary<string, double> nums, Dictionary<string, string[]> names, double chance = ReplyChance)
        {
            if (!RuntimeOn || host.probeMode || host.roster.voices.Count == 0 || host.stress.stress > ReportMaxStress
                || Somber || (!debugForce && rng.NextDouble() >= chance)) return null;
            KerbalVoice k = reportKerbal != null && CanChat(reportKerbal) ? reportKerbal : null;
            if (k == null)
            {
                // the pilot answers a fuel or node line, the engineer the power, the scientist the science
                string job = Persona.JobForLine(line.Id);
                if (job != null && rng.NextDouble() < Persona.JobShare && host.roster.AnyWithTrait(job, CanChat))
                    k = host.roster.PickWithTrait(job, rng, CanChat);
            }
            if (k == null) k = PickRested();
            if (!CanChat(k) || RuntimeVoice.Missing(KindFor(k)) != null) return null;
            string[] kn = FirstNames(k);
            if (kn == null) return null;
            Vessel v = ActiveVessel;
            string sit = v != null ? SituationOf(v) ?? "other" : "other";
            var rn = new Dictionary<string, string[]>(names ?? new Dictionary<string, string[]>());
            rn["self"] = kn;
            rn["kerbal"] = kn;
            var nn = new Dictionary<string, double>(nums ?? new Dictionary<string, double>());
            if (!ReferenceEquals(k, reportKerbal)) CrewValues(k, v, nn);
            List<RuntimeTemplate> fit = RuntimeLines.Replies(PhraseClips.runtimeTemplates, line, sit, nn, rn);
            List<RuntimeTemplate> fresh = fit.FindAll(x => !recentReplies.Contains(x.Id));
            if (fresh.Count == 0) return null;
            RuntimeTemplate t = fresh[rng.Next(fresh.Count)];
            string sp, en;
            List<int> vs = RuntimeLines.FillableVersions(t, nn, rn);
            // an answer rarely says a name: always the versions without one when there are
            int vi = PickRuntimeVersion(t, vs, true);
            if (vi < 0 || !RuntimeLines.Fill(t, vi, nn, rn, out sp, out en)) return null;
            recentReplies.Enqueue(t.Id);
            while (recentReplies.Count > 8) recentReplies.Dequeue();
            var e = new PhraseEntry
            {
                ClipKey = t.Id + "_" + (++runtimeAsked), Event = "chatter", Severity = "chatter", Band = "calm", Role = "crew",
                Scene = line.Id, Seq = 2, Gender = "both", Phase = "any", Cause = "any",
                Spanish = RuntimeSpeech.Unmarked(sp), LonapseText = RuntimeSpeech.Spoken(sp), English = en,
            };
            e.Folder = PhraseTable.FolderFor(e.Role, e.Severity, e.Band, e.Event);
            return new Reply { speaker = k, entry = e, spanish = sp };
        }

        // ---------------- sunrise ----------------

        Guid sunVessel;
        int sunrises;
        bool sunKnown, wasLit;
        float sunCheckAt, lastSunriseLine = -1000f;
        /// <summary>Real seconds between two sunrise lines, and the share of sunrises that get one.</summary>
        const float SunriseGap = 600f;
        const double SunriseChance = 0.5;

        /// <summary>Once a second: the vessel came out of the shadow of its body. In orbit that is a sunrise: it is
        /// counted ({sunrises} for this flight), and sometimes Mission Control says so (kind sunrise). Not in warp.</summary>
        void SunriseTick()
        {
            if (clock < sunCheckAt) return;
            sunCheckAt = clock + 1f;
            Vessel v = ActiveVessel;
            if (v == null) return;
            if (v.id != sunVessel) { sunVessel = v.id; sunrises = 0; sunKnown = false; }
            bool lit;
            try
            {
                CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null, b = v.mainBody;
                if (sun == null || b == null || ReferenceEquals(sun, b)) { sunKnown = false; return; }
                Vector3d p = v.GetWorldPos3D() - b.position, s = sun.position - b.position;
                lit = !DcMath.InShadow(p.x, p.y, p.z, s.x, s.y, s.z, b.Radius);
            }
            catch (Exception) { return; }
            bool rose = sunKnown && !wasLit && lit;
            sunKnown = true;
            wasLit = lit;
            if (!rose || v.situation != Vessel.Situations.ORBITING) return;
            sunrises++;
            if (!debugForce && (host.warpChatter || clock - lastSunriseLine < SunriseGap || rng.NextDouble() >= SunriseChance)) return;
            QueueSunrise(v);
        }

        bool QueueSunrise(Vessel v)
        {
            if (count > 0 || reportPending || host.stress.stress > ReportMaxStress || !host.signal.connected || radioSilent
                || mcBlackout || !host.settings.radioEnabled) return false;
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            CollectValues(v, "orbit", nums, names);
            if (!QueueKind("sunrise", "orbit", nums, names, 0.3f, true)) return false;
            lastSunriseLine = clock;
            return true;
        }

        // ---------------- replies to recorded MC lines ----------------

        /// <summary>Share of calm chatter scenes that end with Mission Control and get a runtime crew reply.</summary>
        const double RecordedReplyChance = 0.2;
        /// <summary>A reply that is not ready this many real seconds after its scene was queued is dropped.</summary>
        const float RecordedReplyMaxWait = 8f;

        /// <summary>
        /// After a calm chatter scene that ends with a Mission Control statement (not a question): sometimes a kerbal
        /// answers with a general reply (replies.tsv without to=). The scene's crew speaker answers when there is one.
        /// </summary>
        void RecordedReply(PhraseScene sc, PhraseEntry last, KerbalVoice sp)
        {
            if (!RuntimeOn || sc == null || last == null || last.Role != "mc" || sc.IsProbe || sc.Event != "chatter" || sc.Band != "calm") return;
            string es = (last.Spanish ?? "").Trim();
            if (es.Length == 0 || es.EndsWith("?", StringComparison.Ordinal)) return;
            if (!debugForce && rng.NextDouble() >= RecordedReplyChance) return;   // before the values: they walk the parts
            Vessel v = ActiveVessel;
            if (v == null) return;
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            try { CollectValues(v, SituationOf(v) ?? "other", nums, names, false); }
            catch (Exception) { return; }
            reportKerbal = sp;
            Reply rp;
            try { rp = PlanReply(new RuntimeTemplate(sc.Id, RuntimeLines.RecordedKind, "", ""), nums, names, 1.0); }
            finally { reportKerbal = null; }
            if (rp == null) return;
            float asked = clock;
            Voice.Request(KindFor(rp.speaker), rp.spanish, r =>
            {
                if (r.clip == null) { runtimeFailed++; return; }
                if (clock - asked > RecordedReplyMaxWait || !CanChat(rp.speaker) || count + 4 > Capacity || radioSilent
                    || !host.signal.connected || !host.settings.radioEnabled)
                {
                    UnityEngine.Object.Destroy(r.clip);
                    return;
                }
                PhraseClips.SetRuntimeClip(rp.entry, r.clip);
                runtimeOnly++;
                EnqueueCrew(rp.entry.Folder, ResponseDelay(host.stress.stress), 0f, rp.speaker, rp.entry);
                Log.Info("Runtime voice: reply " + rp.entry.ClipKey + " (" + rp.speaker.displayName + ") to the recorded scene " + sc.Id);
            });
        }

        // ---------------- Debug tab ----------------

        /// <summary>Debug tab: the next chance checks pass (chat, reply, sunrise), so a test plays at once.</summary>
        bool debugForce;

        string DebugCheck()
        {
            string why = RuntimeVoice.Missing("mc");
            if (why != null) return "runtime voice: " + why;
            if (ActiveVessel == null) return "no vessel";
            recentRuntime.Clear();
            recentReplies.Clear();
            return null;
        }

        /// <summary>Debug tab: a runtime crew chat now, between two kerbals aboard.</summary>
        public string DebugRuntimeChat()
        {
            string why = DebugCheck();
            if (why != null) return why;
            if (host.roster.voices.Count < 2) return "a chat needs two kerbals aboard";
            debugForce = true;
            try
            {
                KerbalVoice a = host.roster.voices[rng.Next(host.roster.voices.Count)], b;
                do b = host.roster.voices[rng.Next(host.roster.voices.Count)]; while (ReferenceEquals(a, b));
                return TryRuntimeChat(a, b) ? "chat requested (" + a.displayName + ", " + b.displayName + ")" : "no chat fits here";
            }
            finally { debugForce = false; }
        }

        /// <summary>Debug tab: a report with a crew reply.</summary>
        public string DebugRuntimeReply()
        {
            string why = DebugCheck();
            if (why != null) return why;
            if (host.roster.voices.Count == 0 || host.probeMode) return "a reply needs crew aboard";
            debugForce = true;
            try { return QueueReport() ? "report and reply requested" : "no report fits here (on the pad, or no vessel)"; }
            finally { debugForce = false; }
        }

        /// <summary>Debug tab: a warp line, as if the warp was on.</summary>
        public string DebugWarpLine()
        {
            string why = DebugCheck();
            if (why != null) return why;
            Vessel v = ActiveVessel;
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            string sit = SituationOf(v) ?? "other";
            CollectValues(v, sit, nums, names);
            nums["in_warp"] = 1;
            return QueueKind("warp", sit, nums, names, 0.3f, true) ? "warp line requested" : "no warp line fits here";
        }

        /// <summary>Debug tab: a sunrise line now (in orbit).</summary>
        public string DebugSunrise()
        {
            string why = DebugCheck();
            if (why != null) return why;
            Vessel v = ActiveVessel;
            if (v.situation != Vessel.Situations.ORBITING) return "only in orbit";
            if (v.id != sunVessel) { sunVessel = v.id; sunrises = 0; sunKnown = false; }
            sunrises++;
            return QueueSunrise(v) ? "sunrise line requested (sunrise " + sunrises + ")" : "the radio is busy, try again";
        }

        /// <summary>Debug tab: a report right now (any template that fits), to hear the numbers.</summary>
        public string DebugRuntimeReport()
        {
            string why = RuntimeVoice.Missing("mc");
            if (why != null) return "runtime voice: " + why;
            recentRuntime.Clear();
            Log.Info("Runtime voice: report from the Debug tab");
            if (!QueueReport()) return "no report fits here (on the pad, or no vessel)";
            return "report requested";
        }
    }
}
