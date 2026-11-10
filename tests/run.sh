#!/usr/bin/env bash
# Compile the pure-math sources with plain Mono (no KSP, no Unity) and run the offline tests.
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$REPO/build/tests"
python3 "$REPO/tools/validate_dialogue.py"
mkdir -p "$OUT"
mcs -sdk:4.x -define:DC_OFFLINE_TEST -warn:4 -out:"$OUT/DcTests.exe" \
    "$REPO/src/DcMath.cs" "$REPO/src/StableHash.cs" "$REPO/src/StressModel.cs" \
    "$REPO/src/FlightSample.cs" "$REPO/src/CrewRoster.cs" "$REPO/src/Settings.cs" "$REPO/src/DockingWatcher.cs" "$REPO/src/Callouts.cs" \
    "$REPO/src/StagePoll.cs" "$REPO/src/Pacing.cs" "$REPO/src/MixMath.cs" "$REPO/src/SignalMath.cs" "$REPO/src/RadioProfile.cs" \
    "$REPO/src/DescentSequence.cs" "$REPO/src/ThrottleWatch.cs" "$REPO/src/Audit.cs" \
    "$REPO/src/PhraseTable.cs" "$REPO/src/Slots.cs" "$REPO/src/SubtitleSlots.cs" "$REPO/src/Countdown.cs" "$REPO/src/FlightPhase.cs" "$REPO/src/MouthMath.cs" "$REPO/src/Persona.cs" \
    "$REPO/src/EventWatchers.cs" "$REPO/src/SubtitleLayout.cs" "$REPO/src/RpmPageText.cs" "$REPO/src/CabinAmbience.cs" "$REPO/src/FaceMath.cs" "$REPO/src/ProbeCore.cs" "$REPO/src/ProbeSynth.cs" "$REPO/src/ProbeVoices.cs" "$REPO/src/SceneMemory.cs" "$REPO/src/RuntimeSpeech.cs" "$REPO/src/DialogueIds.cs" "$REPO/src/RuntimeLines.cs" "$REPO/src/ModEvents.cs" "$REPO/src/ProbeRules.cs" "$REPO/src/ProbeMoments.cs" \
    "$REPO/src/DialogueTable.cs" "$REPO/src/RuntimeSceneTable.cs" "$REPO/src/ProbeRuntimeTable.cs" "$REPO/src/ProbeContext.cs" "$REPO/src/RadioDsp.cs" "$REPO/src/RadioNoise.cs" "$REPO/tests/RadioDspTests.cs" "$REPO/tests/Tests.cs"
mono "$OUT/DcTests.exe"

# Scenario lifecycle regression: compile the real module against a small KSP contract stub.
mcs -sdk:4.x -warn:4 -out:"$OUT/ProbeHistoryTests.exe" \
    "$REPO/src/ProbeHistory.cs" "$REPO/tests/ProbeHistoryTests.cs"
mono "$OUT/ProbeHistoryTests.exe"

# Exercise the production sample gain stage without the Unity runtime.
mcs -sdk:4.x -warn:4 -out:"$OUT/GainDspTests.exe" \
    "$REPO/src/GainDsp.cs" "$REPO/tests/GainDspTests.cs"
mono "$OUT/GainDspTests.exe"
