#!/usr/bin/env bash
# Compile the pure-math sources with plain Mono (no KSP, no Unity) and run the offline tests.
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$REPO/build/tests"
mkdir -p "$OUT"
mcs -sdk:4.x -define:DC_OFFLINE_TEST -warn:4 -out:"$OUT/DcTests.exe" \
    "$REPO/src/DcMath.cs" "$REPO/src/StableHash.cs" "$REPO/src/StressModel.cs" \
    "$REPO/src/FlightSample.cs" "$REPO/src/CrewRoster.cs" "$REPO/src/Settings.cs" "$REPO/src/DockingWatcher.cs" "$REPO/src/Callouts.cs" \
    "$REPO/src/Pacing.cs" "$REPO/src/MixMath.cs" "$REPO/src/SignalMath.cs" "$REPO/src/RadioProfile.cs" \
    "$REPO/src/DescentSequence.cs" "$REPO/src/ThrottleWatch.cs" "$REPO/src/Audit.cs" \
    "$REPO/src/PhraseTable.cs" "$REPO/src/SubtitleSlots.cs" "$REPO/src/Countdown.cs" "$REPO/src/FlightPhase.cs" "$REPO/src/MouthMath.cs" "$REPO/src/Persona.cs" \
    "$REPO/src/EventWatchers.cs" "$REPO/src/SubtitleLayout.cs" "$REPO/src/FaceMath.cs" "$REPO/src/ProbeCore.cs" "$REPO/src/ProbeSynth.cs" "$REPO/src/ProbeVoices.cs" "$REPO/src/SceneMemory.cs" "$REPO/src/ProbeRules.cs" "$REPO/src/ProbeMoments.cs" \
    "$REPO/tests/Tests.cs"
mono "$OUT/DcTests.exe"
