// A powered empty crew pod can carry suit radio without being a controllable spacecraft.
// Stock CommNet suppresses its links through the command module's zero-crew multiplier.
// Calculate only the first radio hop in a private network, then use real CommNet routes to KSC.
using System;
using System.Reflection;
using CommNet;

namespace Lonapse
{
    public static class EvaRadioLink
    {
        private static readonly MethodInfo testOcclusion = typeof(CommNetwork).GetMethod("TestOcclusion", BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class RadioPair : CommNetwork
        {
            public bool ConnectRadio(CommNode from, CommNode to, double distance)
            {
                bool aRelay = from.antennaRelay.power > 0, bRelay = to.antennaRelay.power > 0;
                return TryConnect(from, to, distance, aRelay, bRelay, aRelay && bRelay);
            }
        }

        public static bool TryRead(Vessel ship, out double strength)
        {
            strength = 0;
            if (ship == null || ship.GetCrewCount() != 0 || ship.Connection == null || ship.Connection.InPlasma) return false;
            bool crewPod = false;
            foreach (Part part in ship.parts)
            {
                ModuleCommand command = part != null ? part.FindModuleImplementing<ModuleCommand>() : null;
                if (command != null && command.minimumCrew > 0) { crewPod = true; break; }
            }
            if (!crewPod) return false;
            PartResourceDefinition ec = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
            if (ec == null) return false;
            double available, capacity;
            ship.GetConnectedResourceTotals(ec.id, out available, out capacity, true);
            if (available <= 0 || double.IsNaN(available)) return false;
            CommNode origin = ship.Connection.Comm;
            if (origin == null || origin.Net == null || origin.antennaTransmit.power + origin.antennaRelay.power <= 0) return false;
            CommNetwork network = origin.Net;
            if (testOcclusion == null) return false;
            for (int i = 0; i < network.Count; i++)
            {
                CommNode target = network[i];
                if (target == null || ReferenceEquals(target, origin) || (!target.isHome && target.antennaRelay.power <= 0)) continue;
                double tail = 1;
                if (!target.isHome)
                {
                    var path = new CommPath();
                    if (!network.FindHome(target, path)) continue;
                    tail = path.signalStrength;
                }
                if (tail <= 0) continue;
                double distance = Math.Max(0, (origin.precisePosition - target.precisePosition).magnitude
                                              + origin.distanceOffset + target.distanceOffset);
                if (!(bool)testOcclusion.Invoke(network, new object[] { origin.precisePosition, origin.occluder, target.precisePosition, target.occluder, distance })) continue;
                var pair = new RadioPair();
                CommNode from = Copy(origin), to = Copy(target);
                // Keep the receiver's own power/plasma restrictions. Only the empty pod's crew requirement is bypassed.
                to.OnLinkCreateSignalModifier = other => target.GetSignalStrengthMultiplier(origin);
                pair.Add(from); pair.Add(to);
                if (!pair.ConnectRadio(from, to, Math.Max(1e-7, distance)) || pair.Links.Count == 0) continue;
                strength = Math.Max(strength, pair.Links[0].signalStrength * tail);
            }
            return strength > 0 && !double.IsNaN(strength);
        }

        private static CommNode Copy(CommNode source)
        {
            return new CommNode(source.transform) {
                precisePosition = source.precisePosition, distanceOffset = source.distanceOffset,
                antennaTransmit = source.antennaTransmit, antennaRelay = source.antennaRelay,
                isHome = source.isHome
            };
        }
    }
}
