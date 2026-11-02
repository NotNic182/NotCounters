using System;
using System.IO;
using System.Text;

namespace NotCounters.Util
{
    internal static class AtomicFileWriter
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static bool TryWriteAllText(string path, string contents, out string? error)
        {
            error = null;
            string tempPath = path + ".tmp";
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = Utf8NoBom.GetBytes(contents);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                {
                    ReplaceExisting(tempPath, path);
                }
                else
                {
                    File.Move(tempPath, path);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                TryDelete(tempPath);
                return false;
            }
        }

        private static void ReplaceExisting(string tempPath, string path)
        {
            try
            {
                File.Replace(tempPath, path, destinationBackupFileName: null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Delete(path);
                File.Move(tempPath, path);
            }
            catch (IOException) when (File.Exists(tempPath))
            {
                File.Delete(path);
                File.Move(tempPath, path);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
