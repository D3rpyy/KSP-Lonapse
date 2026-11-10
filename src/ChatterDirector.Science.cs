// Runtime voice: science talk. After an experiment the kerbal who did it reports and Mission Control answers, from
// Phrases/Crew/science_talk.tsv (trigger science_talk). The lines hold real values: the experiment, the biome, the
// science points, what the thermometer, barometer or gravity scanner reads, the science of this flight. The crew
// science scenes have no recording, so without Piper an experiment gets no talk.
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        /// <summary>Share of science talks about the experiment itself, the rest are about any experiment.</summary>
        const double TalkOwnChance = 0.8;
        /// <summary>Share of science talks that pick a talk with a when (sci_first=1, landed, at_home=0 ...) when one
        /// fits. Those only fit now and then, so they get a fair turn when they do.</summary>
        const double TalkSpecialChance = 0.5;
        /// <summary>Share of science talks followed by the science points line, when no line of the talk said {sci}.</summary>
        const double TalkPointsChance = 0.35;

        /// <summary>The science of this flight and the runs per experiment ({sci_flight}, {exp_runs}). Kept per vessel:
        /// a kerbal on EVA counts for the ship it came from.</summary>
        Guid sciFlightVessel;
        double sciFlight;
        readonly Dictionary<string, int> expRuns = new Dictionary<string, int>();

        /// <summary>From GameEventHub, for every experiment (also one in a cooldown): adds it to the science of this
        /// flight and the runs of its experiment.</summary>
        public void NoteExperiment(string subjectId, float dataAmount)
        {
            Vessel v = ActiveVessel;
            if (v == null) return;
            if (!v.isEVA && v.id != sciFlightVessel)
            {
                sciFlightVessel = v.id;
                sciFlight = 0;
                expRuns.Clear();
            }
            string topic = RuntimeLines.ExperimentId(subjectId).ToLowerInvariant();
            int n;
            expRuns.TryGetValue(topic, out n);
            expRuns[topic] = n + 1;
            double pts, left;
            bool first;
            if (SciencePoints(subjectId, dataAmount, out pts, out left, out first)) sciFlight += pts;
            SnapshotProbeScience(subjectId, dataAmount);
        }

        /// <summary>The science points of the data, what is left of the subject after it, and whether it is the first
        /// data of the subject. False without career science (sandbox) or an unknown subject.</summary>
        bool SciencePoints(string subjectId, float dataAmount, out double pts, out double left, out bool first)
        {
            pts = left = double.NaN;
            first = false;
            try
            {
                if (ResearchAndDevelopment.Instance == null) return false;
                ScienceSubject sub = ResearchAndDevelopment.GetSubjectByID(subjectId);
                // the Debug tab test: a subject made for the vessel's situation, maybe not known to R&D yet
                if (sub == null && debugSubject != null && debugSubject.id == subjectId) sub = debugSubject;
                if (sub == null) return false;
                float mul = HighLogic.CurrentGame.Parameters.Career.ScienceGainMultiplier;
                pts = ResearchAndDevelopment.GetScienceValue(dataAmount, sub, 1f) * mul;
                first = sub.science < 0.01f;
                left = (sub.scienceCap - sub.science) * mul - pts;
                return true;
            }
            catch (Exception) { pts = left = double.NaN; return false; }
        }

        /// <summary>The values of one experiment: {sci} {sci_left} {sci_first}, {exp}, {body} and {here}, and what the
        /// instruments read around the active vessel: {temp_c} {press_kpa} {grav_g}. Also {sci_flight} and
        /// {exp_runs} (NoteExperiment).</summary>
        void ScienceValues(string subjectId, float dataAmount, string bodyName, Dictionary<string, double> nums,
                           Dictionary<string, string[]> names)
        {
            double pts, left;
            bool first;
            if (SciencePoints(subjectId, dataAmount, out pts, out left, out first))
            {
                if (pts >= 0.05) nums["sci"] = pts;
                nums["sci_left"] = Math.Max(0, left);
                nums["sci_first"] = first ? 1 : 0;
            }
            // the experiment words are Spanish: reversed like the rest, so they go in a raw mark already reversed
            names["exp"] = ExpNames(subjectId);
            string[] b = BodyNames(bodyName);
            if (b != null) { names["body"] = b; names["here"] = b; }
            if (sciFlight >= 0.05) nums["sci_flight"] = sciFlight;
            if (!double.IsNaN(sciSentNow)) nums["data_sent"] = sciSentNow;
            int runs;
            string topic = RuntimeLines.ExperimentId(subjectId).ToLowerInvariant();
            if (expRuns.TryGetValue(topic, out runs)) nums["exp_runs"] = runs;
            Vessel v = ActiveVessel;
            if (v == null) return;
            // the air around the vessel in °C (0 K and below: no value), the static pressure (0 in a vacuum) and the
            // gravity at this height in g
            try { if (v.externalTemperature > 1) nums["temp_c"] = v.externalTemperature - 273.15; } catch (Exception) { }
            try { nums["press_kpa"] = Math.Max(0, v.staticPressurekPa); } catch (Exception) { }
            try
            {
                CelestialBody mb = v.mainBody;
                double r = mb != null ? (v.CoMD - mb.position).magnitude : 0;
                if (r > 1) nums["grav_g"] = mb.gravParameter / (r * r) / 9.80665;
            }
            catch (Exception) { }
        }

        /// <summary>
        /// A science talk about the experiment: the kerbal k reports and Mission Control answers, every line in its
        /// own voice, queued once all lines are ready. Line versions with a value only play when that value is there.
        /// False when none fits or the voices are missing.
        /// </summary>
        bool ScienceTalk(string topic, string subjectId, float dataAmount, string bodyName, KerbalVoice k,
                         SciBatch batch = null)
        {
            if (runtimeChatPending || k == null || !CanChat(k)) return false;
            if (sciPhase == SciPhase.Rendering || sciPhase == SciPhase.Waiting) return false;   // one talk at a time
            if (RuntimeVoice.Missing(KindFor(k)) != null || RuntimeVoice.Missing("mc") != null) return false;
            Vessel v = ActiveVessel;
            if (v == null) return false;
            string sit = SituationOf(v) ?? "other";
            var nums = new Dictionary<string, double>();
            var common = new Dictionary<string, string[]>();
            CollectValues(v, sit, nums, common);
            ScienceValues(subjectId, dataAmount, bodyName, nums, common);
            if (batch != null) BatchValues(batch, nums, common);
            CrewValues(k, v, nums);
            string[] kn = FirstNames(k);
            if (kn == null) return false;
            var namesCrew = new Dictionary<string, string[]>(common) { { "self", kn } };
            var namesMc = new Dictionary<string, string[]>(common) { { "kerbal", kn } };
            string job = k.persona != null ? k.persona.ChatKeys : null;
            string gen = RuntimeLines.GeneralOf(PhraseClips.runtimeTemplates, topic);

            var fit = new List<List<RuntimeTemplate>>();
            foreach (List<RuntimeTemplate> t in RuntimeLines.Talks(PhraseClips.runtimeTemplates))
                if (RuntimeLines.ForBatch(t) == (batch != null) && RuntimeLines.OnFits(t[0], topic, gen)
                    && RuntimeLines.TalkFits(t, sit, nums, namesCrew, namesMc, job)) fit.Add(t);
            // mostly a talk about this experiment, sometimes one about any experiment
            var own = fit.FindAll(t => t[0].On.Contains(topic ?? ""));
            var rest = fit.FindAll(t => !t[0].On.Contains(topic ?? ""));
            if (own.Count > 0 && (rest.Count == 0 || rng.NextDouble() < TalkOwnChance)) fit = own;
            else if (rest.Count > 0) fit = rest;
            // a talk that only fits now (first data, landed, far from home ...) gets a fair turn. For a batch: a check
            // besides the experiment count.
            var special = fit.FindAll(t => RuntimeLines.SpecialChecks(t[0]) > 0 || t[0].Situations.Count > 0);
            if (special.Count > 0 && (debugTalkSpecial || rng.NextDouble() < TalkSpecialChance)) fit = special;
            List<RuntimeTemplate> talk = FreshestTalk(fit);
            if (talk == null) return false;
            lastTalkId = talk[0].Id;
            lastTalkSpecial = special.Contains(talk);

            var entries = new List<PhraseEntry>();
            var spanish = new List<string>();
            var crewLine = new List<bool>();
            bool saidSci = false;
            RuntimeLines.ActorConditions(nums, job, null);
            for (int i = 0; i < talk.Count; i++)
            {
                bool crew = RuntimeLines.TalkCrewLine(talk, i);
                Dictionary<string, string[]> who = crew ? namesCrew : namesMc;
                int vi = PickTalkVersion(talk[i], RuntimeLines.FillableVersions(talk[i], nums, who));
                string sp, en;
                if (vi < 0 || !RuntimeLines.Fill(talk[i], vi, nums, who, out sp, out en)) return false;
                if (talk[i].Versions[vi][1].IndexOf("{sci}", StringComparison.Ordinal) >= 0) saidSci = true;
                var e = new PhraseEntry
                {
                    ClipKey = talk[i].Id + "_" + (++runtimeAsked), Event = "chatter", Severity = talk[i].Dialogue != null ? talk[i].Dialogue.Get("severity") : "chatter", Band = talk[i].Dialogue != null ? talk[i].Dialogue.Get("band") : "calm",
                    Role = crew ? "crew" : "mc", Scene = talk[0].Id, Seq = i + 1, Gender = crew ? "both" : "m", Phase = "any", Cause = "any",
                    Spanish = RuntimeSpeech.Unmarked(sp), LonapseText = RuntimeSpeech.Spoken(sp), English = en,
                };
                e.Folder = PhraseTable.FolderFor(e.Role, e.Severity, e.Band, e.Event);
                entries.Add(e);
                spanish.Add(sp);
                crewLine.Add(crew);
            }
            Mem.Stamp(talk[0].Id);

            bool kept = SciKept;
            runtimeChatPending = true;
            sciPhase = SciPhase.Rendering;
            sciPhaseClock = clock;
            int left = entries.Count;
            bool failed = false;
            var clips = new UnityEngine.AudioClip[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                int idx = i;
                Voice.Request(RuntimeDelivery.KindFor(crewLine[i] ? KindFor(k) : "mc", entries[i].Band, entries[i].Severity, entries[i].Event), spanish[i], r =>
                {
                    if (r.clip == null) failed = true;
                    clips[idx] = r.clip;
                    if (--left > 0) return;
                    runtimeChatPending = false;
                    if (failed)
                    {
                        runtimeFailed++;
                        foreach (var c in clips) if (c != null) UnityEngine.Object.Destroy(c);
                        sciPhase = SciPhase.None;
                        Log.Info("Runtime voice: science talk " + talk[0].Id + " dropped (Piper failed)");
                        return;
                    }
                    Log.Info("Runtime voice: science talk " + talk[0].Id + " (" + k.displayName + ", " + topic
                             + (batch != null ? ", " + batch.subjects.Count + " experiments" : "") + ") ready in "
                             + r.queueToReadyMs.ToString("0") + " ms");
                    // no points line after a batch: the talk says the total
                    // and none for kept data: Mission Control has not received it
                    bool points = batch == null && !kept && !saidSci && rng.NextDouble() < TalkPointsChance;
                    sciReady = new SciReady
                    {
                        id = talk[0].Id, k = k, entries = entries, clips = clips, crewLine = crewLine,
                        points = points, subjectId = subjectId, amount = dataAmount, body = bodyName,
                    };
                    sciPhase = SciPhase.Waiting;
                    sciPhaseClock = clock;
                    TrySciReady();
                });
            }
            return true;
        }

        // ---------------- batches, the hold and the wait for room ----------------

        /// <summary>Without a results dialog a batch closes this long after its last experiment, or this long after
        /// its first. An action group runs its experiments within a fraction of a second.</summary>
        const float BatchQuiet = 2f, BatchMax = 12f;
        /// <summary>With a results dialog a batch closes this long after the dialog closed: the player has decided
        /// (Keep, Transmit, Reset). An experiment run while the dialog is open joins it as a page.</summary>
        const float DialogClosedQuiet = 1f;
        /// <summary>Finished clips wait this long for room in the queue, then they are dropped.</summary>
        const float ReadyMaxWait = 20f;
        /// <summary>The hold ends after this long, also when the talk is still in the queue.</summary>
        const float PlayMaxHold = 60f;
        /// <summary>A batch waits for the results dialog at most this long after its last experiment.</summary>
        const float DialogMaxWait = 60f;
        /// <summary>At most this many experiments named in {exp_list}, the rest is "and more".</summary>
        const int BatchListMax = 4;

        /// <summary>Experiments run close together, for one talk.</summary>
        public class SciBatch
        {
            public readonly List<string> subjects = new List<string>();
            public readonly List<float> amounts = new List<float>();
            /// <summary>Subjects whose data went out by antenna (GameEvents.OnTriggeredDataTransmission).</summary>
            public readonly HashSet<string> sent = new HashSet<string>();
            public string body;
            public float first, last;
            /// <summary>A results dialog was open for this batch, and when it closed (-1: open or never seen).</summary>
            public bool dialogSeen;
            public float dialogClosed = -1f;
            /// <summary>{data_sent}: 1 all data went out by antenna, 0 all of it was kept aboard, NaN mixed or not
            /// known (DropDiscarded).</summary>
            public double dataSent = double.NaN;
        }

        /// <summary>A talk whose clips are made, waiting for room in the queue.</summary>
        class SciReady
        {
            public string id, subjectId, body;
            public KerbalVoice k;
            public List<PhraseEntry> entries;
            public UnityEngine.AudioClip[] clips;
            public List<bool> crewLine;
            public bool points;
            public float amount;
        }

        enum SciPhase { None, Rendering, Waiting, Playing }

        SciBatch sciBatch;
        SciReady sciReady;
        /// <summary>{data_sent} of the batch being closed, NaN outside CloseBatch (ScienceValues reads it).</summary>
        double sciSentNow = double.NaN;
        /// <summary>The data of the batch being closed was kept aboard: Mission Control has not received it.</summary>
        bool SciKept { get { return sciSentNow == 0; } }
        SciPhase sciPhase;
        float sciPhaseClock;

        /// <summary>No chatter, report or chat starts while experiments are collected, the talk is made, waits or
        /// plays. Events wait in their list (like the docking hold).</summary>
        bool ScienceHoldNow { get { return sciBatch != null || sciPhase != SciPhase.None; } }

        /// <summary>From GameEventHub, for an experiment: true when it goes into a batch for a science talk (with a
        /// link) or a chat among the kerbals (without one). Needs the runtime voice and a kerbal who can report.
        /// False: the event goes on as before.</summary>
        public bool ScienceBatchTakes(string subjectId, float dataAmount, string bodyName)
        {
            if (!RuntimeOn || !host.settings.runtimeScience || radioSilent || !host.settings.radioEnabled || Somber) return false;
            // without a link too: then the kerbals talk about it among themselves
            bool reach = !McUnreachable && host.signal.connected;
            KerbalVoice k = ScienceKerbal();
            if (!host.probeMode && (k == null || RuntimeVoice.Missing(KindFor(k)) != null || (reach && RuntimeVoice.Missing("mc") != null))) return false;
            // a batch that is waiting for the talk before it keeps growing, so it can be long
            if (sciBatch == null) sciBatch = new SciBatch { first = clock, body = bodyName };
            sciBatch.subjects.Add(subjectId);
            sciBatch.amounts.Add(dataAmount);
            sciBatch.last = clock;
            if (bodyName != null) sciBatch.body = bodyName;
            Log.Info("Runtime voice: experiment " + subjectId + " in the science batch (" + sciBatch.subjects.Count + ")");
            return true;
        }

        /// <summary>From Update, before the hold: closes a batch, queues finished clips when there is room, ends the
        /// hold when the talk played.</summary>
        void ScienceTick()
        {
            if (sciPhase == SciPhase.Waiting) TrySciReady();
            if (sciPhase == SciPhase.Playing && (clock - sciPhaseClock > PlayMaxHold || (count == 0 && !RadioAudible)))
                sciPhase = SciPhase.None;
            if (sciPhase == SciPhase.Rendering && clock - sciPhaseClock > PlayMaxHold && !runtimeChatPending)
                sciPhase = SciPhase.None;   // a lost render (never seen, but the hold must end)
            SciBatch b = sciBatch;
            if (b == null) return;
            bool dialog = ResultsDialogOpen;
            if (dialog) { b.dialogSeen = true; b.dialogClosed = -1f; }
            else if (b.dialogSeen && b.dialogClosed < 0f) b.dialogClosed = clock;
            if (sciPhase != SciPhase.None || runtimeChatPending) return;
            // the results dialog is open: the player may still reset the data
            if (dialog && clock - b.last < DialogMaxWait) return;
            // closed a moment ago: the player decided. Without a dialog: a short quiet after the last experiment.
            // A batch that waited for the talk before it closes as soon as that one played.
            bool quiet = b.dialogSeen ? clock - b.dialogClosed >= DialogClosedQuiet
                                      : clock - b.last >= BatchQuiet || clock - b.first >= BatchMax;
            if (!quiet && !dialog) return;
            sciBatch = null;
            if (!DropDiscarded(b)) return;
            sciSentNow = b.dataSent;
            try { CloseBatch(b); }
            catch (Exception e) { Log.Exception("Runtime voice science batch", e); }
            finally { sciSentNow = double.NaN; }
        }

        /// <summary>From GameEventHub: data of subjectId goes out by antenna.</summary>
        public void NoteTransmitted(string subjectId)
        {
            if (sciBatch != null && subjectId != null) sciBatch.sent.Add(subjectId);
            ProbeScienceReceived(subjectId);
        }

        static bool ResultsDialogOpen
        {
            get
            {
                try { return KSP.UI.Screens.Flight.Dialogs.ExperimentsResultDialog.Instance != null; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Removes the experiments the player reset or discarded: their data is not aboard the active vessel
        /// any more and did not go out by antenna (only possible with a comms device aboard). Sets dataSent: kept
        /// aboard (Keep, or no antenna to send it) or sent. False when none is left.</summary>
        bool DropDiscarded(SciBatch b)
        {
            var aboard = new HashSet<string>();
            bool sending = false, comms = false;
            try
            {
                Vessel v = ActiveVessel;
                if (v == null) return true;   // nothing to check against: keep all
                foreach (IScienceDataContainer c in v.FindPartModulesImplementing<IScienceDataContainer>())
                {
                    ScienceData[] data = c.GetData();
                    if (data != null) foreach (ScienceData d in data) if (d != null) aboard.Add(d.subjectID);
                }
                // an antenna that sends right now: data that is gone may be on its way, not reset. Without any
                // antenna or other comms device aboard nothing can be sent, so data that is gone was reset.
                foreach (IScienceDataTransmitter t in v.FindPartModulesImplementing<IScienceDataTransmitter>())
                {
                    if (t == null) continue;
                    comms = true;
                    if (t.IsBusy()) sending = true;
                }
            }
            catch (Exception e) { Log.Debug("Science batch data check: " + e.Message); return true; }
            int kept = 0, sent = 0;
            for (int i = b.subjects.Count - 1; i >= 0; i--)
            {
                string id = b.subjects[i];
                if (aboard.Contains(id)) { kept++; continue; }
                if (comms && (b.sent.Contains(id) || sending)) { sent++; continue; }
                Log.Info("Runtime voice: experiment " + id + " was reset, no talk about it");
                b.subjects.RemoveAt(i);
                b.amounts.RemoveAt(i);
            }
            b.dataSent = kept > 0 && sent == 0 ? 0 : sent > 0 && kept == 0 ? 1 : double.NaN;
            if (b.subjects.Count > 0)
                Log.Info("Runtime voice: science batch of " + b.subjects.Count + ", " + kept + " kept aboard, " + sent + " sent");
            return b.subjects.Count > 0;
        }

        void CloseBatch(SciBatch b)
        {
            if (host.probeMode) { ProbeScience(b.subjects[0], b.amounts[0], b.body, b); return; }
            if (b.subjects.Count == 1)
            {
                if (!RuntimeScienceTakeover(b.subjects[0], b.amounts[0], b.body))
                    Log.Info("Runtime voice: experiment " + b.subjects[0] + " got no talk");
                return;
            }
            if (!RuntimeOn || radioSilent || !host.settings.radioEnabled) return;
            KerbalVoice k = ScienceKerbal();
            if (k == null) return;
            string topic = RuntimeLines.ScienceTopic(PhraseClips.runtimeTemplates, b.subjects[0]);
            if (McUnreachable || !host.signal.connected)
            {
                if (BatchChat(topic, b, k)) return;
            }
            else if (ScienceTalk(topic, b.subjects[0], b.amounts[0], b.body, k, b)) return;
            // no batch talk fits: a talk about the first experiment
            Log.Info("Runtime voice: no batch talk fits " + b.subjects.Count + " experiments, a talk about the first");
            RuntimeScienceTakeover(b.subjects[0], b.amounts[0], b.body);
        }

        /// <summary>No link: the kerbal who did the batch tells a crewmate about it (a kerbal alone talks to
        /// themselves). The data stays aboard. False when no batch chat fits.</summary>
        bool BatchChat(string topic, SciBatch b, KerbalVoice k)
        {
            var nums = new Dictionary<string, double>();
            var names = new Dictionary<string, string[]>();
            names["exp"] = ExpNames(b.subjects[0]);
            BatchValues(b, nums, names);
            nums["data_sent"] = 0;
            KerbalVoice partner = PartnerOf(k);
            ScienceChatStarting();
            bool ok = EventChat(topic, k, partner, names, ScienceChatQueued, nums)
                      || (partner != null && EventChat(topic, k, null, names, ScienceChatQueued, nums));
            if (!ok) ScienceChatFailed();
            Log.Info("Runtime voice: science batch of " + b.subjects.Count + " without a link, " + (ok ? "chat queued" : "no batch chat fits"));
            return ok;
        }

        /// <summary>An event chat about science is being made: the science hold starts (Rendering).</summary>
        void ScienceChatStarting()
        {
            sciPhase = SciPhase.Rendering;
            sciPhaseClock = clock;
        }

        /// <summary>The event chat was queued (or dropped): the hold lasts while it plays.</summary>
        void ScienceChatQueued()
        {
            sciPhase = SciPhase.Playing;
            sciPhaseClock = clock;
        }

        /// <summary>No event chat fits: no hold.</summary>
        void ScienceChatFailed()
        {
            if (sciPhase == SciPhase.Rendering) sciPhase = SciPhase.None;
        }

        /// <summary>Queues the finished clips when there is room. Waits ReadyMaxWait s, drops them when the radio is
        /// cut or the kerbal cannot talk any more.</summary>
        void TrySciReady()
        {
            SciReady s = sciReady;
            if (s == null) { if (sciPhase == SciPhase.Waiting) sciPhase = SciPhase.None; return; }
            string why = null;
            if (!CanChat(s.k)) why = "the kerbal cannot talk";
            else if (Somber) why = "the crew mourns";
            else if (!host.settings.radioEnabled || radioSilent) why = "the radio is off";
            else if (McUnreachable) why = "no link";
            int need = 0;
            for (int n = 0; n < s.entries.Count; n++) need += s.crewLine[n] ? 4 : 7;
            if (why == null && count + need > Capacity)
            {
                if (clock - sciPhaseClock < ReadyMaxWait) return;
                why = "no room in the queue for " + ReadyMaxWait.ToString("0") + " s";
            }
            sciReady = null;
            if (why != null)
            {
                foreach (var c in s.clips) if (c != null) UnityEngine.Object.Destroy(c);
                sciPhase = SciPhase.None;
                Log.Info("Runtime voice: science talk " + s.id + " dropped (" + why + ")");
                return;
            }
            for (int n = 0; n < s.entries.Count; n++)
            {
                PhraseClips.SetRuntimeClip(s.entries[n], s.clips[n]);
                float delay = n == 0 ? U(0.3f, 0.8f) : ResponseDelay(host.stress.stress);
                if (s.crewLine[n]) EnqueueCrew(s.entries[n].Folder, delay, 0f, s.k, s.entries[n]);
                else EnqueueMc(s.entries[n].Folder, delay, s.entries[n], s.k);
            }
            runtimeOnly++;
            sciPhase = SciPhase.Playing;
            sciPhaseClock = clock;
            Log.Info("Runtime voice: science talk " + s.id + " queued");
            // now and then Mission Control adds the points, when the talk did not say them
            if (s.points)
            {
                try { RuntimeScience(s.subjectId, s.amount, s.body, true); }
                catch (Exception e) { Log.Exception("Runtime voice science points", e); }
            }
        }

        /// <summary>The values of a batch: {exp_count}, {exp_list}, {sci} the points of all of it, {sci_first} 1 when
        /// every experiment is a first. No {sci_left} or {exp_runs}: those are about one experiment.</summary>
        void BatchValues(SciBatch b, Dictionary<string, double> nums, Dictionary<string, string[]> names)
        {
            double sum = 0;
            bool anyPts = false, allFirst = true;
            var es = new List<string>();
            var en = new List<string>();
            for (int i = 0; i < b.subjects.Count; i++)
            {
                double pts, left;
                bool first;
                if (SciencePoints(b.subjects[i], b.amounts[i], out pts, out left, out first))
                {
                    anyPts = true;
                    sum += pts;
                    if (!first) allFirst = false;
                }
                else allFirst = false;
                string[] w = ExperimentLabels(b.subjects[i]);
                if (!en.Contains(w[1])) { es.Add(w[0]); en.Add(w[1]); }
            }
            nums.Remove("sci");
            nums.Remove("sci_left");
            nums.Remove("sci_first");
            nums.Remove("exp_runs");
            if (anyPts)
            {
                if (sum >= 0.05) nums["sci"] = sum;
                nums["sci_first"] = allFirst ? 1 : 0;
            }
            nums["exp_count"] = b.subjects.Count;
            string[] list = RuntimeLines.ListWords(es, en, BatchListMax);
            names["exp_list"] = new[] { LonapseText.ReverseText(list[0]), list[1] };
        }

        // ---------------- Debug tab ----------------

        /// <summary>Debug tab: a science talk picks a talk that only fits now (first data, landed ...) whenever one
        /// fits. Not saved.</summary>
        public bool debugTalkSpecial;
        /// <summary>The talk picked last and whether it was one that only fits now, for the Debug tab.</summary>
        string lastTalkId;
        bool lastTalkSpecial;
        /// <summary>The subject the Debug tab test made, so its science points can be worked out.</summary>
        ScienceSubject debugSubject;

        /// <summary>Debug tab: a science talk now about topic (a science topic, else a random experiment), as if the
        /// experiment ran here: the subject of the vessel's situation and biome, with the full data of the experiment.
        /// The talk is picked like in flight (first heard, the topic's own talks, the values of now).</summary>
        public string DebugScienceTalk(string topic)
        {
            string why = DebugCheck();
            if (why != null) return why;
            if (host.probeMode || host.roster.voices.Count == 0) return "a science talk needs crew aboard";
            if (McUnreachable) return "no link to Mission Control (the kerbals talk among themselves then)";
            if (!host.settings.radioEnabled || radioSilent) return "the radio is off or silent";
            if (runtimeChatPending) return "a runtime chat is still being made, try again";
            if (sciPhase != SciPhase.None || sciBatch != null) return "a science talk is still going, try again";
            var topics = new List<string>(PhraseTable.ScienceTopics);
            topics.Remove("any");
            if (string.IsNullOrEmpty(topic) || !topics.Contains(topic)) topic = topics[rng.Next(topics.Count)];
            float amount;
            string subjectId = DebugSubject(topic, out amount);
            KerbalVoice k = ScienceKerbal();
            if (k == null) return "nobody aboard can report (only tourists?)";
            Vessel v = ActiveVessel;
            string bn = v.mainBody != null ? v.mainBody.bodyName : null;
            Log.Info("Runtime voice: science talk from the Debug tab, " + topic + " (" + subjectId + ")");
            if (!ScienceTalk(topic, subjectId, amount, bn, k)) return "no science talk fits " + topic + " here";
            return "science talk " + lastTalkId + (lastTalkSpecial ? " (fits only now)" : "") + ", " + k.displayName + ", " + topic;
        }

        /// <summary>Debug tab: a batch talk now, as if three random experiments ran here at once.</summary>
        public string DebugScienceBatch()
        {
            string why = DebugCheck();
            if (why != null) return why;
            if (host.probeMode || host.roster.voices.Count == 0) return "a science talk needs crew aboard";
            if (!host.settings.radioEnabled || radioSilent) return "the radio is off or silent";
            if (runtimeChatPending || sciPhase != SciPhase.None || sciBatch != null) return "a science talk is still going, try again";
            var topics = new List<string>(PhraseTable.ScienceTopics);
            topics.Remove("any");
            KerbalVoice k = ScienceKerbal();
            if (k == null) return "nobody aboard can report (only tourists?)";
            Vessel v = ActiveVessel;
            var b = new SciBatch { first = clock, last = clock, body = v.mainBody != null ? v.mainBody.bodyName : null };
            var names = new List<string>();
            for (int i = 0; i < 3 && topics.Count > 0; i++)
            {
                string topic = topics[rng.Next(topics.Count)];
                topics.Remove(topic);
                float amount;
                // DebugSubject keeps the last subject for the points: the batch sums them by subject id
                b.subjects.Add(DebugSubject(topic, out amount));
                b.amounts.Add(amount);
                names.Add(topic);
            }
            string t0 = RuntimeLines.ScienceTopic(PhraseClips.runtimeTemplates, b.subjects[0]);
            Log.Info("Runtime voice: science batch from the Debug tab, " + string.Join(", ", names.ToArray()));
            if (McUnreachable || !host.signal.connected)
            {
                // no link: the kerbals talk about it among themselves
                if (!BatchChat(t0, b, k)) return "no batch chat without a link fits here";
                return "batch chat without a link, " + k.displayName + ", " + string.Join(", ", names.ToArray());
            }
            if (!ScienceTalk(t0, b.subjects[0], b.amounts[0], b.body, k, b)) return "no batch talk fits here";
            return "batch talk " + lastTalkId + (lastTalkSpecial ? " (fits only now)" : "") + ", " + k.displayName + ", "
                   + string.Join(", ", names.ToArray());
        }

        /// <summary>The subject id of an experiment of topic in the active vessel's situation and biome, and the full
        /// data of one run. topic@Debug and 0 when KSP has no such experiment (then the talk has no points).</summary>
        string DebugSubject(string topic, out float amount)
        {
            amount = 0f;
            debugSubject = null;
            Vessel v = ActiveVessel;
            try
            {
                foreach (string id in ResearchAndDevelopment.GetExperimentIDs())
                {
                    if (EventWatchers.ExperimentId(id) != topic) continue;
                    ScienceExperiment exp = ResearchAndDevelopment.GetExperiment(id);
                    if (exp == null || v == null || v.mainBody == null) continue;
                    ExperimentSituations sit = ScienceUtil.GetExperimentSituation(v);
                    string biome = exp.BiomeIsRelevantWhile(sit) ? ScienceUtil.GetExperimentBiome(v.mainBody, v.latitude, v.longitude) : "";
                    ScienceSubject sub = ResearchAndDevelopment.GetExperimentSubject(exp, sit, v.mainBody, biome,
                                                                                    ScienceUtil.GetBiomedisplayName(v.mainBody, biome));
                    if (sub == null) continue;
                    amount = exp.baseValue * exp.dataScale;
                    debugSubject = sub;
                    return sub.id;
                }
            }
            catch (Exception e) { Log.Debug("Debug science subject: " + e.Message); }
            return topic + "@Debug";
        }

        /// <summary>A talk not heard yet, else one of the quarter heard longest ago (the line memory, kept between
        /// sessions). Null for an empty list.</summary>
        List<RuntimeTemplate> FreshestTalk(List<List<RuntimeTemplate>> list)
        {
            if (list == null || list.Count == 0) return null;
            var fresh = list.FindAll(t => Mem.LastSeq(t[0].Id) < 0);
            if (fresh.Count == 0)
            {
                var sorted = new List<List<RuntimeTemplate>>(list);
                sorted.Sort((a, b) => Mem.LastSeq(a[0].Id).CompareTo(Mem.LastSeq(b[0].Id)));
                int n = Math.Max(1, (sorted.Count + SceneMemory.OldestShareDivisor - 1) / SceneMemory.OldestShareDivisor);
                fresh = sorted.GetRange(0, n);
            }
            return fresh[rng.Next(fresh.Count)];
        }

        /// <summary>One of the fillable versions of a talk line: one not heard yet first, a version with a value before
        /// the plain one (the line memory). Without a name when a name was said a moment ago and a version allows it.</summary>
        int PickTalkVersion(RuntimeTemplate t, List<int> vs)
        {
            if (vs == null || vs.Count == 0) return -1;
            if (NamesRecent)
            {
                List<int> bare = vs.FindAll(i => !SubtitleSlots.NamesSomeone(t.Versions[i][1]));
                if (bare.Count > 0) vs = bare;
            }
            List<string> keys = vs.ConvertAll(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string p = Mem.TakeValue(SceneMemory.VersionPool + t.Id, keys, rng);
            int v = p != null ? int.Parse(p, System.Globalization.CultureInfo.InvariantCulture) : vs[0];
            if (SubtitleSlots.NamesSomeone(t.Versions[v][1])) NameSaid();
            return v;
        }
    }
}
