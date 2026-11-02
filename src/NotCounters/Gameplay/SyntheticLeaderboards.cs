using NotCounters.Core.Pp;

namespace NotCounters.Gameplay
{
    internal static class SyntheticLeaderboards
    {
        private const double AccRating = 9.05;
        private const double PassRating = 7.35;
        private const double TechRating = 4.4;
        private const double PredictedAccuracy = 0.9675;
        private const double SliderPercentage = 0.02;
        private const double ScoreSaberStars = 7.92;

        public static BeatLeaderDifficulty CreateBeatLeader(int colorNotes, double noteSpanSeconds, int maxScore) =>
            new BeatLeaderDifficulty
            {
                Status = (int)BeatLeaderStatus.Ranked,
                AccRating = AccRating,
                PassRating = PassRating,
                TechRating = TechRating,
                PredictedAcc = PredictedAccuracy,
                SliderPercentage = SliderPercentage,
                Notes = colorNotes,
                NoteSpan = noteSpanSeconds,
                MaxScore = maxScore,
                ModeName = "Standard",
                DifficultyName = "ExpertPlus",
                Value = 9,
            };

        public static ScoreSaberLeaderboard CreateScoreSaber(int maxScore) =>
            new ScoreSaberLeaderboard
            {
                Status = ScoreSaberLeaderboardStatus.Ranked,
                Stars = ScoreSaberStars,
                MaxScore = maxScore,
                PositiveModifiers = false,
                MaxPp = ScoreSaberPpCalculator.MaxPpFromStars(ScoreSaberStars),
                Difficulty = 9,
                GameMode = "SoloStandard",
            };
    }
}
