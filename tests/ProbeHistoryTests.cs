// Run the production history module against a minimal scenario lifecycle contract.
// Full KSP compilation separately checks the actual OnAwake override signature.
using System;
using System.Collections.Generic;
using System.Reflection;
using Lonapse;

public enum GameScenes { FLIGHT, SPACECENTER, TRACKSTATION }
public enum ScenarioCreationOptions { AddToAllGames }
public sealed class KSPScenario : Attribute
{
    public KSPScenario(ScenarioCreationOptions options, params GameScenes[] scenes) { }
}
public class ConfigNode
{
    readonly Dictionary<string, List<string>> values = new Dictionary<string, List<string>>();
    public void AddValue(string key, string value)
    {
        List<string> list;
        if (!values.TryGetValue(key, out list)) values[key] = list = new List<string>();
        list.Add(value);
    }
    public string[] GetValues(string key) { return values.ContainsKey(key) ? values[key].ToArray() : new string[0]; }
}
public class ScenarioModule
{
    string scenarioName;
    public void Awake() { scenarioName = GetType().Name; OnAwake(); }
    public virtual void OnAwake() { }
    public virtual void OnLoad(ConfigNode node) { }
    public virtual void OnSave(ConfigNode node) { }
    public ConfigNode Save()
    {
        if (scenarioName == null) throw new InvalidOperationException("Input is null for field 'name'");
        var node = new ConfigNode(); node.AddValue("name", scenarioName); OnSave(node); return node;
    }
}
public static class ProbeHistoryTests
{
    static void Require(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS  " + name); }
    static ProbeHistory Create()
    {
        var history = new ProbeHistory();
        // Unity message lookup must resolve the inherited Awake, not a hiding method.
        typeof(ProbeHistory).GetMethod("Awake").Invoke(history, null);
        return history;
    }
    public static int Main()
    {
        var first = Create();
        Require(typeof(ProbeHistory).GetMethod("Awake").DeclaringType == typeof(ScenarioModule), "probe history preserves the scenario Awake lifecycle");
        Require(typeof(ProbeHistory).GetMethod("OnAwake").GetBaseDefinition().DeclaringType == typeof(ScenarioModule), "probe history uses the scenario override hook");
        Require(ProbeHistory.Current == first, "probe history is available after initialization");
        first.Remember("v1:science:temperature@DunaSrfLandedMidlands");
        var saved = first.Save();
        Require(saved.GetValues("name")[0] == "ProbeHistory", "scenario save retains the initialized name");
        first.OnDestroy();
        var second = Create(); second.OnLoad(saved);
        Require(second.Has("v1:science:temperature@DunaSrfLandedMidlands"), "history facts round trip through scenario save/load");
        Require(!second.Has("v2:science:temperature@DunaSrfLandedMidlands"), "history facts remain vessel specific");
        var otherSave = Create(); otherSave.OnLoad(new ConfigNode());
        Require(!otherSave.Has("v1:science:temperature@DunaSrfLandedMidlands"), "loading another save clears history");
        second.OnDestroy();
        Require(ProbeHistory.Current == otherSave, "destroying an old scenario leaves the current scenario available");
        otherSave.OnDestroy();
        Require(ProbeHistory.Current == null, "destroying the current scenario clears its reference");
        return 0;
    }
}
