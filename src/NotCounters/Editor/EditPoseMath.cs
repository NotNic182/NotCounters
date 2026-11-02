using System;
using NotCounters.Contracts;
using UnityEngine;

namespace NotCounters.Editor
{
    internal static class EditPoseMath
    {
        public const float MinScale = 0.05f;

        public const float MaxScale = 20f;

        public static float ClampScale(float scale) =>
            float.IsNaN(scale) || float.IsInfinity(scale) ? 1f : Mathf.Clamp(scale, MinScale, MaxScale);

        public static float FiniteScale(float scale) =>
            float.IsNaN(scale) || float.IsInfinity(scale) ? 1f : scale;

        public static float ApplyDeadzone(float value, float deadzone)
        {
            deadzone = Mathf.Clamp(deadzone, 0f, 0.95f);
            var magnitude = Math.Abs(value);
            if (magnitude <= deadzone)
            {
                return 0f;
            }

            return Math.Sign(value) * Mathf.Clamp01((magnitude - deadzone) / (1f - deadzone));
        }

        public static AnchorPose Snap(AnchorPose pose, float gridSize, float angleStep, bool snapRotation)
        {
            var position = pose.Position;
            if (gridSize > 0.0001f)
            {
                position = new Vector3(SnapValue(position.x, gridSize), SnapValue(position.y, gridSize), SnapValue(position.z, gridSize));
            }

            var rotation = pose.Rotation;
            if (snapRotation && angleStep > 0.01f)
            {
                var euler = rotation.eulerAngles;
                rotation = Quaternion.Euler(SnapValue(euler.x, angleStep), SnapValue(euler.y, angleStep), SnapValue(euler.z, angleStep));
            }

            return new AnchorPose(position, rotation, pose.Scale);
        }

        public static Quaternion FaceHead(Transform anchor, Vector3 elementWorld, Vector3 headWorld, Quaternion fallback)
        {
            var direction = elementWorld - headWorld;
            if (direction.sqrMagnitude < 1e-4f)
            {
                return fallback;
            }

            var worldRotation = Quaternion.LookRotation(direction.normalized, anchor.up);
            return Quaternion.Inverse(anchor.rotation) * worldRotation;
        }

        public static float? YawAround(Transform anchor, Vector3 worldDirection)
        {
            var local = anchor.InverseTransformDirection(worldDirection);
            var flat = new Vector2(local.x, local.z);
            if (flat.sqrMagnitude < 1e-4f)
            {
                return null;
            }

            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }

        public static bool Differs(AnchorPose a, AnchorPose b) =>
            (a.Position - b.Position).sqrMagnitude > 1e-10f
            || Quaternion.Angle(a.Rotation, b.Rotation) > 0.01f
            || Math.Abs(a.Scale - b.Scale) > 1e-5f;

        private static float SnapValue(float value, float step) => Mathf.Round(value / step) * step;
    }
}
