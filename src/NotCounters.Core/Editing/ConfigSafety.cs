using NotCounters.Core.Config;

namespace NotCounters.Core.Editing
{
    public enum ConfigLoadNotice
    {
        None,
        PreviousVersionInUse,
        DefaultsInUse,
    }

    public enum BeforeEditCopyAction
    {
        Keep,
        DropNow,
        DropAfterNextSave,
    }

    public static class ConfigSafety
    {
        public const string ReadOnlyLine = "Your config is read-only (see the game log): changes are NOT saved.";

        public const string SaveFailedLine = "Your config could not be saved (see the game log): recent changes are NOT saved.";

        public const string PreviousVersionLine = "Your config could not be read, so its previous version is in use (see the game log).";

        public const string DefaultsLine = "Your config could not be read, so the defaults are in use (see the game log).";

        public const string LeaveUnsavedQuestion = "Your config could not be saved (see the game log). Saving is retried in the menu. Exit?";

        public const string LeaveButton = "Exit";

        public static ConfigLoadNotice ForLoad(ConfigLoadResult? result) =>
            result == null || result.Status != ConfigLoadStatus.RecoveredFromCorrupt || result.IsReadOnly ? ConfigLoadNotice.None
            : result.RestoredFromPath != null ? ConfigLoadNotice.PreviousVersionInUse
            : ConfigLoadNotice.DefaultsInUse;

        public static string? StatusLine(bool readOnly, bool saveFailed, ConfigLoadNotice load) =>
            readOnly ? ReadOnlyLine
            : saveFailed ? SaveFailedLine
            : load == ConfigLoadNotice.PreviousVersionInUse ? PreviousVersionLine
            : load == ConfigLoadNotice.DefaultsInUse ? DefaultsLine
            : null;

        public static bool StayAfterSave(bool saveFailed, bool leaveIfNotSaved) => saveFailed && !leaveIfNotSaved;

        public static BeforeEditCopyAction ForBeforeEditCopy(bool sessionContinues, bool readOnly, bool saveFailed) =>
            sessionContinues || readOnly ? BeforeEditCopyAction.Keep
            : saveFailed ? BeforeEditCopyAction.DropAfterNextSave
            : BeforeEditCopyAction.DropNow;
    }
}
