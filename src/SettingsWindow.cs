// Stock ApplicationLauncher button and the IMGUI settings window.
using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

namespace Lonapse
{
    public class SettingsWindow
    {
        private const int WindowId = 0x44434801;
        private const string LockId = "Lonapse_window";
        private const ControlTypes LockMask = ControlTypes.CAMERACONTROLS | ControlTypes.MAP_UI | ControlTypes.TARGETING |
                                              ControlTypes.TWEAKABLES | ControlTypes.MANNODE_ADDEDIT | ControlTypes.MANNODE_DELETE;
        private bool locked;
        private readonly LonapseAddon host;
        private ApplicationLauncherButton button;
        private bool visible;
        private Rect rect;
        private Texture2D icon;

        private GUIStyle okStyle, lowStyle;
        private string[] folderLines;
        private bool[] folderLow;
        private string muteLabel;
        private bool capturing;   // "Set key" for the countdown: the next key press becomes the hotkey

        public bool CapturingKey { get { return capturing; } }

        private const float WindowWidth = 340f;
        private bool pendingSave; // a toggle flipped this frame: save once after the window is drawn

        public SettingsWindow(LonapseAddon host)
        {
            this.host = host;
            rect = new Rect(host.settings.windowX, host.settings.windowY, WindowWidth, 10f);
            BuildFolderLines();
            muteLabel = "Muted (" + host.HotkeyText + ")";
            if (host.subtitles != null) host.subtitles.settings = host.settings;
        }

        private void BuildFolderLines()
        {
            int n = ClipLibrary.Folders.Length;
            folderLines = new string[n];
            folderLow = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int c = ClipLibrary.CountOf(ClipLibrary.Folders[i]);
                folderLines[i] = ClipLibrary.Folders[i] + ": " + c + " / " + ClipLibrary.MinimumOf(i);
                folderLow[i] = c < ClipLibrary.MinimumOf(i);
            }
        }

        // ------------------------------------------------ button

        public void CreateButton()
        {
            if (button != null) return;
            if (!ApplicationLauncher.Ready || ApplicationLauncher.Instance == null) return;
            visible = false; // a fresh button starts in the off state
            ReleaseLock();
            button = ApplicationLauncher.Instance.AddModApplication(
                OnTrue, OnFalse, Noop, Noop, Noop, Noop,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                GetIcon());
            Log.Debug("AppLauncher button added");
        }

        public void RemoveButton()
        {
            if (button == null) return;
            try { if (ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button); }
            catch (Exception e) { Log.Exception("RemoveButton", e); }
            button = null;
            visible = false;
            ReleaseLock();
        }

        public bool Visible { get { return visible; } }

        /// <summary>Block clicks and scrolling from reaching the game while the mouse is over the window.</summary>
        private void SetLock(bool over)
        {
            if (over == locked) return;
            try
            {
                if (over) InputLockManager.SetControlLock(LockMask, LockId);
                else InputLockManager.RemoveControlLock(LockId);
                locked = over;
            }
            catch (Exception e) { Log.Exception("SettingsWindow.SetLock", e); }
        }

        public void ReleaseLock()
        {
            if (locked) SetLock(false);
        }

        private static void Noop() { }

        private void OnTrue() { visible = true; }

        private void OnFalse()
        {
            visible = false;
            ReleaseLock();
            host.SaveSettings();
        }

