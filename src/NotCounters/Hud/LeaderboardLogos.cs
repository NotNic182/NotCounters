using System;
using System.IO;
using System.Reflection;
using NotCounters.Core.Counters;
using NotCounters.Util;
using SiraUtil.Logging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NotCounters.Hud
{
    internal readonly struct LeaderboardLogo
    {
        public LeaderboardLogo(Sprite sprite, Material? material, float markHeight)
        {
            Sprite = sprite;
            Material = material;
            MarkHeight = markHeight;
        }

        public Sprite Sprite { get; }

        public Material? Material { get; }

        public float MarkHeight { get; }
    }

    internal sealed class LeaderboardLogos
    {
        internal const string ScoreSaberResourceName = "ScoreSaber.Resources.logo.png";

        internal const string BeatLeaderSpriteName = "BeatLeaderLogoGradient";
        internal const string BeatLeaderMaterialName = "LogoMaterial";
        internal const string BeatLeaderShaderName = "BeatLeader/Logo";

        private const string ScoreSaberAssemblyName = "ScoreSaber";
        private const string BeatLeaderAssemblyName = "BeatLeader";
        private const string GlowProperty = "_Glow";
        private const string BlockScaleProperty = "_BlockScale";

        private static Sprite? _scoreSaberLogo;
        private static bool _scoreSaberLoadFailed;
        private static bool? _scoreSaberLoaded;

        private static Sprite? _beatLeaderSprite;
        private static Material? _beatLeaderMaterial;
        private static float _beatLeaderMarkHeight = 1f;
        private static bool? _beatLeaderLoaded;

        private readonly SiraLog _log;

        private readonly Func<Sprite?> _getScoreSaber;
        private readonly Func<bool> _findBeatLeader;

        private bool _beatLeaderSearched;

        public LeaderboardLogos(SiraLog log)
        {
            _log = log;
            _getScoreSaber = GetScoreSaber;
            _findBeatLeader = FindBeatLeader;
        }

        public bool TryGet(string? iconKey, out LeaderboardLogo logo)
        {
            logo = default;
            switch (iconKey)
            {
                case CounterIcons.ScoreSaber:
                    var scoreSaber = Safe.Invoke("hud:logo-scoresaber", _getScoreSaber, null);
                    if (scoreSaber == null)
                    {
                        return false;
                    }

                    logo = new LeaderboardLogo(scoreSaber, null, 1f);
                    return true;

                case CounterIcons.BeatLeader:
                    if (!Safe.Invoke("hud:logo-beatleader", _findBeatLeader, false))
                    {
                        return false;
                    }

                    logo = new LeaderboardLogo(_beatLeaderSprite!, _beatLeaderMaterial, _beatLeaderMarkHeight);
                    return true;

                default:
                    return false;
            }
        }

        private bool FindBeatLeader()
        {
            if (_beatLeaderSprite != null && _beatLeaderMaterial != null)
            {
                return true;
            }

            if (_beatLeaderSearched || !(_beatLeaderLoaded ??= FindAssembly(BeatLeaderAssemblyName) != null))
            {
                return false;
            }

            _beatLeaderSearched = true;
            Sprite? sprite = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (candidate.name == BeatLeaderSpriteName)
                {
                    sprite = candidate;
                    break;
                }
            }

            Material? source = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (candidate.name == BeatLeaderMaterialName && candidate.shader != null && candidate.shader.name == BeatLeaderShaderName)
                {
                    source = candidate;
                    break;
                }
            }

            if (sprite == null || source == null || !source.HasProperty(GlowProperty))
            {
                Safe.WarnOnce("hud:logo-beatleader-missing", "[NotCounters] BeatLeader is loaded but its logo assets were not found; the PP counter's BeatLeader row shows \"BL\".");
                return false;
            }

            if (_beatLeaderMaterial != null)
            {
                Object.Destroy(_beatLeaderMaterial);
            }

            var material = new Material(source)
            {
                name = "NotCounters BeatLeader logo",
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            material.SetFloat(GlowProperty, 0f);
            _beatLeaderMarkHeight = source.HasProperty(BlockScaleProperty) ? Mathf.Clamp(source.GetFloat(BlockScaleProperty), 0.3f, 1f) : 1f;
            _beatLeaderSprite = sprite;
            _beatLeaderMaterial = material;
            _log.Debug($"BeatLeader logo found (sprite {sprite.rect.width}x{sprite.rect.height}, mark {_beatLeaderMarkHeight:0.##} of its height).");
            return true;
        }

        private Sprite? GetScoreSaber()
        {
            if (_scoreSaberLogo != null)
            {
                return _scoreSaberLogo;
            }

            if (_scoreSaberLoadFailed || !IsScoreSaberLoaded())
            {
                return null;
            }

            _scoreSaberLoadFailed = true;
            var assembly = FindAssembly(ScoreSaberAssemblyName);
            using var stream = assembly?.GetManifestResourceStream(ScoreSaberResourceName);
            if (stream == null)
            {
                Safe.WarnOnce("hud:logo-scoresaber-missing", $"[NotCounters] ScoreSaber is loaded but has no resource '{ScoreSaberResourceName}'; the PP counter's ScoreSaber row shows \"SS\".");
                return null;
            }

            byte[] png;
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                png = buffer.ToArray();
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true)
            {
                name = "NotCounters ScoreSaber logo",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            if (!texture.LoadImage(png, markNonReadable: true))
            {
                Object.Destroy(texture);
                Safe.WarnOnce("hud:logo-scoresaber-decode", $"[NotCounters] ScoreSaber's '{ScoreSaberResourceName}' could not be decoded; the PP counter's ScoreSaber row shows \"SS\".");
                return null;
            }

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "NotCounters ScoreSaber logo";
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _scoreSaberLogo = sprite;
            _scoreSaberLoadFailed = false;
            _log.Debug($"ScoreSaber logo loaded ({texture.width}x{texture.height}).");
            return sprite;
        }

        private static bool IsScoreSaberLoaded() => _scoreSaberLoaded ??= FindAssembly(ScoreSaberAssemblyName) != null;

        private static Assembly? FindAssembly(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, name, StringComparison.Ordinal))
                {
                    return assembly;
                }
            }

            return null;
        }
    }
}
