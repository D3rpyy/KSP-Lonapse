// Experimental (runtime voice): lines spoken at runtime by Piper. With the setting "Runtime voice" on:
//   names    some Mission Control lines end with the name of a kerbal aboard or the vessel, some intercom lines with
//            the partner's name
//   bodies   a {body} line on a modded planet says that planet's name instead of "the planet"
//   new rows rows without a clip (GameData/Lonapse/Phrases/Extra/*.tsv) are spoken at runtime
//   reports  now and then Mission Control reads out real values (runtime.tsv): orbit, speed, fuel, power, the next
//            burn, funds and more. Also once per descent (height, speed) and after a docking approach (distance)
//   science  after a science reaction, the experiment, the body and the science points
//   contract a completed contract with its funds, a failed one
// A line that has a clip falls back to it when Piper is not done in time. A line without a clip waits longer and is
// skipped when it still has no sound. Reports, science and contract lines are queued only once their clip is ready.
// Also the Debug tab test: Mission Control greets a kerbal aboard by name.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        static readonly string[][] RuntimeGreetings = {
            new[] { "{n}, aquí Control. ¿Qué tal va todo por ahí arriba?", "{N}, Control here. How is everything up there?" },
            new[] { "Hola, {n}. Te oímos perfectamente.", "Hello, {N}. We hear you perfectly." },
            new[] { "{n}, buen trabajo hoy. Sigue así.", "{N}, good work today. Keep it up." },
            new[] { "{n}, ¿nos copias? Cambio.", "{N}, do you copy? Over." },
        };
        int runtimeTests, runtimeAsked, runtimeUsed, runtimeLate, runtimeFailed, runtimeOnly;
        KerbalVoice lastCrewVoice;
        float lastCrewVoiceClock = -999f;
        float nextReport = -1f;
        bool reportPending;
        readonly Queue<string> recentRuntime = new Queue<string>();
        readonly HashSet<string> warmed = new HashSet<string>();

        /// <summary>Status line of the last runtime voice test, for the Debug tab.</summary>
        public string runtimeStatus;

        /// <summary>Share of Mission Control lines that get a name, and of intercom lines that get the partner's.</summary>
        const double RuntimeNameChance = 0.35, RuntimeIntercomNameChance = 0.3;
        /// <summary>With crew aboard, the share of MC names that is the vessel instead of a kerbal.</summary>
        const double RuntimeVesselShare = 0.25;
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
        string[] VesselNames()
        {
            Vessel v = ActiveVessel;
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
        RuntimeLine PlanLine(PhraseEntry e, KerbalVoice speaker, string[] name)
        {
            if (e == null || e.SubtitleOnly || string.IsNullOrEmpty(e.Spanish) || !RuntimeOn) return null;
            if (e.ClipKey != null && e.ClipKey.StartsWith("rt_", StringComparison.Ordinal)) return null; // already runtime
            bool mc = e.Role == "mc";
            if (!mc && speaker == null) return null;
            string kind = mc ? "mc" : KindFor(speaker);
            if (RuntimeVoice.Missing(kind) != null) return null;
            bool required = PhraseClips.Clip(e, !mc && speaker.isFemale) == null;
            string spanish = e.Spanish, english = e.English ?? "";
            bool changed = false;
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

        /// <summary>An MC line: sometimes with the name of the kerbal who spoke last (else one aboard) or the vessel.
        /// Without crew (a probe), the vessel.</summary>
        RuntimeLine PlanMc(PhraseEntry e)
        {
            if (e == null || !RuntimeOn) return null;
            string[] name = null;
            if (rng.NextDouble() < RuntimeNameChance)
            {
                bool crew = host.roster.voices.Count > 0;
                if (!crew || rng.NextDouble() < RuntimeVesselShare) name = VesselNames();
                if (name == null && crew)
                {
                    KerbalVoice k = lastCrewVoice != null && clock - lastCrewVoiceClock < RuntimeAnswerWindow
                                    && host.roster.voices.Contains(lastCrewVoice) ? lastCrewVoice
                                    : host.roster.voices[rng.Next(host.roster.voices.Count)];
                    string first = k != null ? RuntimeLines.CleanName(SubtitleSlots.FirstName(k.displayName)) : null;
                    if (!string.IsNullOrEmpty(first)) name = new[] { RuntimeSpeech.Respell(first), first };
                }
            }
            return PlanLine(e, null, name);
        }

        /// <summary>An intercom line: sometimes with the partner's first name at the end.</summary>
        RuntimeLine PlanIntercom(PhraseEntry e, KerbalVoice speaker, KerbalVoice partner)
        {
            if (e == null || !RuntimeOn) return null;
            string[] name = null;
            if (partner != null && rng.NextDouble() < RuntimeIntercomNameChance)
            {
                string first = RuntimeLines.CleanName(SubtitleSlots.FirstName(partner.displayName));
                if (!string.IsNullOrEmpty(first)) name = new[] { RuntimeSpeech.Respell(first), first };
            }
            return PlanLine(e, speaker, name);
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
            if (speaker != null && rt.kind != KindFor(speaker) && !rt.required) { u.rt = null; return TurnGo; }
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
        void CollectValues(Vessel v, string sit, Dictionary<string, double> nums, Dictionary<string, string[]> names)
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
            Put(nums, "fuel", ResourcePercent(v, "LiquidFuel"));
            Put(nums, "ec", ResourcePercent(v, "ElectricCharge"));
            Put(nums, "mono", ResourcePercent(v, "MonoPropellant"));
            try { nums["parts"] = v.parts.Count; nums["crew"] = v.GetCrewCount(); } catch (Exception) { }
            try
            {
                if (v.patchedConicSolver != null && v.patchedConicSolver.maneuverNodes.Count > 0)
                {
                    ManeuverNode n = v.patchedConicSolver.maneuverNodes[0];
                    double dt = n.UT - Planetarium.GetUniversalTime();
                    if (dt > 0) { nums["node_min"] = dt / 60.0; nums["node_dv"] = n.DeltaV.magnitude; }
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
            if (SlotFlightValues().TryGetValue("day", out day) && double.TryParse(day, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out dd)) nums["day"] = dd;
            string[] b = BodyNames(v.mainBody != null ? v.mainBody.bodyName : null);
            if (b != null) names["body"] = b;
            string[] vn = VesselNames();
            if (vn != null) names["vessel"] = vn;
        }

        static void Put(Dictionary<string, double> d, string k, double v) { if (!double.IsNaN(v)) d[k] = v; }

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
        bool QueueKind(string kind, string sit, Dictionary<string, double> nums, Dictionary<string, string[]> names, float delay, bool report)
        {
            List<RuntimeTemplate> fit = RuntimeLines.Fitting(PhraseClips.runtimeTemplates, kind, sit, nums, names);
            List<RuntimeTemplate> fresh = fit.FindAll(t => !recentRuntime.Contains(t.Id));
            if (fresh.Count > 0) fit = fresh;
            else if (kind == "report") return false;   // a report can wait, an answer to an event may repeat
            if (fit.Count == 0) return false;
            QueueRuntimeOnly(fit[rng.Next(fit.Count)], nums, names, delay, report);
            return true;
        }

        /// <summary>A report that fits the vessel now, not one of the last few. False when none fits.</summary>
        bool QueueReport()
        {
            Vessel v = ActiveVessel;
            if (v == null) return false;
            string sit = SituationOf(v);
            if (sit == null) return false;   // on the pad there is nothing to report yet
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            CollectValues(v, sit, nums, names);
            return QueueKind("report", sit, nums, names, 0.3f, true);
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
                QueueKind("dock", SituationOf(v), nums, names, ResponseDelay(host.stress.stress) + 0.5f, false);
            }
            catch (Exception e) { Log.Debug("Runtime voice dock: " + e.Message); }
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
            QueueKind("land", SituationOf(v), nums, names, 0.2f, false);
        }

        /// <summary>From GameEventHub after a science reaction was sent: sometimes Mission Control follows up with the
        /// experiment, the body and the science points.</summary>
        public void RuntimeScience(string subjectId, float dataAmount, string bodyName)
        {
            if (!RuntimeOn || !host.settings.runtimeScience || rng.NextDouble() >= RuntimeScienceChance) return;
            double pts = double.NaN;
            try
            {
                if (ResearchAndDevelopment.Instance != null)
                {
                    ScienceSubject sub = ResearchAndDevelopment.GetSubjectByID(subjectId);
                    if (sub != null)
                        pts = ResearchAndDevelopment.GetScienceValue(dataAmount, sub, 1f)
                              * HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
                }
            }
            catch (Exception) { pts = double.NaN; }
            var nums = new Dictionary<string, double>();
            if (!double.IsNaN(pts) && pts >= 0.05) nums["sci"] = pts;
            string[] w = RuntimeLines.ExperimentWords(RuntimeLines.ExperimentId(subjectId));
            // the experiment words are Spanish: reversed like the rest, so they go in a raw mark already reversed
            var names = new Dictionary<string, string[]> { { "exp", new[] { LonapseText.ReverseText(w[0]), w[1] } } };
            string[] b = BodyNames(bodyName);
            if (b != null) names["body"] = b;
            // after the reaction scene: it is queued first, this waits behind it
            float delay = ResponseDelay(host.stress.stress) + 0.8f;
            if (!(nums.ContainsKey("sci") && QueueKind("science_pts", null, nums, names, delay, false)))
                QueueKind("science", null, nums, names, delay, false);
        }

        /// <summary>From GameEventHub: a contract was completed (with its funds) or failed.</summary>
        public void RuntimeContract(bool completed, double funds)
        {
            if (!RuntimeOn || !host.settings.runtimeScience) return;
            var nums = new Dictionary<string, double>();
            if (completed && funds >= 1) nums["funds"] = funds;
            string kind = !completed ? "contract_failed" : nums.Count > 0 ? "contract_funds" : "contract_done";
            if (!QueueKind(kind, null, nums, null, ResponseDelay(host.stress.stress), false) && kind == "contract_funds")
                QueueKind("contract_done", null, nums, null, ResponseDelay(host.stress.stress), false);
        }

        /// <summary>
        /// Renders a runtime-only MC line and queues it once its clip is ready. Nothing plays when Piper fails, or
        /// when the radio is busy or cut by then. report: the report timer waits for it.
        /// </summary>
        void QueueRuntimeOnly(RuntimeTemplate t, Dictionary<string, double> nums, Dictionary<string, string[]> names,
                              float delay, bool report)
        {
            string spanish, english;
            if (!RuntimeLines.Fill(t, nums, names, out spanish, out english)) return;
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
            Voice.Request("mc", spanish, r =>
            {
                if (report) reportPending = false;
                if (r.clip == null) { runtimeFailed++; return; }
                if (count + 7 > Capacity || radioSilent || mcBlackout || !host.signal.connected || !host.settings.radioEnabled)
                {
                    UnityEngine.Object.Destroy(r.clip);
                    Log.Info("Runtime voice: " + e.ClipKey + " dropped, the radio is busy or cut");
                    return;
                }
                PhraseClips.SetRuntimeClip(e, r.clip);
                runtimeOnly++;
                EnqueueMc(e.Folder, delay, e);
                Log.Info("Runtime voice: " + e.ClipKey + " \"" + r.spoken + "\" ready in " + r.queueToReadyMs.ToString("0")
                         + " ms (Piper " + r.piperMs.ToString("0") + " ms), " + r.clip.length.ToString("0.00") + " s");
            });
        }

        // ---------------- Debug tab ----------------

        /// <summary>Debug tab: MC greets a kerbal aboard by name with a runtime clip. Returns a status line.</summary>
        public string DebugRuntimeGreeting()
        {
            string why = RuntimeVoice.Missing("mc");
            if (why != null) return runtimeStatus = "runtime voice: " + why;
            string full = null;
            if (host.roster.voices.Count > 0)
            {
                KerbalVoice k = host.roster.voices[rng.Next(host.roster.voices.Count)];
                full = k != null ? k.displayName : null;
            }
            if (string.IsNullOrEmpty(full)) full = "Jebediah Kerman";
            string name = RuntimeLines.CleanName(SubtitleSlots.FirstName(full));
            string[] g = RuntimeGreetings[runtimeTests++ % RuntimeGreetings.Length];
            string spanish = g[0].Replace("{n}", RuntimeSpeech.MarkName(name));
            var e = new PhraseEntry
            {
                ClipKey = "rt_greet_" + runtimeTests, Event = "chatter", Severity = "chatter", Band = "calm", Role = "mc",
                Scene = "rt_greet", Seq = 1, Gender = "m", Phase = "any", Cause = "any",
                Spanish = g[0].Replace("{n}", name), English = g[1].Replace("{N}", name),
            };
            e.Folder = PhraseTable.FolderFor(e.Role, e.Severity, e.Band, e.Event);
            e.LonapseText = RuntimeSpeech.Spoken(spanish);
            runtimeStatus = "runtime voice: rendering \"" + e.LonapseText + "\"";
            Voice.Request("mc", spanish, r =>
            {
                if (r.clip == null) { runtimeStatus = "runtime voice failed: " + r.error; return; }
                PhraseClips.SetRuntimeClip(e, r.clip);
                EnqueueMc(e.Folder, 0.2f, e);
                runtimeStatus = string.Format("runtime voice: \"{0}\" ready in {1:0} ms (Piper {2:0} ms), {3:0.00} s of speech",
                                              r.spoken, r.queueToReadyMs, r.piperMs, r.clip.length);
                Log.Info("Runtime voice: " + e.ClipKey + " \"" + r.spoken + "\" ready in " + r.queueToReadyMs.ToString("0")
                         + " ms (Piper " + r.piperMs.ToString("0") + " ms), " + r.clip.length.ToString("0.00") + " s");
            });
            return runtimeStatus;
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
