using System;
using System.Collections.Generic;

namespace NotCounters.Core.Leaderboards
{
    public sealed class PlayerSearchPage
    {
        public PlayerSearchPage(IReadOnlyList<LeaderboardPlayer> players, int entryCount, int? total, int? itemsPerPage)
        {
            Players = players ?? throw new ArgumentNullException(nameof(players));
            EntryCount = Math.Max(0, entryCount);
            Total = total;
            ItemsPerPage = itemsPerPage;
        }

        public IReadOnlyList<LeaderboardPlayer> Players { get; }

        public int EntryCount { get; }

        public int? Total { get; }

        public int? ItemsPerPage { get; }
    }
}
