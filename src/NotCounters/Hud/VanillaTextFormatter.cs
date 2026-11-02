using System;
using System.Globalization;
using BGLib.Polyglot;
using NotCounters.Core.Config;
using NotCounters.Core.Formatting;
using TMPro;
using UnityEngine;

namespace NotCounters.Hud
{
    internal sealed class VanillaTextFormatter
    {
        private const string RemainingPrefix = "-";

        private ScoreUIController? _score;
        private ImmediateRankUIPanel? _rankPanel;
        private SongProgressUIController? _songProgress;

        private NumberGrouping _grouping = NumberGrouping.None;
        private int? _relativeDecimals;
        private TimeDisplayMode _timeMode = TimeDisplayMode.Default;

        private float _lastRelativeScore = float.NaN;
        private int _lastRelativeDecimals = -1;
        private CultureInfo? _lastRelativeCulture;
        private string? _lastRelativeText;

        private int _lastRemainingSeconds = -1;
        private string? _lastRemainingMinutesText;
        private string? _lastRemainingSecondsText;

        private bool FormatsScore => _grouping == NumberGrouping.Comma || _grouping == NumberGrouping.Dot;

        public void Track(ScoreUIController? score, ImmediateRankUIPanel? rankPanel, SongProgressUIController? songProgress)
        {
            _score = score;
            _rankPanel = rankPanel;
            _songProgress = songProgress;
        }

        public void Configure(NumberGrouping grouping, int? relativeDecimals, TimeDisplayMode timeMode)
        {
            var scoreChanged = grouping != _grouping;
            var relativeChanged = relativeDecimals != _relativeDecimals;
            var timeChanged = timeMode != _timeMode;

            _grouping = grouping;
            _relativeDecimals = relativeDecimals;
            _timeMode = timeMode;

            if (scoreChanged)
            {
                RefreshScore();
            }

            if (relativeChanged)
            {
                RefreshRelativeScore();
            }

            if (timeChanged)
            {
                RefreshSongProgress();
            }
        }

        public void AfterScoreWritten(ScoreUIController instance, int displayScore)
        {
            if (!FormatsScore || !ReferenceEquals(instance, _score) || instance._scoreText == null)
            {
                return;
            }

            SetIfChanged(instance._scoreText, NumberFormatting.Integer(displayScore, _grouping));
        }

        public void AfterRankPanelRefreshed(ImmediateRankUIPanel instance)
        {
            if (_relativeDecimals is not int decimals || !ReferenceEquals(instance, _rankPanel))
            {
                return;
            }

            var text = instance._relativeScoreText;
            var counter = instance._relativeScoreAndImmediateRankCounter;
            if (text == null || counter == null)
            {
                return;
            }

            var relative = counter.relativeScore;
            var culture = GameCulture();
            if (relative != _lastRelativeScore || decimals != _lastRelativeDecimals
                || !ReferenceEquals(culture, _lastRelativeCulture) || _lastRelativeText == null)
            {
                _lastRelativeText = NumberFormatting.Percent(relative, decimals, culture);
                _lastRelativeScore = relative;
                _lastRelativeDecimals = decimals;
                _lastRelativeCulture = culture;
            }

            SetIfChanged(text, _lastRelativeText);
        }

        public string FormatRelativeScore(double fraction) =>
            NumberFormatting.Percent(fraction, _relativeDecimals ?? 1, GameCulture());

        public void AfterSongProgressWritten(SongProgressUIController instance)
        {
            if (_timeMode != TimeDisplayMode.Remaining || !ReferenceEquals(instance, _songProgress))
            {
                return;
            }

            var minutesText = instance._progressMinutesText;
            var secondsText = instance._progressSecondsText;
            var timeSource = instance._audioTimeSource;
            if (minutesText == null || secondsText == null || timeSource == null)
            {
                return;
            }

            var length = instance._songLength;
            var elapsed = instance._songDidFinish ? length : Mathf.Clamp(timeSource.songTime, 0f, length);
            var remaining = Math.Max(0, (int)Math.Floor(length - elapsed));
            if (remaining != _lastRemainingSeconds || _lastRemainingMinutesText == null || _lastRemainingSecondsText == null)
            {
                var minutesFormat = string.IsNullOrEmpty(instance._progressMinutesFormatString) ? "0" : instance._progressMinutesFormatString;
                _lastRemainingMinutesText = RemainingPrefix + (remaining / 60).ToString(minutesFormat, CultureInfo.InvariantCulture);
                _lastRemainingSecondsText = (remaining % 60).ToString("00", CultureInfo.InvariantCulture);
                _lastRemainingSeconds = remaining;
            }

            SetIfChanged(minutesText, _lastRemainingMinutesText);
            SetIfChanged(secondsText, _lastRemainingSecondsText);
        }

        private void RefreshScore()
        {
            var score = _score;

            if (score == null || score._stringBuilder == null || score._scoreController == null)
            {
                return;
            }

            score.UpdateScore(score._scoreController.multipliedScore, score._scoreController.modifiedScore);
        }

        private void RefreshRelativeScore()
        {
            var panel = _rankPanel;
            if (panel == null || panel._stringBuilder == null || panel._relativeScoreAndImmediateRankCounter == null)
            {
                return;
            }

            _lastRelativeText = null;

            // vanilla only rewrites the text on a 0.001 change
            panel._prevRelativeScore = -1f;
            panel.RefreshUI();
        }

        private void RefreshSongProgress()
        {
            var progress = _songProgress;
            if (progress == null)
            {
                return;
            }

            _lastRemainingSeconds = -1;
            progress._prevMinutes = -1;
            progress._prevSeconds = -1;
        }

        private static CultureInfo GameCulture()
        {
            try
            {
                return Localization.Instance.SelectedCultureInfo ?? CultureInfo.InvariantCulture;
            }
            catch (InvalidOperationException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        private static void SetIfChanged(TMP_Text text, string value)
        {
            if (text.text != value)
            {
                text.text = value;
            }
        }
    }
}
