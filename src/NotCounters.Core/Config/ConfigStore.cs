using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace NotCounters.Core.Config
{
    public sealed class ConfigStore
    {
        public const string FileName = "config.json";

        private const string TempSuffix = ".tmp";
        private const string BackupSuffix = ".bak";
        private const string BeforeEditSuffix = ".before-edit.bak";
        private const string PreMigrationInfix = ".v";
        private const string CorruptSuffix = ".bad-";
        private const string CorruptTimestampFormat = "yyyyMMdd-HHmmss";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly string _directory;
        private readonly Action<string, string, string> _replaceFile;
        private readonly object _gate = new object();
        private volatile bool _isReadOnly;

        public ConfigStore(string directory)
            : this(directory, (source, destination, backup) => File.Replace(source, destination, backup, ignoreMetadataErrors: true))
        {
        }

        internal ConfigStore(string directory, Action<string, string, string> replaceFile)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A directory is required.", nameof(directory));

            _directory = Path.GetFullPath(directory);
            _replaceFile = replaceFile ?? throw new ArgumentNullException(nameof(replaceFile));
            ConfigPath = Path.Combine(_directory, FileName);
        }

        public string ConfigPath { get; }

        public string BeforeEditPath => ConfigPath + BeforeEditSuffix;

        public bool IsReadOnly => _isReadOnly;

        private string TempPath => ConfigPath + TempSuffix;

        private string BackupPath => ConfigPath + BackupSuffix;

        public ConfigLoadResult Load()
        {
            lock (_gate)
            {
                _isReadOnly = false;
                var warnings = new List<string>();

                TryCreateDirectory(warnings);
                if (!RecoverInterruptedSave(warnings))
                {
                    _isReadOnly = true;
                    return Finish(CreateDefaults(warnings), ConfigLoadStatus.CreatedDefault, null, warnings);
                }

                string? restoredFrom = null;
                if (!File.Exists(ConfigPath) && TryRestoreFromBackup(replaceExisting: false, warnings: null) == BackupRestore.Restored)
                {
                    restoredFrom = BackupPath;
                    warnings.Add($"{ConfigPath} was missing and was restored from {BackupPath}. To start from defaults instead, delete both files.");
                }

                if (!File.Exists(ConfigPath))
                {
                    return UseDefaults(ConfigLoadStatus.CreatedDefault, backupPath: null, warnings);
                }

                return LoadExistingFile(warnings, restoredFrom, canRestoreBackup: restoredFrom == null);
            }
        }

        private ConfigLoadResult LoadExistingFile(List<string> warnings, string? restoredFrom, bool canRestoreBackup)
        {
            string json;
            try
            {
                json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                _isReadOnly = true;
                warnings.Add($"Could not read {ConfigPath} ({ex.Message}). Using defaults for this session without saving, so the file is left untouched.");
                return Finish(CreateDefaults(warnings), ConfigLoadStatus.CreatedDefault, null, warnings);
            }

            JObject root;
            try
            {
                root = ConfigSerializer.ParseObject(json);
            }
            catch (ConfigFormatException ex)
            {
                return RecoverFromCorrupt(ex, warnings, canRestoreBackup);
            }

            ConfigLoadStatus status = ConfigLoadStatus.Loaded;
            long fileVersion = ConfigMigrator.ReadSchemaVersion(root, warnings);
            if (fileVersion > HudConfig.CurrentSchemaVersion)
            {
                _isReadOnly = true;
                status = ConfigLoadStatus.NewerSchemaReadOnly;
                warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} was written by a newer NotCounters (schema {1}, this build understands {2}). It was read best-effort and will not be overwritten; changes made now are not saved.",
                    ConfigPath,
                    fileVersion,
                    HudConfig.CurrentSchemaVersion));
            }
            else if (fileVersion < HudConfig.CurrentSchemaVersion)
            {
                ConfigMigrator.Migrate(root, fileVersion, warnings);
                status = ConfigLoadStatus.Migrated;
            }

            HudConfig config;
            try
            {
                config = ConfigSerializer.ToConfig(root, warnings);
            }
            catch (ConfigFormatException ex)
            {
                return RecoverFromCorrupt(ex, warnings, canRestoreBackup);
            }

            if (status == ConfigLoadStatus.Migrated)
            {
                ConfigValidator.UpgradeReleaseDefaults(config, fileVersion, warnings);
            }

            warnings.AddRange(ConfigValidator.Normalize(config));

            if (status == ConfigLoadStatus.Migrated)
            {
                KeepPreMigrationCopy(fileVersion, warnings);
                if (!TrySaveLocked(config, out string? saveError))
                {
                    warnings.Add("The migrated config could not be saved yet: " + saveError);
                }
            }

            return Finish(config, status, null, warnings, restoredFrom);
        }

        public bool TrySave(HudConfig config, out string? error)
        {
            lock (_gate)
            {
                return TrySaveLocked(config, out error);
            }
        }

        public bool TryKeepBeforeEditCopy(HudConfig config, out bool keptEarlier, out string? error)
        {
            lock (_gate)
            {
                keptEarlier = false;
                if (config == null)
                {
                    error = "No config to keep.";
                    return false;
                }

                if (_isReadOnly)
                {
                    error = $"{ConfigPath} is read-only for this session, so no copy is kept.";
                    return false;
                }

                if (File.Exists(BeforeEditPath))
                {
                    keptEarlier = true;
                    error = null;
                    return true;
                }

                string json;
                try
                {
                    json = ConfigSerializer.Serialize(config);
                }
                catch (Exception ex)
                {
                    error = "The config could not be serialised: " + ex.Message;
                    return false;
                }

                string tempPath = BeforeEditPath + TempSuffix;
                try
                {
                    Directory.CreateDirectory(_directory);
                    WriteDurably(tempPath, json);
                    File.Move(tempPath, BeforeEditPath);
                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    TryDelete(tempPath);
                    error = $"Could not write {BeforeEditPath}: {ex.Message}";
                    return false;
                }
            }
        }

        public bool TryDropBeforeEditCopy(out string? error)
        {
            lock (_gate)
            {
                if (_isReadOnly)
                {
                    error = $"{ConfigPath} is read-only for this session.";
                    return false;
                }

                try
                {
                    if (File.Exists(BeforeEditPath)) File.Delete(BeforeEditPath);
                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Could not remove {BeforeEditPath}: {ex.Message}";
                    return false;
                }
            }
        }

        private bool TrySaveLocked(HudConfig config, out string? error)
        {
            if (config == null)
            {
                error = "No config to save.";
                return false;
            }

            if (_isReadOnly)
            {
                error = $"{ConfigPath} is read-only for this session (newer schema or unreadable file); it was not overwritten.";
                return false;
            }

            string json;
            try
            {
                json = ConfigSerializer.Serialize(config);
            }
            catch (Exception ex)
            {
                error = "The config could not be serialised: " + ex.Message;
                return false;
            }

            try
            {
                Directory.CreateDirectory(_directory);
                WriteDurably(TempPath, json);
                if (File.Exists(ConfigPath))
                {
                    ReplaceKeepingBackup();
                }
                else
                {
                    File.Move(TempPath, ConfigPath);
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                if (File.Exists(ConfigPath)) TryDelete(TempPath);
                error = $"Could not save {ConfigPath}: {ex.Message}";
                return false;
            }
        }

        private void ReplaceKeepingBackup()
        {
            try
            {
                _replaceFile(TempPath, ConfigPath, BackupPath);
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                if (!File.Exists(ConfigPath))
                {
                    File.Move(TempPath, ConfigPath);
                    return;
                }

                File.Copy(ConfigPath, BackupPath, overwrite: true);
                File.Delete(ConfigPath);
                File.Move(TempPath, ConfigPath);
            }
        }

        private void KeepPreMigrationCopy(long fileVersion, List<string> warnings)
        {
            string version = fileVersion.ToString(CultureInfo.InvariantCulture);
            string keepPath = ConfigPath + PreMigrationInfix + version + BackupSuffix;
            try
            {
                File.Copy(ConfigPath, keepPath, overwrite: true);
                warnings.Add($"The schema {version} config from before the upgrade was kept as {keepPath}; later saves never replace it.");
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                warnings.Add($"Could not keep a copy of the schema {version} config from before the upgrade as {keepPath} ({ex.Message}); the original is kept only as {BackupPath}, which the next save replaces.");
            }
        }

        private ConfigLoadResult UseDefaults(ConfigLoadStatus status, string? backupPath, List<string> warnings)
        {
            HudConfig config = CreateDefaults(warnings);
            if (!TrySaveLocked(config, out string? error))
            {
                warnings.Add("The default config could not be written: " + error);
            }

            return Finish(config, status, backupPath, warnings);
        }

        private ConfigLoadResult RecoverFromCorrupt(ConfigFormatException problem, List<string> warnings, bool canRestoreBackup)
        {
            warnings.Add($"{ConfigPath} is not a usable config ({problem.Message}).");

            string? backupPath = _isReadOnly ? null : MoveCorruptFileAside(warnings);
            if (backupPath == null)
            {
                _isReadOnly = true;
                warnings.Add("The file could not be backed up, so it is left in place and defaults are used without saving.");
                return Finish(CreateDefaults(warnings), ConfigLoadStatus.RecoveredFromCorrupt, null, warnings);
            }

            BackupRestore restore = canRestoreBackup ? TryRestoreFromBackup(replaceExisting: true, warnings) : BackupRestore.NotTried;
            if (restore == BackupRestore.Restored)
            {
                warnings.Add($"The unusable file was set aside as {backupPath}; {ConfigPath} was restored from {BackupPath}.");

                ConfigLoadResult restored = LoadExistingFile(warnings, BackupPath, canRestoreBackup: false);
                bool read = restored.Status == ConfigLoadStatus.Loaded
                    || restored.Status == ConfigLoadStatus.Migrated
                    || restored.Status == ConfigLoadStatus.NewerSchemaReadOnly;
                return new ConfigLoadResult(
                    restored.Config,
                    ConfigLoadStatus.RecoveredFromCorrupt,
                    backupPath,
                    restored.Warnings,
                    restored.IsReadOnly,
                    read ? BackupPath : null);
            }

            if (restore == BackupRestore.Unreadable || File.Exists(ConfigPath))
            {
                _isReadOnly = true;
                warnings.Add($"The unusable file was set aside as {backupPath}; defaults are used without saving so that no file is overwritten.");
                return Finish(CreateDefaults(warnings), ConfigLoadStatus.RecoveredFromCorrupt, backupPath, warnings);
            }

            warnings.Add($"The unusable file was moved to {backupPath}; defaults are in use.");
            return UseDefaults(ConfigLoadStatus.RecoveredFromCorrupt, backupPath, warnings);
        }

        private string? MoveCorruptFileAside(List<string> warnings)
        {
            string stamp = DateTime.Now.ToString(CorruptTimestampFormat, CultureInfo.InvariantCulture);
            string basePath = ConfigPath + CorruptSuffix + stamp;
            string backupPath = basePath;
            for (int attempt = 2; File.Exists(backupPath) || Directory.Exists(backupPath); attempt++)
            {
                backupPath = basePath + "-" + attempt.ToString(CultureInfo.InvariantCulture);
            }

            try
            {
                File.Move(ConfigPath, backupPath);
                return backupPath;
            }
            catch (Exception moveError) when (IsFileSystemError(moveError))
            {
                try
                {
                    File.Copy(ConfigPath, backupPath, overwrite: false);
                    return backupPath;
                }
                catch (Exception copyError) when (IsFileSystemError(copyError))
                {
                    warnings.Add($"Could not back up {ConfigPath}: {moveError.Message} / {copyError.Message}");
                    return null;
                }
            }
        }

        private bool RecoverInterruptedSave(List<string> warnings)
        {
            if (File.Exists(ConfigPath) || !File.Exists(TempPath))
            {
                return true;
            }

            try
            {
                ConfigSerializer.ParseObject(File.ReadAllText(TempPath, Encoding.UTF8));
                File.Move(TempPath, ConfigPath);
                warnings.Add($"Restored {ConfigPath} from an interrupted save.");
                return true;
            }
            catch (ConfigFormatException)
            {
                TryDelete(TempPath);
                return true;
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                warnings.Add($"Could not restore {ConfigPath} from the interrupted save in {TempPath} ({ex.Message}). The file is kept, and defaults are used for this session without saving.");
                return false;
            }
        }

        private BackupRestore TryRestoreFromBackup(bool replaceExisting, List<string>? warnings)
        {
            if (!File.Exists(BackupPath) || (!replaceExisting && File.Exists(ConfigPath)))
            {
                return BackupRestore.NotTried;
            }

            string text;
            try
            {
                text = File.ReadAllText(BackupPath, Encoding.UTF8);
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                warnings?.Add($"{BackupPath} could not be read ({ex.Message}).");
                return BackupRestore.Unreadable;
            }

            try
            {
                ConfigSerializer.ParseObject(text);
            }
            catch (ConfigFormatException ex)
            {
                warnings?.Add($"{BackupPath} could not be used either ({ex.Message}).");
                return BackupRestore.Unusable;
            }

            try
            {
                File.Copy(BackupPath, ConfigPath, overwrite: replaceExisting);
                return BackupRestore.Restored;
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                warnings?.Add($"{BackupPath} could not be copied to {ConfigPath} ({ex.Message}).");
                return BackupRestore.Unreadable;
            }
        }

        private void TryCreateDirectory(List<string> warnings)
        {
            try
            {
                Directory.CreateDirectory(_directory);
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                warnings.Add($"Could not create {_directory}: {ex.Message}");
            }
        }

        private ConfigLoadResult Finish(HudConfig config, ConfigLoadStatus status, string? backupPath, List<string> warnings, string? restoredFromPath = null) =>
            new ConfigLoadResult(config, status, backupPath, warnings.AsReadOnly(), _isReadOnly, restoredFromPath);

        private static HudConfig CreateDefaults(List<string> warnings)
        {
            HudConfig config = DefaultConfigFactory.CreateDefault();
            warnings.AddRange(ConfigValidator.Normalize(config));
            return config;
        }

        private static void WriteDurably(string path, string contents)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
            }
        }

        private static bool IsFileSystemError(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is NotSupportedException;

        private enum BackupRestore
        {
            NotTried,
            Restored,
            Unusable,
            Unreadable,
        }
    }
}
