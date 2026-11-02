using System.Collections.Generic;
using NotCounters.Core.Scoring;

namespace NotCounters.Gameplay
{
    internal static class BeatmapNoteCollector
    {
        public static BeatmapNoteSummary Collect(IReadonlyBeatmapData beatmapData, float startFilterTime)
        {
            var notes = new List<NoteSpec>(1024);
            int bombs = 0;

            foreach (NoteData note in beatmapData.GetBeatmapDataItems<NoteData>(0))
            {
                if (note.gameplayType == NoteData.GameplayType.Bomb)
                {
                    if (note.time >= startFilterTime) bombs++;
                    continue;
                }

                NoteScoringKind kind = GameTypeMapping.ToScoringKind(note.scoringType);
                if (!ScoreDefinitions.IsScored(kind)) continue;
                notes.Add(new NoteSpec(note.time, kind, GameTypeMapping.ToHand(note.colorType)));
            }

            foreach (SliderData slider in beatmapData.GetBeatmapDataItems<SliderData>(0))
            {
                if (slider.sliderType != SliderData.Type.Burst || slider.sliceCount < 2) continue;

                if (slider.time < startFilterTime) continue;

                Hand hand = GameTypeMapping.ToHand(slider.colorType);
                int segments = slider.sliceCount - 1;
                for (int i = 1; i < slider.sliceCount; i++)
                {
                    float t = slider.time + (slider.tailTime - slider.time) * ((float)i / segments);
                    notes.Add(new NoteSpec(t, NoteScoringKind.ChainLink, hand));
                }
            }

            return BeatmapNoteSummary.Create(notes, bombs, startFilterTime);
        }
    }
}
