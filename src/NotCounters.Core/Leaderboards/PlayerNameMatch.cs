using System;
using System.Collections.Generic;
using System.Text;

namespace NotCounters.Core.Leaderboards
{
    public enum PlayerNameMatchOutcome
    {
        None,
        Single,
        Several,
    }

    public static class PlayerNameMatch
    {
        public static PlayerNameMatchOutcome Select(IReadOnlyList<LeaderboardPlayer>? players, string? name, out LeaderboardPlayer? match, out int count)
        {
            match = null;
            count = 0;
            string wanted = Normalize(name);
            if (players == null || wanted.Length == 0) return PlayerNameMatchOutcome.None;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (LeaderboardPlayer? player in players)
            {
                if (player == null || !string.Equals(Normalize(player.Name), wanted, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ids.Add(player.Id)) continue;
                count++;
                if (count == 1) match = player;
            }

            if (count == 1) return PlayerNameMatchOutcome.Single;
            match = null;
            return count == 0 ? PlayerNameMatchOutcome.None : PlayerNameMatchOutcome.Several;
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value!.Trim();
            try
            {
                return trimmed.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                return trimmed;
            }
        }
    }
}
