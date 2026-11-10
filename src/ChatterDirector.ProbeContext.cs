using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        bool probeContextPending;
        float probeContextAt = -1000f;
        readonly Dictionary<string, float> probeTopics = new Dictionary<string, float>();
        Guid contextVessel;
        int probeContextGeneration;
        SciBatch pendingProbeScience;
        float pendingProbeScienceAt;
        readonly HashSet<string> receivedSubjects = new HashSet<string>();
        float receivedAt;
        readonly Dictionary<string, Dictionary<string, double>> probeMeasurements = new Dictionary<string, Dictionary<string, double>>();
        readonly Dictionary<string, double> receivedPoints = new Dictionary<string, double>();
        bool carrierRunning;
        readonly HashSet<string> sessionFacts = new HashSet<string>();

        string Fact(string key) { Vessel v = ActiveVessel; return (v != null ? v.id.ToString() : "none") + ":" + key; }
        bool Known(string key) { return ProbeHistory.Current != null ? ProbeHistory.Current.Has(Fact(key)) : sessionFacts.Contains(Fact(key)); }
        void Remember(string key)
        {
            if (ProbeHistory.Current != null) ProbeHistory.Current.Remember(Fact(key));
            else sessionFacts.Add(Fact(key));
        }

        void ProbeContextTick()
        {
            Vessel v = ActiveVessel;
            if (v == null) return;
            if (contextVessel != v.id) { contextVessel = v.id; probeTopics.Clear(); probeContextAt = clock; }
            double ec = ResourcePercent(v, "ElectricCharge");
            if (ec < 15) { Remember("low-power"); Forget("power-recovered"); }
            if (!ProbeLink) { Remember("out-of-contact"); Forget("contact-returned"); }
            if (ProbeTense) { Remember("stressed"); Forget("relieved"); }
            FlushProbeReceipts();
            if (pendingProbeScience != null && clock - pendingProbeScienceAt > 30f) pendingProbeScience = null;
            if (pendingProbeScience != null && AirFree && !probeContextPending)
            {
                SciBatch batch = pendingProbeScience; pendingProbeScience = null;
                double previous = sciSentNow; sciSentNow = batch.dataSent;
                try { ProbeScience(batch.subjects[0], batch.amounts[0], batch.body, batch); }
                finally { sciSentNow = previous; }
            }
        }

        void Forget(string key)
        {
            if (ProbeHistory.Current != null) ProbeHistory.Current.Forget(Fact(key));
            sessionFacts.Remove(Fact(key));
        }

        bool TopicReady(string topic)
        {
            float at;
            return !probeTopics.TryGetValue(topic, out at) || clock - at > 240f;
        }

        // Render all speech before starting a turn. Never strand a beep awaiting a slow synthesis job.
        // Guard against vessel switches, changed ModuleLonapse selection and stale measurements.
        bool ContextExchange(string topic, string es, string en, string mood,
            Dictionary<string, double> nums, Dictionary<string, string[]> names, bool hail,
            string followEs = null, string followEn = null, KerbalVoice crew = null,
            Func<bool> valid = null, Action committed = null, string stressCause = null, string band = "calm")
        {
            bool cabin = crew != null;
            bool stressTalk = stressCause != null;
            if (!cabin && hail && host.stationMode) return false;
            string stressId = "probe_stress_" + stressCause;
            if (!RuntimeOn || probeContextPending || (stressTalk
                ? !probeRepeat.Allowed(stressId, ProbeRules.StressEvent, stressCause, clock, Tu.causeRepeatGap, Tu.calmRepeatGap)
                : !TopicReady(topic)) || Somber || !host.CanStartAudio
                || ProbeAsleep || (cabin ? !ProbeCrewPossible : !ProbeLink) || count > 0 || Holding || FlightPhase.IsSerious(CurrentPhase) && !stressTalk && topic != "spin" && topic != "power") return false;
            if (!cabin && !host.settings.radioEnabled) return false;

            // Mods can specialize contextual lines through the existing runtime/event format.
            RuntimeTemplate authoredScene = null;
            nums["x_core_" + Core] = 1;
            List<RuntimeTemplate> overrides = RuntimeLines.Fitting(PhraseClips.runtimeTemplates, "event", (ActiveVessel != null ? SituationOf(ActiveVessel) : null) ?? "other", nums, names)
                .FindAll(t => t.On.Contains("probe." + topic.Replace('-', '_')) && ProbeRuntimeTable.CanFill(t, nums, names)
                    && (!host.stationMode || cabin || stressTalk || t.ProbeLines == null
                        || t.ProbeLines.Count > 0 && t.ProbeLines[0].Role == "mc")
                    && (t.ProbeLines == null || FlightPhase.Matches(t.ProbePhase ?? "any", CurrentPhase)
                        && !t.ProbeLines.Exists(line => line.Role == (cabin ? "mc" : "crew"))));
            if (overrides.Count > 0)
            {
                // A mod's lines and exact lineset checks win over generic shipped wording.
                var modded = overrides.FindAll(t => PhraseClips.ModOf(t.Where) != null);
                if (modded.Count > 0) overrides = modded;
                var specific = overrides.FindAll(t => t.Checks.Exists(c => c.Key == "x_core_" + Core));
                if (specific.Count > 0) overrides = specific;
                overrides.Sort((a, b) => Mem.LastSeq(a.Id).CompareTo(Mem.LastSeq(b.Id)));
                RuntimeTemplate replacement = overrides[0];
                authoredScene = replacement.ProbeLines != null ? replacement : null;
                if (authoredScene != null) band = authoredScene.ProbeBand ?? band;
                Mem.Stamp(replacement.Id);
                List<int> versions = RuntimeLines.FillableVersions(replacement, nums, names);
                if (versions.Count > 0)
                {
                    var choices = new List<string>(); foreach (int version in versions) choices.Add(version.ToString());
                    int selected = int.Parse(Mem.TakeValue("probe:" + replacement.Id, choices, rng));
                    string[] words = replacement.Versions[selected]; es = words[0]; en = words[1];
                }
            }
            string sp, english, fp = null, fe = null;
            if (!RuntimeLines.FillText(es, en, nums, names, out sp, out english)) return false;
            if (followEs != null && !RuntimeLines.FillText(followEs, followEn, nums, names, out fp, out fe)) return false;
            var plan = authoredScene != null ? ProbeRuntimeTable.Fill(authoredScene, nums, names, Mem, rng) : new List<ProbeRuntimeLine>();
            if (plan == null) return false;
            if (authoredScene == null)
            {
                if (hail) plan.Add(new ProbeRuntimeLine { Role = "probe", Mood = mood, English = ProbeRules.BeepSubtitle(mood) });
                plan.Add(new ProbeRuntimeLine { Role = cabin ? "crew" : "mc", Spanish = sp, English = english });
                if (!hail) plan.Add(new ProbeRuntimeLine { Role = "probe", Mood = mood, English = ProbeRules.BeepSubtitle(mood) });
            }
            if (fp != null) plan.Add(new ProbeRuntimeLine { Role = cabin ? "crew" : "mc", Spanish = fp, English = fe });
            var speech = plan.FindAll(line => line.Role != "probe");
            int queueNeed = 1;
            foreach (var line in plan) queueNeed += line.Role == "probe" ? 1 : cabin ? 2 : 7;
            if (speech.Count == 0 || queueNeed > Capacity) return false;
            Vessel vessel = ActiveVessel;
            if (vessel == null) return false;
            Guid id = vessel.id;
            string core = Core, display = host.ProbeDisplayName;
            ProbeVoiceSpec voice = host.probeVoice;
            string signature = voice != null ? voice.Signature() : "";
            float began = clock;
            int generation = probeContextGeneration;
            probeContextPending = true;
            int remaining = speech.Count;
            var clips = new AudioClip[remaining];
            bool failed = false;
            Action<int, string, string> render = (index, spoken, subtitle) => Voice.Request(RuntimeDelivery.KindFor(cabin ? KindFor(crew) : "mc", band, "chatter", stressTalk ? ProbeRules.StressEvent : "probe_chat"), spoken, r =>
            {
                clips[index] = r.clip;
                failed |= r.clip == null;
                if (--remaining != 0) return;
                if (generation != probeContextGeneration) { foreach (AudioClip clip in clips) if (clip != null) UnityEngine.Object.Destroy(clip); return; }
                probeContextPending = false;
                Vessel now = ActiveVessel;
                bool same = now != null && now.id == id && Core == core && host.ProbeDisplayName == display
                    && (host.probeVoice != null ? host.probeVoice.Signature() : "") == signature;
                if (failed || !same || clock - began > 8f || !RuntimeOn || !host.CanStartAudio || Somber
                    || !cabin && hail && host.stationMode
                    || ProbeAsleep || count > 0 || Holding || RadioAudible || count + queueNeed > Capacity
                    || (cabin ? !ProbeCrewPossible || !CanChat(crew) : !ProbeLink || !host.settings.radioEnabled)
                    || (valid != null && !valid()))
                {
                    foreach (AudioClip clip in clips) if (clip != null) UnityEngine.Object.Destroy(clip);
                    return;
                }
                string scene = "generated_probe_" + topic + "_" + (++runtimeAsked);
                var lines = new List<PhraseEntry>();
                int spokenIndex = 0;
                for (int i = 0; i < plan.Count; i++)
                {
                    ProbeRuntimeLine line = plan[i];
                    bool beep = line.Role == "probe";
                    var entry = new PhraseEntry { ClipKey = scene + "_" + i, Role = beep ? "probe" : cabin ? "crew" : "mc",
                        Event = stressTalk ? ProbeRules.StressEvent : "probe_chat", Scene = scene, Seq = i + 1, Core = core,
                        Band = band, Severity = "chatter", Phase = "any", Beep = line.Mood,
                        Spanish = RuntimeSpeech.Unmarked(line.Spanish), LonapseText = RuntimeSpeech.Spoken(line.Spanish),
                        English = line.English, Gender = cabin || beep ? "both" : "m" };
                    entry.Folder = PhraseTable.FolderFor(entry.Role, entry.Severity, entry.Band, entry.Event);
                    if (!beep) PhraseClips.SetRuntimeClip(entry, clips[spokenIndex++]);
                    lines.Add(entry);
                }
                // Explicit queueing keeps custom beep voices on VoiceClip, including ModuleLonapse overrides.
                if (cabin) { chatA = crew; chatB = crew; chatForced = false; chatStarted = false; chatLimit = IntercomAbortLimit; lastChatClock = clock; }
                foreach (PhraseEntry e in lines)
                {
                    if (e.Role == "mc") EnqueueMc(e.Folder, ResponseDelay(host.stress.stress), e);
                    else if (e.Role == "crew")
                    {
                        Enqueue(UttKind.IntercomClick, null, 0.2f, 0f, null);
                        Utterance u = Enqueue(UttKind.IntercomLine, null, 0.04f, 0f, crew); if (u != null) u.entry = e;
                    }
                    else { Utterance u = Enqueue(cabin ? UttKind.CabinBeep : UttKind.ProbeBeep, null, hail ? 0.2f : 0.4f + (cabin ? 0f : host.LightDelay), 0f, null); if (u != null) u.entry = e; }
                }
                if (cabin) Enqueue(UttKind.IntercomClick, null, 0.1f, 0f, null);
                Log.Info("Runtime probe exchange " + topic + " (" + band + ", core " + core + ") queued");
                probeTopics[topic] = clock; probeContextAt = clock;
                if (stressTalk) probeRepeat.Stamp(stressId, ProbeRules.StressEvent, stressCause, clock);
                if (committed != null) committed();
                // Reserve the completed exchange as this scheduler's radio turn.
                if (!cabin && progress > target * 0.5f) progress = target * 0.5f;
                for (int q = 0; q < count; q++) queue[(head + q) % Capacity].contextValid = () =>
                    ActiveVessel != null && ActiveVessel.id == id && Core == core && host.ProbeDisplayName == display
                    && (host.probeVoice != null ? host.probeVoice.Signature() : "") == signature
                    && (cabin || !hail || !host.stationMode)
                    && (cabin ? host.settings.probeCrewChats : ProbeLink) && (valid == null || valid());
                runtimeUsed += clips.Length;
            });
            for (int i = 0; i < speech.Count; i++) render(i, speech[i].Spanish, speech[i].English);
            return true;
        }

        bool TryContextProbe(bool hail)
        {
            if (ProbeTense) return host.settings.probeStressTalk && TryRuntimeProbeStress();
            if (!host.settings.runtimeReports || !host.settings.probePetTalk && !hail || clock - probeContextAt < 45f
                || rng.NextDouble() > 0.65) return false;
            return BuildContextProbe(hail, null);
        }
        bool TryContextCrewProbe()
        {
            if (!host.settings.runtimeReports || rng.NextDouble() > 0.5) return false;
            KerbalVoice k = PickRested();
            return k != null && CanChat(k) && BuildContextProbe(true, k);
        }

        // A topic-specific runtime probe exchange. The tables keep a matching scene for each stock core.
        public bool TryRuntimeProbeTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic) || !host.probeMode) return false;
            Vessel v = ActiveVessel;
            if (v == null) return false;
            var nums = new Dictionary<string, double>(); var names = new Dictionary<string, string[]>();
            CollectValues(v, SituationOf(v) ?? "other", nums, names);
            names["probe"] = new[] { RuntimeSpeech.Respell(host.ProbeDisplayName), host.ProbeDisplayName };
            return ContextExchange(topic, "{probe}, actualización recibida. Todo queda registrado.",
                "{probe}, update received. Everything is logged.", ProbeContext.Mood(topic), nums, names, false);
        }

        bool BuildContextProbe(bool hail, KerbalVoice crew)
        {
            Vessel v = ActiveVessel;
            if (v == null) return false;
            var nums = new Dictionary<string, double>(); var names = new Dictionary<string, string[]>();
            string sit = SituationOf(v) ?? "other";
            CollectValues(v, sit, nums, names);
            names["probe"] = new[] { RuntimeSpeech.Respell(host.ProbeDisplayName), host.ProbeDisplayName };
            double ec = nums.ContainsKey("ec") ? nums["ec"] : 100;
            string topic = ProbeContext.Topic(ec, Known("low-power") && !Known("power-recovered"), host.settings.probePlanTalk && nums.ContainsKey("node_min"),
                host.settings.probeStressTalk && v.angularVelocity.magnitude > 0.5, v.LandedOrSplashed, names.ContainsKey("biome"));
            string es, en, fs = null, fn = null;
            if (topic != "power" && topic != "recovery" && Known("out-of-contact") && !Known("contact-returned")) topic = "contact";
            else if (!ProbeTense && topic != "power" && topic != "recovery" && Known("stressed") && !Known("relieved")) topic = "relief";
            if (topic == "recovery") { es = "Batería al {ec} por ciento. ¿Más ganas de hablar?"; en = "Battery at {ec} percent. Feeling more talkative?"; }
            else if (topic == "power") { es = "{probe}, batería al {ec} por ciento. Ahorra fuerzas."; en = "{probe}, battery at {ec} percent. Save your strength."; }
            else if (topic == "spin") { es = "Vemos el giro, {probe}. Vamos a estabilizarte."; en = "We see the spin, {probe}. Let us get you steady."; }
            else if (topic == "node") { es = "Maniobra de {node_dv} metros por segundo en {node_min} minutos. ¿Listo?"; en = "A {node_dv} meters per second maneuver in {node_min} minutes. Ready?"; }
            else if (host.settings.probePlanTalk && Known("node-planned") && !Known("node-finished") && !nums.ContainsKey("node_min"))
            { topic = "node-finished"; es = "El nodo ya no está en el plan. Revisamos la órbita."; en = "The node is no longer on the plan. Checking the orbit."; }
            else if (topic == "contact") { es = "Ahí estás, {probe}. Volvemos a escucharte."; en = "There you are, {probe}. We can hear you again."; }
            else if (topic == "relief") { es = "Todo más tranquilo, {probe}. Buen trabajo."; en = "Things have settled down, {probe}. Good work."; }
            else if (topic == "biome") { es = "Datos de posición: {biome}, en {body}. Buen sitio para investigar."; en = "Position report: {biome}, on {body}. A good place to investigate."; }
            else if (host.settings.probeMilestones && nums.ContainsKey("day") && nums["day"] >= 10 && !Known("anniversary:" + (int)nums["day"]))
            { topic = "anniversary"; es = "Día {day} alrededor de {body}, {probe}. Ya eres un veterano."; en = "Day {day} at {body}, {probe}. You're a veteran now."; }
            else if (sit == "orbit") { es = "{probe}, periapsis a {pe_km} kilómetros sobre {body}."; en = "{probe}, periapsis {pe_km} kilometers above {body}."; }
            else { es = "Día {day} de misión en {body}, {probe}. Seguimos contigo."; en = "Mission day {day} at {body}, {probe}. We're still with you."; }
            if (crew != null)
            {
                string job = crew.persona != null ? crew.persona.trait : "";
                if (job == "engineer" && nums.ContainsKey("ec")) { topic = "crew-power"; es = "Batería al {ec} por ciento. Gracias por el informe."; en = "Battery at {ec} percent. Thanks for the report."; }
                else if (job == "scientist" && names.ContainsKey("biome")) { topic = "crew-science"; es = "Estamos sobre {biome}. Veamos qué podemos medir."; en = "We're over {biome}. Let's see what we can measure."; }
                else if (job == "pilot" && nums.ContainsKey("node_min")) { topic = "crew-node"; es = "{node_min} minutos hasta la maniobra. Tenemos tiempo."; en = "{node_min} minutes until the maneuver. We have time."; }
            }
            if (crew == null && !hail && !FlightPhase.IsSerious(CurrentPhase))
            {
                switch (ProbeContext.Personality(Core)) {
                    case "precision": fs = "Sí, puedes conservar los decimales."; fn = "Yes, you can keep the decimal places."; break;
                    case "brave": fs = "Pequeño, pero valiente."; fn = "Small, but brave."; break;
                    case "rover": if (v.LandedOrSplashed) { fs = "Con calma por ahí abajo."; fn = "Take it easy down there."; } break;
                    case "confident": fs = "Sí, ya sabemos que lo tienes controlado."; fn = "Yes, we know you've got it under control."; break;
                }
            }
            string chosen = topic;
            return ContextExchange(topic, es, en, ProbeContext.Mood(topic), nums, names, hail, fs, fn, crew,
                () => { double current = ResourcePercent(ActiveVessel, "ElectricCharge"); return (!nums.ContainsKey("ec") || Math.Abs(current - ec) < 3) && (chosen != "spin" || ActiveVessel.angularVelocity.magnitude > 0.5) && (chosen != "node" || ActiveVessel.patchedConicSolver != null && ActiveVessel.patchedConicSolver.maneuverNodes.Count > 0); },
                () => { if (chosen == "node") { Remember("node-planned"); Forget("node-finished"); } if (chosen == "anniversary") Remember("anniversary:" + (int)nums["day"]); if (chosen == "recovery") Remember("power-recovered"); if (chosen == "contact") Remember("contact-returned"); if (chosen == "relief") Remember("relieved"); if (chosen == "node-finished") Remember(chosen); });
        }

        bool ProbeScience(string subject, float amount, string body, SciBatch batch)
        {
            Vessel v = ActiveVessel;
            if (v == null || !RuntimeOn || !host.settings.runtimeScience || !host.settings.eventScience) return false;
            // A disconnected probe stores data silently. Receipt is acknowledged by NoteTransmitted.
            if (!ProbeLink) { Remember("stored:" + subject); Remember("science:" + subject); return true; }
            if (!AirFree || probeContextPending)
            {
                if (pendingProbeScience == null)
                {
                    pendingProbeScience = batch ?? new SciBatch { body = body, dataSent = sciSentNow };
                    if (batch == null) { pendingProbeScience.subjects.Add(subject); pendingProbeScience.amounts.Add(amount); }
                    pendingProbeScienceAt = clock;
                }
                else if (batch != pendingProbeScience)
                {
                    if (batch != null)
                    {
                        for (int i = 0; i < batch.subjects.Count; i++)
                            if (pendingProbeScience.subjects.Count < 32) { pendingProbeScience.subjects.Add(batch.subjects[i]); pendingProbeScience.amounts.Add(batch.amounts[i]); }
                        foreach (string sent in batch.sent) pendingProbeScience.sent.Add(sent);
                    }
                    else if (pendingProbeScience.subjects.Count < 32) { pendingProbeScience.subjects.Add(subject); pendingProbeScience.amounts.Add(amount); }
                    if (pendingProbeScience.dataSent != sciSentNow) pendingProbeScience.dataSent = double.NaN;
                }
                return true;
            }
            var nums = new Dictionary<string, double>(); var names = new Dictionary<string, string[]>();
            CollectValues(v, SituationOf(v) ?? "other", nums, names);
            ScienceValues(subject, amount, body, nums, names);
            Dictionary<string, double> measurement;
            if (probeMeasurements.TryGetValue(subject, out measurement))
                foreach (var value in measurement) nums[value.Key] = value.Value;
            if (batch != null) BatchValues(batch, nums, names);
            if (batch != null && batch.sent.Count > 0)
            {
                // Let receipt finish first; the follow-up contains the successful batch's experiment names.
                foreach (string sent in batch.sent) receivedSubjects.Add(sent);
                receivedAt = clock;
            }
            bool repeat = Known("science:" + subject);
            nums["x_repeat"] = repeat ? 1 : 0;
            nums["x_stored"] = SciKept ? 1 : 0;
            string topic = RuntimeLines.ScienceTopic(PhraseClips.runtimeTemplates, subject);
            string es = "Experimento de {exp} completado en {body}.", en = "{exp} experiment completed at {body}.";
            if (batch != null && batch.subjects.Count > 1) { es = "{exp_count} experimentos completados: {exp_list}. Buen trabajo."; en = "{exp_count} experiments completed: {exp_list}. Good work."; }
            else if (repeat) { es = "Otra lectura de {exp} en el mismo lugar. Muy meticuloso."; en = "Another {exp} reading at the same location. Very thorough."; }
            else if (topic == "temperaturescan" && nums.ContainsKey("temp_c")) { es = "Temperatura local: {temp_c} grados. Lectura registrada."; en = "Local temperature: {temp_c} degrees Celsius. Reading recorded."; }
            else if (topic == "barometerscan" && nums.ContainsKey("press_kpa")) { es = "Presión local: {press_kpa} kilopascales. Confirmado."; en = "Local pressure: {press_kpa} kilopascals. Confirmed."; }
            else if (topic == "gravityscan" && nums.ContainsKey("grav_g")) { es = "Gravedad local: {grav_g}. Buena medición."; en = "Local gravity: {grav_g} g. Good measurement."; }
            else if (topic == "mysterygoo") { es = "Observación de moco en {body} completada. Gracias por vigilar el bote."; en = "Goo observation at {body} complete. Thanks for watching the canister."; }
            else if (topic == "mobilematerialslab") { es = "Estudio de materiales en {body} completado. Buen trabajo con la bahía."; en = "Materials study at {body} complete. Nice work with the bay."; }
            string fs = null, fn = null;
            if (!repeat && nums.ContainsKey("sci_first") && nums["sci_first"] == 1 && batch == null)
            { fs = "Primera observación de este experimento aquí. Bien hecho."; fn = "First observation of this experiment here. Well done."; }
            // Only the completed transmission hook confirms receipt, never collection or a busy antenna.
            if (SciKept) { fs = "Datos guardados a bordo. Los recibiremos cuando los mandes."; fn = "Data stored aboard. We'll receive it when you send it."; }
            bool first = !repeat;
            return ContextExchange("science-" + (batch != null && batch.subjects.Count > 1 ? "batch" : topic), es, en,
                repeat ? "bored" : "proud", nums, names, true, fs, fn, null, null,
                () => { if (batch != null) foreach (string s in batch.subjects) Remember("science:" + s); else Remember("science:" + subject); if (first) Remember("science-first:" + body); });
        }

        void ProbeScienceReceived(string subject)
        {
            if (!host.probeMode || subject == null || !RuntimeOn || !host.settings.runtimeScience) return;
            receivedSubjects.Add(subject);
            receivedAt = clock;
        }

        void FlushProbeReceipts()
        {
            if (receivedSubjects.Count == 0 || clock - receivedAt < 2f || !ProbeLink || probeContextPending) return;
            var list = new List<string>(receivedSubjects);
            var es = new List<string>(); var en = new List<string>();
            foreach (string subject in list)
            {
                string[] w = ExperimentLabels(subject);
                if (!en.Contains(w[1])) { es.Add(w[0]); en.Add(w[1]); }
            }
            string[] words = RuntimeLines.ListWords(es, en, BatchListMax);
            var nums = new Dictionary<string, double>(); var names = new Dictionary<string, string[]>();
            names["exp_list"] = new[] { LonapseText.ReverseText(words[0]), words[1] };
            double points = 0; bool allPoints = true;
            foreach (string subject in list) { double value; if (receivedPoints.TryGetValue(subject, out value)) points += value; else allPoints = false; }
            if (allPoints && points > 0) nums["sci"] = points;
            string followEs = nums.ContainsKey("sci") ? "Valor científico estimado: {sci} puntos." : null;
            string followEn = nums.ContainsKey("sci") ? "Estimated science value: {sci} points." : null;
            if (ContextExchange("science-received", "Datos de {exp_list} recibidos. Gracias por cuidarlos.",
                "{exp_list} data received. Thanks for keeping it safe.", "happy", nums, names, true,
                followEs, followEn, null, null, () => { foreach (string subject in list) { receivedSubjects.Remove(subject); receivedPoints.Remove(subject); } })) receivedAt = clock;
        }

        public void ProbeTransmissionAborted(string subject)
        {
            if (!host.probeMode || !host.settings.probeTransmit || !RuntimeOn || !host.settings.runtimeScience) return;
            receivedSubjects.Remove(subject ?? "");
            var nums = new Dictionary<string, double>(); var names = new Dictionary<string, string[]>();
            ContextExchange("science-interrupted", "La transmisión se ha interrumpido. Revisamos el enlace y la energía.",
                "Transmission interrupted. Checking the link and power.", "scared", nums, names, true);
        }

        void SnapshotProbeScience(string subject, float amount)
        {
            if (!host.probeMode) return;
            var nums = new Dictionary<string, double>(); var names = new Dictionary<string, string[]>();
            ScienceValues(subject, amount, BodyName, nums, names);
            var snapshot = new Dictionary<string, double>();
            foreach (string key in new[] { "temp_c", "press_kpa", "grav_g" }) { double value; if (nums.TryGetValue(key, out value)) snapshot[key] = value; }
            if (probeMeasurements.Count < 256 || probeMeasurements.ContainsKey(subject)) probeMeasurements[subject] = snapshot;
            double pts, left; bool first;
            if (SciencePoints(subject, amount, out pts, out left, out first)) receivedPoints[subject] = pts;
        }

        void StartProbeCarrier()
        {
            carrierRunning = true;
            VoiceChannel b = host.controlBed;
            if (!b.Busy && host.settings.radioStatic > 0 && host.CanStartAudio)
            {
                AudioClip clip = ClipLibrary.Next("sfx/loop", rng);
                if (clip != null) b.Play(clip, RadioNoise.ControlLevel(Tu.controlBedVolume, host.StaticLevel, host.settings.weakSignalVolume), 1f, true, 0.04f);
            }
            AudioClip key = ClipLibrary.Next("sfx/squelch", rng);
            if (key != null && !host.keyCh.Busy) { host.keyCh.Play(key, Tu.radioKeyVolume * 0.35f, 1.5f, false, 0f); host.keyCh.keySound = true; }
        }
        void UpdateProbeRadio()
        {
            VoiceChannel b = host.beep[1];
            bool remote = host.probeMode && b.Busy && b.probeBeep && carrierRunning;
            if (remote)
            {
                b.SetBand(Profile.HighPassHz, Profile.LowPassHz);
                b.baseDistortion = Math.Max(0.08f, 0.5f * Profile.Distortion) + 0.3f * host.StaticLevel * host.settings.weakSignalVolume;
                b.extraDistortion = staticChop ? Tu.staticExtraDistortion : 0f;
                b.dropout = host.Dropout;
                if (!ProbeLink) b.FadeOutOnce(0.04f);
            }
            if (carrierRunning && !remote)
            {
                carrierRunning = false;
                if (!host.control.Busy) host.controlBed.FadeOutOnce(0.1f);
                AudioClip tail = ClipLibrary.Next("sfx/squelch", rng);
                if (tail != null && !host.keyCh.Busy && ProbeLink && host.CanStartAudio)
                { host.keyCh.Play(tail, Tu.radioKeyVolume * 0.35f, 1f, false, 0f); host.keyCh.keySound = true; }
            }
        }
    }
}
