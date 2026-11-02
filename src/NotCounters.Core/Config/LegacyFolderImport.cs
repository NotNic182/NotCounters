using System;
using System.IO;

namespace NotCounters.Core.Config
{
    internal static class LegacyFolderImport
    {
        public static bool ShouldImport(string legacyDirectory, string dataDirectory)
        {
            if (!Directory.Exists(legacyDirectory)) return false;
            if (!Directory.Exists(dataDirectory)) return true;

            foreach (string path in Directory.EnumerateFiles(dataDirectory))
            {
                if (Path.GetFileName(path).StartsWith(ConfigStore.FileName, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }
    }
}
