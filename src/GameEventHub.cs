// Game events, the KSP side. Subscribes to GameEvents for the active vessel,
// feeds the pure watchers in EventWatchers.cs and hands every EventRequest to `play` (wired by the addon to
// ChatterDirector.PlayEvent). Init and Dispose add and remove exactly the same handlers.
#if !DC_OFFLINE_TEST
using System;
using System.Collections.Generic;

namespace Lonapse
{
    public class GameEventHub : IDisposable
    {
        /// <summary>Receives each event. Returns true when the director took it (played or queued).</summary>
        public Func<EventRequest, bool> play;

        public readonly SoiWatcher soi = new SoiWatcher();
        public readonly ScienceWatcher science = new ScienceWatcher();
        public readonly PartLossWatcher partLoss = new PartLossWatcher();
        public readonly PowerWatcher power = new PowerWatcher();
        public readonly SignalWatcher signalWatch = new SignalWatcher();
        public readonly CrewLossWatcher crewLoss = new CrewLossWatcher();

        // debug overlay
        public int sent, accepted;
        public string lastEvent = "";

        private LonapseAddon host;
        private bool subscribed;
        private double now;                 // hub clock: sum of the Tick dt values
        private Vessel lastVessel;          // active vessel at the last Tick (it may die before onPartDie fires)
        private bool lastPanic, lastPlasma;
        // power state per vessel, so an EVA and back does not repeat power_low
        private readonly Dictionary<Guid, bool> powerLowById = new Dictionary<Guid, bool>();

        public double Now { get { return now; } }

        public void Init(LonapseAddon owner)
        {
            host = owner;
            now = 0;
            soi.Reset(); science.Reset(); partLoss.Reset(); power.Reset(); signalWatch.Reset(); crewLoss.Reset();
            powerLowById.Clear();
            lastVessel = null;
            if (subscribed) return;
            try
            {
                GameEvents.onVesselSOIChanged.Add(OnSoiChanged);
                GameEvents.OnExperimentDeployed.Add(OnExperimentDeployed);
                GameEvents.onGroundSciencePartDeployed.Add(OnGroundPartDeployed);
                GameEvents.onPartDie.Add(OnPartDie);
                GameEvents.onKerbalStatusChange.Add(OnKerbalStatusChange);
                GameEvents.onCrewKilled.Add(OnCrewKilled);
                GameEvents.Contract.onCompleted.Add(OnContractCompleted);
                GameEvents.Contract.onFailed.Add(OnContractFailed);
                subscribed = true;
            }
            catch (Exception e) { Log.Exception("GameEventHub.Init", e); }
        }

        public void Dispose()
        {
            if (!subscribed) return;
            subscribed = false;
            try
            {
                GameEvents.onVesselSOIChanged.Remove(OnSoiChanged);
                GameEvents.OnExperimentDeployed.Remove(OnExperimentDeployed);
                GameEvents.onGroundSciencePartDeployed.Remove(OnGroundPartDeployed);
                GameEvents.onPartDie.Remove(OnPartDie);
                GameEvents.onKerbalStatusChange.Remove(OnKerbalStatusChange);
                GameEvents.onCrewKilled.Remove(OnCrewKilled);
                GameEvents.Contract.onCompleted.Remove(OnContractCompleted);
                GameEvents.Contract.onFailed.Remove(OnContractFailed);
            }
            catch (Exception e) { Log.Exception("GameEventHub.Dispose", e); }
            play = null;
            host = null;
        }

        /// <summary>Call once per frame from the addon, after the sample and the signal provider are updated.</summary>
        public void Tick(float dt, FlightSample s)
        {
            if (host == null || host.settings == null) return;
            if (dt > 0f && !float.IsNaN(dt)) now += dt;
            Settings cfg = host.settings;
            Tuning tu = cfg.tuning;
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (!ReferenceEquals(v, lastVessel)) OnActiveVesselChanged(v);
                lastVessel = v;

                // kerbals of this flight (raw names, the same as ProtoCrewMember.name)
                List<KerbalVoice> voices = host.roster.voices;
                for (int i = 0; i < voices.Count; i++) crewLoss.Know(voices[i].name);
                lastPanic = host.roster.AnyPanic;
                lastPlasma = s.valid && s.inPlasmaStock;

                EventRequest r;
                if (science.Tick(now, tu.scienceCooldown, out r))
                {
                    Log.Info("Event science (from the queue): topic " + r.topic);
                    if (Enabled && cfg.eventScience) Send(r);
                }
                if (partLoss.Tick(now, tu.partLostWindow, tu.partLostCooldown, lastPanic, out r))
                {
                    Log.Info("Event part_lost: " + partLoss.lastCount + " parts");
                    if (cfg.eventPartLoss) Send(r);
                }
                if (crewLoss.Tick(now, tu.crewLostDelay, tu.eventMaxAge, StillLost, out r))
                {
                    Log.Info("Event crew_lost: " + string.Join(", ", crewLoss.lastNames.ToArray()));
                    if (cfg.eventCrewLoss) Send(r);
                }
                if (s.valid && power.Update(s.ecFrac, s.ecMax, s.eva, tu.powerLowFrac, tu.powerOkFrac, tu.powerMinMax, out r))
                {
                    Log.Info("Event " + r.ev + ": EC " + (s.ecFrac * 100.0).ToString("0") + "% of " + s.ecMax.ToString("0"));
                    if (cfg.eventPower) Send(r);
                }
                if (v != null && signalWatch.Update(now, host.signal.connected, lastPlasma, tu.signalHold, tu.signalCooldown, out r))
                {
                    Log.Info("Event " + r.ev);
                    if (cfg.eventSignal) Send(r);
                }
            }
            catch (Exception e) { Log.Exception("GameEventHub.Tick", e); }
        }