        private Texture GetIcon()
        {
            if (icon != null) return icon;
            try
            {
                GameDatabase db = GameDatabase.Instance;
                if (db != null && db.ExistsTexture("Lonapse/Icons/button"))
                {
                    Texture2D t = db.GetTexture("Lonapse/Icons/button", false);
                    if (t != null) { icon = t; return icon; }
                }
            }
            catch (Exception e) { Log.Exception("GetIcon", e); }

            // procedural 38x38 icon: antenna mast with two radio arcs
            const int size = 38;
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color clear = new Color(0f, 0f, 0f, 0f);
            Color fg = new Color(0.85f, 0.95f, 1f, 1f);
            float cx = 19f, cy = 22f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool upper = dy > 2f;
                    bool arc = upper && ((Mathf.Abs(r - 8f) < 1.3f) || (Mathf.Abs(r - 14f) < 1.3f));
                    bool mast = Mathf.Abs(dx) < 1.3f && y >= 4 && y <= 22;
                    bool dot = r < 2.6f;
                    bool foot = y >= 4 && y <= 6 && Mathf.Abs(dx) < 7f;
                    tex.SetPixel(x, y, (arc || mast || dot || foot) ? fg : clear);
                }
            }
            tex.Apply();
            icon = tex;
            return icon;
        }

        // ------------------------------------------------ window

        public void Draw()
        {
            if (host.subtitles != null && host.subtitles.settings != host.settings) host.subtitles.settings = host.settings;
            if (!visible) { capturing = false; return; }
            if (capturing) CaptureKey();
            if (okStyle == null)
            {
                okStyle = new GUIStyle(GUI.skin.label);
                headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                headerStyle.normal.textColor = new Color(0.95f, 0.78f, 0.35f);
                valueStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
                lowStyle = new GUIStyle(GUI.skin.label);
                lowStyle.normal.textColor = new Color(1f, 0.35f, 0.3f);
            }
            Rect r = GUILayout.Window(WindowId, rect, DrawWindow, "Lonapse", GUILayout.Width(WindowWidth));
            if (pendingSave) { pendingSave = false; host.SaveSettings(); }
            r.x = Mathf.Clamp(r.x, 0f, Math.Max(0f, Screen.width - 60f));
            r.y = Mathf.Clamp(r.y, 0f, Math.Max(0f, Screen.height - 40f));
            if (r.x != rect.x || r.y != rect.y)
            {
                host.settings.windowX = r.x;
                host.settings.windowY = r.y;
                host.settingsDirty = true;
            }
            rect = r;
            SetLock(visible && rect.Contains(Event.current.mousePosition));
        }

        private void CaptureKey()
        {
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.KeyDown || ev.keyCode == KeyCode.None) return;
            KeyCode k = ev.keyCode;
            // a modifier alone is not a hotkey, wait for the real key
            if (k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.AltGr || k == KeyCode.LeftControl ||
                k == KeyCode.RightControl || k == KeyCode.LeftShift || k == KeyCode.RightShift) return;
            Settings s = host.settings;
            if (k == KeyCode.Escape) { capturing = false; ev.Use(); return; }
            if (k == KeyCode.Backspace || k == KeyCode.Delete) { s.countdownKey = "None"; s.countdownModifier = "None"; }
            else
            {
                s.countdownKey = k.ToString();
                s.countdownModifier = ev.alt ? "LeftAlt" : ev.control ? "LeftControl" : ev.shift ? "LeftShift" : "None";
            }
            capturing = false;
            ev.Use();
            host.RefreshKeys();
            host.SaveSettings();
            Log.Info("Countdown hotkey set to " + host.CountdownKeyText);
        }

        // cached value texts, keyed by row name: rebuilt only when the shown value changes
        private sealed class CachedLabel { public float value = float.NaN; public string text; }
        private readonly Dictionary<string, CachedLabel> labels = new Dictionary<string, CachedLabel>();

        private string Label(string name, float v, string fmt)
        {
            CachedLabel c;
            if (!labels.TryGetValue(name, out c)) { c = new CachedLabel(); labels[name] = c; }
            if (c.text == null || v != c.value)
            {
                c.value = v;
                c.text = v.ToString(fmt);
            }
            return c.text;
        }

        private GUIStyle headerStyle, valueStyle;

        private void Header(string text)
        {
            GUILayout.Space(8f);
            GUILayout.Label(text, headerStyle);
        }

        /// <summary>One row: name, slider, value. shown is the value in display units, fmt formats it.</summary>
        private float Row(string name, float v, float min, float max, float shown, string fmt)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, GUILayout.Width(118f));
            GUILayout.BeginVertical();
            GUILayout.Space(8f);
            float nv = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndVertical();
            GUILayout.Label(Label(name, shown, fmt), valueStyle, GUILayout.Width(46f));
            GUILayout.EndHorizontal();
            if (Math.Abs(nv - v) > 1e-5f) host.settingsDirty = true;
            return nv;
        }

        /// <summary>A 0..max volume row shown in percent.</summary>
        private float Volume(string name, float v, float max = 1f)
        {
            return Row(name, v, 0f, max, Mathf.Round(v * 100f), "0'%'");
        }

        /// <summary>Old style slider (label above, slider below). Only on the Advanced and Debug tabs.</summary>
        private float Slider(string name, float v, float min, float max, string fmt)
        {
            GUILayout.Label(name + ": " + Label(name, v, fmt));
            float nv = GUILayout.HorizontalSlider(v, min, max);
            if (Math.Abs(nv - v) > 1e-5f) host.settingsDirty = true;
            return nv;
        }

        /// <summary>Toggle that saves the settings right after this window pass when it flips.</summary>
        private bool Toggle(bool v, string text)
        {
            bool nv = GUILayout.Toggle(v, text);
            if (nv != v) { host.settingsDirty = true; pendingSave = true; }
            return nv;
        }

        /// <summary>Row of buttons that act as radio buttons. Returns the chosen index.</summary>
        private static int Choice(int current, string[] options)
        {
            GUILayout.BeginHorizontal();
            int pick = current;
            for (int i = 0; i < options.Length; i++)
            {
                bool on = GUILayout.Toggle(current == i, options[i], GUI.skin.button);
                if (on && current != i) pick = i;
            }
            GUILayout.EndHorizontal();
            return pick;
        }

        // ------------------------------------------------ tabs

        private static readonly string[] AllTabs = { "General", "Sound", "Crew", "Probes", "Subtitles", "Advanced", "Debug" };
        private const int TabGeneral = 0, TabSound = 1, TabCrew = 2, TabProbes = 3, TabSubtitles = 4, TabAdvanced = 5, TabDebug = 6;
        private int tab;
        private int pendingTab = -1;   // applied on the next Layout event
        private string pendingPos;     // subtitle position preset, applied on the next Layout event (it shows or hides sliders)
        private string pendingSignalMode; // signal mode, applied on the next Layout event (fixed shows a slider)
        private readonly Vector2[] scrolls = new Vector2[7];

        // phrase test (Debug tab).
        private static readonly string[] TestEvents =
        {
            "", "chatter", "relief", "staging", "countdown", "touchdown", "splashdown", "throttle_full", "throttle_cut",
            "plan_node", "burn_soon", "eva_check", "eva_fall", "station_check", "dock_approach", "dock_close", "dock_hard", "docked",
            "chute_semi", "chute_full",
            // v4 game events
            "soi_enter", "science", "part_lost", "power_low", "power_ok", "signal_lost", "signal_back", "crew_lost",
            // v5 probe events
            "probe_chat", "probe_sleepy",
            // v6 probe events
            "probe_hail", "probe_stress", "probe_say_again", "probe_milestone", "probe_rover", "probe_data",
        };
        // "" (any), the science topics and home (soi_enter)
        private static readonly string[] TestTopics = PhraseTable.TopicCycle();
        private static readonly string[] TestPhases =
        {
            "", "prelaunch", "launch", "ascent", "orbit", "coast", "reentry", "descent", "landed", "blackout", "serious", "goofy",
        };
        private static readonly string[] ForcePhases =
        {
            null, "prelaunch", "launch", "ascent", "orbit", "coast", "reentry", "descent", "landed", "blackout",
        };
        private static readonly string[] TestCauses =
        {
            "", "spin", "gforce", "q", "ground", "heat", "plasma", "fuel", "impulse", "engine_out",
        };
        private static readonly string[] TestBodies =
        {
            "", "sun", "moho", "eve", "gilly", "kerbin", "mun", "minmus", "duna", "ike", "dres", "jool", "laythe", "vall",
            "tylo", "bop", "pol", "eeloo", "generic",
        };
        private static readonly string[] AxisNames = { "X", "Y", "Z" };
        private string testEvent = "", testPhase = "", testCause = "", testBody = "", testTopic = "";
        private string testStatus = "";

        /// <summary>Button that cycles <paramref name="value"/> through <paramref name="list"/>. Empty shows as "any".</summary>
        private static string Cycle(string caption, string value, string[] list)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(caption, GUILayout.Width(60f));
            string shown = string.IsNullOrEmpty(value) ? "any" : value;
            if (GUILayout.Button(shown))
            {
                int i = Array.IndexOf(list, value);
                value = list[(i + 1) % list.Length];
            }
            GUILayout.EndHorizontal();
            return value;
        }

        private void DrawWindow(int id)
        {
            try
            {
                Settings s = host.settings;
                // Apply changes that add or remove controls only on a Layout event, so Layout and the following
                // events draw the same controls.
                if (Event.current.type == EventType.Layout)
                {
                    if (pendingTab >= 0) { tab = pendingTab; pendingTab = -1; GUI.FocusControl(null); }
                    if (pendingPos != null)
                    {
                        s.subtitlePos = pendingPos; pendingPos = null;
                        host.settingsDirty = true; pendingSave = true;
                    }
                    if (pendingSignalMode != null)
                    {
                        s.signalMode = pendingSignalMode; pendingSignalMode = null;
                        host.settingsDirty = true; pendingSave = true;
                    }
                }
                if (tab >= AllTabs.Length) tab = TabGeneral;
                int nt = GUILayout.SelectionGrid(tab, AllTabs, 4);
                if (nt != tab) pendingTab = nt;
                GUILayout.Space(4f);
                scrolls[tab] = GUILayout.BeginScrollView(scrolls[tab], GUILayout.Height(Math.Min(460f, Screen.height * 0.6f)));
                GUILayout.BeginVertical();
                switch (tab)
                {
                    case TabGeneral: DrawGeneral(s); break;
                    case TabSound: DrawSound(s); break;
                    case TabCrew: DrawCrew(s); break;
                    case TabProbes: DrawProbes(s); break;
                    case TabSubtitles: DrawSubtitles(s); break;
                    case TabAdvanced: DrawAdvanced(s); break;
                    default: DrawDebug(s); break;
                }
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                if (GUILayout.Button("Close"))
                {
                    if (button != null) button.SetFalse(true);
                    else OnFalse();
                }
            }
            catch (Exception e) { Log.Exception("SettingsWindow", e); }
            GUI.DragWindow();
        }

        // ------------------------------------------------ tab contents

        private void DrawGeneral(Settings s)
        {
            s.enabled = Toggle(s.enabled, "Enabled");
            s.muted = Toggle(s.muted, muteLabel);

            Header("Chatter");
            s.radioEnabled = Toggle(s.radioEnabled, "Radio with Mission Control");
            s.intercomEnabled = Toggle(s.intercomEnabled, "Intercom between kerbals");
            s.beepsEnabled = Toggle(s.beepsEnabled, "Telemetry beeps");
            s.rateMultiplier = Mathf.Round(Row("Radio amount", s.rateMultiplier, 0.25f, 4f, s.rateMultiplier, "0.0#'x'") * 20f) / 20f;
            Tuning tu = s.tuning;
            // TUNING/intercomInterval in seconds, shown in minutes
            float g = Row("Intercom gap", tu.intercomInterval, 30f, 1800f, tu.intercomInterval / 60f, "0.#' min'");
            tu.intercomInterval = Mathf.Round(g / 10f) * 10f;

            Header("Launch countdown");
            GUILayout.BeginHorizontal();
            bool onPad = FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.situation == Vessel.Situations.PRELAUNCH;
            bool running = host.countdown != null && host.countdown.Running;
            GUI.enabled = host.countdown != null && (running || onPad);
            if (GUILayout.Button(running ? "Stop" : "Start", GUILayout.Width(70f)))
            {
                if (running) host.countdown.Stop();
                else host.countdown.Start(FlightGlobals.ActiveVessel);
            }
            GUI.enabled = true;
            GUILayout.Label(capturing ? "Press a key" : "Key: " + host.CountdownKeyText);
            if (GUILayout.Button(capturing ? "Cancel" : "Change", GUILayout.Width(70f))) capturing = !capturing;
            GUILayout.EndHorizontal();
            s.countdownAutoStage = Toggle(s.countdownAutoStage, "Stage on liftoff");
        }

        private void DrawSound(Settings s)
        {
            s.masterVolume = Volume("Master", s.masterVolume);
            s.controlVolume = Volume("Mission Control", s.controlVolume);
            s.crewVolume = Volume("Crew", s.crewVolume);
            s.intercomVolume = Volume("Intercom", s.intercomVolume);
            s.calloutVolume = Volume("Callouts", s.calloutVolume, 2f);
            s.beepVolume = Volume("Beeps", s.beepVolume);
            s.staticVolume = Volume("Static", s.staticVolume);
            s.evaRadioVolume = Volume("EVA radio", s.evaRadioVolume);
            s.duckDepth = Row("Lower game audio", s.duckDepth, 0f, 0.8f, Mathf.Round(s.duckDepth * 100f), "0'%'");

            Header("Effects");
            s.rogerBeep = Toggle(s.rogerBeep, "Roger beep");
            s.masterAlarm = Toggle(s.masterAlarm, "Master alarm at very high stress");
        }

        private void DrawCrew(Settings s)
        {
            Header("Radio");
            s.throttleCallouts = Toggle(s.throttleCallouts, "Throttle callouts");
            s.planChatter = Toggle(s.planChatter, "Maneuver node talk");
            s.lightDelay = Toggle(s.lightDelay, "Answer delay far from Kerbin");

            Header("Mission Control reacts to");
            s.eventSoi = Toggle(s.eventSoi, "A new sphere of influence");
            s.eventScience = Toggle(s.eventScience, "Science");
            s.eventPartLoss = Toggle(s.eventPartLoss, "Lost parts");
            s.eventPower = Toggle(s.eventPower, "Low power");
            s.eventSignal = Toggle(s.eventSignal, "Lost and regained signal");
            s.eventCrewLoss = Toggle(s.eventCrewLoss, "A lost kerbal");

            Header("Kerbals");
            s.personalityEffects = Toggle(s.personalityEffects, "Personality");
            s.faceExpressions = Toggle(s.faceExpressions, "Facial expressions");
            s.headTurn = Toggle(s.headTurn, "Turn to the speaker");

            Header("EVA");
            s.evaRadio = Toggle(s.evaRadio, "Suit radio");
            s.evaRelay = Toggle(s.evaRelay, "Relay through a nearby ship");
        }

        private void DrawProbes(Settings s)
        {
            Tuning tu = s.tuning;
            // every control is always drawn, so Layout and Repaint see the same control count
            s.probeMode = Toggle(s.probeMode, "Probe mode");
            s.probePetTalk = Toggle(s.probePetTalk, "Mission Control talks to the probe");
            s.probeHail = Toggle(s.probeHail, "Probe calls Mission Control");
            s.probeSleep = Toggle(s.probeSleep, "Sleepy on low power");
            float h = Row("Call gap", tu.hailInterval, 60f, 1800f, tu.hailInterval / 60f, "0.#' min'");
            tu.hailInterval = Mathf.Round(h / 10f) * 10f;

            Header("Reactions");
            s.probeCallouts = Toggle(s.probeCallouts, "Staging, chutes and throttle");
            s.probePlanTalk = Toggle(s.probePlanTalk, "Maneuver nodes");
            s.probeStressTalk = Toggle(s.probeStressTalk, "Stress");
            s.probeMilestones = Toggle(s.probeMilestones, "Milestones");
            s.probeRover = Toggle(s.probeRover, "Rover driving");
            s.probeTransmit = Toggle(s.probeTransmit, "Sending science");
            s.probeSayAgain = Toggle(s.probeSayAgain, "\"Say again?\" on a weak signal");
        }

        private void DrawSubtitles(Settings s)
        {
            s.subtitles = Toggle(s.subtitles, "Show subtitles");
            GUI.enabled = s.subtitles;
            s.subtitleSpeaker = Toggle(s.subtitleSpeaker, "Speaker name");
            s.subtitleColors = Toggle(s.subtitleColors, "Colour per speaker");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Position", GUILayout.Width(118f));
            if (GUILayout.Button(SubtitleLayout.PresetCaption(pendingPos ?? s.subtitlePos)))
                pendingPos = SubtitleLayout.NextPreset(pendingPos ?? s.subtitlePos);
            GUILayout.EndHorizontal();
            if (s.subtitlePos == SubtitleLayout.Custom)
            {
                s.subtitleX = Mathf.Round(Row("Horizontal", s.subtitleX, 0f, 1f, Mathf.Round(s.subtitleX * 100f), "0'%'") * 100f) / 100f;
                s.subtitleY = Mathf.Round(Row("Vertical", s.subtitleY, 0f, 1f, Mathf.Round(s.subtitleY * 100f), "0'%'") * 100f) / 100f;
            }

            s.subtitleSize = Mathf.Round(Row("Text size", s.subtitleSize, 0.7f, 1.6f, Mathf.Round(s.subtitleSize * 100f), "0'%'") * 20f) / 20f;
            s.subtitleOpacity = Mathf.Round(Row("Background", s.subtitleOpacity, 0f, 1f, Mathf.Round(s.subtitleOpacity * 100f), "0'%'") * 20f) / 20f;
            int max = (int)Math.Round(Row("Lines", s.subtitleMaxEntries, 1f, Settings.SubtitleMaxEntriesCap, Mathf.Round(s.subtitleMaxEntries), "0"));
            if (max < 1) max = 1;
            if (max > Settings.SubtitleMaxEntriesCap) max = Settings.SubtitleMaxEntriesCap;
            s.subtitleMaxEntries = max;

            GUILayout.Space(6f);
            if (GUILayout.Button("Preview") && host.subtitles != null)
            {
                host.subtitles.settings = s;
                host.subtitles.ShowTest(FirstCrewName());
            }
            GUI.enabled = true;
        }

        /// <summary>Options most players never touch.</summary>
        private void DrawAdvanced(Settings s)
        {
            Tuning tu = s.tuning;
            ChatterDirector d = host.director;

            Header("Sound");
            s.landingTones = Toggle(s.landingTones, "Landing and splashdown tones");
            s.stageTone = Toggle(s.stageTone, "Stage tone");
            s.voiceBoostDb = Mathf.Round(Row("Voice boost", s.voiceBoostDb, 0f, 12f, Mathf.Round(s.voiceBoostDb), "0' dB'"));
            s.weakSignalVolume = Volume("Weak signal noise", s.weakSignalVolume);
            s.signalDropouts = Toggle(s.signalDropouts, "Dropouts on a weak signal");

            Header("Signal");
            GUILayout.BeginHorizontal();
            string[] modes = { "auto", "off", "fixed" };
            string shownMode = pendingSignalMode ?? s.signalMode;
            for (int i = 0; i < modes.Length; i++)
            {
                bool on = GUILayout.Toggle(shownMode == modes[i], modes[i], GUI.skin.button);
                // applied on the next Layout event: "fixed" adds a slider, the control count must not change mid-event
                if (on && shownMode != modes[i]) pendingSignalMode = modes[i];
            }
            GUILayout.EndHorizontal();
            if (s.signalMode == "fixed") s.signalFixed = Volume("Fixed signal", s.signalFixed);

            Header("Timing");
            // calm gap: TUNING/tQuiet, the mean interval at stress 0 before the rate multiplier
            float q = Row("Calm radio gap", tu.tQuiet, 20f, 600f, Mathf.Round(tu.tQuiet), "0' s'");
            tu.tQuiet = Mathf.Round(q / 5f) * 5f;
            tu.probeRateFactor = Mathf.Round(Row("Probe talk gap", tu.probeRateFactor, 0.5f, 4f, tu.probeRateFactor, "0.0#'x'") * 20f) / 20f;
            GUILayout.Label(d != null ? "Radio gap now: " + Label("radio now", Mathf.Round(d.currentInterval), "0' s'") : "Radio gap now: -");
            if (d != null)
            {
                float next = d.IntercomTimeToNext;
                GUILayout.Label(next < 0f ? "Next intercom: not now" : "Next intercom in " + Label("intercom next", Mathf.Round(next), "0' s'"));
            }
            else GUILayout.Label("Next intercom: -");

            Header("Line choice");
            tu.traitPreference = Mathf.Round(Row("Fit the job", tu.traitPreference, 0f, 1f, Mathf.Round(tu.traitPreference * 100f), "0'%'") * 20f) / 20f;
            tu.personaPreference = Mathf.Round(Row("Fit the character", tu.personaPreference, 0f, 1f, Mathf.Round(tu.personaPreference * 100f), "0'%'") * 20f) / 20f;

            Header("Probe");
            tu.probeDrowsyFrac = Mathf.Round(Row("Drowsy below", tu.probeDrowsyFrac, 0f, 0.5f, Mathf.Round(tu.probeDrowsyFrac * 100f), "0'%'") * 100f) / 100f;
            tu.probeSleepFrac = Mathf.Round(Row("Asleep below", tu.probeSleepFrac, 0f, 0.2f, Mathf.Round(tu.probeSleepFrac * 100f), "0'%'") * 100f) / 100f;
            if (tu.probeSleepFrac > tu.probeDrowsyFrac) tu.probeSleepFrac = tu.probeDrowsyFrac;
            tu.probeBeepPitchMax = Mathf.Round(Row("Max beep pitch", tu.probeBeepPitchMax, 1f, 2f, tu.probeBeepPitchMax, "0.0#'x'") * 20f) / 20f;

            GUILayout.Space(8f);
            if (GUILayout.Button("Reset timing to defaults")) host.ResetTuning();
        }

        private static string FirstCrewName()
        {
            try
            {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v == null) return null;
                List<ProtoCrewMember> crew = v.GetVesselCrew();
                return crew != null && crew.Count > 0 && crew[0] != null ? crew[0].name : null;
            }
            catch (Exception) { return null; }
        }

        private void DrawDebug(Settings s)
        {
            Tuning tu = s.tuning;
            ChatterDirector d = host.director;

            s.showDebug = Toggle(s.showDebug, "Debug overlay");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Test exchange")) host.TestExchange();
            if (GUILayout.Button("Dump kerbal bones"))
            {
                string r = KerbalDump.Run();
                try { ScreenMessages.PostScreenMessage("Lonapse: " + r, 6f); } catch (Exception) { }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Lonapse: " + PhraseClips.status);

            // --- talking mouths ---
            Header("Mouth");
            bool anim = Toggle(s.mouthAnimation, "Mouth animation");
            if (anim != s.mouthAnimation) { s.mouthAnimation = anim; if (host.mouth != null) host.mouth.Invalidate(); }
            s.mouthDebug = Toggle(s.mouthDebug, "Mouth debug");
            GUILayout.Label("Status: " + (host.mouth != null ? (host.mouth.Status ?? "") : "no mouth driver"));
            if (host.mouth != null) GUILayout.Label(host.mouth.PortraitStatus ?? "");
            bool refresh = Toggle(tu.portraitRefresh >= 0.5f, "Portraits: render again after the pose (end of frame)");
            tu.portraitRefresh = refresh ? 1f : 0f;
            if (host.portraits != null) GUILayout.Label(host.portraits.Status);
            bool alt = Toggle(tu.mouthPortraitAlternate >= 0.5f, "Portraits: wide/closed per portrait frame");
            tu.mouthPortraitAlternate = alt ? 1f : 0f;
            if (GUILayout.Button("Test mouth (3 s)") && host.mouth != null) host.mouth.TestMouth(3f);
            GUILayout.Label("Hinge axis:");
            int axis = Choice((int)Math.Round(tu.mouthAxis), AxisNames);
            if (axis != (int)Math.Round(tu.mouthAxis)) { tu.mouthAxis = axis; pendingSave = true; }
            bool inv = Toggle(tu.mouthInvert >= 0.5f, "Invert jaw direction");
            tu.mouthInvert = inv ? 1f : 0f;
            tu.mouthOpenDeg = Mathf.Round(Slider("Mouth open degrees", tu.mouthOpenDeg, 0f, 45f, "0") * 2f) / 2f;

            // --- faces and head turn (axis and sign are not verified offline: tune them live) ---
            Header("Faces and head turn");
            GUILayout.Label("Head turn axis:");
            int hax = Choice((int)Math.Round(tu.headTurnAxis), AxisNames);
            if (hax != (int)Math.Round(tu.headTurnAxis)) { tu.headTurnAxis = hax; host.settingsDirty = true; pendingSave = true; }
            bool hinv = Toggle(tu.headTurnInvert >= 0.5f, "Invert head turn direction");
            tu.headTurnInvert = hinv ? 1f : 0f;
            tu.headTurnMaxDeg = Mathf.Round(Slider("Head turn max degrees", tu.headTurnMaxDeg, 0f, 60f, "0"));
            tu.headTurnSpeed = Mathf.Round(Slider("Head turn speed", tu.headTurnSpeed, 0.1f, 30f, "0.0") * 10f) / 10f;
            tu.faceWeight = Mathf.Round(Slider("Face expression weight", tu.faceWeight, 0f, 1f, "0.00") * 20f) / 20f;
            bool flip = Toggle(tu.faceExpressionSign < 0f, "Flip expression sign (grin vs fear)");
            tu.faceExpressionSign = flip ? -1f : 1f;

            // --- phrase test ---
            Header("Phrase test");
            testEvent = Cycle("Event", testEvent, TestEvents);
            testPhase = Cycle("Phase", testPhase, TestPhases);
            testCause = Cycle("Cause", testCause, TestCauses);
            testBody = Cycle("Body", testBody, TestBodies);
            testTopic = Cycle("Topic", testTopic, TestTopics);
            if (GUILayout.Button("Play random line"))
            {
                if (d == null) testStatus = "no director";
                else
                {
                    try { testStatus = d.DebugPlayRandom(testEvent, testPhase, testCause, testBody, testTopic) ?? ""; }
                    catch (Exception e) { testStatus = "error: " + e.Message; Log.Exception("DebugPlayRandom", e); }
                }
            }
            GUILayout.Label(string.IsNullOrEmpty(testStatus) ? " " : testStatus);   // always drawn: keeps the control count stable

            GUILayout.Label("Current phase: " + (d != null ? (d.CurrentPhase ?? "-") : "-"));
            if (d != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Force phase", GUILayout.Width(80f));
                if (GUILayout.Button(d.forcePhase ?? "auto"))
                {
                    int i = Array.IndexOf(ForcePhases, d.forcePhase);
                    d.forcePhase = ForcePhases[(i + 1) % ForcePhases.Length];
                }
                GUILayout.EndHorizontal();
            }

            Header("Clips per folder (have / target)");
            for (int i = 0; i < folderLines.Length; i++)
                GUILayout.Label(folderLines[i], folderLow[i] ? lowStyle : okStyle);
        }
    }
}
