// Action group voicelines in the VAB and SPH: the Lonapse button there, the window that sets up the lines of the
// craft, and the preview. The lines are stored in the command parts of the craft (ModuleLonapseVoicelines).
// The preview asks the same Piper voices as a flight (RuntimeVoice.Shared, kept running by RuntimeVoiceHost) and
// plays the clip on a VoiceChannel with the radio or intercom sound of a flight. It has no key clicks, no quindar
// tones and no static bed, and a kerbal is not moved in pitch away from the rest of a crew.
using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;

namespace Lonapse
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public sealed class VoicelineEditor : MonoBehaviour
    {
        private const int WindowId = 0x44434802;
        private const string LockId = "Lonapse_voicelines", TypeLockId = "Lonapse_voicelines_typing";
        private const string TextField = "lonapse_voiceline_text", ReplyField = "lonapse_voiceline_reply";
        private const string BeepField = "lonapse_probe_beep";
        private const float WindowWidth = 560f;

        private Settings settings;
        private ApplicationLauncherButton button;
        private Texture icon;
        private bool visible, locked, typingLock, destroyed, started;
        private Rect rect = new Rect(320f, 90f, WindowWidth, 10f);
        private readonly UiClickBlocker uiBlocker = new UiClickBlocker("Lonapse_voicelines_blocker");
        private Vector2 listScroll, pickScroll;
        private GUIStyle headerStyle, hintStyle, lineStyle, offStyle, warnStyle, toggleStyle;

        // the craft
        private ActionVoiceConfig config = new ActionVoiceConfig();   // what the window shows
        private bool own;              // the craft stores voicelines of its own, else it follows the built-in ones
        private string stored = "";    // the data last read from or written to the parts
        private bool unsaved;          // changed since the craft was loaded or saved
        private int carriers;          // parts that can hold the lines
        private int group;             // index in ActionVoiceGroups.All
        private bool needReload = true, needSync;
        /// <summary>A click changes what the window shows. It runs after the window is drawn, so one pass of the
        /// window never sees two different sets of controls.</summary>
        private Action pending;

        // what the list shows, rebuilt when something changed
        private sealed class Row { public ActionVoiceLine line; public int index; public string detail; }
        private readonly List<Row> rows = new List<Row>();
        private string[] groupLabels = new string[0];
        private bool rowsDirty = true;

        // the line being written
        private ActionVoiceLine draft;
        private int draftIndex = -1;   // in config.Lines, -1 for a new line
        private bool pickingPreset;
        private List<ActionVoiceLine> presets = new List<ActionVoiceLine>();
        private readonly List<string> crewNames = new List<string>();
        private string[] crewLabels = new string[0];
        private string status = "";

        // preview
        private VoiceChannel crewCh, controlCh;
        private AudioClip previewClip;
        private int previewToken;
        private string previewStatus = "";

        private static readonly string[] SpeakerOptions = { "Mission Control", "Any kerbal", "One kerbal" };
        private static readonly string[] ModeOptions = { "Radio", "Intercom" };
        private static readonly string[] StateOptions = { "Switched on", "Switched off", "Both" };
        private static readonly string[] TabOptions = { "Voicelines", "Probe beeps" };
        private static readonly string[] MoodLabels = { "Happy", "Curious", "Proud", "Scared", "Bored", "Offended", "Sleepy" };
        private static readonly string[] MoodKeys = { "happy", "curious", "proud", "scared", "bored", "offended", "sleepy" };

        private int tab;   // 0 voicelines, 1 probe beeps
        // the probe beeps of the group, and the beep being written
        private sealed class BeepRow { public ProbeBeepLine beep; public int index; public string detail; }
        private readonly List<BeepRow> beepRows = new List<BeepRow>();
        private ProbeBeepLine beepDraft;
        private int beepDraftIndex = -1;   // in config.Beeps, -1 for a new beep
        private bool pickingBeepPreset;
        private List<ProbeBeepLine> beepPresets = new List<ProbeBeepLine>();
        private bool previewShared;   // the preview plays a clip of the clip library, which is not ours to free
        private readonly System.Random beepRng = new System.Random();

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            Log.SetMainThread();
            try
            {
                ProbeLinesetNodes.Load();   // before the settings, as in a flight
                settings = Settings.Load();
                if (settings.migrated) { settings.migrated = false; settings.Save(); }
            }
            catch (Exception e)
            {
                Log.Exception("VoicelineEditor.Awake", e);
                if (settings == null) settings = new Settings();
            }
        }

        private void Start()
        {
            try
            {
                // before the channels: a voice channel reads its microphone ambience from the clip library once per session
                ClipLibrary.Build();
                Tuning tu = settings.tuning;
                crewCh = new VoiceChannel(transform, "previewCrew", ChannelKind.Crew, 64, tu.crewHp, tu.crewLpCalm, 0f, true);
                controlCh = new VoiceChannel(transform, "previewControl", ChannelKind.Control, 80, tu.controlHp, tu.controlLp, tu.controlDistortion, false);
                GameEvents.onGUIApplicationLauncherReady.Add(CreateButton);
                GameEvents.onGUIApplicationLauncherDestroyed.Add(OnLauncherDestroyed);
                GameEvents.onEditorLoad.Add(OnEditorLoad);
                GameEvents.onEditorRestart.Add(OnEditorRestart);
                GameEvents.onEditorUndo.Add(OnEditorUndoRedo);
                GameEvents.onEditorRedo.Add(OnEditorUndoRedo);
                GameEvents.onEditorShipModified.Add(OnShipModified);
                GameEvents.onAboutToSaveShip.Add(OnAboutToSaveShip);
                started = true;
                if (ApplicationLauncher.Ready) CreateButton();
                Log.Info("Action group voicelines: editor window ready");
            }
            catch (Exception e) { Log.Exception("VoicelineEditor.Start", e); }
        }

        private void OnDestroy()
        {
            destroyed = true;
            try
            {
                GameEvents.onGUIApplicationLauncherReady.Remove(CreateButton);
                GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnLauncherDestroyed);
                GameEvents.onEditorLoad.Remove(OnEditorLoad);
                GameEvents.onEditorRestart.Remove(OnEditorRestart);
                GameEvents.onEditorUndo.Remove(OnEditorUndoRedo);
                GameEvents.onEditorRedo.Remove(OnEditorUndoRedo);
                GameEvents.onEditorShipModified.Remove(OnShipModified);
                GameEvents.onAboutToSaveShip.Remove(OnAboutToSaveShip);
            }
            catch (Exception e) { Log.Exception("VoicelineEditor.events", e); }
            try { RemoveButton(); } catch (Exception e) { Log.Exception("VoicelineEditor.button", e); }
            ReleaseLocks();
            uiBlocker.Destroy();
            // a preview that Piper still renders ends without a clip
            try { RuntimeVoice.Shared.DropCallbacks(); } catch (Exception) { }
            try
            {
                if (crewCh != null) crewCh.Destroy();
                if (controlCh != null) controlCh.Destroy();
                if (previewClip != null) Destroy(previewClip);
                previewClip = null;
            }
            catch (Exception e) { Log.Exception("VoicelineEditor.channels", e); }
        }

        private void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType type) { needReload = true; }
        private void OnEditorRestart() { needReload = true; }
        private void OnEditorUndoRedo(ShipConstruct ship) { needReload = true; }
        private void OnShipModified(ShipConstruct ship) { needSync = true; }
        private void OnAboutToSaveShip(ShipConstruct ship) { unsaved = false; }
        private void OnLauncherDestroyed() { button = null; }

        private void Update()
        {
            if (!started) return;
            try
            {
                if (pending != null) { Action a = pending; pending = null; a(); }
                if (needReload) { needReload = false; needSync = false; Reload(); }
                else if (needSync) { needSync = false; Sync(); }
                TickPreview();
            }
            catch (Exception e) { Log.Exception("VoicelineEditor.Update", e); }
        }

        // ---------------------------------------------------------------- button

        private void CreateButton()
        {
            if (button != null || destroyed) return;
            if (!ApplicationLauncher.Ready || ApplicationLauncher.Instance == null) return;
            visible = false;
            button = ApplicationLauncher.Instance.AddModApplication(
                () => { visible = true; needSync = true; }, Hide, Noop, Noop, Noop, Noop,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH, GetIcon());
        }

        private void RemoveButton()
        {
            if (button == null) return;
            if (ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button);
            button = null;
        }

        private static void Noop() { }

        private void Hide()
        {
            visible = false;
            ReleaseLocks();
            StopPreview();
        }

        private Texture GetIcon()
        {
            if (icon != null) return icon;
            try
            {
                GameDatabase db = GameDatabase.Instance;
                if (db != null && db.ExistsTexture("Lonapse/Icons/button")) icon = db.GetTexture("Lonapse/Icons/button", false);
            }
            catch (Exception e) { Log.Exception("VoicelineEditor.icon", e); }
            return icon ?? (icon = Texture2D.whiteTexture);
        }

        // ---------------------------------------------------------------- the craft

        private static List<Part> Parts()
        {
            try
            {
                EditorLogic ed = EditorLogic.fetch;
                return ed != null && ed.ship != null ? ed.ship.parts : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Reads the lines of the craft: a loaded craft, a new craft, an undo.</summary>
        private void Reload()
        {
            List<Part> parts = Parts();
            carriers = ModuleLonapseVoicelines.Carriers(parts);
            Adopt(ModuleLonapseVoicelines.Find(parts));
            unsaved = false;
            status = "";
        }

        private void Adopt(string data)
        {
            stored = data ?? "";
            ActionVoiceConfig c = ActionVoiceConfig.Decode(stored);
            if (c == null && stored.Length > 0) Log.Warn("Action group voicelines of this craft could not be read, the built-in lines are shown");
            own = c != null;
            config = c ?? ActionVoiceDefaults.Library.DefaultConfig();
            if (!config.HasBeeps)
            {
                // lines from before the probe beeps: a flight plays the built-in beeps, so the window shows them
                config.Beeps.AddRange(ActionVoiceDefaults.Library.DefaultBeeps());
                config.HasBeeps = true;
            }
            CloseDrafts();
            rowsDirty = true;
        }

        /// <summary>Nothing is being written or picked any more.</summary>
        private void CloseDrafts()
        {
            draft = null;
            pickingPreset = false;
            beepDraft = null;
            pickingBeepPreset = false;
        }

        /// <summary>The craft changed. A merged craft or subassembly can bring lines when this craft has none, and a
        /// new command part gets the copy the others hold.</summary>
        private void Sync()
        {
            List<Part> parts = Parts();
            carriers = ModuleLonapseVoicelines.Carriers(parts);
            string data = ModuleLonapseVoicelines.Find(parts);
            if (data.Length > 0 && data != stored) Adopt(data);
            if (own) ModuleLonapseVoicelines.WriteAll(parts, stored);
        }

        /// <summary>Stores what the window shows in every command part, as a step the editor can undo.</summary>
        private void Commit(string what)
        {
            own = true;
            stored = config.Encode();
            List<Part> parts = Parts();
            carriers = ModuleLonapseVoicelines.Carriers(parts);
            ModuleLonapseVoicelines.WriteAll(parts, stored);
            unsaved = true;
            rowsDirty = true;
            status = what;
            try { if (EditorLogic.fetch != null && carriers > 0) EditorLogic.fetch.SetBackup(); }
            catch (Exception e) { Log.Exception("VoicelineEditor.SetBackup", e); }
            Log.Info("Action group voicelines: " + what + " (" + config.Lines.Count + " lines and " + config.Beeps.Count + " probe beeps in " + carriers + " command part(s), " + stored.Length + " characters)");
        }

        /// <summary>The craft follows the built-in lines again.</summary>
        private void ForgetCraft()
        {
            List<Part> parts = Parts();
            ModuleLonapseVoicelines.WriteAll(parts, "");
            Adopt("");
            unsaved = true;
            status = "This craft follows the built-in lines again.";
            try { if (EditorLogic.fetch != null && carriers > 0) EditorLogic.fetch.SetBackup(); }
            catch (Exception e) { Log.Exception("VoicelineEditor.SetBackup", e); }
        }

        private ActionGroupDef Def { get { return ActionVoiceGroups.All[Mathf.Clamp(group, 0, ActionVoiceGroups.All.Length - 1)]; } }

        private void BuildRows()
        {
            rowsDirty = false;
            ActionGroupDef[] defs = ActionVoiceGroups.All;
            if (groupLabels.Length != defs.Length) groupLabels = new string[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                int n = tab == 1 ? config.BeepCount(defs[i].Key) : config.Count(defs[i].Key);
                groupLabels[i] = n > 0 ? defs[i].Label + " (" + n + ")" : defs[i].Label;
            }
            rows.Clear();
            ActionGroupDef def = Def;
            for (int i = 0; i < config.Lines.Count; i++)
            {
                ActionVoiceLine l = config.Lines[i];
                if (l.Group != def.Key) continue;
                string who = l.IsMc ? "Mission Control" : l.Speaker == ActionVoiceLine.SpeakerKerbal ? Names.Localize(l.Kerbal) : "Any kerbal";
                string when = !def.OnOff ? "" : l.State == ActionVoiceLine.StateOn ? ", when switched on"
                              : l.State == ActionVoiceLine.StateOff ? ", when switched off" : ", on and off";
                rows.Add(new Row { line = l, index = i,
                                   detail = who + ", " + (l.Intercom ? "intercom" : "radio") + when + (l.BuiltIn ? ", built-in" : ", custom")
                                            + (l.WantsReply ? ", Mission Control replies" : "") });
            }
            beepRows.Clear();
            for (int i = 0; i < config.Beeps.Count; i++)
            {
                ProbeBeepLine b = config.Beeps[i];
                if (b.Group != def.Key) continue;
                string when = !def.OnOff ? "" : b.State == ActionVoiceLine.StateOn ? ", when switched on"
                              : b.State == ActionVoiceLine.StateOff ? ", when switched off" : ", on and off";
                beepRows.Add(new BeepRow { beep = b, index = i,
                                           detail = MoodLabel(b.Mood) + " beep" + when + (b.ReplyOn ? ", Mission Control replies" : "") });
            }
        }

        private static string MoodLabel(string mood)
        {
            int i = Array.IndexOf(MoodKeys, mood);
            return i >= 0 ? MoodLabels[i] : "No";
        }

        // ---------------------------------------------------------------- window

        private void OnGUI()
        {
            if (!started || !visible) { uiBlocker.Hide(); if (locked || typingLock) ReleaseLocks(); return; }
            try
            {
                if (headerStyle == null)
                {
                    headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                    headerStyle.normal.textColor = new Color(0.95f, 0.78f, 0.35f);
                    hintStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
                    hintStyle.normal.textColor = new Color(0.68f, 0.72f, 0.78f);
                    hintStyle.margin = new RectOffset(4, 4, 0, 4);
                    lineStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                    offStyle = new GUIStyle(lineStyle);
                    offStyle.normal.textColor = new Color(0.55f, 0.55f, 0.55f);
                    warnStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                    warnStyle.normal.textColor = new Color(1f, 0.6f, 0.3f);
                    toggleStyle = new GUIStyle(GUI.skin.toggle);
                }
                // a click outside the window ends the typing, so the editor gets its keys back
                Event ev = Event.current;
                if (typingLock && ev.type == EventType.MouseDown && !rect.Contains(ev.mousePosition)) GUIUtility.keyboardControl = 0;
                Rect r = GUILayout.Window(WindowId, rect, DrawWindow, "Lonapse: Action Group Voicelines", GUILayout.Width(WindowWidth));
                r.x = Mathf.Clamp(r.x, 0f, Math.Max(0f, Screen.width - 60f));
                r.y = Mathf.Clamp(r.y, 0f, Math.Max(0f, Screen.height - 40f));
                rect = r;
                uiBlocker.Cover(rect);
                SetLock(rect.Contains(Event.current.mousePosition));
            }
            catch (Exception e) { Log.Exception("VoicelineEditor.OnGUI", e); }
        }

        /// <summary>The mouse is over the window: no part is picked or placed through it.</summary>
        private void SetLock(bool over)
        {
            if (over == locked) return;
            try
            {
                if (over) InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK | ControlTypes.CAMERACONTROLS, LockId);
                else InputLockManager.RemoveControlLock(LockId);
                locked = over;
            }
            catch (Exception e) { Log.Exception("VoicelineEditor.SetLock", e); }
        }

        /// <summary>While the text field has the keyboard the editor gets no keys.</summary>
        private void UpdateTypingLock()
        {
            string focus = GUI.GetNameOfFocusedControl();
            bool typing = visible && (draft != null && (focus == TextField || focus == ReplyField)
                                      || beepDraft != null && (focus == BeepField || focus == ReplyField));
            if (typing == typingLock) return;
            try
            {
                if (typing) InputLockManager.SetControlLock(ControlTypes.All, TypeLockId);
                else InputLockManager.RemoveControlLock(TypeLockId);
                typingLock = typing;
            }
            catch (Exception e) { Log.Exception("VoicelineEditor.TypingLock", e); }
        }

        private void ReleaseLocks()
        {
            try
            {
                if (locked) InputLockManager.RemoveControlLock(LockId);
                if (typingLock) InputLockManager.RemoveControlLock(TypeLockId);
            }
            catch (Exception) { }
            locked = false;
            typingLock = false;
        }

        private void Hint(string text) { GUILayout.Label(text, hintStyle); }

        private void Header(string text)
        {
            GUILayout.Space(6f);
            GUILayout.Label(text, headerStyle);
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

        private void DrawWindow(int id)
        {
            try
            {
                if (rowsDirty && Event.current.type == EventType.Layout) BuildRows();
                bool canStore = carriers > 0;
                if (!canStore) GUILayout.Label("This craft has no command pod or probe core yet. The voicelines are stored in those parts, so add one first.", warnStyle);
                else if (!own) Hint("This craft uses the built-in lines. Change anything here and it gets lines of its own.");
                else if (unsaved) GUILayout.Label("Changed. The lines are in the craft now, save the craft to keep them.", warnStyle);
                else Hint("This craft has its own voicelines. Nothing changed since it was loaded or saved.");

                int nt = Choice(tab, TabOptions);
                if (nt != tab) { int t = nt; pending = () => { tab = t; CloseDrafts(); status = ""; rowsDirty = true; listScroll = Vector2.zero; GUIUtility.keyboardControl = 0; }; }

                Header("Action group");
                int ng = GUILayout.SelectionGrid(group, groupLabels, 6);
                if (ng != group) { int g = ng; pending = () => { group = g; CloseDrafts(); rowsDirty = true; listScroll = Vector2.zero; }; }
                ActionGroupDef def = Def;

                if (tab == 1) DrawBeeps(def, canStore);
                else DrawVoicelines(def, canStore);

                GUILayout.Space(4f);
                if (!string.IsNullOrEmpty(status)) Hint(status);
                GUILayout.BeginHorizontal();
                GUILayout.Label(string.IsNullOrEmpty(previewStatus) ? "Preview: nothing playing" : "Preview: " + previewStatus, hintStyle);
                if (GUILayout.Button("Stop preview", GUILayout.Width(100f))) pending = StopPreviewQuiet;
                if (GUILayout.Button("Close", GUILayout.Width(70f)))
                {
                    if (button != null) button.SetFalse(true);
                    else Hide();
                }
                GUILayout.EndHorizontal();
            }
            catch (Exception e) { Log.Exception("VoicelineEditor.Window", e); }
            UpdateTypingLock();
            GUI.DragWindow();
        }

        private void DrawVoicelines(ActionGroupDef def, bool canStore)
        {
            {
                Header("Voicelines (one is picked at random)");
                listScroll = GUILayout.BeginScrollView(listScroll, false, true, GUILayout.Height(Math.Min(250f, Screen.height * 0.3f)));
                if (rows.Count == 0) Hint("No voicelines for this group");
                for (int i = 0; i < rows.Count; i++) DrawRow(rows[i], canStore);
                GUILayout.EndScrollView();

                if (draft != null) DrawDraft(def, canStore);
                else if (pickingPreset) DrawPresets(canStore);
                else DrawActions(def, canStore);
            }
        }

        // ---------------------------------------------------------------- probe beeps

        private void DrawBeeps(ActionGroupDef def, bool canStore)
        {
            Hint("A probe has no kerbal to say a voiceline. On a craft without crew the probe core gives one of these beeps instead, "
                 + "and Mission Control can reply. A crewed craft uses the voicelines. A group without a beep plays its Mission Control voicelines.");
            Header("Probe beeps (one is picked at random)");
            listScroll = GUILayout.BeginScrollView(listScroll, false, true, GUILayout.Height(Math.Min(250f, Screen.height * 0.3f)));
            if (beepRows.Count == 0) Hint("No probe beeps for this group");
            for (int i = 0; i < beepRows.Count; i++) DrawBeepRow(beepRows[i], canStore);
            GUILayout.EndScrollView();

            if (beepDraft != null) DrawBeepDraft(def, canStore);
            else if (pickingBeepPreset) DrawBeepPresets(canStore);
            else DrawBeepActions(def, canStore);
        }

        private void DrawBeepRow(BeepRow row, bool canStore)
        {
            ProbeBeepLine b = row.beep;
            GUILayout.BeginHorizontal();
            GUI.enabled = canStore;
            bool on = GUILayout.Toggle(b.Enabled, "", toggleStyle, GUILayout.Width(20f));
            GUI.enabled = true;
            if (on != b.Enabled) pending = () => { b.Enabled = on; Commit(on ? "Beep switched on." : "Beep switched off."); };
            GUILayout.BeginVertical();
            GUILayout.Label(b.Subtitle, b.Enabled ? lineStyle : offStyle);
            GUILayout.Label(row.detail, hintStyle);
            GUILayout.EndVertical();
            if (GUILayout.Button("Preview", GUILayout.Width(64f))) pending = () => PreviewBeep(b);
            GUI.enabled = canStore;
            if (GUILayout.Button("Edit", GUILayout.Width(44f)))
            {
                int index = row.index;
                pending = () => { beepDraft = b.Clone(); beepDraftIndex = index; pickingBeepPreset = false; status = ""; };
            }
            if (GUILayout.Button("Delete", GUILayout.Width(54f)))
                pending = () => { config.Beeps.Remove(b); config.HasBeeps = true; beepDraft = null; Commit("Beep deleted."); };
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawBeepActions(ActionGroupDef def, bool canStore)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = canStore;
            if (GUILayout.Button("+ Add beep"))
                pending = () =>
                {
                    beepDraft = new ProbeBeepLine { Group = Def.Key, Mood = MoodKeys[0], ReplyOn = true,
                                                    State = Def.OnOff ? ActionVoiceLine.StateOn : ActionVoiceLine.StateAny };
                    beepDraftIndex = -1;
                    status = "";
                };
            if (GUILayout.Button("Choose preset"))
                pending = () => { beepPresets = ActionVoiceDefaults.Library.BeepPresets(Def); pickingBeepPreset = true; pickScroll = Vector2.zero; status = ""; };
            if (GUILayout.Button("Restore defaults")) pending = RestoreBeeps;
            GUI.enabled = canStore && own;
            if (GUILayout.Button("Reset whole craft")) pending = ForgetCraft;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            Hint(def.Custom ? "Restore defaults empties this group: a custom group has no built-in beeps of its own, only presets."
                            : "Restore defaults puts the built-in beeps of " + def.Label + " back. Reset whole craft drops every change of this craft.");
        }

        private void RestoreBeeps()
        {
            ActionGroupDef def = Def;
            config.RemoveBeeps(def.Key);
            if (!def.Custom) config.Beeps.AddRange(ActionVoiceDefaults.Library.BeepPresets(def));
            config.HasBeeps = true;
            Commit(def.Custom ? def.Label + " has no beeps again." : "The built-in beeps of " + def.Label + " are back.");
        }

        private void DrawBeepPresets(bool canStore)
        {
            Header("Presets");
            pickScroll = GUILayout.BeginScrollView(pickScroll, false, true, GUILayout.Height(130f));
            if (beepPresets.Count == 0) Hint("No presets for this group.");
            for (int i = 0; i < beepPresets.Count; i++)
            {
                ProbeBeepLine p = beepPresets[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(p.Subtitle + "  (" + MoodLabel(p.Mood).ToLowerInvariant() + ")", lineStyle);
                if (GUILayout.Button("Preview", GUILayout.Width(64f))) pending = () => PreviewBeep(p);
                GUI.enabled = canStore;
                if (GUILayout.Button("Add", GUILayout.Width(44f)))
                    pending = () =>
                    {
                        if (config.Beeps.Count >= ActionVoiceConfig.MaxBeeps) { status = "A craft holds at most " + ActionVoiceConfig.MaxBeeps + " probe beeps."; return; }
                        ProbeBeepLine n = p.Clone();
                        n.Enabled = true;
                        config.Beeps.Add(n);
                        config.HasBeeps = true;
                        Commit("Preset added.");
                    };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Done")) pending = () => { pickingBeepPreset = false; };
        }

        private void DrawBeepDraft(ActionGroupDef def, bool canStore)
        {
            ProbeBeepLine d = beepDraft;
            Header(beepDraftIndex < 0 ? "New probe beep" : "Edit probe beep");
            int cur = Array.IndexOf(MoodKeys, d.Mood);
            int pick = GUILayout.SelectionGrid(cur, MoodLabels, 7);
            if (pick != cur && pick >= 0) d.Mood = MoodKeys[pick];

            GUILayout.BeginHorizontal();
            GUILayout.Label("Subtitle: [beeps", GUILayout.Width(110f));
            GUI.SetNextControlName(BeepField);
            string typed = GUILayout.TextField(d.Words ?? "", ProbeBeepLine.MaxText, GUILayout.Width(200f));
            if (typed != d.Words) d.Words = ProbeBeepLine.Filter(typed);
            GUILayout.Label("]", GUILayout.Width(12f));
            GUILayout.EndHorizontal();
            Hint("1 to " + ProbeBeepLine.MaxWords + " lowercase words. Empty: " + ProbeRules.BeepSubtitle(d.Mood) + ".");

            if (def.OnOff)
            {
                int st = d.State == ActionVoiceLine.StateOn ? 0 : d.State == ActionVoiceLine.StateOff ? 1 : 2;
                int nst = Choice(st, StateOptions);
                if (nst != st) d.State = nst == 0 ? ActionVoiceLine.StateOn : nst == 1 ? ActionVoiceLine.StateOff : ActionVoiceLine.StateAny;
            }

            bool ron = GUILayout.Toggle(d.ReplyOn, " Mission Control replies");
            if (ron != d.ReplyOn) d.ReplyOn = ron;
            GUI.enabled = d.ReplyOn;
            GUI.SetNextControlName(ReplyField);
            string rtyped = GUILayout.TextField(d.Reply ?? "", ActionVoiceLine.MaxText);
            if (rtyped != d.Reply) d.Reply = ActionVoiceLine.Filter(rtyped);
            GUI.enabled = true;
            Hint("The reply, in English. Leave it empty for a standard reply like \"Copy that.\"");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Preview beep")) pending = () => PreviewBeep(BeepCopy(d));
            GUI.enabled = d.ReplyOn;
            if (GUILayout.Button("Preview reply")) pending = () => Preview(ActionVoiceDefaults.Library.ReplyFor(BeepCopy(d), previewRng));
            GUI.enabled = canStore;
            if (GUILayout.Button("Save beep")) pending = SaveBeepDraft;
            GUI.enabled = true;
            if (GUILayout.Button("Cancel")) pending = () => { beepDraft = null; status = ""; };
            GUILayout.EndHorizontal();
        }

        /// <summary>The draft as it would be stored: a changed reply is no longer the built-in one, so Mission
        /// Control says the new text.</summary>
        private ProbeBeepLine BeepCopy(ProbeBeepLine d)
        {
            ProbeBeepLine c = d.Clone();
            ProbeBeepLine was = beepDraftIndex >= 0 && beepDraftIndex < config.Beeps.Count ? config.Beeps[beepDraftIndex] : null;
            c.Normalise();
            if (was == null || was.Reply != c.Reply) c.ReplySpanish = "";
            return c;
        }

        private void SaveBeepDraft()
        {
            if (beepDraft == null) return;
            ProbeBeepLine n = BeepCopy(beepDraft);
            if (!n.HasMood) { status = "Pick a mood first."; return; }
            if (beepDraftIndex >= 0 && beepDraftIndex < config.Beeps.Count) config.Beeps[beepDraftIndex] = n;
            else if (config.Beeps.Count >= ActionVoiceConfig.MaxBeeps) { status = "A craft holds at most " + ActionVoiceConfig.MaxBeeps + " probe beeps."; return; }
            else config.Beeps.Add(n);
            config.HasBeeps = true;
            GUIUtility.keyboardControl = 0;
            beepDraft = null;
            Commit(beepDraftIndex >= 0 ? "Beep changed." : "Beep added.");
        }

        /// <summary>The probe core of the craft for the preview: the first stock core, else generic.</summary>
        private static string CraftCore()
        {
            var names = new List<string>();
            try
            {
                List<Part> parts = Parts();
                if (parts != null)
                    foreach (Part p in parts)
                    {
                        if (p == null || p.partInfo == null) continue;
                        ModuleCommand mc = p.FindModuleImplementing<ModuleCommand>();
                        if (mc != null && mc.minimumCrew == 0) names.Add(p.partInfo.name);
                    }
            }
            catch (Exception) { }
            return ProbeCores.Pick(names) ?? ProbeCores.Generic;
        }

        private void PreviewBeep(ProbeBeepLine b)
        {
            StopPreview();
            if (b == null || !b.HasMood) { previewStatus = "no beep to play yet"; return; }
            string mood = b.Mood;
            if (!settings.enabled || settings.muted) { previewStatus = "Lonapse is " + (settings.muted ? "muted" : "disabled") + " in its settings"; return; }
            string core = CraftCore();
            AudioClip clip = ClipLibrary.Next(ProbeCores.Folder(core, mood), beepRng);
            if (clip == null) { previewStatus = "no beep sound for " + core + " " + mood; return; }
            RadioProfile p = RadioProfiles.Get(RadioContext.Ship);
            VoiceChannel c = controlCh;
            c.SetBand(p.HighPassHz, p.LowPassHz);
            c.Play(clip, 0.7f, 1f, false, 0f);
            c.SetRadioVoice(RadioVoice.Control);
            c.boost = 1f;
            c.baseDistortion = Math.Max(0.08f, 0.5f * p.Distortion);
            previewShared = true;
            previewStatus = ProbeCores.DisplayName(core) + ", " + b.Subtitle + " (stock sound of the core)";
        }

        private void DrawRow(Row row, bool canStore)
        {
            ActionVoiceLine l = row.line;
            GUILayout.BeginHorizontal();
            GUI.enabled = canStore;
            bool on = GUILayout.Toggle(l.Enabled, "", toggleStyle, GUILayout.Width(20f));
            GUI.enabled = true;
            if (on != l.Enabled) pending = () => { l.Enabled = on; Commit(on ? "Line switched on." : "Line switched off."); };
            GUILayout.BeginVertical();
            GUILayout.Label(l.Text, l.Enabled ? lineStyle : offStyle);
            GUILayout.Label(row.detail, hintStyle);
            GUILayout.EndVertical();
            if (GUILayout.Button("Preview", GUILayout.Width(64f))) pending = () => Preview(l);
            GUI.enabled = canStore;
            if (GUILayout.Button("Edit", GUILayout.Width(44f)))
            {
                int index = row.index;
                pending = () => { draft = l.Clone(); draftIndex = index; pickingPreset = false; BuildCrew(); status = ""; };
            }
            if (GUILayout.Button("Delete", GUILayout.Width(54f)))
                pending = () => { config.Lines.Remove(l); draft = null; Commit("Line deleted."); };
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawActions(ActionGroupDef def, bool canStore)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = canStore;
            if (GUILayout.Button("+ Add voiceline"))
                pending = () =>
                {
                    draft = new ActionVoiceLine { Group = Def.Key, ReplyOn = true, State = Def.OnOff ? ActionVoiceLine.StateOn : ActionVoiceLine.StateAny };
                    draftIndex = -1;
                    BuildCrew();
                    status = "";
                };
            if (GUILayout.Button("Choose preset"))
                pending = () => { presets = ActionVoiceDefaults.Library.Presets(Def); pickingPreset = true; pickScroll = Vector2.zero; status = ""; };
            if (GUILayout.Button("Restore defaults")) pending = RestoreGroup;
            GUI.enabled = canStore && own;
            if (GUILayout.Button("Reset whole craft")) pending = ForgetCraft;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            Hint(def.Custom ? "Restore defaults empties this group: a custom group has no built-in lines of its own, only presets."
                            : "Restore defaults puts the built-in lines of " + def.Label + " back. Reset whole craft drops every change of this craft.");
        }

        private void RestoreGroup()
        {
            ActionGroupDef def = Def;
            config.RemoveGroup(def.Key);
            if (!def.Custom) config.Lines.AddRange(ActionVoiceDefaults.Library.Presets(def));
            Commit(def.Custom ? def.Label + " is empty again." : "The built-in lines of " + def.Label + " are back.");
        }

        private void DrawPresets(bool canStore)
        {
            Header("Presets");
            pickScroll = GUILayout.BeginScrollView(pickScroll, false, true, GUILayout.Height(130f));
            if (presets.Count == 0) Hint("No presets for this group.");
            for (int i = 0; i < presets.Count; i++)
            {
                ActionVoiceLine p = presets[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(p.Text, lineStyle);
                if (GUILayout.Button("Preview", GUILayout.Width(64f))) pending = () => Preview(p);
                GUI.enabled = canStore;
                if (GUILayout.Button("Add", GUILayout.Width(44f)))
                    pending = () =>
                    {
                        if (config.Lines.Count >= ActionVoiceConfig.MaxLines) { status = "A craft holds at most " + ActionVoiceConfig.MaxLines + " voicelines."; return; }
                        ActionVoiceLine n = p.Clone();
                        n.Enabled = true;
                        config.Lines.Add(n);
                        Commit("Preset added.");
                    };
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Done")) pending = () => { pickingPreset = false; };
        }

        private void DrawDraft(ActionGroupDef def, bool canStore)
        {
            ActionVoiceLine d = draft;
            // typing or a click further up changes which reply controls fit. One pass of the window draws one set.
            if (Event.current.type == EventType.Layout)
                replyShape = d.IsMc || d.Intercom ? 0 : PreviewCopy(d).BuiltIn ? 1 : 2;
            Header(draftIndex < 0 ? "New voiceline" : "Edit voiceline");
            GUI.SetNextControlName(TextField);
            string typed = GUILayout.TextField(d.Text ?? "", ActionVoiceLine.MaxText);
            if (typed != d.Text) d.Text = ActionVoiceLine.Filter(typed);
            Hint("Write what the subtitle shows, in English. " + (d.Text ?? "").Length + " of " + ActionVoiceLine.MaxText + " characters.");
            Hint(SaidHint(d));

            int sp = d.IsMc ? 0 : d.Speaker == ActionVoiceLine.SpeakerKerbal ? 2 : 1;
            int nsp = Choice(sp, SpeakerOptions);
            if (nsp != sp)
                pending = () =>
                {
                    d.Speaker = nsp == 0 ? ActionVoiceLine.SpeakerMc : nsp == 2 ? ActionVoiceLine.SpeakerKerbal : ActionVoiceLine.SpeakerAuto;
                    if (nsp == 0) d.Intercom = false;
                    if (nsp == 2 && string.IsNullOrEmpty(d.Kerbal) && crewNames.Count > 0) d.Kerbal = crewNames[0];
                };
            if (d.Speaker == ActionVoiceLine.SpeakerKerbal)
            {
                if (crewLabels.Length == 0) Hint("No kerbals to choose from.");
                else
                {
                    int cur = crewNames.IndexOf(d.Kerbal);
                    pickScroll = GUILayout.BeginScrollView(pickScroll, false, true, GUILayout.Height(78f));
                    int pick = GUILayout.SelectionGrid(cur, crewLabels, 3);
                    GUILayout.EndScrollView();
                    if (pick != cur && pick >= 0) d.Kerbal = crewNames[pick];
                }
                Hint("A star marks the crew of this craft. When the kerbal is not aboard in flight, another kerbal says the line. Without crew it stays silent, so give a probe a beep in the Probe beeps tab.");
            }

            if (d.IsMc) Hint("Mission Control talks on the radio. Its lines also play on a probe, when there is a signal.");
            else
            {
                int mode = d.Intercom ? 1 : 0;
                int nmode = Choice(mode, ModeOptions);
                if (nmode != mode) d.Intercom = nmode == 1;
            }
            if (def.OnOff)
            {
                int st = d.State == ActionVoiceLine.StateOn ? 0 : d.State == ActionVoiceLine.StateOff ? 1 : 2;
                int nst = Choice(st, StateOptions);
                if (nst != st) d.State = nst == 0 ? ActionVoiceLine.StateOn : nst == 1 ? ActionVoiceLine.StateOff : ActionVoiceLine.StateAny;
            }

            if (replyShape == 1) Hint("Mission Control always replies to a built-in line. Change the text and the line is a custom one, with a reply you set.");
            else if (replyShape == 2)
            {
                bool ron = GUILayout.Toggle(d.ReplyOn, " Mission Control replies");
                if (ron != d.ReplyOn) d.ReplyOn = ron;
                GUI.enabled = d.ReplyOn;
                GUI.SetNextControlName(ReplyField);
                string rtyped = GUILayout.TextField(d.Reply ?? "", ActionVoiceLine.MaxText);
                if (rtyped != d.Reply) d.Reply = ActionVoiceLine.Filter(rtyped);
                GUI.enabled = true;
                Hint("The reply, in English. Leave it empty for a standard reply like \"Copy that.\"");
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Preview voice")) pending = () => Preview(PreviewCopy(d));
            GUI.enabled = replyShape != 0 && (replyShape == 1 || d.ReplyOn);
            if (GUILayout.Button("Preview reply")) pending = () => Preview(ActionVoiceDefaults.Library.ReplyFor(PreviewCopy(d), previewRng));
            GUI.enabled = canStore;
            if (GUILayout.Button("Save line")) pending = SaveDraft;
            GUI.enabled = true;
            if (GUILayout.Button("Cancel")) pending = () => { draft = null; status = ""; };
            GUILayout.EndHorizontal();
        }

        private string saidFor, saidHint = "";
        private int replyShape;   // 0 no reply possible, 1 built-in line, 2 custom line
        private readonly System.Random previewRng = new System.Random();

        /// <summary>What the voice will say for the draft, before it is turned around. Worked out again only when
        /// the text changes.</summary>
        private string SaidHint(ActionVoiceLine d)
        {
            ActionVoiceLine was = draftIndex >= 0 && draftIndex < config.Lines.Count ? config.Lines[draftIndex] : null;
            bool builtIn = was != null && was.BuiltIn && was.Text == ActionVoiceLine.Clean(d.Text) && was.Spanish.Length > 0;
            string key = (builtIn ? "b" : "t") + d.Text;
            if (key == saidFor) return saidHint;
            saidFor = key;
            string said = builtIn ? was.Spanish : RuntimeSpeech.Unmarked(ActionVoiceDefaults.Dictionary.Translate(ActionVoiceLine.Clean(d.Text)));
            saidHint = said.Length == 0 ? "The voice says it in Spanish, turned into loñapsE."
                       : "The voice says, in loñapsE: " + said + (builtIn ? "" : " (words Lonapse does not know stay as typed)");
            return saidHint;
        }

        /// <summary>The draft as it would be stored: a changed text is no longer the built-in line, so the voice says
        /// the new text.</summary>
        private ActionVoiceLine PreviewCopy(ActionVoiceLine d)
        {
            ActionVoiceLine c = d.Clone();
            ActionVoiceLine was = draftIndex >= 0 && draftIndex < config.Lines.Count ? config.Lines[draftIndex] : null;
            c.Normalise();
            if (was == null || !was.BuiltIn || was.Text != c.Text) { c.BuiltIn = false; c.Spanish = ""; }
            return c;
        }

        private void SaveDraft()
        {
            if (draft == null) return;
            ActionVoiceLine n = PreviewCopy(draft);
            if (n.Text.Length == 0) { status = "Type a line first."; return; }
            if (draftIndex >= 0 && draftIndex < config.Lines.Count) config.Lines[draftIndex] = n;
            else if (config.Lines.Count >= ActionVoiceConfig.MaxLines) { status = "A craft holds at most " + ActionVoiceConfig.MaxLines + " voicelines."; return; }
            else config.Lines.Add(n);
            GUIUtility.keyboardControl = 0;
            draft = null;
            Commit(draftIndex >= 0 ? "Line changed." : "Line added.");
        }

        /// <summary>The kerbals a line can name: the crew assigned to this craft first (with a star), then the rest
        /// of the roster that can fly, then the kerbal the line already names when it is in neither.</summary>
        private void BuildCrew()
        {
            crewNames.Clear();
            var labels = new List<string>();
            pickScroll = Vector2.zero;
            try
            {
                VesselCrewManifest m = ShipConstruction.ShipManifest;
                if (m != null)
                    foreach (ProtoCrewMember p in m.GetAllCrew(false))
                        if (p != null && !crewNames.Contains(p.name)) { crewNames.Add(p.name); labels.Add(Names.Localize(p.name) + " *"); }
            }
            catch (Exception e) { Log.Debug("Voicelines: crew manifest not read (" + e.Message + ")"); }
            try
            {
                foreach (ProtoCrewMember p in HighLogic.CurrentGame.CrewRoster.Crew)
                    if (p != null && p.rosterStatus == ProtoCrewMember.RosterStatus.Available && !crewNames.Contains(p.name))
                    { crewNames.Add(p.name); labels.Add(Names.Localize(p.name)); }
            }
            catch (Exception e) { Log.Debug("Voicelines: roster not read (" + e.Message + ")"); }
            if (draft != null && !string.IsNullOrEmpty(draft.Kerbal) && !crewNames.Contains(draft.Kerbal))
            { crewNames.Insert(0, draft.Kerbal); labels.Insert(0, Names.Localize(draft.Kerbal) + " (not available)"); }
            crewLabels = labels.ToArray();
        }

        // ---------------------------------------------------------------- preview

        private static ProtoCrewMember FindCrew(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || HighLogic.CurrentGame == null) return null;
                foreach (ProtoCrewMember p in HighLogic.CurrentGame.CrewRoster.Crew) if (p != null && p.name == name) return p;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>The voice a kerbal line is previewed with: the chosen kerbal, else the first of the crew of this
        /// craft, else the first of the roster, else a stand-in. The same name gives the same voice as in a flight.</summary>
        private KerbalVoice PreviewVoice(ActionVoiceLine l, out string note)
        {
            note = "";
            ProtoCrewMember p = l.Speaker == ActionVoiceLine.SpeakerKerbal ? FindCrew(l.Kerbal) : null;
            if (p == null)
            {
                bool wanted = l.Speaker == ActionVoiceLine.SpeakerKerbal;
                try
                {
                    VesselCrewManifest m = ShipConstruction.ShipManifest;
                    if (m != null) foreach (ProtoCrewMember c in m.GetAllCrew(false)) if (c != null) { p = c; break; }
                    if (p == null)
                        foreach (ProtoCrewMember c in HighLogic.CurrentGame.CrewRoster.Crew)
                            if (c != null && c.rosterStatus == ProtoCrewMember.RosterStatus.Available) { p = c; break; }
                }
                catch (Exception) { }
                if (p == null)
                {
                    note = ", a stand-in voice (no kerbals found)";
                    return KerbalVoice.Create("Jebediah Kerman", 0.5f, 0.5f, false, false);
                }
                note = wanted ? ", " + Names.Localize(l.Kerbal) + " is not available so " + Names.Localize(p.name) + " says it"
                              : ", said by " + Names.Localize(p.name);
            }
            KerbalVoice k = KerbalVoice.Create(p.name, p.courage, p.stupidity, p.isBadass, p.gender == ProtoCrewMember.Gender.Female);
            k.displayName = Names.Localize(p.name);
            return k;
        }

        private void Preview(ActionVoiceLine l)
        {
            StopPreview();
            string template = l != null ? ActionVoiceDefaults.SpokenFor(l) : "";
            if (template.Trim().Length == 0) { previewStatus = "nothing to say yet"; return; }
            if (!settings.enabled || settings.muted) { previewStatus = "Lonapse is " + (settings.muted ? "muted" : "disabled") + " in its settings"; return; }
            int gi = ActionVoiceGroups.IndexOf(l.Group);
            bool urgent = gi >= 0 && ActionVoiceGroups.All[gi].Urgent;
            bool mc = l.IsMc, intercom = !mc && l.Intercom;
            string note = "";
            KerbalVoice kv = mc ? null : PreviewVoice(l, out note);
            // the same voice kind as in a flight: Abort is said in the tense delivery
            string kind = RuntimeDelivery.KindFor(mc ? "mc" : kv.isFemale ? "f" : "m", urgent ? "tense" : "calm", urgent ? "warning" : "chatter", "chatter");
            string missing = kind != null ? RuntimeVoice.Missing(kind) : "no voice";
            if (missing != null) { previewStatus = "no runtime voice (" + missing + ")"; return; }
            int token = ++previewToken;
            string who = mc ? "Mission Control" : intercom ? "intercom" : "radio";
            previewStatus = "generating speech";
            RuntimeVoice.Shared.Request(kind, template, r =>
            {
                if (destroyed || token != previewToken) { if (r.clip != null) Destroy(r.clip); return; }
                if (r.clip == null)
                {
                    previewStatus = "speech failed (" + (r.error ?? "no sound") + ")";
                    return;
                }
                try
                {
                    PlayPreview(r.clip, kv, intercom);
                    previewStatus = who + note + ", ready in " + r.queueToReadyMs.ToString("0") + " ms";
                    Log.Info("Voiceline preview: \"" + r.spoken + "\" (" + who + note + "), ready in " + r.queueToReadyMs.ToString("0")
                             + " ms (Piper " + r.piperMs.ToString("0") + " ms)");
                }
                catch (Exception e)
                {
                    Destroy(r.clip);
                    previewClip = null;
                    previewStatus = "playback failed (" + e.Message + ")";
                    Log.Exception("VoicelineEditor.PlayPreview", e);
                }
            });
        }

        /// <summary>The channel setup of ChatterDirector.Start for a calm crew, intercom or Mission Control line.</summary>
        private void PlayPreview(AudioClip clip, KerbalVoice kv, bool intercom)
        {
            Tuning tu = settings.tuning;
            RadioProfile p = RadioProfiles.Get(RadioContext.Ship);
            float boost = MixMath.DbToLin(settings.voiceBoostDb);
            previewClip = clip;
            if (kv == null)
            {
                VoiceChannel c = controlCh;
                c.SetBand(p.HighPassHz, p.LowPassHz);
                c.Play(clip, DcMath.McVolume, 1f, false, 0f);
                c.SetRadioVoice(RadioVoice.Control);
                c.boost = boost;
                c.compressor = settings.voiceCompressor;
                c.baseDistortion = Math.Max(tu.controlDistortion, p.Distortion);
                return;
            }
            VoiceChannel v = crewCh;
            float pitch = ChatterDirector.CrewPlaybackPitch(tu, kv, kv.Pitch, 0f, 0f, false);
            v.SetPreset(intercom, tu, intercom && settings.intercomRadioFx);
            v.Play(clip, DcMath.CrewVolume(0f), pitch, false, 0f);
            v.boost = boost;
            v.compressor = settings.voiceCompressor;
            v.tremRate = kv.tremRate;
            if (intercom && !v.radioFx) { v.SetIntercomFilters(kv.timbre, 0f, tu); return; }
            v.SetCrewFilters(0f, kv.timbre, 0f, tu);
            v.CapLowPass(settings.useDsp ? 3000f : p.LowPassHz);
            v.SetRadioVoice(RadioVoice.Headset);
            if (v.radioFx) v.baseDistortion = Math.Max(v.baseDistortion, 0.5f * p.Distortion);
        }

        private void StopPreviewQuiet() { StopPreview(); previewStatus = ""; }

        /// <summary>Ends the preview that plays or is still being rendered. A new preview replaces the old one.</summary>
        private void StopPreview()
        {
            previewToken++;
            if (crewCh != null) crewCh.Stop();
            if (controlCh != null) controlCh.Stop();
            if (previewClip != null) Destroy(previewClip);
            previewClip = null;
            previewShared = false;
        }

        private void TickPreview()
        {
            if (crewCh == null || controlCh == null) return;
            float dt = Math.Min(Math.Max(0f, Time.unscaledDeltaTime), 0.1f);
            float game = 1f;
            try { if (settings.useGameVolume) game = DcMath.Clamp01(GameSettings.VOICE_VOLUME * GameSettings.MASTER_VOLUME); }
            catch (Exception) { }
            float mul = settings.masterVolume * game;
            crewCh.SetDsp(settings.useDsp);
            controlCh.SetDsp(settings.useDsp);
            crewCh.Update(dt, crewCh.intercom ? settings.intercomVolume : settings.crewVolume, mul);
            controlCh.Update(dt, settings.controlVolume, mul);
            if ((previewClip != null || previewShared) && !crewCh.Busy && !controlCh.Busy)
            {
                // a clip of Piper is ours alone: free it when the line is over. A beep belongs to the clip library.
                if (previewClip != null) Destroy(previewClip);
                previewClip = null;
                previewShared = false;
                previewStatus = "";
            }
        }
    }
}
