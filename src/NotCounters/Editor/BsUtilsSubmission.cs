using System;
using System.Reflection;
using IPA.Loader;
using SiraUtil.Logging;

namespace NotCounters.Editor
{
    internal static class BsUtilsSubmission
    {
        internal const string PluginId = "BS Utils";

        private const string ScoreSubmissionTypeName = "BS_Utils.Gameplay.ScoreSubmission";
        private const string DisableSubmissionMethodName = "DisableSubmission";

        public static bool TryDisable(string modName, SiraLog log)
        {
            try
            {
                var metadata = PluginManager.GetPluginFromId(PluginId);
                if (metadata == null || !PluginManager.IsEnabled(metadata))
                {
                    return false;
                }

                var method = metadata.Assembly?
                    .GetType(ScoreSubmissionTypeName, throwOnError: false)?
                    .GetMethod(DisableSubmissionMethodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (method == null)
                {
                    log.Warn($"BS_Utils is installed but {ScoreSubmissionTypeName}.{DisableSubmissionMethodName}(string) was not found; its submission flag was not set.");
                    return false;
                }

                method.Invoke(null, new object[] { modName });
                return true;
            }
            catch (Exception ex)
            {
                log.Warn($"Could not disable BS_Utils score submission: {ex.Message}");
                return false;
            }
        }
    }
}
