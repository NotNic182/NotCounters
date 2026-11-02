using System;
using System.Globalization;

namespace NotCounters.Core.Leaderboards
{
    public sealed class BeatLeaderApi
    {
        public const string DefaultBaseUrl = "https://api.beatleader.xyz";

        public BeatLeaderApi(string? baseUrl = null)
        {
            BaseUrl = ApiUrl.NormalizeBase(baseUrl, DefaultBaseUrl);
        }

        public string BaseUrl { get; }

        public string? LeaderboardUrl(LevelIdentity? level)
        {
            if (level == null || !level.IsValid) return null;
            return ApiUrl.Join(BaseUrl, "/leaderboard/", ApiUrl.Segment(level.Hash), "/", ApiUrl.Segment(level.BeatLeaderDifficultySegment),
                "/", ApiUrl.Segment(level.BeatLeaderModeSegment));
        }

        public string? LeaderboardsByHashUrl(string? hash)
        {
            if (!LevelIdentity.TryParseHashFromLevelId(hash, out string h)) return null;
            return ApiUrl.Join(BaseUrl, "/leaderboards/hash/", ApiUrl.Segment(h));
        }

        public string? PlayerScoreUrl(string? playerId, LevelIdentity? level)
        {
            if (string.IsNullOrWhiteSpace(playerId) || level == null || !level.IsValid) return null;
            return ApiUrl.Join(BaseUrl, "/score/", ApiUrl.Segment(playerId!.Trim()), "/", ApiUrl.Segment(level.Hash), "/",
                ApiUrl.Segment(level.BeatLeaderDifficultySegment), "/", ApiUrl.Segment(level.BeatLeaderModeSegment));
        }

        public string? PlayerUrl(string? idOrAlias)
        {
            if (string.IsNullOrWhiteSpace(idOrAlias)) return null;
            return ApiUrl.Join(BaseUrl, "/player/", ApiUrl.Segment(idOrAlias!.Trim()), "?stats=false");
        }

        public string? PlayerSearchUrl(string? query, int count = 50, int page = 1)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            return ApiUrl.Join(BaseUrl, "/players?search=", ApiUrl.Segment(query!.Trim()),
                "&count=", Math.Max(1, count).ToString(CultureInfo.InvariantCulture),
                "&page=", Math.Max(1, page).ToString(CultureInfo.InvariantCulture));
        }
    }
}
