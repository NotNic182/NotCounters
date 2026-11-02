using System;
using System.Collections.Generic;
using System.Globalization;

namespace NotCounters.Core.Leaderboards
{
    public enum PlayerLookupResponse
    {
        Success,
        NotFound,
        Rejected,
        Failed,
    }

    public enum PlayerLookupOutcome
    {
        Found,
        NotFound,
        Ambiguous,
        TooManyMatches,
        Error,
    }

    public sealed class PlayerLookupResult
    {
        public PlayerLookupResult(PlayerLookupOutcome outcome, LeaderboardPlayer? player, int matchCount, bool isOverride, string query, string? error)
        {
            Outcome = outcome;
            Player = player;
            MatchCount = matchCount;
            IsOverride = isOverride;
            Query = query ?? string.Empty;
            Error = error;
        }

        public PlayerLookupOutcome Outcome { get; }

        public LeaderboardPlayer? Player { get; }

        public int MatchCount { get; }

        public bool IsOverride { get; }

        public string Query { get; }

        public string? Error { get; }

        public override string ToString() =>
            Outcome + (Player != null ? " " + Player.Name + " (" + Player.Id + ")" : string.Empty) + (Error != null ? ": " + Error : string.Empty);
    }

    public sealed class PlayerLookup
    {
        public const int BeatLeaderSearchCount = 50;

        public const int ScoreSaberSearchPageSize = 50;

        public const int MaxSearchPages = 4;

        private readonly BeatLeaderApi? _beatLeader;
        private readonly ScoreSaberApi? _scoreSaber;

        private readonly List<LeaderboardPlayer> _searchResults = new List<LeaderboardPlayer>();

        private Step _step;

        private int _searchPage;

        private int _searchEntriesRead;

        private PlayerLookup(LeaderboardService service, string? baseUrl, bool isOverride, string query)
        {
            Service = service;
            IsOverride = isOverride;
            Query = query;
            if (service == LeaderboardService.BeatLeader)
            {
                _beatLeader = new BeatLeaderApi(baseUrl);
            }
            else
            {
                _scoreSaber = new ScoreSaberApi(baseUrl);
            }
        }

        private enum Step
        {
            Profile,
            Search,
            Done,
        }

        public LeaderboardService Service { get; }

        public bool IsOverride { get; }

        public string Query { get; }

        public string? NextUrl { get; private set; }

        public PlayerLookupResult? Result { get; private set; }

        public bool IsComplete => Result != null;

        public static PlayerLookup ForPlatformUser(LeaderboardService service, string? baseUrl, ulong platformUserId)
        {
            var lookup = new PlayerLookup(service, baseUrl, false, platformUserId.ToString(CultureInfo.InvariantCulture));
            if (platformUserId == 0)
            {
                lookup.Finish(PlayerLookupOutcome.NotFound, error: "The game's platform user id is not known.");
            }
            else
            {
                lookup.GoToProfile();
            }

            return lookup;
        }

        public static PlayerLookup ForOverride(LeaderboardService service, string? baseUrl, PlayerOverride? input)
        {
            if (input == null || input.Kind != PlayerOverrideKind.Query)
            {
                var invalid = new PlayerLookup(service, baseUrl, true, string.Empty);
                invalid.Finish(PlayerLookupOutcome.NotFound, error: "The override is not an id, alias, name or supported profile link.");
                return invalid;
            }

            var lookup = new PlayerLookup(service, baseUrl, true, input.Query);
            if (service == LeaderboardService.ScoreSaber && !input.IsNumeric)
            {
                lookup.GoToSearch();
            }
            else
            {
                lookup.GoToProfile();
            }

            return lookup;
        }

        public void Advance(PlayerLookupResponse response, string? body)
        {
            if (IsComplete) return;
            try
            {
                if (_step == Step.Profile)
                {
                    AdvanceProfile(response, body);
                }
                else
                {
                    AdvanceSearch(response, body);
                }
            }
            catch (Exception ex)
            {
                Finish(PlayerLookupOutcome.Error, error: "Unexpected failure: " + ex.Message);
            }
        }

