using System;
using System.Collections.Generic;

namespace NotCounters.Core.Config
{
    public enum ConfigLoadStatus
    {
        Loaded,
        CreatedDefault,
        RecoveredFromCorrupt,
        NewerSchemaReadOnly,
        Migrated,
    }

    public sealed class ConfigLoadResult
    {
        public ConfigLoadResult(
            HudConfig config,
            ConfigLoadStatus status,
            string? backupPath,
            IReadOnlyList<string> warnings,
            bool isReadOnly,
            string? restoredFromPath = null)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Status = status;
            BackupPath = backupPath;
            Warnings = warnings ?? Array.Empty<string>();
            IsReadOnly = isReadOnly;
            RestoredFromPath = restoredFromPath;
        }

        public HudConfig Config { get; }

        public ConfigLoadStatus Status { get; }

        public string? BackupPath { get; }

        public string? RestoredFromPath { get; }

        public IReadOnlyList<string> Warnings { get; }

        public bool IsReadOnly { get; }
    }
}
