// Lonapse in-game API test: a made-up mod, ExampleMod, that uses the Lonapse API exactly as another mod would.
// It talks to Lonapse only through LonapseWrapper.cs (copied in by install.sh), so this DLL has no reference to
// Lonapse.dll. A window in flight has one button per test. Checks that can be answered at once say PASS or FAIL.
// What plays is heard in game and listed in the Lonapse Mods tab under "Last mod events".
// Not part of Lonapse, never shipped. Alt+F10 shows or hides the window.
using System;
using System.Collections.Generic;
using System.IO;
using Contracts;
using UnityEngine;

namespace ExampleMod
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ExampleModTest : MonoBehaviour
    {
        const string Repair = "examplemod.repair_done", Supplies = "examplemod.supplies_low", Experiment = "examplemod.experiment_done";
        const string ReloadEvent = "examplemod.reload_check";

        Rect win = new Rect(80, 100, 460, 10);
        bool show = true;
        Vector2 scroll;
        int days = 4;
        int pass, fail;
        readonly List<string> results = new List<string>();

        void Update()
        {
            if (Input.GetKey(KeyCode.LeftAlt) && Input.GetKeyDown(KeyCode.F10)) show = !show;
        }

        void OnGUI()
        {
            if (!show) return;
            win = GUILayout.Window(0x4C41504, win, Draw, "ExampleMod: Lonapse API test (Alt+F10)", GUILayout.Width(460));
        }

        void Check(string name, bool ok, string detail = null)
        {
            if (ok) pass++; else fail++;
            Add((ok ? "PASS  " : "FAIL  ") + name + (detail != null ? "  (" + detail + ")" : ""));
        }

        void Note(string text) { Add("....  " + text); }

        void Add(string line)
        {
            results.Insert(0, line);
            while (results.Count > 60) results.RemoveAt(results.Count - 1);
            Debug.Log("[ExampleMod] " + line);
        }

        static ProtoCrewMember ActiveKerbal()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return null;
            List<ProtoCrewMember> c = v.GetVesselCrew();
            return c.Count > 0 ? c[0] : null;
        }

        static string ReloadFile()
        {
            return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/ExampleMod/Lonapse/reload_test.tsv");
        }

        void Draw(int id)
        {
            GUILayout.Label("Passed " + pass + ", failed " + fail + ". Kerbal: " + (ActiveKerbal() != null ? ActiveKerbal().name : "none (a probe)"));

            if (GUILayout.Button("1. Status: installed, version, lines"))
            {
                Check("Installed", LonapseWrapper.Installed);
                Check("ApiVersion is 1", LonapseWrapper.ApiVersion == 1, "got " + LonapseWrapper.ApiVersion);
                Check("Available in flight", LonapseWrapper.Available);
                Check("HasLines " + Repair, LonapseWrapper.HasLines(Repair));
                Check("HasLines " + Supplies, LonapseWrapper.HasLines(Supplies));
                Check("HasLines " + Experiment, LonapseWrapper.HasLines(Experiment));
                Check("no HasLines for the experiment topic examplemod.soil", !LonapseWrapper.HasLines("examplemod.soil"));
                Check("no HasLines for an unknown event", !LonapseWrapper.HasLines("examplemod.nothing"));
            }

            if (GUILayout.Button("2. Repair done by the active kerbal, sent twice"))
            {
                var names = new Dictionary<string, string> { { "x_part", "la antena|the antenna" } };
                Check("first Trigger accepted", LonapseWrapper.Trigger(Repair, null, ActiveKerbal(), null, names));
                Check("second Trigger refused, already waiting", !LonapseWrapper.Trigger(Repair, null, ActiveKerbal(), null, names));
                Note("listen: an event chat about the antenna, or Mission Control with a reply");
            }

            if (GUILayout.Button("3. Repair again, after it played (cooldown 180 s)"))
                Check("Trigger refused by the cooldown", !LonapseWrapper.Trigger(Repair, null, ActiveKerbal()));

            if (GUILayout.Button("4. Supplies low: Trigger, then Cancel at once"))
            {
                var nums = new Dictionary<string, double> { { "x_days", 4 } };
                Check("Trigger accepted", LonapseWrapper.Trigger(Supplies, null, null, nums, null));
                Check("Cancel finds it waiting", LonapseWrapper.Cancel(Supplies));
                Check("second Cancel finds nothing", !LonapseWrapper.Cancel(Supplies));
                Note("nothing about supplies should play now");
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Days: " + days, GUILayout.Width(90))) days = days == 4 ? 1 : 4;
            if (GUILayout.Button("5. Supplies low (high priority, cooldown 600 s)"))
            {
                Check("Trigger accepted after the cancel (no cooldown)",
                      LonapseWrapper.Trigger(Supplies, null, null, new Dictionary<string, double> { { "x_days", days } }, null));
                Note(days == 1 ? "listen: one day of food, said without a plural" : "listen: supplies for 4 days");
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("6. Experiment done (who=mc, needs a link)"))
            {
                var names = new Dictionary<string, string> { { "x_experiment", "crecimiento de plantas|plant growth" }, { "days", "ignored" } };
                Check("Trigger accepted", LonapseWrapper.Trigger(Experiment, null, ActiveKerbal(), null, names));
                Note("listen: Mission Control about plant growth, a reply. KSP.log: value days ignored");
            }

            if (GUILayout.Button("7. Ids that Trigger must refuse"))
            {
                foreach (string bad in new[] { "examplemod.soil", "examplemod.drill", "examplemod.salvage", "examplemod.nothing", "ExampleMod.Bad", "flag", "", null })
                    Check("refused: " + (bad ?? "null"), !LonapseWrapper.Trigger(bad, null, null));
            }

            GUILayout.Label("Contracts (fired as KSP events, use a test save):");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("8. Salvage done")) FireContract(new ExampleSalvageContract(), true);
            if (GUILayout.Button("9. Salvage failed")) FireContract(new ExampleSalvageContract(), false);
            if (GUILayout.Button("10. Other type done")) FireContract(new ExampleOtherContract(), true);
            GUILayout.EndHorizontal();

            GUILayout.Label("Reload (Mods tab, Reload lines):");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("11. Write file")) WriteReloadFile();
            if (GUILayout.Button("12. Check after reload")) CheckReload();
            if (GUILayout.Button("13. Remove file"))
            {
                try { File.Delete(ReloadFile()); Note("reload_test.tsv removed, press Reload lines again"); }
                catch (Exception e) { Note("remove failed: " + e.Message); }
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("14. Check with ExampleMod turned off in the Mods tab"))
            {
                Check("no HasLines while muted", !LonapseWrapper.HasLines(Repair));
                Check("Trigger refused while muted", !LonapseWrapper.Trigger(Experiment, null, null));
            }

            if (GUILayout.Button("Clear results")) { results.Clear(); pass = fail = 0; }
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(220));
            GUILayout.Label(results.Count > 0 ? string.Join("\n", results.ToArray()) : "No results yet.");
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        void FireContract(TestContract c, bool done)
        {
            try
            {
                c.SetTestFunds(12000f, 3000f);
                if (done) GameEvents.Contract.onCompleted.Fire(c);
                else GameEvents.Contract.onFailed.Fire(c);
                Note(c.GetType().Name + (done ? " done" : " failed") + " fired, funds " + c.FundsCompletion + " / " + c.FundsFailure
                     + (c is ExampleSalvageContract ? ". Listen: a salvage line, usually (70%)" : ". Listen: a general contract line, no salvage words"));
            }
            catch (Exception e) { Note("contract test failed: " + e.Message); }
        }

        void WriteReloadFile()
        {
            try
            {
                File.WriteAllLines(ReloadFile(), new[] {
                    "id\tline\tscene\tseq\trole\ttrigger\tchannel\ton\twhen\tspanish\tenglish",
                    "examplemod_reload\texamplemod_reload\texamplemod_reload\t1\tmc\tevent\tradio\t" + ReloadEvent + "\t\tLa recarga funciona.\tThe reload works.",
                    "chat_examplemod_reloadsolo_1\tchat_examplemod_reloadsolo_1\tchat_examplemod_reloadsolo\t1\tcrew\tchat\tauto\t" + ReloadEvent + "\t\tLa recarga funciona.\tThe reload works." },
                    new System.Text.UTF8Encoding(false));
                Check("before the reload there are no lines yet", !LonapseWrapper.HasLines(ReloadEvent));
                Note("now press Reload lines in the Lonapse Mods tab, then 12");
            }
            catch (Exception e) { Note("write failed: " + e.Message); }
        }

        void CheckReload()
        {
            bool has = LonapseWrapper.HasLines(ReloadEvent);
            Check("HasLines after the reload", has, File.Exists(ReloadFile()) ? null : "the file is gone");
            if (has)
            {
                Check("Trigger accepted", LonapseWrapper.Trigger(ReloadEvent, null, ActiveKerbal()));
                Note("listen: \"The reload works.\"");
            }
        }
    }

    /// <summary>Test contracts: never offered by the contract system, only fired as events by the test window.</summary>
    public abstract class TestContract : Contract
    {
        protected override bool Generate() { return false; }
        public override bool MeetRequirements() { return false; }
        public override bool CanBeCancelled() { return true; }
        public override bool CanBeDeclined() { return true; }
        protected override string GetHashString() { return GetType().Name; }
        protected override string GetTitle() { return "Lonapse API test contract"; }
        public void SetTestFunds(float completion, float failure) { SetFunds(0f, completion, failure, null); }
    }

    /// <summary>Registered in example_mod.tsv (kind contract, id=ExampleSalvageContract).</summary>
    public class ExampleSalvageContract : TestContract { }

    /// <summary>Not registered: only the general contract lines fit it.</summary>
    public class ExampleOtherContract : TestContract { }
}