        private void AdvanceProfile(PlayerLookupResponse response, string? body)
        {
            switch (response)
            {
                case PlayerLookupResponse.Success:
                    bool parsed = _beatLeader != null
                        ? PlayerApiParser.TryParseBeatLeaderPlayer(body, out LeaderboardPlayer? player, out string? error)
                        : PlayerApiParser.TryParseScoreSaberPlayer(body, out player, out error);
                    if (parsed)
                    {
                        Finish(PlayerLookupOutcome.Found, player);
                    }
                    else
                    {
                        Finish(PlayerLookupOutcome.Error, error: error);
                    }

                    return;
                case PlayerLookupResponse.NotFound:
                    if (IsOverride)
                    {
                        GoToSearch();
                    }
                    else
                    {
                        Finish(PlayerLookupOutcome.NotFound, error: "No account is linked to the platform user id.");
                    }

                    return;
                case PlayerLookupResponse.Rejected:
                    if (IsOverride)
                    {
                        GoToSearch();
                    }
                    else
                    {
                        Finish(PlayerLookupOutcome.Error, error: "The leaderboard refused the platform user id request.");
                    }

                    return;
                default:
                    Finish(PlayerLookupOutcome.Error, error: "The player request failed.");
                    return;
            }
        }

        private void AdvanceSearch(PlayerLookupResponse response, string? body)
        {
            if (response != PlayerLookupResponse.Success)
            {
                if (_searchPage > 1)
                {
                    Finish(PlayerLookupOutcome.Error, error: "The name search failed on page " + _searchPage + ".");
                }
                else if (response == PlayerLookupResponse.Failed)
                {
                    Finish(PlayerLookupOutcome.Error, error: "The name search failed.");
                }
                else
                {
                    Finish(PlayerLookupOutcome.NotFound, error: "The name search found nothing or refused the name.");
                }

                return;
            }

            bool parsed = _beatLeader != null
                ? PlayerApiParser.TryParseBeatLeaderSearchPage(body, out PlayerSearchPage page, out string? error)
                : PlayerApiParser.TryParseScoreSaberSearchPage(body, out page, out error);
            if (!parsed)
            {
                Finish(PlayerLookupOutcome.Error, error: error);
                return;
            }

            _searchResults.AddRange(page.Players);
            _searchEntriesRead += page.EntryCount;

            PlayerNameMatchOutcome match = PlayerNameMatch.Select(_searchResults, Query, out LeaderboardPlayer? player, out int count);
            if (match == PlayerNameMatchOutcome.Several)
            {
                Finish(PlayerLookupOutcome.Ambiguous, matchCount: count, error: count + " players have this name.");
                return;
            }

            if (HasMorePages(page))
            {
                if (_searchPage < MaxSearchPages)
                {
                    GoToSearchPage(_searchPage + 1);
                    return;
                }

                string total = page.Total.HasValue ? page.Total.Value.ToString(CultureInfo.InvariantCulture) : "more";
                Finish(PlayerLookupOutcome.TooManyMatches, matchCount: count,
                    error: "Only " + _searchEntriesRead + " of " + total + " players containing this name were searched (" + count + " exact).");
                return;
            }

            if (match == PlayerNameMatchOutcome.Single)
            {
                Finish(PlayerLookupOutcome.Found, player);
            }
            else
            {
                Finish(PlayerLookupOutcome.NotFound, error: "No player has exactly this name (" + _searchEntriesRead + " searched).");
            }
        }

        private bool HasMorePages(PlayerSearchPage page)
        {
            int pageSize = page.ItemsPerPage ?? (_beatLeader != null ? BeatLeaderSearchCount : ScoreSaberSearchPageSize);
            bool full = page.EntryCount > 0 && page.EntryCount >= pageSize;
            return full && (!page.Total.HasValue || _searchEntriesRead < page.Total.Value);
        }

        private void GoToProfile()
        {
            _step = Step.Profile;
            NextUrl = _beatLeader != null ? _beatLeader.PlayerUrl(Query) : _scoreSaber!.PlayerBasicUrl(Query);
            if (NextUrl == null) Finish(PlayerLookupOutcome.NotFound, error: "The value cannot be sent as a player id.");
        }

        private void GoToSearch() => GoToSearchPage(1);

        private void GoToSearchPage(int page)
        {
            _step = Step.Search;
            _searchPage = page;
            NextUrl = _beatLeader != null
                ? _beatLeader.PlayerSearchUrl(Query, BeatLeaderSearchCount, page)
                : _scoreSaber!.PlayerSearchUrl(Query, page);
            if (NextUrl == null) Finish(PlayerLookupOutcome.NotFound, error: "The value cannot be sent as a search.");
        }

        private void Finish(PlayerLookupOutcome outcome, LeaderboardPlayer? player = null, int matchCount = 0, string? error = null)
        {
            _step = Step.Done;
            NextUrl = null;
            Result = new PlayerLookupResult(outcome, outcome == PlayerLookupOutcome.Found ? player : null, matchCount, IsOverride, Query, error);
        }
    }
}
