using System;

namespace NotCounters.Core.Leaderboards
{
    public static class PlaySpeed
    {
        public const float SlowerMultiplier = 0.85f;

        public const float NormalMultiplier = 1f;

        public const float FasterMultiplier = 1.2f;

        public const float SuperFastMultiplier = 1.5f;

        public const float Tolerance = 0.001f;

        public static bool TryGetSongSpeed(float speedMultiplier, out SongSpeed speed)
        {
            if (SameSpeed(speedMultiplier, NormalMultiplier)) speed = SongSpeed.Normal;
            else if (SameSpeed(speedMultiplier, FasterMultiplier)) speed = SongSpeed.Faster;
            else if (SameSpeed(speedMultiplier, SlowerMultiplier)) speed = SongSpeed.Slower;
            else if (SameSpeed(speedMultiplier, SuperFastMultiplier)) speed = SongSpeed.SuperFast;
            else
            {
                speed = SongSpeed.Normal;
                return false;
            }

            return true;
        }

        public static bool IsPracticeRun(float startSongTime, float practiceSpeedMultiplier, float modifierSpeedMultiplier) =>
            !(startSongTime <= Tolerance)
            || !SameSpeed(practiceSpeedMultiplier, NormalMultiplier)
            || !SameSpeed(practiceSpeedMultiplier, modifierSpeedMultiplier);

        private static bool SameSpeed(float a, float b) =>
            !float.IsNaN(a) && !float.IsInfinity(a) && !float.IsNaN(b) && !float.IsInfinity(b) && Math.Abs(a - b) <= Tolerance;
    }
}
