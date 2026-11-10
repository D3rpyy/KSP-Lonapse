// Run the production provider against vessel/network contracts, including inactive ships with stale link caches.
using System;
using System.Collections.Generic;
using Lonapse;

class SignalProviderTests
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS  " + message);
    }

    static Vessel Eva(double x)
    {
        return new Vessel { isEVA = true, position = new Vector3d(x, 0, 0), Connection = new Connection() };
    }

    static void Main()
    {
        var config = new Settings();
        var eva = Eva(10);
        var otherEva = Eva(20);
        var network = new CommNet.CommNetwork { home = true, strength = 0.9 };
        var ship = new Vessel { vesselName = "Empty lander", Connection = new Connection {
            // Cached connection has not caught up since the last kerbal went outside.
            IsConnected = false, SignalStrength = 0,
            Comm = new CommNet.CommNode { Net = network } } };
        ship.parts.Add(new Part { command = new ModuleCommand() });
        FlightGlobals.VesselsLoaded.AddRange(new[] { eva, otherEva, ship });
        var provider = new SignalProvider();
        provider.Update(1, eva, config);
        Check(provider.connected && Math.Abs(provider.signal - 0.9f) < 1e-6 && provider.relayName == "Empty lander",
              "EVA uses the empty ship's actual KSC route despite a stale disconnected cache");
        Check(!ship.Connection.IsConnected && ship.Connection.SignalStrength == 0 && network.queries == 1,
              "reading an EVA relay does not change the ship's cached connection or control state");
        provider.Reset(); provider.Update(1, otherEva, config);
        Check(provider.connected && provider.relayName == ship.vesselName,
              "switching between EVA kerbals keeps the nearby ship link");
        network.home = false;
        ship.Connection.IsConnected = true; ship.Connection.SignalStrength = 1;
        provider.Update(1, otherEva, config);
        Check(!provider.connected && provider.signal == 0,
              "a stale connected ship cache cannot invent a KSC route when the network is disconnected");
        network.home = true; network.strength = 0.8;
        otherEva.position = new Vector3d(1650, 0, 0);
        provider.Update(1, otherEva, config);
        Check(provider.connected && Math.Abs(provider.signal - 0.4f) < 1e-5,
              "the actual ship link still fades with suit distance");
        otherEva.position = new Vector3d(2400, 0, 0);
        provider.Update(1, otherEva, config);
        Check(!provider.connected && provider.relayName == null, "EVA outside suit range cannot use the ship link");
        otherEva.position = new Vector3d(10, 0, 0);
        config.evaRelay = false;
        provider.Update(1, otherEva, config);
        Check(!provider.connected, "the EVA relay setting is respected");
        config.evaRelay = true;
        ship.Connection.Comm = null;
        provider.Update(1, otherEva, config);
        Check(provider.connected && provider.signal == 1, "an unavailable network node falls back to readable ship status");
        ship.Connection.IsConnected = false; ship.Connection.SignalStrength = 0;
        otherEva.Connection.IsConnected = true; otherEva.Connection.SignalStrength = 0.7;
        provider.Update(1, otherEva, config);
        Check(provider.connected && Math.Abs(provider.signal - 0.7f) < 1e-6, "an EVA kerbal keeps its own better connection");
        otherEva.Connection.IsConnected = false; otherEva.Connection.SignalStrength = 0;
        ship.crewCount = 0; ship.electricCharge = 10;
        var origin = new CommNet.CommNode { antennaTransmit = new CommNet.CommNode.AntennaInfo { power = 5000 },
            OnLinkCreateSignalModifier = other => 0 }; // Stock disables the command link when its crew leaves.
        var home = new CommNet.CommNode { isHome = true, precisePosition = new Vector3d(100, 0, 0),
            antennaRelay = new CommNet.CommNode.AntennaInfo { power = 1e12 } };
        ship.Connection.Comm = origin;
        network.Add(origin); network.Add(home);
        provider.Update(1, otherEva, config);
        Check(provider.connected && provider.signal > 0.99f && origin.GetSignalStrengthMultiplier(home) == 0 && network.Links.Count == 0,
              "powered empty crew pod relays suit radio without enabling its command link or editing the live network");
        ship.electricCharge = 0;
        provider.Update(1, otherEva, config);
        Check(!provider.connected, "an empty ship without electric charge cannot relay suit radio");
        ship.electricCharge = 10; network.visible = false;
        provider.Update(1, otherEva, config);
        Check(!provider.connected, "an empty ship's suit radio respects stock body occlusion");
        network.visible = true; home.precisePosition = new Vector3d(1e12, 0, 0);
        provider.Update(1, otherEva, config);
        Check(!provider.connected, "an empty ship's suit radio respects antenna range");
        home.precisePosition = new Vector3d(100, 0, 0); home.isHome = false; network.home = false;
        provider.Update(1, otherEva, config);
        Check(!provider.connected, "a reachable relay without a path home cannot connect an empty ship to KSC");
        network.home = true; network.strength = 0.5;
        provider.Update(1, otherEva, config);
        Check(provider.connected && Math.Abs(provider.signal - 0.5f) < 1e-6,
              "an empty ship can reach KSC through a real relay and inherits its remaining path strength");
        home.isHome = true; ship.crewCount = 1;
        provider.Update(1, otherEva, config);
        Check(!provider.connected, "the empty-pod radio exception does not override ordinary crewed vessel restrictions");
        ship.crewCount = 0; origin.antennaTransmit.power = 0;
        provider.Update(1, otherEva, config);
        Check(!provider.connected, "an empty pod needs an available antenna to relay suit radio");
        Check(Log.errors.Count == 0, "EVA provider regression scenarios produce no provider errors");
    }
}

