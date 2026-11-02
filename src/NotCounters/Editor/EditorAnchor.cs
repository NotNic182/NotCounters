using System;
using NotCounters.Contracts;
using NotCounters.Util;
using UnityEngine;

namespace NotCounters.Editor
{
    internal static class EditorAnchor
    {
        public static Transform? TryGet(IHudAnchorProvider? provider)
        {
            if (provider == null)
            {
                return null;
            }

            try
            {
                var anchor = provider.Anchor;
                return anchor != null ? anchor : null;
            }
            catch (Exception ex)
            {
                Safe.Report("EditorAnchor.TryGet", ex);
                return null;
            }
        }
    }
}
