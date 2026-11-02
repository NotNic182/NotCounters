using System;
using System.Globalization;

namespace NotCounters.Core.Leaderboards
{
    public sealed class ScoreSaberApi
    {
        public const string DefaultBaseUrl = "https://scoresaber.com";

        public ScoreSaberApi(string? baseUrl = null)
        {
            BaseUrl = ApiUrl.NormalizeBase(baseUrl, DefaultBaseUrl);
        }

        public string BaseUrl { get; }

        public string? LeaderboardUrl(LevelIdentity? level, int? realmId = null)
        {
            if (level == null || !level.IsValid || level.ScoreSaberGameMode == null) return null;
            string? url = ApiUrl.Join(BaseUrl, "/api/v2/leaderboards/hash/", ApiUrl.Segment(level.Hash), "/",
                ApiUrl.Segment(level.ScoreSaberGameMode), "/", level.ScoreSaberDifficulty.ToString(CultureInfo.InvariantCulture));
            if (url == null) return null;
            return realmId.HasValue ? url + "?realmId=" + realmId.Value.ToString(CultureInfo.InvariantCulture) : url;
        }

        public string? LeaderboardInfoV1Url(LevelIdentity? level)
        {
            if (level == null || !level.IsValid || level.ScoreSaberGameMode == null) return null;
            return ApiUrl.Join(BaseUrl, "/api/leaderboard/by-hash/", ApiUrl.Segment(level.Hash), "/info?difficulty=",
                level.ScoreSaberDifficulty.ToString(CultureInfo.InvariantCulture), "&gameMode=", ApiUrl.Segment(level.ScoreSaberGameMode));
        }

        public string PpCurveUrl(int realmId = 1) =>
            BaseUrl + "/api/v2/realms/" + (realmId > 0 ? realmId : 1).ToString(CultureInfo.InvariantCulture) + "/pp-curve";

        public string? LeaderboardScoresUrl(long leaderboardId, int limit = 10, int page = 1)
        {
            if (leaderboardId <= 0) return null;
            return BaseUrl + "/api/v2/leaderboards/" + leaderboardId.ToString(CultureInfo.InvariantCulture)
                   + "/scores?limit=" + Math.Max(1, limit).ToString(CultureInfo.InvariantCulture)
                   + "&page=" + Math.Max(1, page).ToString(CultureInfo.InvariantCulture);
        }

        public string? PlayerScoreUrl(string? playerId, LevelIdentity? level, long? cacheBuster = null)
        {
            if (string.IsNullOrWhiteSpace(playerId) || level == null || !level.IsValid || level.ScoreSaberGameMode == null) return null;
            string? url = ApiUrl.Join(BaseUrl, "/api/v2/players/", ApiUrl.Segment(playerId!.Trim()), "/scores/hash/", ApiUrl.Segment(level.Hash), "/",
                ApiUrl.Segment(level.ScoreSaberGameMode), "/", level.ScoreSaberDifficulty.ToString(CultureInfo.InvariantCulture));
            if (url == null) return null;
            return cacheBuster.HasValue && cacheBuster.Value > 0
                ? url + "?_=" + cacheBuster.Value.ToString(CultureInfo.InvariantCulture)
                : url;
        }

        public string? PlayerBasicUrl(string? playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return null;
            return ApiUrl.Join(BaseUrl, "/api/v2/players/", ApiUrl.Segment(playerId!.Trim()), "/basic");
        }

        public string? PlayerSearchUrl(string? query, int page = 1)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            return ApiUrl.Join(BaseUrl, "/api/players?search=", ApiUrl.Segment(query!.Trim()),
                "&page=", Math.Max(1, page).ToString(CultureInfo.InvariantCulture));
        }

        public string? DifficultiesUrl(string? hash)
        {
            if (!LevelIdentity.TryParseHashFromLevelId(hash, out string h)) return null;
            return ApiUrl.Join(BaseUrl, "/api/leaderboard/get-difficulties/", ApiUrl.Segment(h));
        }
    }
}
