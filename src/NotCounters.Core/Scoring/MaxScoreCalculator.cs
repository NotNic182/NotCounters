using System.Collections.Generic;

namespace NotCounters.Core.Scoring
{
    public static class MaxScoreCalculator
    {
        public static int ComputeMaxMultipliedScore(IEnumerable<NoteScoringKind>? kindsInTimeOrder)
        {
            long total = ComputeMaxRemaining(null, kindsInTimeOrder);
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        public static long ComputeMaxRemaining(MultiplierState? current, IEnumerable<NoteScoringKind>? remainingInTimeOrder)
        {
            if (remainingInTimeOrder == null) return 0;
            MultiplierState state = current?.Clone() ?? new MultiplierState();
            long total = 0;
            foreach (NoteScoringKind kind in remainingInTimeOrder)
            {
                int max = ScoreDefinitions.MaxCutScore(kind);
                if (max <= 0) continue;
                state.ProcessHit();
                total += (long)max * state.Multiplier;
            }

            return total;
        }
    }
}
