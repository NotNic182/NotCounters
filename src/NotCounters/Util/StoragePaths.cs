using System;
using System.IO;
using IPA.Utilities;
using NotCounters.Core.Config;

namespace NotCounters.Util
{
    internal static class StoragePaths
    {
        private const string LegacyFolderName = "HudStudio";

        public static string DataDirectory => Path.Combine(UnityGame.UserDataPath, "NotCounters");

        public static string LeaderboardCacheFile => Path.Combine(DataDirectory, "leaderboard-cache.json");

        public static bool MigrateLegacyFolder()
        {
            try
            {
                var legacy = Path.Combine(UnityGame.UserDataPath, LegacyFolderName);
                if (!LegacyFolderImport.ShouldImport(legacy, DataDirectory))
                {
                    return false;
                }

                Directory.CreateDirectory(DataDirectory);
                var copied = false;
                foreach (var source in Directory.GetFiles(legacy))
                {
                    var target = Path.Combine(DataDirectory, Path.GetFileName(source));
                    if (!File.Exists(target))
                    {
                        File.Copy(source, target);
                        copied = true;
                    }
                }

                return copied;
            }
            catch (Exception ex)
            {
                Safe.Report("StoragePaths.MigrateLegacyFolder", ex);
                return false;
            }
        }
    }
}
