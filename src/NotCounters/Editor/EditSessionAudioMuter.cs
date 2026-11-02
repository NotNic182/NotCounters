using System;
using NotCounters.Util;
using SiraUtil.Logging;
using UnityEngine;
using Zenject;

namespace NotCounters.Editor
{
    internal sealed class EditSessionAudioMuter : ITickable, IDisposable
    {
        private readonly AudioSource? _source;
        private readonly bool _wasMuted;
        private bool _disposed;
        private bool _failed;

        public EditSessionAudioMuter(SiraLog log, [InjectOptional] AudioTimeSyncController? audioTimeSyncController)
        {
            try
            {
                var source = audioTimeSyncController != null ? audioTimeSyncController._audioSource : null;
                if (source == null)
                {
                    log.Warn("The edit session's song source was not found; the song may be audible in the HUD editor.");
                    return;
                }

                _source = source;
                _wasMuted = source.mute;
                source.mute = true;
            }
            catch (Exception ex)
            {
                _failed = true;
                Safe.Report("EditSessionAudioMuter.Mute", ex);
            }
        }

        public void Tick()
        {
            if (_disposed || _failed || _source == null)
            {
                return;
            }

            try
            {
                if (!_source.mute)
                {
                    _source.mute = true;
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                Safe.Report("EditSessionAudioMuter.Tick", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Safe.Invoke("EditSessionAudioMuter.Restore", () =>
            {
                if (_source != null)
                {
                    _source.mute = _wasMuted;
                }
            });
        }
    }
}
