// Stage-number fallback. Docking changes can renumber stages and remove parts without firing a stage.
namespace Lonapse
{
    public enum StagePollResult { None, Fired, Reordered }

    public sealed class StagePoll
    {
        public const float ProofWindow = 1.5f;
        private int lastStage = int.MinValue;
        private int parts = -1, engines = -1;
        private float lastEvent = -999f, pendingAt, settleUntil = -999f;
        public bool Pending { get; private set; }
        public int From { get; private set; }
        public int To { get; private set; }

        public void Reset(int stage)
        {
            lastStage = stage;
            parts = engines = -1;
            Pending = false;
        }

        public void TopologyChanged(float now)
        {
            Reset(int.MinValue);
            settleUntil = now + ProofWindow;
        }

        public bool Settling(float now) { return now <= settleUntil; }
        public bool NeedsProof(int stage) { return Pending || lastStage != int.MinValue && stage < lastStage; }

        public void Scan(int partCount, int ignited)
        {
            if (Pending) return;
            parts = partCount;
            engines = ignited;
        }

        public void Activated(float now)
        {
            lastEvent = now;
            Pending = false;
        }

        public StagePollResult Poll(float now, int stage, int partCount, int ignited)
        {
            if (stage == int.MinValue) return StagePollResult.None;
            if (Settling(now))
            {
                Reset(stage);
                Scan(partCount, ignited);
                return StagePollResult.None;
            }
            if (lastStage != int.MinValue && stage < lastStage && now - lastEvent > 1f && !Pending)
            {
                From = lastStage;
                To = stage;
                pendingAt = now;
                Pending = true;
            }
            lastStage = stage;
            if (!Pending) return StagePollResult.None;
            if (lastEvent >= pendingAt - 1f)
            {
                Pending = false;
                return StagePollResult.None;
            }
            StagePollResult result = StagePollResult.None;
            if (parts >= 0 && (partCount < parts || ignited > engines)) result = StagePollResult.Fired;
            else if (now - pendingAt > ProofWindow) result = StagePollResult.Reordered;
            if (result != StagePollResult.None)
            {
                Pending = false;
                Scan(partCount, ignited);
            }
            return result;
        }
    }
}
