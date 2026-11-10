using System;
using System.Collections.Generic;

namespace Lonapse
{
    // Save-local history: never shared between careers. Keys include vessel id and full science subject.
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class ProbeHistory : ScenarioModule
    {
        public static ProbeHistory Current;
        readonly HashSet<string> facts = new HashSet<string>();
        // ScenarioModule.Awake initializes the scenario name and save bookkeeping.
        // Use its extension hook so Unity still invokes that inherited initialization.
        public override void OnAwake() { base.OnAwake(); Current = this; }
        public void OnDestroy() { if (Current == this) Current = null; }
        public bool Has(string key) { return facts.Contains(key); }
        public bool Remember(string key) { return facts.Count < 20000 && facts.Add(key); }
        public void Forget(string key) { facts.Remove(key); }
        public override void OnLoad(ConfigNode node)
        {
            Current = this;
            facts.Clear();
            foreach (string fact in node.GetValues("fact")) if (facts.Count < 20000) facts.Add(fact);
        }
        public override void OnSave(ConfigNode node)
        {
            foreach (string fact in facts) node.AddValue("fact", fact);
        }
    }
}