namespace UnityEngine { }
public struct Vector3d
{
    public double x, y, z;
    public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    public double magnitude { get { return Math.Sqrt(x * x + y * y + z * z); } }
    public Vector3d normalized { get { double m = magnitude; return m > 0 ? new Vector3d(x / m, y / m, z / m) : this; } }
    public static Vector3d operator -(Vector3d a, Vector3d b) { return new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z); }
    public static Vector3d operator +(Vector3d a, Vector3d b) { return new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z); }
    public static Vector3d operator *(Vector3d a, double b) { return new Vector3d(a.x * b, a.y * b, a.z * b); }
}
public enum VesselType { Ship, Debris, Flag, SpaceObject }
public class Vessel
{
    public bool isEVA, loaded = true;
    public string vesselName;
    public VesselType vesselType;
    public Guid id = Guid.NewGuid();
    public Vector3d position;
    public CelestialBody mainBody;
    public readonly List<Part> parts = new List<Part>();
    public Connection Connection;
    public int crewCount = 1;
    public double electricCharge;
    public int GetCrewCount() { return crewCount; }
    public void GetConnectedResourceTotals(int id, out double amount, out double capacity, bool flow) { amount = electricCharge; capacity = 10; }
    public Vector3d GetWorldPos3D() { return position; }
}
public class Connection
{
    public bool IsConnected;
    public double SignalStrength;
    public CommNet.CommNode Comm;
    public bool InPlasma;
}
namespace CommNet
{
    public class CommPath { public double signalStrength; }
    public class Occluder { }
    public class CommLink { public double signalStrength; }
    public class CommNode
    {
        public class AntennaInfo { public double power; }
        public AntennaInfo antennaTransmit = new AntennaInfo(), antennaRelay = new AntennaInfo();
        public CommNetwork Net;
        public bool isHome;
        public double distanceOffset;
        public Vector3d precisePosition;
        public object transform;
        public Occluder occluder;
        public Func<CommNode, double> OnLinkCreateSignalModifier;
        public CommNode() { }
        public CommNode(object transform) { this.transform = transform; }
        public double GetSignalStrengthMultiplier(CommNode other) { return OnLinkCreateSignalModifier != null ? OnLinkCreateSignalModifier(other) : 1; }
    }
    public class CommNetwork
    {
        public bool home;
        public double strength;
        public int queries;
        public bool visible = true;
        readonly List<CommNode> nodes = new List<CommNode>();
        public readonly List<CommLink> Links = new List<CommLink>();
        public int Count { get { return nodes.Count; } }
        public CommNode this[int i] { get { return nodes[i]; } }
        public void Add(CommNode node) { node.Net = this; nodes.Add(node); }
        public bool FindHome(CommNode node, CommPath path)
        {
            queries++;
            bool connected = home && node.GetSignalStrengthMultiplier(node) > 0;
            path.signalStrength = connected ? strength : 0;
            return connected;
        }
        protected bool TestOcclusion(Vector3d a, Occluder ao, Vector3d b, Occluder bo, double distance) { return visible; }
        protected bool TryConnect(CommNode a, CommNode b, double distance, bool ar, bool br, bool both)
        {
            double range = Math.Sqrt(Math.Max(a.antennaTransmit.power, a.antennaRelay.power) * b.antennaRelay.power);
            double signal = a.GetSignalStrengthMultiplier(b) * b.GetSignalStrengthMultiplier(a);
            if (distance > range || range <= 0 || signal <= 0) return false;
            Links.Add(new CommLink { signalStrength = signal });
            return true;
        }
    }
}
public class Part
{
    public ModuleCommand command;
    public T FindModuleImplementing<T>() where T : class { return command as T; }
}
public class ModuleCommand { public int minimumCrew = 1; }
public class PartResourceDefinition { public int id; }
public class PartResourceLibrary
{
    public static readonly PartResourceLibrary Instance = new PartResourceLibrary();
    public PartResourceDefinition GetDefinition(string name) { return new PartResourceDefinition(); }
}
public class CelestialBody
{
    public Vector3d position;
    public double Radius;
    public double TerrainAltitude(double lat, double lon) { return 0; }
    public Vector3d GetWorldSurfacePosition(double lat, double lon, double alt) { return position; }
}
public static class FlightGlobals
{
    public static readonly List<Vessel> VesselsLoaded = new List<Vessel>();
    public static CelestialBody GetHomeBody() { return null; }
}
public static class HighLogic { public static readonly Game CurrentGame = new Game(); }
public class Game { public readonly Parameters Parameters = new Parameters(); }
public class Parameters { public readonly Difficulty Difficulty = new Difficulty(); }
public class Difficulty { public bool EnableCommNet = true; }
public static class AssemblyLoader
{
    public class LoadedAssembly { public string name; public System.Reflection.Assembly assembly; }
    public static readonly List<LoadedAssembly> loadedAssemblies = new List<LoadedAssembly>();
}
namespace Lonapse
{
    public class Settings
    {
        public readonly Tuning tuning = new Tuning();
        public string signalMode = "auto";
        public float signalFixed = 1;
        public bool evaRelay = true, signalOcclusion;
    }
    public class Tuning
    {
        public float signalPollSec = 0.1f, signalHoldTime, signalStaticMax = 1;
        public float evaRelayFullKm = 1, evaRelayZeroKm = 2.3f;
        public float signalRangeFullKm = 500, signalRangeZeroKm = 5000;
    }
    public static class DcMath { public static float Clamp01(float value) { return Math.Max(0, Math.Min(1, value)); } }
    public static class Log
    {
        public static readonly List<string> errors = new List<string>();
        public static void Info(string message) { }
        public static void WarnOnce(string key, string message) { errors.Add(message); }
    }
}
