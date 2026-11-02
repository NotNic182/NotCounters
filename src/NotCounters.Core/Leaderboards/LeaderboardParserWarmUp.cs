using System;
using System.Collections.Generic;
using NotCounters.Core.Pp;

namespace NotCounters.Core.Leaderboards
{
    public static class LeaderboardParserWarmUp
    {
        public const int SampleCount = 4;

        internal const string BeatLeaderLeaderboardSample =
            @"{""id"":""1216291"",""difficulty"":{""value"":9,""modeName"":""Standard"",""difficultyName"":""ExpertPlus"",""status"":3," +
            @"""modifierValues"":{""modifierId"":1036735,""da"":0,""fs"":0.4,""sf"":0.72,""ss"":-0.3,""nf"":-1,""no"":-0.2}," +
            @"""modifiersRating"":{""ssPredictedAcc"":0.9762351,""ssPassRating"":7.7443595,""ssAccRating"":9.408239," +
            @"""ssTechRating"":4.550004,""ssStars"":8.201078,""fsStars"":10.577773}," +
            @"""stars"":9.309627,""predictedAcc"":0.97242546,""passRating"":9.148026,""accRating"":10.243986,""techRating"":4.757814," +
            @"""sliderPercentage"":0.015125958,""noteSpan"":183.35107,""notes"":1264,""maxScore"":1155635}}";

        internal const string ScoreSaberLeaderboardSample =
            @"{""id"":4,""map"":{""hash"":""19F2879D11A91B51A5C090D63471C3E8D9B7AEE3""}," +
            @"""difficulty"":{""difficulty"":7,""gameMode"":""SoloStandard""},""maxScore"":495075," +
            @"""realm"":{""realmId"":1,""leaderboardStatus"":""RANKED"",""positiveModifiers"":false,""stars"":4.58," +
            @"""modifierValues"":""[[\""NO\"",-0.05],[\""NF\"",-0.5],[\""FS\"",0.08],[\""GN\"",0.04]]""}}";

        internal const string ScoreSaberScoresSample =
            @"{""data"":[" +
            @"{""id"":91587472,""unmodifiedScore"":488148,""modifiedScore"":488148,""pp"":373.603,""mods"":[],""playOutcome"":""CLEAR""}," +
            @"{""id"":14486251,""unmodifiedScore"":488084,""modifiedScore"":488084,""pp"":371.584,""mods"":[],""playOutcome"":""CLEAR""}," +
            @"{""id"":7430382,""unmodifiedScore"":487516,""modifiedScore"":487516,""pp"":354.569,""mods"":[""SC"",""PM""],""playOutcome"":""CLEAR""}]}";

        internal const string ScoreSaberCurvesSample =
            @"{""curve"":[[1,5.367394282890631],[0.9875,2.058947159052738],[0.985,1.8563887693647105],[0.9825,1.697536248647543],[0,0]]," +
            @"""positiveModifierCurve"":[[1.14,1.25],[1,1.12],[0,0]]}";

        public static int Run()
        {
            int parsed = 0;
            try
            {
                if (BeatLeaderApiParser.TryParseLeaderboard(BeatLeaderLeaderboardSample, out _, out _)) parsed++;
                bool board = ScoreSaberApiParser.TryParseLeaderboardV2(ScoreSaberLeaderboardSample, out ScoreSaberLeaderboard? leaderboard, out _);
                if (board) parsed++;
                if (ScoreSaberApiParser.TryParseScores(ScoreSaberScoresSample, out IReadOnlyList<ScoreSaberScoreSample> scores, out _)) parsed++;
                if (ScoreSaberApiParser.TryParseCurves(ScoreSaberCurvesSample, out ScoreSaberCurves? curves, out _)) parsed++;
                if (board)
                {
                    ScoreSaberPpCalculator.BackSolveMaxPp(scores, leaderboard, ScoreSaberCurves.Choose(curves, out _));
                }
            }
            catch (Exception)
            {
            }

            return parsed;
        }
    }
}
