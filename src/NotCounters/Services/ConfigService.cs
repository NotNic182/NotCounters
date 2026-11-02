using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NotCounters.Contracts;
using NotCounters.Core.Config;
using NotCounters.Core.Editing;
using NotCounters.Util;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace NotCounters.Services
{
    // HMLib has a global ConfigSerializer that would shadow ours
    using ConfigSerializer = NotCounters.Core.Config.ConfigSerializer;

    internal sealed class ConfigService : IConfigService, IInitializable, ITickable, IDisposable
    {
        public const double SaveDelaySeconds = 1.0;

        private readonly SiraLog _log;
        private readonly ConfigStore? _store;

        private readonly object _storeGate = new object();

        private readonly object _workerGate = new object();

        private HudConfig _config;

        private readonly bool _savingBlocked;

        private bool _hasUnsavedChanges;
        private bool _saveScheduled;
        private double _saveDueAt;
        private bool _readOnlyEditLogged;
        private bool _raisingChanged;
        private bool _disposed;

        private long _snapshotSequence;
        private readonly SaveTracker _saves = new SaveTracker();
        private string? _lastSaveError;
        private volatile bool _lastSaveFailed;

        private bool _dropBeforeEditCopyAfterSave;

        private ConfigLoadNotice _loadNotice;

        private PendingSave? _queuedSave;
        private bool _workerRunning;

        public ConfigService(SiraLog log)
        {
            _log = log;
            ConfigLoadResult? result = null;
            try
            {
                if (StoragePaths.MigrateLegacyFolder())
                {
                    _log.Info("Copied the settings of the earlier \"HUD Studio\" build (UserData/HudStudio) to UserData/NotCounters.");
                }

                _store = new ConfigStore(StoragePaths.DataDirectory);
                result = _store.Load();
            }
            catch (Exception ex)
            {
                _log.Error("Could not load the NotCounters config; using defaults and leaving the file untouched this session.");
                _log.Error(ex);
                _savingBlocked = true;
            }

            _config = result?.Config ?? CreateFallbackConfig();
            _loadNotice = ConfigSafety.ForLoad(result);
            if (result != null)
            {
                Safe.Invoke("Config.ReportLoad", () => ReportLoad(result));
            }
        }

        public event Action? Changed;

        public HudConfig Config => _config;

        public HudProfile ActiveProfile
        {
            get
            {
                try
                {
                    return ResolveActiveProfile();
                }
                catch (Exception ex)
                {
                    Safe.Report("Config.ActiveProfile", ex);
                    return FallbackProfile();
                }
            }
        }

        public bool IsReadOnly => _savingBlocked || _store == null || _store.IsReadOnly;

        public bool LastSaveFailed => _lastSaveFailed;

        public HudLayout GetEffectiveLayout(string? environmentSerializedName)
        {
            HudProfile profile = ActiveProfile;
            return Safe.Invoke("Config.GetEffectiveLayout",
                () => LayoutResolver.GetEffective(profile, environmentSerializedName) ?? profile.Layout,
                profile.Layout);
        }

        public HudLayout GetOrCreateEnvironmentLayout(string environmentSerializedName)
        {
            HudProfile profile = ActiveProfile;
            return Safe.Invoke("Config.GetOrCreateEnvironmentLayout",
                () => LayoutResolver.GetEditable(profile, environmentSerializedName, environmentSpecific: true) ?? profile.Layout,
                profile.Layout);
        }

        public bool RemoveEnvironmentOverride(string environmentSerializedName)
        {
            bool removed = Safe.Invoke("Config.RemoveEnvironmentOverride",
                () => !string.IsNullOrEmpty(environmentSerializedName)
                      && LayoutResolver.RemoveOverride(ActiveProfile, environmentSerializedName),
                false);
            if (removed)
            {
                MarkDirty();
            }

            return removed;
        }

        public void SetActiveProfile(string profileId)
        {
            bool changed = Safe.Invoke("Config.SetActiveProfile", () =>
            {
                if (string.IsNullOrEmpty(profileId) || FindProfile(_config, profileId) == null)
                {
                    _log.Warn($"Cannot activate unknown profile '{profileId}'.");
                    return false;
                }

                if (string.Equals(_config.ActiveProfileId, profileId, StringComparison.Ordinal))
                {
                    return false;
                }

                _config.ActiveProfileId = profileId;
                return true;
            }, false);

            if (changed)
            {
                MarkDirty();
            }
        }

        public HudConfig CreateSnapshot() =>
            Safe.Invoke("Config.CreateSnapshot", () => ConfigSerializer.DeepClone(_config), _config);

        public void Restore(HudConfig snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            bool restored = Safe.Invoke("Config.Restore", () =>
            {
                HudConfig copy = ConfigSerializer.DeepClone(snapshot);
                foreach (string warning in ConfigValidator.Normalize(copy))
                {
                    _log.Debug("Restored config: " + warning);
                }

                _config = copy;
                return true;
            }, false);

            if (!restored)
            {
                return;
            }

            _hasUnsavedChanges = true;
            RaiseChanged();
            if (!IsReadOnly && !_disposed)
            {
                ScheduleSave();
            }
        }

        public void MarkDirty()
        {
            if (_disposed)
            {
                return;
            }

            _hasUnsavedChanges = true;
            RaiseChanged();

            if (IsReadOnly)
            {
                if (!_readOnlyEditLogged)
                {
                    _readOnlyEditLogged = true;
                    _log.Warn("The config is read-only this session; changes apply now but will not be saved.");
                }

                return;
            }

            ScheduleSave();
        }

        public void SaveNow()
        {
            Safe.Invoke("Config.SaveNow", () =>
            {
                _saveScheduled = false;
                if (IsReadOnly)
                {
                    return;
                }

                long sequence = ++_snapshotSequence;
                lock (_workerGate)
                {
                    _queuedSave = null;
                }

                _hasUnsavedChanges = !Write(_config, sequence);
            });
        }

        public ConfigLoadNotice TakeLoadNotice()
        {
            ConfigLoadNotice notice = _loadNotice;
            _loadNotice = ConfigLoadNotice.None;
            return notice;
        }

        public void KeepBeforeEditCopy(HudConfig snapshot) => Safe.Invoke("Config.KeepBeforeEditCopy", () =>
        {
            CancelDropAfterSave();

            ConfigStore? store = _store;
            if (snapshot == null || store == null || IsReadOnly)
            {
                return;
            }

            if (!store.TryKeepBeforeEditCopy(snapshot, out bool keptEarlier, out string? error))
            {
                _log.Warn("Could not keep the config from before the editor: " + error);
            }
            else if (keptEarlier)
            {
                _log.Info($"Kept the earlier {store.BeforeEditPath}: an editor session did not end normally.");
            }
            else
            {
                _log.Debug($"Kept the config from before the editor as {store.BeforeEditPath}.");
            }
        });

        public void DropBeforeEditCopy() => Safe.Invoke("Config.DropBeforeEditCopy", () =>
        {
            CancelDropAfterSave();

            ConfigStore? store = _store;
            if (store == null || IsReadOnly)
            {
                return;
            }

            if (!store.TryDropBeforeEditCopy(out string? error))
            {
                _log.Warn("Could not remove the config from before the editor: " + error);
            }
        });

        public void DropBeforeEditCopyAfterNextSave() => Safe.Invoke("Config.DropBeforeEditCopyAfterNextSave", () =>
        {
            if (_store == null || IsReadOnly)
            {
                return;
            }

            lock (_storeGate)
            {
                _dropBeforeEditCopyAfterSave = true;
            }
        });

        public void Initialize()
        {
            Application.quitting += OnApplicationQuitting;
        }

        public void Tick()
        {
            if (!_saveScheduled || MonotonicClock.Now < _saveDueAt)
            {
                return;
            }

            _saveScheduled = false;
            Safe.Invoke("Config.DebouncedSave", BeginBackgroundSave);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Application.quitting -= OnApplicationQuitting;
            FlushPendingChanges();
            _disposed = true;
            Changed = null;
        }

        private void OnApplicationQuitting() => Safe.Invoke("Config.Quitting", FlushPendingChanges);

        private void FlushPendingChanges()
        {
            bool unwritten;
            lock (_storeGate)
            {
                unwritten = _snapshotSequence > _saves.LastWritten;
            }

            if (_hasUnsavedChanges || _saveScheduled || _lastSaveFailed || unwritten)
            {
                SaveNow();
            }
        }

        private void ScheduleSave()
        {
            _saveScheduled = true;
            _saveDueAt = MonotonicClock.Now + SaveDelaySeconds;
        }

        private void BeginBackgroundSave()
        {
            if (IsReadOnly || !_hasUnsavedChanges)
            {
                return;
            }

            HudConfig copy = ConfigSerializer.DeepClone(_config);
            long sequence = ++_snapshotSequence;
            _hasUnsavedChanges = false;

            lock (_workerGate)
            {
                _queuedSave = new PendingSave(copy, sequence);
                if (_workerRunning)
                {
                    return;
                }

                _workerRunning = true;
            }

            Task.Run(RunSaveWorker);
        }

        private void RunSaveWorker()
        {
            while (true)
            {
                PendingSave? job;
                lock (_workerGate)
                {
                    job = _queuedSave;
                    _queuedSave = null;
                    if (job == null)
                    {
                        _workerRunning = false;
                        return;
                    }
                }

                try
                {
                    Write(job.Config, job.Sequence);
                }
                catch (Exception ex)
                {
                    lock (_storeGate)
                    {
                        _saves.Failed(job.Sequence);
                        _lastSaveFailed = _saves.IsFailing;
                    }

                    Safe.Report("Config.SaveWorker", ex);
                }
            }
        }

        private bool Write(HudConfig config, long sequence)
        {
            ConfigStore? store = _store;
            if (store == null)
            {
                return false;
            }

            lock (_storeGate)
            {
                if (_saves.IsWritten(sequence))
                {
                    return true;
                }

                if (store.TrySave(config, out string? error))
                {
                    _saves.Written(sequence);
                    _lastSaveFailed = _saves.IsFailing;
                    if (!_lastSaveFailed)
                    {
                        _lastSaveError = null;
                        if (_dropBeforeEditCopyAfterSave)
                        {
                            _dropBeforeEditCopyAfterSave = false;
                            DropBeforeEditCopyAfterSave(store);
                        }
                    }

                    return true;
                }

                _saves.Failed(sequence);
                _lastSaveFailed = _saves.IsFailing;
                if (!string.Equals(error, _lastSaveError, StringComparison.Ordinal))
                {
                    _lastSaveError = error;
                    _log.Warn("Config save failed: " + (error ?? "unknown error"));
                }

                return false;
            }
        }

        private void DropBeforeEditCopyAfterSave(ConfigStore store) => Safe.Invoke("Config.DropBeforeEditCopyAfterSave", () =>
        {
            if (store.TryDropBeforeEditCopy(out string? error))
            {
                _log.Debug($"Removed {store.BeforeEditPath}: the config is saved again after the editor's failed save.");
            }
            else
            {
                _log.Warn("Could not remove the config from before the editor: " + error);
            }
        });

        private void CancelDropAfterSave()
        {
            lock (_storeGate)
            {
                _dropBeforeEditCopyAfterSave = false;
            }
        }

        private void RaiseChanged()
        {
            Action? handlers = Changed;

            if (handlers == null || _raisingChanged)
            {
                return;
            }

            _raisingChanged = true;
            try
            {
                foreach (Delegate handler in handlers.GetInvocationList())
                {
                    var action = (Action)handler;
                    Safe.Invoke("Config.Changed:" + (action.Method.DeclaringType?.Name ?? "?"), action);
                }
            }
            finally
            {
                _raisingChanged = false;
            }
        }

        private HudProfile ResolveActiveProfile()
        {
            HudConfig config = _config;
            config.Profiles ??= new List<HudProfile>();

            HudProfile? profile = FindProfile(config, config.ActiveProfileId);
            if (profile != null)
            {
                return profile;
            }

            foreach (HudProfile? candidate in config.Profiles)
            {
                if (candidate != null)
                {
                    _log.Warn($"Active profile '{config.ActiveProfileId}' does not exist; switching to '{candidate.Id}'.");
                    config.ActiveProfileId = candidate.Id;
                    MarkRepaired();
                    return candidate;
                }
            }

            HudProfile created = FallbackProfile();
            config.Profiles.Add(created);
            config.ActiveProfileId = created.Id;
            _log.Warn("The config had no profile; created the default profile.");
            MarkRepaired();
            return created;
        }

        private void MarkRepaired()
        {
            _hasUnsavedChanges = true;
            if (!IsReadOnly && !_disposed)
            {
                ScheduleSave();
            }
        }

        private static HudProfile? FindProfile(HudConfig config, string? id)
        {
            if (config.Profiles == null || id == null)
            {
                return null;
            }

            foreach (HudProfile? profile in config.Profiles)
            {
                if (profile != null && string.Equals(profile.Id, id, StringComparison.Ordinal))
                {
                    return profile;
                }
            }

            return null;
        }

        private static HudProfile FallbackProfile()
        {
            try
            {
                return DefaultConfigFactory.CreateProfile(HudProfile.DefaultProfileId, "Default");
            }
            catch (Exception ex)
            {
                Safe.Report("Config.DefaultProfile", ex);
                return new HudProfile();
            }
        }

        private static HudConfig CreateFallbackConfig()
        {
            try
            {
                return DefaultConfigFactory.CreateDefault();
            }
            catch (Exception ex)
            {
                Safe.Report("Config.DefaultConfig", ex);
                var config = new HudConfig();
                config.Profiles.Add(new HudProfile());
                return config;
            }
        }

        private void ReportLoad(ConfigLoadResult result)
        {
            string path = _store?.ConfigPath ?? StoragePaths.DataDirectory;
            switch (result.Status)
            {
                case ConfigLoadStatus.Loaded:
                    _log.Info($"Loaded {path}.");
                    break;
                case ConfigLoadStatus.CreatedDefault:
                    _log.Info($"Created the default config at {path}.");
                    break;
                case ConfigLoadStatus.Migrated:
                    _log.Info($"Loaded {path} and migrated it to schema {HudConfig.CurrentSchemaVersion}.");
                    break;
                case ConfigLoadStatus.RecoveredFromCorrupt when result.RestoredFromPath != null:
                    _log.Warn($"{path} could not be read; it was set aside as {result.BackupPath} and its previous version ({result.RestoredFromPath}) is in use.");
                    break;
                case ConfigLoadStatus.RecoveredFromCorrupt:
                    _log.Warn($"{path} could not be read; it was backed up to {result.BackupPath ?? "(no backup)"} and defaults are used.");
                    break;
                case ConfigLoadStatus.NewerSchemaReadOnly:
                    _log.Warn($"{path} was written by a newer NotCounters; it is loaded best-effort and will not be overwritten.");
                    break;
                default:
                    _log.Info($"Loaded {path} ({result.Status}).");
                    break;
            }

            if (result.Warnings != null)
            {
                foreach (string warning in result.Warnings)
                {
                    _log.Warn("Config: " + warning);
                }
            }

            if (result.IsReadOnly || IsReadOnly)
            {
                _log.Notice("The config is read-only for this session; edits will not be saved.");
            }
        }

        private sealed class PendingSave
        {
            public PendingSave(HudConfig config, long sequence)
            {
                Config = config;
                Sequence = sequence;
            }

            public HudConfig Config { get; }

            public long Sequence { get; }
        }
    }
}