        private void OnActiveVesselChanged(Vessel v)
        {
            try
            {
                if (lastVessel != null) powerLowById[lastVessel.id] = power.low;
                bool low;
                power.low = v != null && powerLowById.TryGetValue(v.id, out low) && low;
            }
            catch (Exception) { power.Reset(); }
            // a new vessel has its own link: its first reading sets the state without a call
            signalWatch.Reset();
            // partLoss is kept: the root part blowing up switches the active vessel while the losses still count
        }

        private void Send(EventRequest r)
        {
            sent++;
            lastEvent = r.ev;
            Func<EventRequest, bool> p = play;
            if (p == null) { Log.Debug("Event " + r.ev + " dropped: no director wired"); return; }
            bool ok = false;
            try { ok = p(r); }
            catch (Exception e) { Log.Exception("GameEventHub.play", e); }
            if (ok) accepted++;
            Log.Debug("Event " + r + (ok ? " taken" : " refused"));
        }

        private bool Enabled { get { return host != null && host.settings != null && host.settings.enabled; } }

        private static bool IsActive(Vessel v)
        {
            return v != null && ReferenceEquals(v, FlightGlobals.ActiveVessel);
        }

        // ---- GameEvents handlers ----

        private void OnSoiChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> d)
        {
            try
            {
                if (!Enabled || !host.settings.eventSoi || !IsActive(d.host) || d.to == null) return;
                // no welcome while the crew panics or the ship is in plasma
                if (lastPanic || lastPlasma) { Log.Debug("SOI change to " + d.to.bodyName + " skipped (panic or plasma)"); return; }
                Tuning tu = host.settings.tuning;
                CelestialBody home = FlightGlobals.GetHomeBody();
                bool isHome = home != null && ReferenceEquals(d.to, home);
                EventRequest r;
                if (soi.OnSoiChange(now, d.to.bodyName, FlightPhase.BodyKey(d.to.bodyName), isHome, tu.soiCooldown,
                                    EventWatchers.MaxAgeFor(EventWatchers.SoiEnter, tu.eventMaxAge, tu.eventMaxAge), out r))
                {
                    Log.Info("Event soi_enter: " + (d.from != null ? d.from.bodyName : "?") + " -> " + d.to.bodyName);
                    Send(r);
                }
            }
            catch (Exception e) { Log.Exception("GameEventHub.OnSoiChanged", e); }
        }

        private void OnExperimentDeployed(ScienceData data)
        {
            try
            {
                if (!Enabled || !host.settings.eventScience || data == null) return;
                Vessel v = FlightGlobals.ActiveVessel;   // ScienceData carries no vessel: the active one ran it
                if (v == null) return;
                Tuning tu = host.settings.tuning;
                string bodyKey = v.mainBody != null ? FlightPhase.BodyKey(v.mainBody.bodyName) : FlightPhase.GenericBody;
                EventRequest r;
                if (science.OnExperiment(now, data.subjectID, bodyKey, tu.scienceCooldown, tu.scienceTopicCooldown,
                                         EventWatchers.MaxAgeFor(EventWatchers.Science, tu.eventMaxAge, tu.eventMaxAge), out r))
                {
                    Log.Info("Event science: " + data.subjectID + " -> topic " + r.topic);
                    // runtime voice: an EVA experiment, or any without a link, as a chat with the biome and the body
                    if (host.director != null && host.director.RuntimeScienceTakeover(data.subjectID, data.dataAmount,
                                                                                       v.mainBody != null ? v.mainBody.bodyName : null)) return;
                    Send(r);
                    // experimental runtime voice: sometimes the numbers after the reaction
                    if (host.director != null)
                        host.director.RuntimeScience(data.subjectID, data.dataAmount, v.mainBody != null ? v.mainBody.bodyName : null);
                }
                else Log.Debug("Science " + data.subjectID + (science.QueuedCount > 0 ? " queued or skipped (" + science.QueuedCount + " waiting)" : " skipped (cooldown)"));
            }
            catch (Exception e) { Log.Exception("GameEventHub.OnExperimentDeployed", e); }
        }

        /// <summary>Breaking Ground: a kerbal on EVA deployed a ground science part close by.</summary>
        private void OnGroundPartDeployed(ModuleGroundSciencePart m)
        {
            try
            {
                if (!Enabled || !host.settings.eventScience || m == null || m.part == null || host.director == null) return;
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null || !v.isEVA || m.part.vessel == null) return;
                if (Vector3d.Distance(m.part.vessel.GetWorldPos3D(), v.GetWorldPos3D()) > 50.0) return;
                string kind = m is ModuleGroundExpControl ? "station" : m is ModuleGroundCommsPart ? "comms"
                            : m is ModuleGroundExperiment ? "experiment" : m.PowerUnitsProduced > 0 ? "power" : "part";
                string name = m.part.partInfo != null ? m.part.partInfo.name : m.part.name;
                Log.Info("Event deploy: " + name + " (" + kind + ")");
                // a mod's device row gives its own part a topic and words
                host.director.RuntimeDeploy(RuntimeLines.DeviceWords(PhraseClips.runtimeTemplates, name, kind),
                                            RuntimeLines.DeviceTopic(PhraseClips.runtimeTemplates, name));
            }
            catch (Exception e) { Log.Exception("GameEventHub.OnGroundPartDeployed", e); }
        }

        // experimental runtime voice: Mission Control on a finished or failed contract, only in flight
        private void OnContractCompleted(Contracts.Contract c)
        {
            try
            {
                if (!Enabled || c == null || !HighLogic.LoadedSceneIsFlight || host.director == null) return;
                host.director.RuntimeContract(true, c.FundsCompletion, c.ScienceCompletion, c.ReputationCompletion, c.GetType().Name);
            }
            catch (Exception e) { Log.Exception("GameEventHub.OnContractCompleted", e); }
        }

        private void OnContractFailed(Contracts.Contract c)
        {
            try
            {
                if (!Enabled || c == null || !HighLogic.LoadedSceneIsFlight || host.director == null) return;
                host.director.RuntimeContract(false, Math.Abs(c.FundsFailure), 0, Math.Abs(c.ReputationFailure), c.GetType().Name);
            }
            catch (Exception e) { Log.Exception("GameEventHub.OnContractFailed", e); }
        }

        private void OnPartDie(Part p)
        {
            try
            {
                if (!Enabled || p == null) return;
                Vessel v = p.vessel;
                if (v == null) return;
                if (!IsActive(v) && !ReferenceEquals(v, lastVessel)) return;
                if (v.isEVA) return;   // a kerbal, not a part: crew_lost covers it
                partLoss.OnPartLost(now, 1);
            }
            catch (Exception e) { Log.Exception("GameEventHub.OnPartDie", e); }
        }

        private void OnKerbalStatusChange(ProtoCrewMember pcm, ProtoCrewMember.RosterStatus from, ProtoCrewMember.RosterStatus to)
        {
            try
            {
                if (pcm == null || from == to) return;   // set_rosterStatus fires even when the value stays the same
                if (to != ProtoCrewMember.RosterStatus.Dead && to != ProtoCrewMember.RosterStatus.Missing) return;
                // DeepFreeze makes a frozen kerbal Unowned before it sets Dead: stored, not lost
                if (pcm.type == ProtoCrewMember.KerbalType.Unowned) { Log.Info("Crew frozen or stored, not lost: " + Names.Localize(pcm.name)); return; }
                if (crewLoss.OnLost(now, pcm.name)) Log.Info("Crew lost: " + Names.Localize(pcm.name) + " (" + to + ")");
            }
            catch (Exception e) { Log.Exception("GameEventHub.OnKerbalStatusChange", e); }
        }

        /// <summary>At crew_lost time: the kerbal is still dead or missing, not frozen or thawed in the meantime.
        /// A kerbal the roster does not know counts as lost.</summary>
        private static bool StillLost(string name)
        {
            try
            {
                ProtoCrewMember pcm = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster[name] : null;
                if (pcm == null) return true;
                if (pcm.type == ProtoCrewMember.KerbalType.Unowned) return false;
                return pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing;
            }
            catch (Exception) { return true; }
        }

        /// <summary>DeepFreeze: a frozen kerbal died with the freezer. The roster status stays Dead, so no status change
        /// comes for it.</summary>
        public void OnFrozenKerbalDied(string name)
        {
            if (crewLoss.OnLost(now, name)) Log.Info("Crew lost: " + Names.Localize(name) + " (died frozen)");
        }

        // fallback for a death that does not go through the roster status (sender is the kerbal name)
        private void OnCrewKilled(EventReport rep)
        {
            try
            {
                if (rep == null) return;
                if (crewLoss.OnLost(now, rep.sender)) Log.Info("Crew killed: " + Names.Localize(rep.sender));
            }
            catch (Exception e) { Log.Exception("GameEventHub.OnCrewKilled", e); }
        }
    }
}
#endif
