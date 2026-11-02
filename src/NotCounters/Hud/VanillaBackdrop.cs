using System.Collections.Generic;
using NotCounters.Core.Config;
using NotCounters.Core.Counters;
using UnityEngine;

namespace NotCounters.Hud
{
    internal sealed class VanillaBackdrop
    {
        private readonly List<Part> _parts;
        private readonly bool _isEditSession;

        private VanillaBackdrop(string id, List<Part> parts, bool isEditSession)
        {
            Id = id;
            _parts = parts;
            _isEditSession = isEditSession;
        }

        public string Id { get; }

        public static VanillaBackdrop? TryCapture(string id, Transform panel, bool isEditSession)
        {
            var parts = new List<Part>();
            for (var i = 0; i < panel.childCount; i++)
            {
                var child = panel.GetChild(i);
                var sprite = child.gameObject.activeSelf ? child.GetComponent<SpriteRenderer>() : null;
                if (sprite != null)
                {
                    parts.Add(new Part(sprite));
                }
            }

            return parts.Count > 0 ? new VanillaBackdrop(id, parts, isEditSession) : null;
        }

        public void Apply(VanillaElementSettings settings, VanillaElementDescriptor? descriptor)
        {
            ColorValue? configured = descriptor == null || descriptor.SupportsPrimaryColor ? settings.PrimaryColor : null;
            foreach (var part in _parts)
            {
                if (part.Sprite == null)
                {
                    continue;
                }

                var color = configured is ColorValue c ? HudMath.Tint(c, part.OriginalColor.a) : part.OriginalColor;
                var enabled = part.OriginalEnabled;
                if (settings.Removed)
                {
                    enabled = false;
                }
                else if (!settings.Visible)
                {
                    if (_isEditSession)
                    {
                        color.a *= HudMath.GhostAlpha;
                    }
                    else
                    {
                        enabled = false;
                    }
                }

                if (part.Sprite.color != color)
                {
                    part.Sprite.color = color;
                }

                if (part.Sprite.enabled != enabled)
                {
                    part.Sprite.enabled = enabled;
                }
            }
        }

        private sealed class Part
        {
            public Part(SpriteRenderer sprite)
            {
                Sprite = sprite;
                OriginalColor = sprite.color;
                OriginalEnabled = sprite.enabled;
            }

            public SpriteRenderer Sprite { get; }

            public Color OriginalColor { get; }

            public bool OriginalEnabled { get; }
        }
    }
}
