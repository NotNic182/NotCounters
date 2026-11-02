using System;
using System.Globalization;
using NotCounters.Gameplay;
using NotCounters.Hud;
using NotCounters.Util;
using TMPro;
using UnityEngine;
using Zenject;

namespace NotCounters.Editor
{
    internal sealed class EditPreviewValues : ILateTickable
    {
        private readonly SimulatedStatsSource? _stats;
        private readonly HudAnchorProvider? _anchors;
        private readonly VanillaTextFormatter? _formatter;

        private ScoreUIController? _score;
        private ComboUIController? _combo;
        private ImmediateRankUIPanel? _rankPanel;
        private ScoreMultiplierUIController? _multiplier;
        private GameEnergyUIPanel? _energy;
        private bool _resolved;
        private bool _disabled;

        private int _shownScore = -1;
        private string? _writtenScoreText;
        private int _shownCombo = -1;
        private string? _writtenComboText;
        private double _shownRelative = double.NaN;
        private string? _writtenRelativeText;

        public EditPreviewValues(
            [InjectOptional] SimulatedStatsSource? stats,
            [InjectOptional] HudAnchorProvider? anchors,
            [InjectOptional] VanillaTextFormatter? formatter)
        {
            _stats = stats;
            _anchors = anchors;
            _formatter = formatter;
        }

        public void LateTick()
        {
            if (_disabled || _stats == null)
            {
                return;
            }

            try
            {
                var stats = _stats.Current;

                if (stats == null || stats.Version == 0 || stats.Accuracy is not double accuracy)
                {
                    return;
                }

                if (!_resolved && !TryResolve())
                {
                    return;
                }

                WriteScore(stats.ModifiedScore, stats.MultipliedScore);
                WriteCombo(stats.Combo);
                WriteRank(stats.Rank, accuracy);
                WriteMultiplier(stats.Multiplier, stats.MultiplierProgress);
                WriteEnergy(stats.Energy, stats.IsBatteryEnergy);
            }
            catch (Exception ex)
            {
                _disabled = true;
                Safe.Report("EditPreviewValues", ex);
            }
        }

        private bool TryResolve()
        {
            var hud = _anchors?.HudController;
            if (hud == null)
            {
                return false;
            }

            var root = hud.transform;

            _score = root.GetComponentInChildren<ScoreUIController>(true);
            _combo = root.GetComponentInChildren<ComboUIController>(true);
            _rankPanel = root.GetComponentInChildren<ImmediateRankUIPanel>(true);
            _multiplier = root.GetComponentInChildren<ScoreMultiplierUIController>(true);
            _energy = root.GetComponentInChildren<GameEnergyUIPanel>(true);
            _resolved = true;
            return true;
        }

        private void WriteScore(int modifiedScore, int multipliedScore)
        {
            var score = _score;

            if (score == null || score._stringBuilder == null || score._scoreText == null)
            {
                return;
            }

            var initData = score._initData;
            var display = initData != null && initData.scoreDisplayType == ScoreUIController.ScoreDisplayType.MultipliedScore
                ? multipliedScore
                : modifiedScore;
            if (display == _shownScore && TextIs(score._scoreText, _writtenScoreText))
            {
                return;
            }

            score.UpdateScore(Math.Max(0, display));
            _shownScore = display;
            _writtenScoreText = score._scoreText.text;
        }

        private void WriteCombo(int comboValue)
        {
            var combo = _combo;
            var text = combo != null ? combo._comboText : null;
            if (text == null)
            {
                return;
            }

            if (comboValue != _shownCombo || _writtenComboText == null)
            {
                _writtenComboText = Math.Max(0, comboValue).ToString(CultureInfo.InvariantCulture);
                _shownCombo = comboValue;
            }

            SetIfChanged(text, _writtenComboText);
        }

        private void WriteRank(string? rank, double fraction)
        {
            var panel = _rankPanel;
            if (panel == null)
            {
                return;
            }

            if (panel._rankText != null && rank != null)
            {
                SetIfChanged(panel._rankText, rank);
            }

            var relativeText = panel._relativeScoreText;
            if (relativeText == null)
            {
                return;
            }

            if (fraction != _shownRelative || _writtenRelativeText == null || !TextIs(relativeText, _writtenRelativeText))
            {
                _writtenRelativeText = _formatter != null
                    ? _formatter.FormatRelativeScore(fraction)
                    : fraction.ToString("P1", CultureInfo.InvariantCulture);
                _shownRelative = fraction;
                SetIfChanged(relativeText, _writtenRelativeText);
            }
        }

        private void WriteMultiplier(int multiplier, float progress)
        {
            var controller = _multiplier;
            var texts = controller != null ? controller._multiplierTexts : null;
            if (texts == null)
            {
                return;
            }

            var text = MultiplierText(multiplier);
            for (var i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    SetIfChanged(texts[i], text);
                }
            }

            controller!._prevMultiplier = multiplier;
            var ring = controller._multiplierProgressImage;
            if (ring != null && (controller._progressTarget != progress || ring.fillAmount != progress))
            {
                controller._progressTarget = progress;
                ring.fillAmount = progress;
            }
        }

        private void WriteEnergy(float energy, bool battery)
        {
            var bar = _energy != null ? _energy._energyBar : null;
            if (battery || bar == null || !bar.gameObject.activeSelf)
            {
                return;
            }

            var fill = Mathf.Clamp01(energy);
            var rect = bar.rectTransform;
            var anchor = rect.anchorMax;
            if (anchor.x != fill || anchor.y != 1f)
            {
                rect.anchorMax = new Vector2(fill, 1f);
            }
        }

        private static string MultiplierText(int multiplier)
        {
            switch (multiplier)
            {
                case 1: return "1";
                case 2: return "2";
                case 4: return "4";
                case 8: return "8";
                default: return multiplier.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void SetIfChanged(TMP_Text text, string value)
        {
            if (!string.Equals(text.text, value, StringComparison.Ordinal))
            {
                text.text = value;
            }
        }

        private static bool TextIs(TMP_Text text, string? expected) => expected != null && string.Equals(text.text, expected, StringComparison.Ordinal);
    }
}
