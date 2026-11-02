using System;

namespace NotCounters.Core.Config
{
    public static class HudFollow
    {
        public static readonly Vec3 ReferenceEnergyPosition = new Vec3(0f, -0.64f, 7.75f);

        public const float ReferenceEnergyScale = 0.02f;

        public const float EnergyBarHalfHeightUnits = 7f;

        public const float RaisedEnergyBarMetres = 0.3f;

        public const float EyeHeightMetres = 1.7f;

        public const float ReferenceEnergyHalfWidth = 1.87f;

        public static float ReferenceEnergyBottom => ReferenceEnergyPosition.Y - (EnergyBarHalfHeightUnits * ReferenceEnergyScale);

        public static float ReferenceEnergyTop => ReferenceEnergyPosition.Y + (EnergyBarHalfHeightUnits * ReferenceEnergyScale);

        public const float MinPullFactor = 0.35f;

        public static float ReferenceElementScale(string? elementId)
        {
            switch (elementId)
            {
                case Counters.VanillaElementIds.Combo:
                case Counters.VanillaElementIds.Multiplier:
                    return SidePanelReference.ElementScale;
                case Counters.VanillaElementIds.Score:
                case Counters.VanillaElementIds.Rank:
                case Counters.VanillaElementIds.RelativeScore:
                case Counters.VanillaElementIds.SongProgress:
                case Counters.VanillaElementIds.Energy:
                    return ReferenceEnergyScale;
                default:
                    return 0f;
            }
        }

        public static float SideWeight(float referenceX)
        {
            float left = SidePanelReference.LeftPanelElementPosition.X;
            float right = SidePanelReference.RightPanelElementPosition.X;
            if (float.IsNaN(referenceX))
            {
                return 0.5f;
            }

            float weight = (referenceX - left) / (right - left);
            return weight < 0f ? 0f : weight > 1f ? 1f : weight;
        }

        public const float FloorLineMetres = 0f;

        public static float? FloorHeight(float energyCentreY, float energyHalfHeight)
        {
            if (float.IsNaN(energyCentreY) || float.IsInfinity(energyCentreY) || float.IsNaN(energyHalfHeight))
            {
                return null;
            }

            float referenceBottom = ReferenceEnergyPosition.Y - (EnergyBarHalfHeightUnits * ReferenceEnergyScale);
            float bottom = energyCentreY - Math.Abs(energyHalfHeight);
            if (bottom < referenceBottom + RaisedEnergyBarMetres)
            {
                return null;
            }

            return FloorLineMetres;
        }

        public static float EnergyBarClearance(
            float referenceTop,
            float referenceBottom,
            float mappedTop,
            float mappedBottom,
            float energyTop,
            float energyBottom,
            float scale)
        {
            float clearance = 0f;
            if (referenceTop <= ReferenceEnergyBottom)
            {
                float limit = energyBottom - ((ReferenceEnergyBottom - referenceTop) * scale);
                clearance = mappedTop > limit ? limit - mappedTop : 0f;
            }
            else if (referenceBottom >= ReferenceEnergyTop)
            {
                float limit = energyTop + ((referenceBottom - ReferenceEnergyTop) * scale);
                clearance = mappedBottom < limit ? limit - mappedBottom : 0f;
            }

            return float.IsNaN(clearance) || float.IsInfinity(clearance) ? 0f : clearance;
        }

        public const int MaxClearancePasses = 4;

        public const float ClearanceTolerance = 1e-4f;

        public static float SettleClearance(float start, Func<float, float> clearanceAt)
        {
            if (clearanceAt == null) throw new ArgumentNullException(nameof(clearanceAt));

            float clearance = float.IsNaN(start) || float.IsInfinity(start) ? 0f : start;
            for (int pass = 0; pass < MaxClearancePasses; pass++)
            {
                float next = clearanceAt(clearance);
                if (float.IsNaN(next) || float.IsInfinity(next))
                {
                    return 0f;
                }

                if (Math.Abs(next - clearance) <= ClearanceTolerance)
                {
                    return clearance;
                }

                clearance = next;
            }

            return 0f;
        }

        public static float PullFactor(float eyeY, float centreY, float halfHeight, float? floorY)
        {
            if (floorY == null || float.IsNaN(centreY) || float.IsNaN(halfHeight))
            {
                return 1f;
            }

            float floor = floorY.Value;
            float bottomBelowEyes = centreY - Math.Abs(halfHeight) - eyeY;
            float floorBelowEyes = floor - eyeY;
            if (centreY - Math.Abs(halfHeight) >= floor || bottomBelowEyes >= 0f || floorBelowEyes >= 0f)
            {
                return 1f;
            }

            float t = floorBelowEyes / bottomBelowEyes;
            return t < MinPullFactor ? MinPullFactor : t > 1f ? 1f : t;
        }
    }
}
