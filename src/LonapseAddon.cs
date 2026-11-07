// KSPAddon entry point for the flight scene. Owns all state, the crew gate, pause handling and the hotkey.
using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

namespace Lonapse
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class LonapseAddon : MonoBehaviour
    {
        public const string Version = "0.8.1";

        // gate reasons (constant strings, no per-frame allocation)
        public const string GateOpenReason = "open";
        private const string RDisabled = "disabled", RMuted = "muted", RScene = "not flight", RNoVessel = "no vessel",
                             RNotLoaded = "not loaded", REva = "EVA", RNoCrew = "no crew", RWarp = "warp",
                             RPaused = "paused", RDestroyed = "vessel destroyed";

        public Settings settings;
        public StressModel stress;
        public readonly CrewRoster roster = new CrewRoster();
        public FlightSample sample;
        private readonly FlightSampler sampler = new FlightSampler();

        public VoiceChannel[] crew = new VoiceChannel[0];
        public VoiceChannel control, controlBed, staticCh;
        public VoiceChannel crewBed;   // static loop under crew radio lines (radioStatic)
        public VoiceChannel[] beep = new VoiceChannel[0];
        public VoiceChannel[] allChannels = new VoiceChannel[0];

        public ChatterDirector director;
        public RuntimeVoice runtimeVoice;  // lines spoken at runtime by Piper: RuntimeVoice.Shared, kept across scenes
        public BeepDirector beeps;
        public BlackoutMachine blackout;
        private SettingsWindow window;
        private DebugOverlay overlay;
        public MouthDriver mouth;         // talking kerbals (jaw follows the playing voice line)
        public FaceDriver face;           // expression follows the line, head turns to the speaker
        public PortraitRefresh portraits; // portraits rendered again at the end of the frame, after the jaw and head writes
        public GameEventHub events;       // game events to director.PlayEvent
        private DeepFreezeBridge deepFreeze;   // DeepFreeze freeze and thaw as mod events, when it is installed
        /// <summary>A DeepFreeze freeze or thaw is running or its line waits: the director starts no other talk.</summary>
        public bool ModHoldsAir { get { return deepFreeze != null && deepFreeze.Busy; } }
        private bool eventsTickFailed;    // log a failing hub Tick once, never kill Update

        public bool gateOpen;
        public float gateGain;
        public string gateReason = RNoVessel;
        private bool silenced = true;
        public bool paused;
        private bool pauseEventFlag;
        private bool uiHidden;
        public bool settingsDirty;
        public float gameGain = 1f;
        private Vessel lastVessel;
        private Vessel destroyedVessel;   // gate stays closed for this vessel, even within the same frame
        private string silenceReason = "";  // gate reason at the last hard silence (warp end gets a ping)

        public bool stationMode;          // active vessel type is Station or Base
        public bool railsWarp;
        /// <summary>The time warp rate in a rails warp, else 1.</summary>
        public float warpRate = 1f;
        /// <summary>Rails warp with "Chatter during time warp" on: the gate stays open, calm chatter only. Events wait
        /// for the end of the warp, the alarm and the rover moments stay off.</summary>
        public bool warpChatter;
        private bool lastWarpChatter;
        public readonly DockingWatcher docking = new DockingWatcher();
        public readonly DockingGeometry dockGeo = new DockingGeometry();
        public readonly CalloutGate callouts = new CalloutGate();
        public readonly ChuteTracker chutes = new ChuteTracker();
        public VoiceChannel clickCh;
        public VoiceChannel calloutCh;    // callouts never wait for a free crew channel
        public VoiceChannel keyCh;        // 16: radio key-up and key-down (desk click or headset squelch)
        public VoiceChannel breathCh;     // helmet breathing bed on EVA

        // radio profile and signal
        public RadioProfile profile;
        public readonly SignalProvider signal = new SignalProvider();
        public readonly PlanWatcher plan = new PlanWatcher();
        public CountdownPlayer countdown;
        private ThrottleWatch throttle;
        public Subtitles subtitles = new Subtitles();
        public bool evaMode;              // gate open on an EVA kerbal with evaRadio on
        public bool launchPhase;          // from the pad until launchCapAlt (or landed again)
        private readonly RecentMax closing = new RecentMax(0.5f); // closing speed just before contact
        public float lastDockSpeed = -1f;
        private bool hardDockPending;
        // Ducking of the game audio through the listener
        private float listenerBase = -1f, lastWrittenListener = -1f;
        public float duckGain = 1f;
        private int duckMisses;
        private bool duckDisabled;
        private bool lastSubtitles;

        // probe mode: an uncrewed vessel with a probe core (minimumCrew 0) opens the gate in probe mode
        public bool probeMode;
        public bool frozenShip;           // nobody awake aboard, DeepFreeze has them frozen: only Mission Control talks (mod events, crew_lost)
        public bool touristCabin;         // probe mode with only tourists aboard: the probe flies, the tourists talk among themselves and to it
        public string probeCoreKey;       // ProbeCores key of the root-most probe core, null outside probe mode
        public ProbeSleepState probeSleepState = ProbeSleepState.Awake;
        private readonly ProbeSleep probeSleep = new ProbeSleep();
        // Milestones (session memory per vessel), rover moments and data beeps of the probe
        private readonly ProbeMilestones milestones = new ProbeMilestones();
        private readonly RoverMoments roverMoments = new RoverMoments();
        private readonly DataBeeps dataBeeps = new DataBeeps();
        private float probeMomentPoll;
        public double missionTime;        // s, active vessel mission time
        public double dayLength = ProbeRules.KerbinDay;    // seconds per day on the game clock
        private readonly List<ModuleCommand> probeCmds = new List<ModuleCommand>();
        private readonly List<string> probeNames = new List<string>();
        // probe voices: every core part root first, the voices that exist, and the result of the pick
        private readonly List<ProbeCorePart> probeCoreParts = new List<ProbeCorePart>();
        private static List<ProbeVoiceSpec> configVoices;   // LONAPSE_PROBE_VOICE nodes, read once per game session
        public ProbeVoiceRegistry probeVoices = new ProbeVoiceRegistry();

        // line memory: one for the game session, read from and written to PluginData/played.txt
        private static SceneMemory lineMemory;
        public static SceneMemory LineMemory { get { return lineMemory ?? (lineMemory = LoadLineMemory()); } }

        private static string LineMemoryPath
        {
            get { return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Settings.FilePath), "played.txt"); }
        }

        private static SceneMemory LoadLineMemory()
        {
            try
            {
                string p = LineMemoryPath;
                if (System.IO.File.Exists(p))
                {
                    SceneMemory m = SceneMemory.Parse(System.IO.File.ReadAllLines(p));
                    Log.Info("Line memory: " + m.Count + " scenes from " + p);
                    return m;
                }
            }
            catch (Exception e) { Log.Warn("Line memory could not be read, starting fresh: " + e.Message); }
            return new SceneMemory();
        }

        public static void SaveLineMemory()
        {
            if (lineMemory == null) return;
            try
            {
                string p = LineMemoryPath;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p));
                string tmp = p + ".tmp";
                System.IO.File.WriteAllLines(tmp, lineMemory.Serialize().ToArray());
                if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
                System.IO.File.Move(tmp, p);
            }
            catch (Exception e) { Log.Warn("Line memory could not be saved: " + e.Message); }
        }
        public ProbeVoiceSpec probeVoice;                    // voice of the active probe, null without a core
        public string probeVoicePart;                        // part name of the core that decided it
        public string probeModuleName;                       // ModuleLonapse.displayName of that core, null without one
        public ProbeVoicePick.Why probeVoiceWhy;
        public string probeDefaultVoice;                     // what the pick gives without the player's own picks
        public ProbeVoicePick.Why probeDefaultWhy;
        private readonly List<int> probeStates = new List<int>();
        private readonly List<bool> probeHib = new List<bool>();
        private Vessel probeScanVessel;
        private int probeScanParts = -1;
        private bool probeDirty = true;
        private float probePollLeft;

        public VoiceChannel alarmCh;      // soft master alarm while a kerbal panics, far in the background
        private const float DockIntentRange = 600f;   // m, the port signs are read inside the furthest far callout
        /// <summary>An approach is on (from the approach call): other chatter waits.</summary>
        public bool DockingHoldsAir { get { return docking != null && settings != null && docking.HoldsChat(settings.tuning); } }
        private Vessel dockLogTarget;
        private bool dockEnterLogged;
        public int DockIntentFlags { get { return dockIntent; } }
        private int dockIntent;        // DockIntent flags of the nearest port pair, 0 beyond the approach distance
        public VoiceChannel sfxCh;        // sound effects of the ship itself: airlock on dock and undock, no radio filtering
        private float alarmHoldLeft;

        /// <summary>Extra answer delay far from Kerbin: lightDelayScale * log10(1 + km / 10000), capped.</summary>
        public float LightDelay
        {
            get
            {
                if (!settings.lightDelay) return 0f;
                Tuning tu = settings.tuning;
                return (float)Math.Min(tu.lightDelayMax, tu.lightDelayScale * Math.Log10(1.0 + signal.homeKm / 10000.0));
            }
        }

        /// <summary>Strength of MC dropouts: none at signal 0.6 or better, full at 0.</summary>
        public float Dropout
        {
            get
            {
                if (!settings.signalDropouts || settings.signalMode == "off") return 0f;
                return DcMath.Clamp01((0.6f - signal.signal) / 0.6f);
            }
        }

        /// <summary>Static level from the signal chain, 0 at full signal.</summary>
        public float StaticLevel { get { return signal.StaticLevel(settings); } }
        private float chutePollTimer;
        private int lastPolledStage = int.MinValue;
        private float lastStageEventTime = -999f;
        private bool started;
        /// <summary>True after Start succeeded (MouthDriver stays idle before that).</summary>
        public bool Started { get { return started; } }

        private KeyCode muteKey = KeyCode.Y;
        private KeyCode muteModifier = KeyCode.LeftAlt;
        private KeyCode countdownKey = KeyCode.C;
        private KeyCode countdownModifier = KeyCode.LeftAlt;

        private System.Random rng;

        /// <summary>
        /// The single condition every code path checks before it starts any sound.
        /// Gate open means: enabled, not muted, flight scene, loaded non-EVA active vessel with crew, warp ok.
        /// </summary>
        public bool CanStartAudio
        {
            get { return started && gateOpen && !paused && settings != null; }
        }

        // ---------------------------------------------------------------- lifecycle

        /// <summary>The running flight addon, for LonapseApi. Null outside flight.</summary>
        internal static LonapseAddon Instance;

        private void Awake()
        {
            Instance = this;
            Log.SetMainThread();
            try
            {
                settings = Settings.Load();
                if (settings.migrated) { settings.migrated = false; settings.Save(); } // write the new keys once
                Log.verbose = settings.verboseLog;
                RefreshKeys();
            }
            catch (Exception e)
            {
                Log.Exception("Awake", e);
                if (settings == null) settings = new Settings();
            }
        }

        /// <summary>The mod before the rename (GameData/Quindar, Quindar.dll) is still installed. Both would play at once.</summary>
        private static bool OldInstallLoaded()
        {
            try
            {
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                    if (a != null && a.name == "Quindar") return true;
            }
            catch (Exception) { }
            return false;
        }

        private void Start()
        {
            if (OldInstallLoaded())
            {
                // stay silent: the old copy runs its own audio. started stays false, so nothing plays from here.
                Log.Error("GameData/Quindar (the old name of this mod) is still installed. Delete that folder: Lonapse stays off until then.");
                try { ScreenMessages.PostScreenMessage("Lonapse: delete GameData/Quindar (old version), Lonapse is off until then", 15f); } catch (Exception) { }
                return;
            }
            try
            {
                rng = new System.Random(Environment.TickCount);
                Log.Info("Lonapse " + Version + " starting, settings: " + Settings.FilePath);
                ClipLibrary.Build();
                PhraseClips.SetMutedMods(settings.mutedMods);
                PhraseClips.Build();
                RebuildProbeVoices();

                stress = new StressModel(settings.tuning);
                ReadPhysicsGlobals();

                CreateChannels();
                Tuning tu0 = settings.tuning;
                throttle = new ThrottleWatch(tu0.throttleFullAt, tu0.throttleCutAt, 0.5f, 0.3f, tu0.throttleMinGap, 0.5f);
                roster.genderPitchFemale = tu0.genderPitchFemale;
                roster.genderPitchMale = tu0.genderPitchMale;
                roster.personalityEffects = settings.personalityEffects;
                director = new ChatterDirector(this, rng);
                beeps = new BeepDirector(this, rng);
                countdown = new CountdownPlayer(this);
                blackout = new BlackoutMachine(this, rng);
                window = new SettingsWindow(this);
                overlay = new DebugOverlay(this);
                mouth = gameObject.AddComponent<MouthDriver>();
                mouth.host = this;
                face = gameObject.AddComponent<FaceDriver>();
                face.host = this;
                portraits = gameObject.AddComponent<PortraitRefresh>();
                events = new GameEventHub();
                events.Init(this);
                events.play = director.PlayEvent;
                deepFreeze = new DeepFreezeBridge();
                deepFreeze.Init(this);

                LogGainInputs();
                Subscribe();
                if (ApplicationLauncher.Ready) window.CreateButton();
                started = true;
                Log.Info("Started. Mute hotkey: " + HotkeyText);
                LogBuildIdentity();
            }
            catch (Exception e)
            {
                started = false;
                Log.Error("Start failed, plugin stays silent: " + e);
            }
        }

        private void OnDestroy()
        {
            Log.Flush();
            if (ReferenceEquals(Instance, this)) Instance = null;
            started = false;
            gateOpen = false;
            RestoreListener();
            // the Piper processes stay for the next flight (RuntimeVoiceHost), only this flight's callbacks go
            try { if (runtimeVoice != null) runtimeVoice.DropCallbacks(); runtimeVoice = null; }
            catch (Exception e) { Log.Exception("OnDestroy.runtimeVoice", e); }
            try
            {
                for (int i = 0; i < allChannels.Length; i++) if (allChannels[i] != null) allChannels[i].Destroy();
            }
            catch (Exception e) { Log.Exception("OnDestroy.channels", e); }
            try { Unsubscribe(); } catch (Exception e) { Log.Exception("OnDestroy.events", e); }
            try { if (events != null) events.Dispose(); } catch (Exception e) { Log.Exception("OnDestroy.eventHub", e); }
            try { if (deepFreeze != null) deepFreeze.Dispose(); } catch (Exception e) { Log.Exception("OnDestroy.deepFreeze", e); }
            try { if (window != null) { window.ReleaseLock(); window.RemoveButton(); } } catch (Exception e) { Log.Exception("OnDestroy.button", e); }
            try { InputLockManager.RemoveControlLock("Lonapse_window"); } catch (Exception) { }
            try { if (settingsDirty && settings != null) settings.Save(); } catch (Exception e) { Log.Exception("OnDestroy.save", e); }
            try { SaveLineMemory(); } catch (Exception e) { Log.Exception("OnDestroy.lineMemory", e); }
        }

        private void CreateChannels()
        {
            Tuning tu = settings.tuning;
            Transform t = transform;
            crew = new VoiceChannel[3];
            for (int i = 0; i < 3; i++)
                crew[i] = new VoiceChannel(t, "crew" + i, ChannelKind.Crew, 64, tu.crewHp, tu.crewLpCalm, 0f, true);
            control = new VoiceChannel(t, "control", ChannelKind.Control, 80, tu.controlHp, tu.controlLp, tu.controlDistortion, false);
            controlBed = new VoiceChannel(t, "controlBed", ChannelKind.ControlBed, 100, 300f, 3500f, 0.2f, false);
            beep = new VoiceChannel[2];
            for (int i = 0; i < 2; i++)
                beep[i] = new VoiceChannel(t, "beep" + i, ChannelKind.Beep, 100, 250f, 6000f, 0.08f, false);
            staticCh = new VoiceChannel(t, "static", ChannelKind.Static, 100, 200f, 5000f, 0.4f, false);
            crewBed = new VoiceChannel(t, "crewBed", ChannelKind.Static, 100, 300f, 3500f, 0.2f, false);
            // dedicated intercom click source, so a click is never skipped when crew channels are busy
            clickCh = new VoiceChannel(t, "intercomClick", ChannelKind.IntercomClick, 90, 150f, 9000f, 0f, false);
            calloutCh = new VoiceChannel(t, "callout", ChannelKind.Crew, 40, tu.crewHp, tu.crewLpCalm, 0f, true);
            keyCh = new VoiceChannel(t, "radioKey", ChannelKind.Static, 70, 200f, 8000f, 0f, false);
            breathCh = new VoiceChannel(t, "breath", ChannelKind.Breath, 110, 100f, 4000f, 0f, false);
            alarmCh = new VoiceChannel(t, "alarm", ChannelKind.Breath, 125, 300f, 3500f, 0f, false);
            sfxCh = new VoiceChannel(t, "sfx", ChannelKind.Sfx, 95, 10f, 22000f, 0f, false);
            allChannels = new VoiceChannel[] { crew[0], crew[1], crew[2], control, controlBed, beep[0], beep[1], staticCh, crewBed, clickCh, calloutCh, keyCh, breathCh, alarmCh, sfxCh };
        }

        private void ReadPhysicsGlobals()
        {
            try
            {
                stress.qvMin = PhysicsGlobals.CommNetQTimesVelForBlackoutMin;
                stress.qvMax = PhysicsGlobals.CommNetQTimesVelForBlackoutMax;
                stress.tempForBlackout = PhysicsGlobals.CommNetTempForBlackout;
                stress.densityForBlackout = PhysicsGlobals.CommNetDensityForBlackout;
            }
            catch (Exception e)
            {
                Log.Warn("PhysicsGlobals blackout thresholds unavailable (" + e.Message + "), using Physics.cfg defaults");
            }
            Log.Info("Blackout thresholds: qv " + stress.qvMin + ".." + stress.qvMax + ", temp " + stress.tempForBlackout + " K, density " + stress.densityForBlackout);
        }

        /// <summary>Logs which DLL build is loaded, so a swapped file on disk shows up in KSP.log.</summary>
        private static bool buildIdentityLogged;
        private void LogBuildIdentity()
        {
            if (buildIdentityLogged) return;
            buildIdentityLogged = true;
            try
            {
                var asm = typeof(LonapseAddon).Assembly;
                string path = asm.Location;
                string stamp = string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)
                    ? "unknown" : System.IO.File.GetLastWriteTimeUtc(path).ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
                Log.Info("Loaded build " + asm.ManifestModule.ModuleVersionId + ", DLL written " + stamp);
            }
            catch (Exception e) { Log.Warn("Could not read build identity: " + e.GetType().Name); }
        }

        private void LogGainInputs()
        {
            try
            {
                Log.Info("Gain inputs: AudioListener.volume " + AudioListener.volume.ToString("0.00") +
                         ", MASTER_VOLUME " + GameSettings.MASTER_VOLUME.ToString("0.00") +
                         ", VOICE_VOLUME " + GameSettings.VOICE_VOLUME.ToString("0.00") +
                         ", useGameVolume " + settings.useGameVolume);
            }
            catch (Exception e) { Log.Exception("LogGainInputs", e); }
        }

        public void RefreshKeys()
        {
            muteKey = settings.MuteKeyCode;
            muteModifier = settings.MuteModifierCode;
            countdownKey = settings.CountdownKeyCode;
            countdownModifier = settings.CountdownModifierCode;
        }

        public string CountdownKeyText
        {
            get { return countdownKey == KeyCode.None ? "none" : (countdownModifier == KeyCode.None ? "" : countdownModifier + "+") + countdownKey; }
        }

        public string HotkeyText
        {
            get { return (muteModifier == KeyCode.None ? "" : muteModifier + "+") + muteKey; }
        }

        // ---------------------------------------------------------------- events

        private void Subscribe()
        {
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onVesselCrewWasModified.Add(OnVesselCrewWasModified);
            GameEvents.onVesselWasModified.Add(OnVesselWasModified);
            GameEvents.onPartUndock.Add(OnPartUndock);
            GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails);
            GameEvents.onCrewOnEva.Add(OnCrewMoved);
            GameEvents.onCrewOnEva.Add(OnCrewOnEva);
            GameEvents.afterFlagPlanted.Add(OnFlagPlanted);
            GameEvents.onCrewBoardVessel.Add(OnCrewMoved);
            GameEvents.onPartCouple.Add(OnPartCouple);
            GameEvents.onDockingComplete.Add(OnDockingComplete);
            GameEvents.onCrewTransferred.Add(OnCrewTransferred);
            GameEvents.onStageActivate.Add(OnStageActivate);
            GameEvents.onVesselSituationChange.Add(OnSituationChange);
            GameEvents.onPartDie.Add(OnPartDie);
            GameEvents.onGamePause.Add(OnGamePause);
            GameEvents.onGameUnpause.Add(OnGameUnpause);
            GameEvents.onHideUI.Add(OnHideUI);
            GameEvents.onShowUI.Add(OnShowUI);
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnAppLauncherDestroyed);
        }

        private void Unsubscribe()
        {
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onVesselCrewWasModified.Remove(OnVesselCrewWasModified);
            GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
            GameEvents.onPartUndock.Remove(OnPartUndock);
            GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);
            GameEvents.onCrewOnEva.Remove(OnCrewMoved);
            GameEvents.onCrewOnEva.Remove(OnCrewOnEva);
            GameEvents.afterFlagPlanted.Remove(OnFlagPlanted);
            GameEvents.onCrewBoardVessel.Remove(OnCrewMoved);
            GameEvents.onPartCouple.Remove(OnPartCouple);
            GameEvents.onDockingComplete.Remove(OnDockingComplete);
            GameEvents.onCrewTransferred.Remove(OnCrewTransferred);
            GameEvents.onStageActivate.Remove(OnStageActivate);
            GameEvents.onVesselSituationChange.Remove(OnSituationChange);
            GameEvents.onPartDie.Remove(OnPartDie);
            GameEvents.onGamePause.Remove(OnGamePause);
            GameEvents.onGameUnpause.Remove(OnGameUnpause);
            GameEvents.onHideUI.Remove(OnHideUI);
            GameEvents.onShowUI.Remove(OnShowUI);
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnAppLauncherDestroyed);
        }

        private void OnVesselChange(Vessel v) { roster.dirty = true; }
        private void OnVesselCrewWasModified(Vessel v) { roster.dirty = true; }
        private void OnVesselWasModified(Vessel v) { roster.dirty = true; dockGeo.dirty = true; chutes.dirty = true; }

        private void OnPartUndock(Part p)
        {
            roster.dirty = true;
            dockGeo.dirty = true;
            // moving away from the ship we just left must not trigger an approach call
            chutes.dirty = true;
            try
            {
                if (p != null && ReferenceEquals(p.vessel, FlightGlobals.ActiveVessel))
                {
                    docking.Reset();
                    Log.Info("Undocked: " + (p.partInfo != null ? p.partInfo.title : p.name));
                    PlaySfx("sfx/airlock_undock");
                    director.NoteUndock();
                }
            }
            catch (Exception e) { Log.Exception("OnPartUndock", e); }
        }
        private void OnCrewMoved(GameEvents.FromToAction<Part, Part> a) { roster.dirty = true; }

        /// <summary>One sound effect of the ship on the sfx channel. Same gate as every other sound, and the setting.</summary>
        private void PlaySfx(string folder)
        {
            if (!CanStartAudio || !settings.dockingSfx) return;
            AudioClip c = ClipLibrary.Next(folder, rng);
            if (c == null) return;
            sfxCh.Play(c, 1f, 1f, false, 0f);
            Log.Info("Sound effect: " + folder);
        }

        /// <summary>A kerbal goes out: the director keeps the time, for the lines that fit only the start of an EVA.</summary>
        private void OnCrewOnEva(GameEvents.FromToAction<Part, Part> a)
        {
            try
            {
                Vessel ev = a.to != null ? a.to.vessel : null;
                if (ev == null || director == null) return;
                foreach (ProtoCrewMember pcm in ev.GetVesselCrew()) director.NoteEvaStart(pcm.name);
            }
            catch (Exception e) { Log.Exception("OnCrewOnEva", e); }
        }

        /// <summary>A flag stands: Mission Control and the kerbal who planted it have something to say.</summary>
        private void OnFlagPlanted(FlagSite f)
        {
            try
            {
                if (!started || f == null || director == null) return;
                string flag = f.vessel != null ? f.vessel.vesselName : null;
                Log.Info("Flag planted by " + (f.placedBy ?? "?") + ": " + (flag ?? "?"));
                director.RuntimeFlag(f.placedBy, flag);
            }
            catch (Exception e) { Log.Exception("OnFlagPlanted", e); }
        }
        private bool InvolvesActive(GameEvents.FromToAction<Part, Part> a)
        {
            Vessel av = FlightGlobals.ActiveVessel;
            if (av == null) return false;
            return (a.from != null && ReferenceEquals(a.from.vessel, av)) || (a.to != null && ReferenceEquals(a.to.vessel, av));
        }

        /// <summary>Contact (fires before the merge): the roster changes and the jolt must not count as stress.</summary>
        private void OnPartCouple(GameEvents.FromToAction<Part, Part> a)
        {
            roster.dirty = true;
            dockGeo.dirty = true;
            try
            {
                if (!started || !InvolvesActive(a)) return;
                PlaySfx("sfx/airlock_dock");
                Tuning tu = settings.tuning;
                // The closing speed of the last half second decides between soft capture and hard dock
                lastDockSpeed = closing.Max;
                float imp = StressModel.DockImpulse(lastDockSpeed, tu.dockSoftSpeed, tu.dockHardSpeed, tu.impulseDockHard);
                stress.QuietMotion(tu.dockQuietTime);
                hardDockPending = lastDockSpeed >= tu.dockHardSpeed;
                if (imp > 0f)
                {
                    stress.AddImpulse(imp, true); // critical: passes the docking quiet window
                    Log.Info("Dock contact at " + lastDockSpeed.ToString("0.00") + " m/s, impulse " + imp.ToString("0.00") + (hardDockPending ? " (hard dock)" : ""));
                }
            }
            catch (Exception e) { Log.Exception("OnPartCouple", e); }
        }

        private void OnDockingComplete(GameEvents.FromToAction<Part, Part> a)
        {
            roster.dirty = true;
            dockGeo.dirty = true;
            try
            {
                if (!started || !InvolvesActive(a)) return;
                stress.QuietMotion(settings.tuning.dockQuietTime);
                docking.OnDocked();
                Log.Info("Docking complete");
                director.EnqueueDocked(hardDockPending); // checks CanStartAudio itself
                hardDockPending = false;
            }
            catch (Exception e) { Log.Exception("OnDockingComplete", e); }
        }

        private void OnCrewTransferred(GameEvents.HostedFromToAction<ProtoCrewMember, Part> a) { roster.dirty = true; }

        private void OnVesselWillDestroy(Vessel v)
        {
            try
            {
                if (v != null && ReferenceEquals(v, FlightGlobals.ActiveVessel))
                {
                    destroyedVessel = v;
                    gateOpen = false;
                    gateReason = RDestroyed;
                    HardSilence(RDestroyed);
                }
            }
            catch (Exception e) { Log.Exception("OnVesselWillDestroy", e); }
        }

        private void OnVesselGoOffRails(Vessel v)
        {
            try { if (stress != null && ReferenceEquals(v, FlightGlobals.ActiveVessel)) stress.StartWarmup(); }
            catch (Exception e) { Log.Exception("OnVesselGoOffRails", e); }
        }

        private void OnStageActivate(int stage)
        {
            try
            {
                if (!started) return;
                stress.AddImpulse(settings.tuning.impulseStage);
                lastStageEventTime = Time.unscaledTime;
                beeps.OnStage(stage, null); // checks the gate itself and logs every outcome
            }
            catch (Exception e) { Log.Exception("OnStageActivate", e); }
        }

        private bool evaFallSeen;   // this ragdoll episode already had its eva_fall lines
        private float lastEvaFall = -1e9f;

        /// <summary>An EVA kerbal that ragdolls on the ground (a trip, a fall, a hard landing) gets eva_fall
        /// lines instead of the touchdown lines. Once per ragdoll episode, at most once per evaFallCooldown s.</summary>
        private void PollEvaFall()
        {
            if (!evaMode || !sample.valid || !sample.evaRagdoll) { evaFallSeen = false; return; }
            if (evaFallSeen || !sample.landed) return;
            evaFallSeen = true;
            float now = Time.unscaledTime;
            if (now - lastEvaFall < settings.tuning.evaFallCooldown) return;
            if (director.EnqueueEvaFall()) lastEvaFall = now;
        }

        private void OnSituationChange(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> a)
        {
            try
            {
                if (!started || a.host == null || !ReferenceEquals(a.host, FlightGlobals.ActiveVessel)) return;
                Vessel.Situations from = a.from, to = a.to;
                if (from == Vessel.Situations.PRELAUNCH) launchPhase = true;
                if (to == Vessel.Situations.LANDED || to == Vessel.Situations.SPLASHED) launchPhase = false;
                if ((from == Vessel.Situations.PRELAUNCH || from == Vessel.Situations.LANDED || from == Vessel.Situations.SPLASHED)
                    && to == Vessel.Situations.FLYING && !a.host.isEVA) // an EVA hop is no liftoff
                    stress.AddImpulse(settings.tuning.impulseLiftoff);
                // an EVA kerbal goes LANDED and FLYING on every hop: no touchdown there, a fall has its own lines
                if ((to == Vessel.Situations.LANDED || to == Vessel.Situations.SPLASHED) && !a.host.isEVA
                    && (from == Vessel.Situations.FLYING || from == Vessel.Situations.SUB_ORBITAL))
                    beeps.OnTouchdown(to == Vessel.Situations.SPLASHED); // checks CanStartAudio itself
            }
            catch (Exception e) { Log.Exception("OnSituationChange", e); }
        }

        private void OnPartDie(Part p)
        {
            try
            {
                if (!started || p == null) return;
                Vessel pv = p.vessel;
                if (pv != null && ReferenceEquals(pv, FlightGlobals.ActiveVessel))
                {
                    // part deaths always count, also inside the docking quiet window
                    bool inDocking = stress.motionQuietLeft > 0f || docking.InGuard(settings.tuning);
                    stress.AddImpulse(settings.tuning.impulsePartDie, true);
                    if (inDocking)
                    {
                        // a couple that breaks parts is not gentle: stop hiding motion right away
                        stress.EndQuietMotion();
                        docking.BreakGuard();
                        Log.Info("Part destroyed during docking, docking guard ended");
                    }
                }
            }
            catch (Exception e) { Log.Exception("OnPartDie", e); }
        }

        private void OnGamePause() { pauseEventFlag = true; }
        private void OnGameUnpause() { pauseEventFlag = false; }
        private void OnHideUI() { uiHidden = true; }
        private void OnShowUI() { uiHidden = false; }

        private void OnAppLauncherReady()
        {
            try { if (window != null) window.CreateButton(); }
            catch (Exception e) { Log.Exception("OnAppLauncherReady", e); }
        }

        private void OnAppLauncherDestroyed()
        {
            try { if (window != null) window.RemoveButton(); }
            catch (Exception e) { Log.Exception("OnAppLauncherDestroyed", e); }
        }

        // ---------------------------------------------------------------- physics tick

        private void FixedUpdate()
        {
            if (!started) return;
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null || !v.loaded) { sample.valid = false; return; }
                float dt = Time.fixedDeltaTime;
                // stress time constants are in real seconds: one physics tick covers fixedDeltaTime / timeScale
                // real seconds, so physics warp does not speed up attack, hold, decay, impulse decay or warmup
                float dtReal = dt / Mathf.Max(Time.timeScale, 1e-3f);
                sampler.Sample(v, dt, settings.tuning, ref sample);
                if (!settings.roverStress) sample.rover = false;
                if (sample.valid) stress.Tick(ref sample, dtReal);
            }
            catch (Exception e) { Log.Exception("FixedUpdate", e); }
        }

        // ---------------------------------------------------------------- frame

        private void Update()
        {
            Log.Flush(); // the lines the Piper threads logged
            if (!started) return;
            try
            {
                float rawDt = Math.Max(0f, Time.unscaledDeltaTime); // unclamped: the gate fade must never take longer than gateFadeTime
                float dt = Math.Min(rawDt, 0.1f);
                railsWarp = ReadRailsWarp(); // every frame, for the gate and the overlay
                try { warpRate = railsWarp ? TimeWarp.CurrentRate : 1f; } catch (Exception) { warpRate = 1f; }
                HandleHotkey();
                if (runtimeVoice != null) runtimeVoice.Tick();
                if (window != null && (!window.Visible || uiHidden)) window.ReleaseLock();

                Vessel v = FlightGlobals.ActiveVessel;
                bool vesselChanged = false;
                if (!ReferenceEquals(v, lastVessel))
                {
                    vesselChanged = true;
                    bool evaHop = IsEvaHop(lastVessel, v);
                    lastVessel = v;
                    docking.Reset();
                    dockGeo.dirty = true;
                    chutes.dirty = true;
                    lastPolledStage = SafeStage(v);
                    pollDropFrom = int.MinValue;
                    scanParts = scanIgnited = -1;
                    stress.ResetAll();
                    stress.StartWarmup();
                    roster.dirty = true;
                    director.ResetPacing(evaHop);
                    evaScanLeft = 0f;   // the group of the new vessel at once
                    if (throttle != null) throttle.Reset();
                    if (countdown != null) countdown.Abort("vessel change");
                    plan.Reset();
                    signal.Reset();
                    closing.Clear();
                    hardDockPending = false;
                    // subtitles stay: a line that is still playing keeps its subtitle after a switch
                    alarmCh.FadeOutOnce(0.5f);
                    alarmHoldLeft = 0f;
                    try { launchPhase = v != null && v.situation == Vessel.Situations.PRELAUNCH; } catch (Exception) { launchPhase = false; }
                    Log.Info("Active vessel: " + Names.Of(v));
                }
                // decouple/undock does not always fire a crew event, and onPartCouple fires before the merge,
                // so compare counts every frame as well
                ScanEvaGroup(v, dt);
                try { if (v != null && v.GetCrewCount() + GroupCrew() != roster.GroupCount) roster.dirty = true; }
                catch (Exception) { roster.dirty = true; }
                if (vesselChanged) { probeSleep.Reset(); probeSleepState = ProbeSleepState.Awake; roverMoments.Reset(); dataBeeps.Reset(); }
                UpdateProbeScan(v, vesselChanged || roster.dirty, dt);
                if (roster.dirty)
                {
                    // roster.dirty also fires on onVesselWasModified (staging, decouple, part loss). The mouth rigs
                    // are only re-resolved on a vessel change or a real crew change. A rig whose bones were destroyed
                    // is dropped by MouthDriver.GetRig and the Tick loop on their own.
                    bool crewChanged = roster.Rebuild(v, evaNearby.Count > 0 ? evaNearby : null, radioNearby.Count > 0 ? radioNearby : null);
                    if (vesselChanged || crewChanged)
                    {
                        if (mouth != null) mouth.CrewChanged();
                        if (face != null) face.CrewChanged();
                    }
                }
                // persona switch: the settings window writes the bool, the roster applies it to every voice
                if (settings.personalityEffects != roster.personalityEffects) roster.SetPersonalityEffects(settings.personalityEffects);

                bool nowPaused = IsPaused();
                if (nowPaused != paused)
                {
                    paused = nowPaused;
                    for (int i = 0; i < allChannels.Length; i++)
                    {
                        if (paused) allChannels[i].Pause(); else allChannels[i].UnPause();
                    }
                    Log.Debug(paused ? "Paused" : "Unpaused");
                }
                if (paused)
                {
                    RestoreListener(); // the pause menu owns the master volume
                    // everything frozen: sources are paused, directors do not run
                    TickEvents(0f); // handlers that fire during the pause still see a live hub, its clock stands still
                    if (overlay != null) overlay.Tick(dt);
                    return;
                }

                ReadListenerBase();
                gameGain = ComputeGameGain();
                UpdateStationMode(v);
                UpdateDocking(v, dt);

                string reason = EvaluateGate(v);
                bool open = ReferenceEquals(reason, GateOpenReason);
                sample.probeCoreKey = probeMode ? probeCoreKey : null;
                sample.probeAsleep = probeMode && probeSleepState == ProbeSleepState.Asleep;
                if (!ReferenceEquals(reason, gateReason))
                    Log.Info(open ? "Gate open" : "Gate closed: " + reason);
                gateReason = reason;
                if (warpChatter != lastWarpChatter)
                {
                    lastWarpChatter = warpChatter;
                    Log.Info(warpChatter ? "Time warp: calm chatter goes on, events wait" : "Time warp chatter ended");
                }
                if (open && silenced)
                {
                    silenced = false;
                    if (ReferenceEquals(silenceReason, RWarp))
                    {
                        // radio back after rails warp: one short ping instead of the usual silent holdoff
                        director.OnGateOpenedAfterWarp();
                        beeps.OnGateOpenedAfterWarp(sample.situation);
                    }
                    else
                    {
                        director.OnGateOpened();
                        beeps.OnGateOpened(sample.situation);
                    }
                }
                gateOpen = open;

                float fade = Math.Max(0.01f, settings.tuning.gateFadeTime);
                if (open) gateGain = Math.Min(1f, gateGain + rawDt / fade);
                else gateGain = Math.Max(0f, gateGain - rawDt / fade);
                if (!open && gateGain <= 0f && !silenced) HardSilence(reason);
                else if (!open && silenced) silenceReason = reason; // latest closing reason decides the reopen behaviour

                UpdateLaunchCap();
                roster.UpdateStress(stress.stress);
                profile = RadioProfiles.Select(stationMode, evaMode, settings.radioProfile);
                signal.Update(dt, v, settings);
                // after the sample and the signal. Also with the gate closed, so the crew_lost delay runs after the
                // active vessel is destroyed (the director keeps the pending call until the gate opens again)
                TickEvents(dt);
                // the whole crew is gone: a pending crew_lost may still play its MC call with the gate closed
                if (!gateOpen) director.UpdateClosedGate(silenced && FinalCallAllowed(reason));
                if (probeMode) UpdateProbeMoments(v, dt);
                if (settings.subtitles != lastSubtitles) { lastSubtitles = settings.subtitles; if (!lastSubtitles) subtitles.Clear(); }

                PollStage(v);
                PollChutes(v, dt);
                PollThrottle(v, dt);
                countdown.Update(dt, v);
                PollPlan(v, dt);

                if (gateOpen && frozenShip) director.UpdateFrozenShip(dt);
                else if (gateOpen)
                {
                    blackout.Update(dt);
                    director.Update(dt);
                    beeps.Update(dt, ref sample);
                    PollEvaFall();
                }

                UpdateBreath(dt);
                UpdateAlarm(dt);
                EnforceCategoryToggles();
                UpdateChannels(dt);
                UpdateDuck(dt);
                if (overlay != null) overlay.Tick(dt);
            }
            catch (Exception e)
            {
                Log.Exception("Update", e);
                RestoreListener(); // never leave the game audio ducked after an error
            }
        }

        /// <summary>
        /// Gate reasons that let a pending crew_lost play its one MC line while the gate is closed: the crew or the
        /// vessel is gone. Not for pause, warp, a scene change, EVA, an unloaded vessel, mute or disabled.
        /// </summary>
        private static bool FinalCallAllowed(string reason)
        {
            return ReferenceEquals(reason, RNoCrew) || ReferenceEquals(reason, RDestroyed) || ReferenceEquals(reason, RNoVessel);
        }

        private void TickEvents(float dt)
        {
            if (events == null) return;
            try { if (deepFreeze != null) deepFreeze.Tick(dt); } catch (Exception e) { Log.Exception("DeepFreezeBridge.Tick", e); }
            try { events.Tick(dt, sample); }
            catch (Exception e)
            {
                if (!eventsTickFailed) { eventsTickFailed = true; Log.Exception("GameEventHub.Tick (logged once)", e); }
            }
        }

        private void UpdateChannels(float dt)
        {
            float globalMul = gateGain * settings.masterVolume * gameGain * (evaMode ? settings.evaRadioVolume : 1f);
            if (!gateOpen && silenced) globalMul = 0f;
            // crew_lost final call with the gate closed: only the control channel, at full gate gain
            bool finalCall = !gateOpen && director != null && director.FinalCallActive;
            for (int i = 0; i < allChannels.Length; i++)
            {
                VoiceChannel c = allChannels[i];
                c.SetDsp(settings.useDsp);
                float mul = finalCall && ReferenceEquals(c, control) ? settings.masterVolume * gameGain : globalMul;
                c.Update(dt, CategoryVolume(c), mul);
            }
        }

        private float CategoryVolume(VoiceChannel c)
        {
            switch (c.kind)
            {
                case ChannelKind.Crew: return c.intercom ? settings.intercomVolume : settings.crewVolume;
                case ChannelKind.IntercomClick: return settings.intercomVolume;
                case ChannelKind.Control:
                case ChannelKind.ControlBed: return settings.controlVolume;
                case ChannelKind.Beep: return settings.beepVolume;
                case ChannelKind.Breath: return settings.crewVolume;
                case ChannelKind.Sfx: return settings.sfxVolume;
                default: return settings.staticVolume;
            }
        }

        // ---------------------------------------------------------------- probe mode

        /// <summary>
        /// Finds the probe cores (ModuleCommand with minimumCrew 0) root-first on a vessel or part count change or a
        /// roster change, and polls their state at 4 Hz for the sleep state. probeCoreKey is null without a core.
        /// </summary>
        private void UpdateProbeScan(Vessel v, bool dirty, float dt)
        {
            int parts = -1;
            try { parts = v != null && v.parts != null ? v.parts.Count : -1; } catch (Exception) { }
            if (dirty || !ReferenceEquals(v, probeScanVessel) || parts != probeScanParts) probeDirty = true;
            if (probeDirty)
            {
                probeDirty = false;
                probeScanVessel = v;
                probeScanParts = parts;
                string before = probeCoreKey;
                ScanProbeCores(v);
                if (before != probeCoreKey)
                    Log.Info(probeCoreKey == null ? "No probe core" : "Probe core: " + probeCoreKey + " (" + string.Join(", ", probeNames.ToArray()) + ")");
                if (probeVoice != null)
                    Log.Debug("Probe voice " + probeVoice.Name + " from " + probeVoiceWhy + " (" + (probeVoicePart ?? "-") + ")");
                probePollLeft = 0f;
            }
            probePollLeft -= dt;
            if (probePollLeft > 0f) return;
            probePollLeft = 0.25f;
            dayLength = DayLengthLogged();
            try { missionTime = v != null ? v.missionTime : 0.0; } catch (Exception) { missionTime = 0.0; }
            if (probeCoreKey == null || !settings.probeSleep)
            {
                probeSleep.Reset();
                probeSleepState = ProbeSleepState.Awake;
                return;
            }
            probeStates.Clear();
            probeHib.Clear();
            for (int i = 0; i < probeCmds.Count; i++)
            {
                ModuleCommand c = probeCmds[i];
                if (c == null) continue; // destroyed module (Unity null)
                try
                {
                    probeStates.Add((int)c.ModuleState);
                    probeHib.Add(c.IsHibernating);
                }
                catch (Exception e) { Log.WarnOnce("probestate", "Probe core state unreadable: " + e.Message); }
            }
            bool forced = ProbeRules.ForcedAsleep(probeStates, probeHib);
            double ec = sample.valid ? sample.ecFrac : 1.0;
            if (sample.ecMax <= 0) ec = 1.0;
            ProbeSleepState was = probeSleepState;
            Tuning tu = settings.tuning;
            probeSleepState = probeSleep.Update(ec, tu.probeDrowsyFrac, tu.probeSleepFrac, ProbeCores.WakeMargin, forced);
            if (was != probeSleepState)
                Log.Info("Probe " + probeSleepState.ToString().ToLowerInvariant() + " (EC " + (ec * 100.0).ToString("0") + "%" + (forced ? ", no power or hibernating" : "") + ")");
        }

        private void ScanProbeCores(Vessel v)
        {
            probeCmds.Clear();
            probeNames.Clear();
            probeCoreParts.Clear();
            probeCoreKey = null;
            probeVoice = null;
            probeVoicePart = null;
            probeModuleName = null;
            probeVoiceWhy = ProbeVoicePick.Why.None;
            if (v == null) return;
            try
            {
                // breadth first from the root part, so the names are root-first
                Part root = v.rootPart;
                var todo = new Queue<Part>();
                var seen = new HashSet<Part>();
                if (root != null) { todo.Enqueue(root); seen.Add(root); }
                while (todo.Count > 0)
                {
                    Part p = todo.Dequeue();
                    AddProbeCores(p);
                    if (p.children != null)
                        for (int i = 0; i < p.children.Count; i++)
                        {
                            Part ch = p.children[i];
                            if (ch != null && seen.Add(ch)) todo.Enqueue(ch);
                        }
                }
                // parts the tree walk did not reach (should not happen)
                if (v.parts != null)
                    for (int i = 0; i < v.parts.Count; i++)
                        if (v.parts[i] != null && seen.Add(v.parts[i])) AddProbeCores(v.parts[i]);
            }
            catch (Exception e) { Log.Exception("ScanProbeCores", e); }
            int at;
            probeVoice = ProbeVoicePick.Resolve(probeCoreParts, settings.probeParts, probeVoices, out at, out probeVoiceWhy);
            probeVoicePart = at >= 0 && at < probeCoreParts.Count ? probeCoreParts[at].partName : null;
            probeModuleName = ProbeVoicePick.ModuleName(probeCoreParts, at);
            int dat;
            ProbeVoiceSpec def = ProbeVoicePick.Resolve(probeCoreParts, null, probeVoices, out dat, out probeDefaultWhy);
            probeDefaultVoice = def != null ? def.Name : null;
            // the phrase table's core column: a custom voice uses the lines of the stock core it names
            probeCoreKey = probeVoice != null ? probeVoice.Lines : null;
        }

        /// <summary>
        /// Subtitle name of the probe: the ModuleLonapse displayName of the deciding part when it has one, else the
        /// voice's display name. The unchanged generic voice (a core Lonapse does not know) and a voice without a name
        /// use the part's own title, like "Probodobodyne OKTO" or a mod's name.
        /// </summary>
        public string ProbeDisplayName
        {
            get
            {
                if (probeVoice == null) return ProbeCores.DisplayName(probeCoreKey);
                if (!string.IsNullOrEmpty(probeModuleName)) return probeModuleName;
                bool plainGeneric = probeVoice.Name == ProbeCores.Generic && probeVoices.SourceOf(ProbeCores.Generic) == ProbeVoiceSource.Stock;
                if ((plainGeneric || string.IsNullOrEmpty(probeVoice.DisplayName)) && !string.IsNullOrEmpty(probeVoicePart))
                    return PartTitle(probeVoicePart);
                return string.IsNullOrEmpty(probeVoice.DisplayName) ? "Probe" : probeVoice.DisplayName;
            }
        }

        /// <summary>The title of a part in the game ("Probodobodyne OKTO"), the part name when there is none.</summary>
        public static string PartTitle(string partName)
        {
            try
            {
                AvailablePart ap = PartLoader.getPartInfoByName(partName);
                if (ap != null && !string.IsNullOrEmpty(ap.title)) return ap.title;
            }
            catch (Exception) { }
            return partName;
        }

        /// <summary>
        /// Voices from stock, GameData (LONAPSE_PROBE_VOICE) and the settings, then a new pick for the vessel.
        /// Call after a voice was added or removed in the settings. A changed value needs no rebuild: the registry
        /// holds the same object the window edits.
        /// </summary>
        public void RebuildProbeVoices()
        {
            if (configVoices == null)
            {
                configVoices = new List<ProbeVoiceSpec>();
                try
                {
                    if (GameDatabase.Instance != null)
                        foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes("LONAPSE_PROBE_VOICE"))
                        {
                            var errs = new List<string>();
                            ProbeVoiceSpec v = ProbeVoiceNodes.FromNode(n, errs);
                            foreach (string e in errs) Log.Warn("LONAPSE_PROBE_VOICE: " + e);
                            if (v != null) configVoices.Add(v);
                        }
                    if (configVoices.Count > 0) Log.Info("Probe voices from GameData: " + configVoices.Count);
                }
                catch (Exception e) { Log.Exception("LONAPSE_PROBE_VOICE", e); }
            }
            probeVoices = ProbeVoiceRegistry.Build(configVoices, settings != null ? settings.probeVoices : null);
            probeDirty = true;
        }

        /// <summary>LONAPSE_PROBE_VOICE voices from GameData (empty before the first RebuildProbeVoices).</summary>
        public IList<ProbeVoiceSpec> ConfigVoices { get { return configVoices ?? new List<ProbeVoiceSpec>(); } }

        /// <summary>The probe voice changed (window): pick again on the next frame.</summary>
        public void RefreshProbeVoice() { probeDirty = true; }

        private void AddProbeCores(Part p)
        {
            if (p == null || p.Modules == null) return;
            for (int m = 0; m < p.Modules.Count; m++)
            {
                ModuleCommand c = p.Modules[m] as ModuleCommand;
                if (c == null || c.minimumCrew != 0) continue;
                probeCmds.Add(c);
                string partName = p.partInfo != null && !string.IsNullOrEmpty(p.partInfo.name) ? p.partInfo.name : p.name;
                probeNames.Add(partName);
                string mv = null, dn = null;
                int prio = 0;
                for (int k = 0; k < p.Modules.Count; k++)
                {
                    ModuleLonapse ml = p.Modules[k] as ModuleLonapse;
                    if (ml == null) continue;
                    mv = ml.voice;
                    prio = ml.priority;
                    dn = ml.displayName;
                    // a mod may give a localization tag (#autoLOC_... or its own #LOC_...)
                    if (!string.IsNullOrEmpty(dn) && dn.Trim().StartsWith("#", StringComparison.Ordinal))
                    {
                        try { dn = KSP.Localization.Localizer.Format(dn.Trim()); }
                        catch (Exception) { }
                    }
                    break;
                }
                probeCoreParts.Add(new ProbeCorePart(partName, mv, prio, dn));
                return; // one entry per part
            }
        }

        /// <summary>True once per vessel and body, the first landing there this session (milestone).</summary>
        public bool ProbeFirstLanding()
        {
            if (!probeMode || !settings.probeMilestones) return false;
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                return v != null && milestones.TakeLanding(v.id.ToString(), sample.body);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Probe mode only: milestones (mission days, first orbit) as events, rover moments as events and the
        /// data beeps while an antenna sends. Milestones also run with the gate closed (time warp): the event waits.
        /// </summary>
        private void UpdateProbeMoments(Vessel v, float dt)
        {
            if (v == null) return;
            Tuning tu = settings.tuning;
            try
            {
                if (gateOpen && !warpChatter && settings.probeRover)
                {
                    bool rover = v.vesselType == VesselType.Rover;
                    string moment = roverMoments.Update(dt, rover, sample.landed, (float)sample.srfSpeed, sample.roverAirTime, sample.roverTilt, tu);
                    if (moment != null)
                    {
                        Log.Info("Probe rover moment: " + moment);
                        director.PlayEvent(EventWatchers.Make(ProbeRules.RoverEvent, "calm", moment, sample.body, false, 10f, EventPriority.Soi));
                    }
                }
                probeMomentPoll -= dt;
                if (probeMomentPoll > 0f) return;
                probeMomentPoll = 0.5f;
                if (settings.probeMilestones)
                {
                    bool orbiting = v.situation == Vessel.Situations.ORBITING;
                    string m = milestones.Update(v.id.ToString(), missionTime, dayLength, orbiting, v.LandedOrSplashed, sample.body);
                    if (m != null)
                    {
                        Log.Info("Probe milestone: " + m + " (" + sample.body + ")");
                        director.PlayEvent(EventWatchers.Make(ProbeRules.MilestoneEvent, "calm", m, sample.body, false, 120f, EventPriority.Science));
                    }
                }
                if (gateOpen && settings.probeTransmit)
                {
                    bool sending = false;
                    foreach (IScienceDataTransmitter t in v.FindPartModulesImplementing<IScienceDataTransmitter>())
                        if (t != null && t.IsBusy()) { sending = true; break; }
                    bool first;
                    if (dataBeeps.Update(0.5f, sending, tu.dataBeepGap, (int)tu.dataBeepMax, out first)) director.PlayProbeData(first);
                }
            }
            catch (Exception e) { Log.WarnOnce("probe:moments", "Probe moments failed: " + e.Message); }
        }

        private string EvaluateGate(Vessel v)
        {
            warpChatter = false;
            evaMode = false;
            probeMode = false;
            touristCabin = false;
            frozenShip = false;
            if (!settings.enabled) return RDisabled;
            if (settings.muted) return RMuted;
            if (!HighLogic.LoadedSceneIsFlight) return RScene;
            if (v == null) return RNoVessel;
            if (ReferenceEquals(v, destroyedVessel)) return RDestroyed;
            if (!v.loaded) return RNotLoaded;
            bool eva = v.isEVA;
            // An EVA kerbal has a suit radio to Mission Control (one kerbal per EVA vessel)
            if (eva && (!settings.evaRadio || v.GetCrewCount() != 1)) { evaMode = false; return REva; }
            if (v.GetCrewCount() <= 0)
            {
                // No crew but a probe core opens the gate in probe mode. Without one it stays RNoCrew, so
                // FinalCallAllowed (crew_lost with the gate closed) keeps working for crewed flights.
                if (settings.probeMode && probeCoreKey != null) probeMode = true;
                else if (deepFreeze != null && deepFreeze.FrozenAboard > 0) frozenShip = true;   // Mission Control can still call the freezer
                else return RNoCrew;
            }
            else if (!eva && settings.probeMode && probeCoreKey != null && OnlyTourists(v))
            {
                // tourists cannot fly: the probe core is the crew, Mission Control talks to it
                probeMode = true;
                touristCabin = true;
            }
            if (railsWarp && !settings.warpChatter) return RWarp;
            warpChatter = railsWarp;
            evaMode = eva;
            return GateOpenReason;
        }

        /// <summary>Seconds per day on the game clock. Mods like Kronometer replace the date formatter, so JNSQ and
        /// rescale packs count their own day. Without a formatter the game setting picks 6 h or 24 h.</summary>
        public static double DayLength()
        {
            string from;
            return DayLength(out from);
        }

        /// <summary>DayLength, with where it came from for the log: the game clock (its formatter type) or the
        /// game setting.</summary>
        public static double DayLength(out string from)
        {
            try
            {
                var f = KSPUtil.dateTimeFormatter;
                if (f != null && f.Day > 0) { from = "game clock " + f.GetType().Name; return ProbeRules.SaneDay(f.Day); }
            }
            catch (Exception) { }
            from = "game setting";
            try { return GameSettings.KERBIN_TIME ? ProbeRules.KerbinDay : ProbeRules.EarthDay; }
            catch (Exception) { return ProbeRules.KerbinDay; }
        }

        private static double loggedDay = double.NaN;

        /// <summary>The day length, logged once and again when it changes (the calendar setting).</summary>
        private static double DayLengthLogged()
        {
            string from;
            double d = DayLength(out from);
            if (d != loggedDay)
            {
                loggedDay = d;
                Log.Info("Day length: " + (d / 3600.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                         + " h (from the " + from + ")");
            }
            return d;
        }

        /// <summary>Every kerbal aboard is a tourist (and there is at least one).</summary>
        private static bool OnlyTourists(Vessel v)
        {
            try
            {
                List<ProtoCrewMember> crew = v.GetVesselCrew();
                if (crew == null || crew.Count == 0) return false;
                foreach (ProtoCrewMember c in crew)
                    if (c == null || Persona.TraitKey(c.trait) != Persona.Tourist) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        private static bool ReadRailsWarp()
        {
            try { return DcMath.IsRailsWarp(TimeWarp.WarpMode == TimeWarp.Modes.HIGH, TimeWarp.CurrentRate); }
            catch (Exception) { return false; }
        }

        private bool IsPaused()
        {
            bool p = pauseEventFlag;
            try { p |= FlightDriver.Pause; } catch (Exception) { }
            try { p |= PauseMenu.exists && PauseMenu.isOpen; } catch (Exception) { }
            return p;
        }

        /// <summary>
        /// Our sources ignore the listener (it is ducked under voice), so the gain applies the stored listener base.
        /// When the listener sits at 1, KSP did not put the master volume on it, so the master volume is applied here.
        /// </summary>
        private float ComputeGameGain()
        {
            float lb = listenerBase >= 0f ? listenerBase : 1f;
            try
            {
                float master = lb > 0.99f ? GameSettings.MASTER_VOLUME : lb;
                if (!settings.useGameVolume) return DcMath.Clamp01(lb > 0.99f ? 1f : lb);
                return DcMath.Clamp01(GameSettings.VOICE_VOLUME * master);
            }
            catch (Exception) { return 1f; }
        }

        // ---------------------------------------------------------------- ducking

        /// <summary>Takes the listener value as the base, unless it is the value we wrote ourselves.</summary>
        private void ReadListenerBase()
        {
            float cur;
            try { cur = AudioListener.volume; } catch (Exception) { return; }
            if (listenerBase < 0f) { listenerBase = cur; Log.Info("Listener base volume " + cur.ToString("0.00")); return; }
            if (lastWrittenListener < 0f) { listenerBase = cur; return; }
            if (Math.Abs(cur - lastWrittenListener) > 0.002f)
            {
                // someone else set the listener (settings menu, another mod, or KSP every frame)
                listenerBase = cur;
                if (duckGain < 0.99f && ++duckMisses >= 3 && !duckDisabled)
                {
                    duckDisabled = true;
                    Log.Warn("The listener volume is reset by something else, ducking is switched off for this scene");
                }
                lastWrittenListener = -1f;
            }
            else duckMisses = 0;
        }

        private void UpdateDuck(float dt)
        {
            if (listenerBase < 0f) return;
            bool on = settings.duckMode == "listener" && !duckDisabled && gateOpen && !paused;
            float targetGain = on && director != null && director.VoiceActive ? 1f - settings.duckDepth : 1f;
            Tuning tu = settings.tuning;
            duckGain = MixMath.DuckStep(duckGain, targetGain, dt, tu.duckAttack, tu.duckRelease);
            if (Math.Abs(duckGain - targetGain) < 0.001f) duckGain = targetGain;
            if (!on && duckGain > 0.999f) duckGain = 1f;
            if (duckGain >= 1f && lastWrittenListener < 0f) return; // nothing to undo, leave the listener alone
            float v = listenerBase * duckGain;
            try { AudioListener.volume = v; lastWrittenListener = v; }
            catch (Exception) { }
            if (duckGain >= 1f) lastWrittenListener = -1f;
        }

        private void RestoreListener()
        {
            duckGain = 1f;
            if (lastWrittenListener < 0f || listenerBase < 0f) return;
            try { AudioListener.volume = listenerBase; } catch (Exception) { }
            lastWrittenListener = -1f;
        }

        // ---------------------------------------------------------------- pollers

        /// <summary>Req 10: panic only after a real failure while the launch is below launchCapAlt.</summary>
        private void UpdateLaunchCap()
        {
            Tuning tu = settings.tuning;
            if (launchPhase && sample.valid && sample.h >= tu.launchCapAlt) launchPhase = false;
            bool fail = StressModel.RealFailure(sample.heatRatio, tu.heatHi, sample.engineOut, stress.lossOfControlTime);
            roster.maxBand = launchPhase ? KerbalVoice.LaunchMaxBand(tu.launchCap >= 0.5f, sample.h, tu.launchCapAlt, fail) : Band.Panic;
        }

        private void PollThrottle(Vessel v, float dt)
        {
            if (throttle == null || v == null || v.isEVA) { if (throttle != null) throttle.Reset(); return; }
            float th;
            Vessel.Situations sit;
            try { th = v.ctrlState.mainThrottle; sit = v.situation; } catch (Exception) { return; }
            bool airborne = sit != Vessel.Situations.LANDED && sit != Vessel.Situations.SPLASHED;
            string ev = throttle.Update(dt, th, airborne);
            if (ev == null || !settings.throttleCallouts) return;
            if (countdown != null && countdown.Running) { Log.Info("Throttle callout skipped: countdown running"); return; }
            string evName = ev == "full" ? "throttle_full" : "throttle_cut";
            if (!callouts.TryShared(Time.unscaledTime, settings.tuning.calloutCooldown)) { Log.Info("Throttle callout skipped (" + evName + "): cooldown"); return; }
            string why = director.PlayCallout(evName, null, 0f, evName);
            if (why != null) Log.Info("Throttle callout not spoken (" + evName + "): " + why);
        }

        private void PollPlan(Vessel v, float dt)
        {
            if (v == null || v.isEVA) return;
            // only while the radio can talk: a warning must not be used up during warp or blackout
            if (!CanStartAudio || !settings.radioEnabled || director.radioSilent) return;
            plan.Update(dt, v, settings.tuning);
            if (!settings.planChatter) { plan.planDue = plan.burnDue = false; return; }
            if (countdown.Running) return;
            if (plan.burnDue && director.EnqueueBurnSoon()) plan.burnDue = false;
            if (plan.planDue && director.EnqueuePlan()) plan.planDue = false;
        }

        /// <summary>EVA breathing bed of the radio profile. Fades out outside EVA.</summary>
        /// <summary>Master alarm: fades in slowly while the vessel stress is at or above alarmStress, holds a few seconds, fades out slowly.</summary>
        private void UpdateAlarm(float dt)
        {
            Tuning tu = settings.tuning;
            bool panic = gateOpen && settings.masterAlarm && !evaMode && !warpChatter && stress != null && stress.stress >= tu.alarmStress;
            if (panic) alarmHoldLeft = tu.alarmHold;
            else alarmHoldLeft -= dt;
            bool want = settings.masterAlarm && gateOpen && (panic || alarmHoldLeft > 0f) && ClipLibrary.CountOf("beeps/master_alarm") > 0;
            if (want && (!alarmCh.Busy || alarmCh.FadingOut))
            {
                AudioClip c = ClipLibrary.Next("beeps/master_alarm", rng);
                if (c != null) { alarmCh.Play(c, tu.alarmVolume, 1f, true, tu.alarmFadeIn); Log.Info("Master alarm on"); }
            }
            else if (!want && alarmCh.Busy && !alarmCh.FadingOut) { alarmCh.FadeOut(tu.alarmFadeOut); Log.Info("Master alarm off"); }
            if (alarmCh.Busy) alarmCh.volumeScale = tu.alarmVolume;
        }

        public float evaExertion;   // 0..1, EVA running builds it up slowly

        private void UpdateBreath(float dt)
        {
            RadioProfile p = profile;
            Tuning tu = settings.tuning;
            float run = evaMode && sample.valid ? sample.evaRun : 0f;
            evaExertion = DcMath.Exertion(evaExertion, run, dt, tu.evaExertRise, tu.evaExertFall);
            bool want = gateOpen && settings.radioEnabled && p != null && p.BedClip != null && ClipLibrary.CountOf(p.BedClip) > 0;
            float rate = 1f + (tu.evaBreathRate - 1f) * evaExertion;
            float gain = p != null ? p.BedGain * (1f + (tu.evaBreathGain - 1f) * evaExertion) : 0f;
            if (want && !breathCh.Busy)
            {
                AudioClip c = ClipLibrary.Next(p.BedClip, rng);
                if (c != null) breathCh.Play(c, gain, rate, true, 1.5f);
            }
            else if (!want) breathCh.FadeOutOnce(0.8f);
            else if (breathCh.src != null)
            {
                // the loop speeds up and gets louder with the exertion, smoothly every frame
                breathCh.src.pitch = rate;
                breathCh.volumeScale = gain;
            }
        }

        /// <summary>Immediate stop of every source, queue cleared, scheduler and blackout reset.</summary>
        public void HardSilence(string why)
        {
            for (int i = 0; i < allChannels.Length; i++)
            {
                try { allChannels[i].Stop(); } catch (Exception e) { Log.Exception("HardSilence", e); }
            }
            gateGain = 0f;
            if (director != null) director.HardReset();
            if (blackout != null) blackout.Reset();
            if (!silenced) Log.Info("All audio stopped (" + why + ")");
            silenced = true;
            silenceReason = why;
        }

        private void UpdateStationMode(Vessel v)
        {
            bool st = false;
            try { st = v != null && (v.vesselType == VesselType.Station || v.vesselType == VesselType.Base); }
            catch (Exception) { st = false; }
            if (st != stationMode)
            {
                stationMode = st;
                Log.Info(st ? "Station/base mode on" : "Station/base mode off");
            }
        }

        /// <summary>Approach detection against the active target vessel (docking port targets resolve to their vessel).</summary>
        private void UpdateDocking(Vessel v, float dt)
        {
            bool valid = false;
            Vessel tv = null;
            float dist = -1f, rel = 0f, portDist = -1f;
            try
            {
                ITargetable t = (v != null && FlightGlobals.fetch != null) ? FlightGlobals.fetch.VesselTarget : null;
                if (IsDestroyed(t)) t = null;
                if (t == null && v != null) t = v.targetObject;
                if (IsDestroyed(t)) t = null;
                tv = t != null ? t.GetVessel() : null;
                if (tv != null && !ReferenceEquals(tv, v) && tv.loaded && v.loaded)
                {
                    Transform a = v.GetTransform(), b = t.GetTransform();
                    if (a != null && b != null)
                    {
                        dist = (a.position - b.position).magnitude;
                        rel = (v.GetObtVelocity() - t.GetObtVelocity()).magnitude;
                        valid = !float.IsNaN(dist) && !float.IsNaN(rel);
                        if (valid && dist < DockIntentRange)
                        {
                            portDist = dockGeo.PortDistance(v, tv, t);
                            dockIntent = dockGeo.Intent(t, settings.tuning.dockIntentAngle);
                        }
                    }
                }
            }
            catch (Exception e) { valid = false; Log.Exception("UpdateDocking", e); }
            if (!valid || dist >= DockIntentRange) dockIntent = 0;

            if (valid && dist < 200f) closing.Add(rel, dt); else closing.Add(0f, dt);
            if (docking.Update(valid, tv, dist, portDist, rel, dt, settings.tuning))
            {
                float chance = DockIntent.Chance(dockIntent, settings.tuning.dockIntentBase);
                bool play = rng.NextDouble() < chance;
                Log.Info("Docking approach: " + dist.ToString("0.0") + " m at " + rel.ToString("0.00") + " m/s (" + DockIntent.Describe(dockIntent)
                         + ", chance " + chance.ToString("0.00") + (play ? "" : ", skipped") + ")");
                if (play) director.EnqueueDockingApproach(); // checks CanStartAudio itself
            }
            Tuning tu = settings.tuning;
            float intentChance = DockIntent.Chance(dockIntent, tu.dockIntentBase);
            // Say in the log why a docking call does or does not come, one line per target and one on entering the approach distance
            if (!valid) { dockLogTarget = null; dockEnterLogged = false; }
            else
            {
                if (!ReferenceEquals(tv, dockLogTarget))
                {
                    dockLogTarget = tv;
                    dockEnterLogged = false;
                    Log.Info("Docking target: " + Names.Of(tv) + " at " + dist.ToString("0") + " m, "
                             + (docking.seenFar ? "seen far away, the approach call can come" : "closer than " + tu.dockApproachDist.ToString("0") + " m already: no approach call until it has been beyond that"));
                }
                if (dist < tu.dockApproachDist && !dockEnterLogged)
                {
                    dockEnterLogged = true;
                    Log.Info("Docking: within " + tu.dockApproachDist.ToString("0") + " m, seenFar " + docking.seenFar + ", armed " + docking.armed + ", fired " + docking.fired
                             + ", rel " + rel.ToString("0.00") + " m/s (call under " + tu.dockApproachSpeed.ToString("0.0") + "), range rate " + docking.rangeRate.ToString("0.00")
                             + " (call below -" + tu.dockClosingRate.ToString("0.00") + "), " + DockIntent.Describe(dockIntent));
                }
                if (dist > tu.dockApproachDist + 10f) dockEnterLogged = false;
            }
            if (docking.closePhaseJustStarted)
            {
                docking.closePhaseJustStarted = false;
                Log.Info("Docking close approach started: " + dist.ToString("0.0") + " m, closing at " + docking.ClosingSpeed.ToString("0.00") + " m/s");
            }
            if (docking.farCalloutDue > 0f)
            {
                float th = docking.farCalloutDue;
                docking.farCalloutDue = -1f;
                DockCallout(th, intentChance, tu);
            }
            if (docking.calloutDue > 0f)
            {
                float th = docking.calloutDue;
                docking.calloutDue = -1f;
                DockCallout(th, intentChance, tu);
            }
            if (docking.closeExchangeDue)
            {
                docking.closeExchangeDue = false;
                if (rng.NextDouble() >= intentChance) Log.Info("Docking close exchange skipped by chance " + intentChance.ToString("0.00"));
                else director.EnqueueDockingClose(docking.ClosingSpeed >= tu.dockTenseClosing); // checks the gate itself
            }
            if (docking.InGuard(settings.tuning)) stress.QuietMotion(settings.tuning.dockQuietTime);
        }

        /// <summary>One distance callout (far or near): by chance, from the runtime voice, else the old callout path.</summary>
        private void DockCallout(float th, float intentChance, Tuning tu)
        {
            if (rng.NextDouble() >= intentChance)
            {
                Log.Info("Docking callout at " + th.ToString("0") + " m skipped by chance " + intentChance.ToString("0.00") + " (" + DockIntent.Describe(dockIntent) + ")");
                return;
            }
            if (director.RuntimeDockCallout(th)) return;
            string why = director.PlayCallout(null, "crew/calm", tu.dockCalloutLen, "docking " + th.ToString("0") + " m");
            if (why != null) Log.Info("Docking callout at " + th.ToString("0") + " m not spoken: " + why);
        }

        /// <summary>A destroyed Unity object (e.g. a docking port that blew up) still passes a plain C# null check.</summary>
        private static bool IsDestroyed(ITargetable t)
        {
            UnityEngine.Object uo = t as UnityEngine.Object;
            if (ReferenceEquals(uo, null)) return false; // null, or not a Unity object
            return uo == null;                           // Unity's overloaded == is true for destroyed objects
        }

        /// <summary>
        /// True when the switch is a kerbal going out on EVA or
        /// coming back, or between a kerbal and a ship close by. Waiting science lines then stay (a crew report was lost on EVA
        /// without this).
        /// </summary>
        private static bool IsEvaHop(Vessel from, Vessel to)
        {
            try
            {
                if (from == null || to == null || (!from.isEVA && !to.isEVA)) return false;
                return Vector3d.Distance(from.GetWorldPos3D(), to.GetWorldPos3D()) < EvaHopDistance;
            }
            catch (Exception) { return false; }
        }
        private const double EvaHopDistance = 2500.0;   // m, about the physics range around the active vessel

        // evaGroup: other EVA kerbals near the active EVA kerbal join the roster, so they talk with Mission Control.
        // Kerbals of other vessels nearby (a ship, a lander, an EVA kerbal further out, or EVA kerbals near the
        // active ship) join as radio partners: they only talk over the radio with the kerbals of the active vessel.
        private readonly List<Vessel> evaNearby = new List<Vessel>();
        private readonly List<Vessel> radioNearby = new List<Vessel>();
        private readonly List<Vessel> evaScan = new List<Vessel>();
        private readonly List<Vessel> radioScan = new List<Vessel>();
        private float evaScanLeft;
        private const double EvaGroupRange = 150.0;    // m
        private const double RadioGroupRange = 2300.0; // m, inside the physics range
        private const int RadioGroupMax = 6;           // kerbals, the nearest first

        /// <summary>Crew count of the scanned group vessels (for the roster count check).</summary>
        private int GroupCrew()
        {
            int n = 0;
            try
            {
                foreach (Vessel o in evaNearby) if (o != null) n += o.GetCrewCount();
                foreach (Vessel o in radioNearby) if (o != null) n += o.GetCrewCount();
            }
            catch (Exception) { }
            return n;
        }

        /// <summary>Once a second: on EVA the other EVA kerbals within EvaGroupRange, and the crewed vessels within
        /// RadioGroupRange as radio partners. A change marks the roster dirty.</summary>
        private void ScanEvaGroup(Vessel v, float dt)
        {
            bool on = v != null && settings.evaGroup && !probeMode;
            if (!on)
            {
                if (evaNearby.Count > 0 || radioNearby.Count > 0) { evaNearby.Clear(); radioNearby.Clear(); roster.dirty = true; }
                return;
            }
            evaScanLeft -= dt;
            if (evaScanLeft > 0f) return;
            evaScanLeft = 1f;
            evaScan.Clear();
            radioScan.Clear();
            try
            {
                bool eva = v.isEVA;
                Vector3d here = v.GetWorldPos3D();
                var dist = new Dictionary<Vessel, double>();
                List<Vessel> loaded = FlightGlobals.VesselsLoaded;
                for (int i = 0; i < loaded.Count; i++)
                {
                    Vessel o = loaded[i];
                    if (o == null || ReferenceEquals(o, v) || o.GetCrewCount() <= 0) continue;
                    double d = Vector3d.Distance(o.GetWorldPos3D(), here);
                    if (d > RadioGroupRange) continue;
                    if (eva && o.isEVA && o.GetCrewCount() == 1 && d <= EvaGroupRange) evaScan.Add(o);
                    else { radioScan.Add(o); dist[o] = d; }
                }
                radioScan.Sort((x, y) => dist[x].CompareTo(dist[y]));
                int crew = 0;
                for (int i = 0; i < radioScan.Count; i++)
                {
                    crew += radioScan[i].GetCrewCount();
                    if (crew > RadioGroupMax) { radioScan.RemoveRange(i, radioScan.Count - i); break; }
                }
            }
            catch (Exception e) { Log.WarnOnce("evagroup", "EVA group scan failed: " + e.Message); evaScan.Clear(); radioScan.Clear(); }
            if (SameSet(evaScan, evaNearby) && SameSet(radioScan, radioNearby)) return;
            bool evaSame = SameSet(evaScan, evaNearby);
            evaNearby.Clear();
            evaNearby.AddRange(evaScan);
            radioNearby.Clear();
            radioNearby.AddRange(radioScan);
            roster.dirty = true;
            if (!evaSame || evaNearby.Count > 0)
                Log.Info("EVA group: " + evaNearby.Count + " other kerbal(s) on EVA within " + EvaGroupRange.ToString("0") + " m");
            Log.Info("Radio group: " + radioNearby.Count + " other vessel(s) with crew within " + RadioGroupRange.ToString("0") + " m");
        }

        private static bool SameSet(List<Vessel> a, List<Vessel> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!b.Contains(a[i])) return false;
            return true;
        }

        private static int SafeStage(Vessel v)
        {
            try { return v != null ? v.currentStage : int.MinValue; } catch (Exception) { return int.MinValue; }
        }

        /// <summary>
        /// Fallback for staging: if Vessel.currentStage drops without an onStageActivate event within 1 s, treat it as
        /// staging. The shared callout cooldown prevents double callouts.
        /// </summary>
        // stage number -> bit 1 decoupler or fairing, bit 2 engine. Refreshed every second, because the decoupled
        // parts may already belong to another vessel when onStageActivate arrives.
        private readonly Dictionary<int, int> stageParts = new Dictionary<int, int>();
        private float stageScanLeft;

        // what the 1 s scan saw: the part count and the burning engines. A stage that really fired drops parts or
        // lights an engine. Editing the stage order in flight also moves currentStage, but fires nothing.
        private int scanParts = -1, scanIgnited = -1;
        // a currentStage drop without onStageActivate, waiting for proof that something fired
        private int pollDropFrom = int.MinValue, pollDropTo;
        private float pollDropTime;
        private const float PollProofWindow = 1.5f;

        private static int IgnitedEngines(Vessel v)
        {
            int n = 0;
            for (int i = 0; i < v.parts.Count; i++)
            {
                Part p = v.parts[i];
                if (p == null) continue;
                for (int m = 0; m < p.Modules.Count; m++)
                {
                    var e = p.Modules[m] as ModuleEngines;
                    if (e != null && e.EngineIgnited) n++;
                }
            }
            return n;
        }

        private void ScanStages(Vessel v)
        {
            stageParts.Clear();
            if (v == null || v.parts == null) return;
            if (pollDropFrom == int.MinValue)
            {
                scanParts = v.parts.Count;
                scanIgnited = IgnitedEngines(v);
            }
            for (int i = 0; i < v.parts.Count; i++)
            {
                Part p = v.parts[i];
                if (p == null) continue;
                int bits = 0;
                for (int m = 0; m < p.Modules.Count; m++)
                {
                    PartModule pm = p.Modules[m];
                    if (pm is ModuleDecouplerBase || pm is ModuleProceduralFairing) bits |= 1;
                    else if (pm is ModuleEngines) bits |= 2;
                }
                if (bits == 0) continue;
                int st = p.inverseStage, old;
                stageParts.TryGetValue(st, out old);
                stageParts[st] = old | bits;
            }
        }

        /// <summary>What the stage holds (cached scan plus a live scan of the active vessel).</summary>
        public StageKind StageContents(int stage)
        {
            int bits;
            stageParts.TryGetValue(stage, out bits);
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null)
                    for (int i = 0; i < v.parts.Count; i++)
                    {
                        Part p = v.parts[i];
                        if (p == null || p.inverseStage != stage) continue;
                        for (int m = 0; m < p.Modules.Count; m++)
                        {
                            PartModule pm = p.Modules[m];
                            if (pm is ModuleDecouplerBase || pm is ModuleProceduralFairing) bits |= 1;
                            else if (pm is ModuleEngines) bits |= 2;
                        }
                    }
            }
            catch (Exception e) { Log.WarnOnce("stagecontents", "Stage scan failed: " + e.Message); }
            return PacingRules.ClassifyStage((bits & 1) != 0, (bits & 2) != 0);
        }

        private void PollStage(Vessel v)
        {
            stageScanLeft -= Time.unscaledDeltaTime;
            if (stageScanLeft <= 0f)
            {
                stageScanLeft = 1f;
                try { ScanStages(v); } catch (Exception e) { Log.WarnOnce("stagescan", "Stage scan failed: " + e.Message); }
            }
            int cur = SafeStage(v);
            if (cur == int.MinValue) return;
            if (lastPolledStage != int.MinValue && cur < lastPolledStage && Time.unscaledTime - lastStageEventTime > 1.0f
                && pollDropFrom == int.MinValue)
            {
                pollDropFrom = lastPolledStage;
                pollDropTo = cur;
                pollDropTime = Time.unscaledTime;
            }
            if (pollDropFrom != int.MinValue)
            {
                if (lastStageEventTime >= pollDropTime - 1f)
                    pollDropFrom = int.MinValue;   // onStageActivate came after all, it did the callout
                else
                {
                    bool fired = false;
                    try { fired = scanParts >= 0 && (v.parts.Count < scanParts || IgnitedEngines(v) > scanIgnited); }
                    catch (Exception) { fired = false; }
                    if (fired)
                    {
                        Log.Info("Staging detected by currentStage poll (" + pollDropFrom + " -> " + pollDropTo + "), no onStageActivate seen");
                        stress.AddImpulse(settings.tuning.impulseStage);
                        beeps.OnStage(pollDropTo, "poll");
                        pollDropFrom = int.MinValue;
                    }
                    else if (Time.unscaledTime - pollDropTime > PollProofWindow)
                    {
                        Log.Info("Stage order changed (" + pollDropFrom + " -> " + pollDropTo + "), nothing fired: no staging callout");
                        pollDropFrom = int.MinValue;
                    }
                }
            }
            lastPolledStage = cur;
        }

        /// <summary>Parachute callouts: poll deployment states at ~4 Hz on the cached module list.</summary>
        private void PollChutes(Vessel v, float dt)
        {
            chutePollTimer -= dt;
            if (chutePollTimer > 0f) return;
            Tuning tu = settings.tuning;
            chutePollTimer = Math.Max(0.05f, tu.chutePollInterval);
            int semi, full;
            try { chutes.Poll(v, out semi, out full); }
            catch (Exception e) { Log.Exception("PollChutes", e); return; }
            if (semi == 0 && full == 0) return;
            ChuteCallout c = callouts.OnChutes(Time.unscaledTime, semi, full, tu.chuteGroupCooldown, tu.chuteFullMinGap, tu.calloutCooldown);
            string what = semi > 0 ? "parachute semi-deployed x" + semi : "parachute deployed x" + full;
            if (c == ChuteCallout.None) { Log.Info("Parachute callout skipped (" + what + "): grouped or cooldown"); return; }
            string why = director.PlayCallout(semi > 0 ? "chute_semi" : "chute_full", null, 0f, what); // checks the gate itself
            if (why != null) Log.Info("Parachute callout not spoken (" + what + "): " + why);
        }

        /// <summary>Category toggles also silence what is already playing (fade once, never re-arm a fade).</summary>
        private void EnforceCategoryToggles()
        {
            if (!settings.radioEnabled)
            {
                control.FadeOutOnce(0.3f);
                controlBed.FadeOutOnce(0.3f);
                staticCh.FadeOutOnce(0.3f);
                crewBed.FadeOutOnce(0.3f);
                calloutCh.FadeOutOnce(0.3f);
                keyCh.FadeOutOnce(0.1f);
                breathCh.FadeOutOnce(0.3f);
                for (int i = 0; i < crew.Length; i++) if (!crew[i].intercom) crew[i].FadeOutOnce(0.3f);
            }
            if (!settings.intercomEnabled)
            {
                clickCh.FadeOutOnce(0.1f);
                for (int i = 0; i < crew.Length; i++) if (crew[i].intercom) crew[i].FadeOutOnce(0.3f);
            }
            if (!settings.beepsEnabled)
                for (int i = 0; i < beep.Length; i++) beep[i].FadeOutOnce(0.2f);
        }

        /// <summary>Blackout: fade every radio channel except static.</summary>
        public void FadeOutRadio(float seconds)
        {
            for (int i = 0; i < crew.Length; i++) crew[i].FadeOut(seconds);
            control.FadeOut(seconds);
            controlBed.FadeOut(seconds);
            crewBed.FadeOut(seconds);
            calloutCh.FadeOut(seconds);
            keyCh.FadeOut(seconds);
            for (int i = 0; i < beep.Length; i++) beep[i].FadeOut(seconds);
        }

        // ---------------------------------------------------------------- hotkey and settings

        private void HandleHotkey()
        {
            if (window != null && window.CapturingKey) return;
            bool mute = muteKey != KeyCode.None && Input.GetKeyDown(muteKey);
            bool count = countdownKey != KeyCode.None && Input.GetKeyDown(countdownKey);
            if (!mute && !count) return;
            if (GUIUtility.keyboardControl != 0) return;
            try { if (InputLockManager.IsLocked(ControlTypes.KEYBOARDINPUT)) return; } catch (Exception) { }
            if (mute && (muteModifier == KeyCode.None || ModifierHeld(muteModifier))) ToggleMute();
            if (count && (countdownModifier == KeyCode.None || ModifierHeld(countdownModifier))) ToggleCountdown();
        }

        /// <summary>Countdown hotkey: starts the count on the pad, or stops a running count (with the yell).</summary>
        public void ToggleCountdown()
        {
            if (countdown == null) return;
            if (countdown.Running) countdown.Stop();
            else countdown.Start(FlightGlobals.ActiveVessel);
        }

        private static bool ModifierHeld(KeyCode m)
        {
            if (Input.GetKey(m)) return true;
            switch (m)
            {
                case KeyCode.LeftAlt: return Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr);
                case KeyCode.RightAlt: return Input.GetKey(KeyCode.LeftAlt);
                case KeyCode.LeftControl: return Input.GetKey(KeyCode.RightControl);
                case KeyCode.RightControl: return Input.GetKey(KeyCode.LeftControl);
                case KeyCode.LeftShift: return Input.GetKey(KeyCode.RightShift);
                case KeyCode.RightShift: return Input.GetKey(KeyCode.LeftShift);
                default: return false;
            }
        }

        public void ToggleMute()
        {
            settings.muted = !settings.muted;
            SaveSettings();
            try { ScreenMessages.PostScreenMessage(settings.muted ? "Lonapse: muted" : "Lonapse: unmuted", 2f); }
            catch (Exception e) { Log.Exception("ToggleMute", e); }
            Log.Info(settings.muted ? "Muted by hotkey" : "Unmuted by hotkey");
        }

        public void SaveSettings()
        {
            settings.Clamp();
            Log.verbose = settings.verboseLog;
            if (settings.Save()) settingsDirty = false;
            SaveLineMemory();   // also when the window closes, so a crash loses little
        }

        public void ResetTuning()
        {
            settings.tuning = new Tuning();
            stress.tu = settings.tuning;
            SaveSettings();
            Log.Info("Tuning reset to defaults");
        }

        public void TestExchange()
        {
            if (!CanStartAudio)
            {
                Log.Info("Test exchange ignored, gate is " + gateReason);
                try { ScreenMessages.PostScreenMessage("Lonapse: no test, gate is " + gateReason, 2f); } catch (Exception) { }
                return;
            }
            director.TriggerTestExchange();
        }

        // ---------------------------------------------------------------- GUI

        private void OnGUI()
        {
            if (!started || uiHidden) return;
            try
            {
                // with the gate closed only the crew_lost final call's line is shown (director.UpdateClosedGate)
                if (settings.subtitles && (gateOpen || (director != null && Time.unscaledTime < director.finalSubtitleUntil)))
                    subtitles.Draw();
                if (window != null) window.Draw();
                if (overlay != null && settings.showDebug) overlay.Draw();
            }
            catch (Exception e) { Log.Exception("OnGUI", e); }
        }
    }
}
