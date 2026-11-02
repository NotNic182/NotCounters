using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NotCounters.Core.Scoring
{
    public readonly struct NoteSpec
    {
        public NoteSpec(float time, NoteScoringKind kind, Hand hand)
        {
            Time = time;
            Kind = kind;
            Hand = hand;
        }

        public float Time { get; }

        public NoteScoringKind Kind { get; }

        public Hand Hand { get; }
    }

    public sealed class BeatmapNoteSummary
    {
        private BeatmapNoteSummary(NoteSpec[] notes, float startFilterTime, int totalNotes, int totalChainLinks, int totalBombs, int fullMapMax)
        {
            Notes = new ReadOnlyCollection<NoteSpec>(notes);
            StartFilterTime = startFilterTime;
            TotalNotes = totalNotes;
            TotalChainLinks = totalChainLinks;
            TotalBombs = totalBombs;
            FullMapMaxMultipliedScore = fullMapMax;
        }

        public static BeatmapNoteSummary Empty { get; } =
            new BeatmapNoteSummary(Array.Empty<NoteSpec>(), float.NegativeInfinity, 0, 0, 0, 0);

        public int TotalNotes { get; }

        public int TotalChainLinks { get; }

        public int TotalBombs { get; }

        public int FullMapMaxMultipliedScore { get; }

        public float StartFilterTime { get; }

        public IReadOnlyList<NoteSpec> Notes { get; }

        public static BeatmapNoteSummary Create(IEnumerable<NoteSpec>? scoringNotes, int bombCount, float startFilterTime)
        {
            float filter = float.IsNaN(startFilterTime) ? float.NegativeInfinity : startFilterTime;
            NoteSpec[] notes = (scoringNotes ?? Enumerable.Empty<NoteSpec>())
                .Where(n => ScoreDefinitions.IsScored(n.Kind) && !float.IsNaN(n.Time) && n.Time >= filter)
                .OrderBy(n => n.Time)
                .ThenBy(n => ScoreDefinitions.MaxCutScore(n.Kind))
                .ToArray();

            int links = 0;
            foreach (NoteSpec n in notes)
            {
                if (ScoreDefinitions.IsChainLink(n.Kind)) links++;
            }

            int fullMax = MaxScoreCalculator.ComputeMaxMultipliedScore(notes.Select(n => n.Kind));
            return new BeatmapNoteSummary(notes, filter, notes.Length - links, links, Math.Max(0, bombCount), fullMax);
        }
    }
}
