using System;
using System.Collections.Generic;

namespace Lonapse
{
    public partial class ChatterDirector
    {
        // These are safety exchanges, so serious flight phases allow them. Calm reports keep their own gates.
        // Runtime voice + probe stress talk control this path; the calm report toggle does not suppress it.
        bool TryRuntimeProbeStress()
        {
            if (!RuntimeOn || !ProbeTense || !host.settings.probeStressTalk || !ProbeLink || ProbeAsleep) return false;
            Vessel v = ActiveVessel;
            if (v == null) return false;
            string cause = ProbeContext.StressCause(DominantCause);
            bool panic = host.stress.stress >= 0.75f;
            string band = panic ? "panic" : "tense";
            var nums = ProbeContext.StressValues(host.sample, host.stress.stress, panic);
            var names = new Dictionary<string, string[]>();
            names["probe"] = new[] { RuntimeSpeech.Respell(host.ProbeDisplayName), host.ProbeDisplayName };
            string[] body = BodyNames(BodyName);
            if (body != null) names["body"] = body;
            string es, en;
            switch (cause)
            {
                case "q": es = "{probe}, presión dinámica de {x_q_kpa} kilopascales. Vigilamos las cargas."; en = "{probe}, dynamic pressure {x_q_kpa} kilopascals. Monitoring the loads."; break;
                case "spin": es = "{probe}, giro de {x_spin_rpm} revoluciones por minuto. Vigilamos la actitud."; en = "{probe}, rotation {x_spin_rpm} revolutions per minute. Monitoring attitude."; break;
                case "heat": es = "{probe}, temperatura al {x_heat_pct} por ciento del límite. Vigilamos la estructura."; en = "{probe}, temperature at {x_heat_pct} percent of the limit. Monitoring the structure."; break;
                case "gforce": es = "{probe}, carga de {x_load_g}. Vigilamos las fuerzas."; en = "{probe}, load {x_load_g} g. Monitoring the forces."; break;
                case "fuel": es = "{probe}, combustible al {x_fuel_pct} por ciento. Revisamos las reservas."; en = "{probe}, fuel at {x_fuel_pct} percent. Checking reserves."; break;
                case "ground": es = "{probe}, altura {x_height_m} metros. Descenso {x_descent_ms} metros por segundo."; en = "{probe}, height {x_height_m} meters. Descent {x_descent_ms} meters per second."; break;
                case "plasma": es = "{probe}, el plasma afecta al enlace. Te escuchamos mientras podemos."; en = "{probe}, plasma is affecting the link. Listening while we can."; break;
                case "engine_out": es = "{probe}, detectamos un motor apagado. Revisamos el estado."; en = "{probe}, an engine has flamed out. Checking its status."; break;
                case "impulse": es = "{probe}, hemos visto la sacudida. Comprobamos la telemetría."; en = "{probe}, we saw the jolt. Checking telemetry."; break;
                default: es = "{probe}, estamos atentos a la telemetría. Seguimos contigo."; en = "{probe}, watching telemetry closely. We're still with you."; break;
            }
            WarmOnce(RuntimeDelivery.KindFor("mc", band, "chatter", ProbeRules.StressEvent));
            return ContextExchange("stress-" + cause, es, en, ProbeRules.ScaredMood, nums, names, false,
                null, null, null,
                () => host.settings.probeStressTalk && ProbeTense && (host.stress.stress >= 0.75f) == panic
                    && ProbeContext.StressCause(DominantCause) == cause
                    && ProbeContext.StressReadingFresh(nums, ProbeContext.StressValues(host.sample, host.stress.stress, panic), cause),
                null, cause, band);
        }
    }
}
