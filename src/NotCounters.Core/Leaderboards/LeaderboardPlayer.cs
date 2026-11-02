namespace NotCounters.Core.Leaderboards
{
    public sealed record LeaderboardPlayer(string Id, string Name, string? Alias, int? Rank);
}
