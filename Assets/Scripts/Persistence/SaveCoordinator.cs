using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Persistence
{
    /// <summary>
    /// Main-thread snapshot coordinator. Participants remain game-specific; storage stays generic.
    /// </summary>
    public sealed class SaveCoordinator<TData> : ISaveCoordinator<TData>
        where TData : class
    {
        private readonly ISaveRepository<TData> _repository;
        private readonly Func<TData> _dataFactory;
        private readonly List<ISaveParticipant<TData>> _participants =
            new List<ISaveParticipant<TData>>();
        private readonly int _ownerThreadId;
        private readonly bool _requireTransactionalRestore;

        private TData _loadedData;
        private bool _hasRestored;

        /// <param name="requireTransactionalRestore">
        /// Rejects non-transactional participants before any restore mutation. Keep enabled for
        /// fail-closed restores; pass false only while migrating legacy participants.
        /// </param>
        public SaveCoordinator(
            ISaveRepository<TData> repository,
            Func<TData> dataFactory,
            bool requireTransactionalRestore = true)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _dataFactory = dataFactory ?? throw new ArgumentNullException(nameof(dataFactory));
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            _requireTransactionalRestore = requireTransactionalRestore;
        }

        public IDisposable Register(ISaveParticipant<TData> participant)
        {
            EnsureOwnerThread();
            if (participant == null)
                throw new ArgumentNullException(nameof(participant));
            if (string.IsNullOrWhiteSpace(participant.Id))
                throw new ArgumentException("Participant Id is required.", nameof(participant));
            if (_participants.Exists(existing =>
                    string.Equals(existing.Id, participant.Id, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Save participant '{participant.Id}' is already registered.");
            }

            if (_hasRestored &&
                _requireTransactionalRestore &&
                !(participant is ITransactionalSaveParticipant<TData>))
            {
                throw new InvalidOperationException(
                    $"Save participant '{participant.Id}' must implement " +
                    $"{nameof(ITransactionalSaveParticipant<TData>)} for late restore.");
            }

            _participants.Add(participant);
            _participants.Sort(CompareParticipants);
            try
            {
                if (_hasRestored)
                    RestoreLateParticipant(participant, _loadedData);
            }
            catch
            {
                _participants.Remove(participant);
                throw;
            }

            return new Registration(this, participant);
        }

        public Task SaveAsync(
            SaveReason reason,
            CancellationToken cancellationToken = default)
        {
            EnsureOwnerThread();
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = _dataFactory();
            if (snapshot == null)
                throw new InvalidOperationException("Save data factory returned null.");

            for (var i = 0; i < _participants.Count; i++)
            {
                var participant = _participants[i];
                try
                {
                    participant.Capture(snapshot, reason);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"Save participant '{participant.Id}' failed to capture.",
                        exception);
                }
            }

            return _repository.SaveAsync(snapshot, cancellationToken);
        }

        public bool TryRestore(out SaveLoadInfo info)
        {
            EnsureOwnerThread();
            if (!_repository.TryLoad(out var data, out info))
                return false;

            if (!TryCaptureRestoreCheckpoints(out var checkpoints, out var checkpointError))
            {
                info = new SaveLoadInfo(
                    SaveLoadStatus.RestoreFailed,
                    info.Source,
                    info.DataVersion,
                    info.RequiresRewrite,
                    checkpointError);
                return false;
            }

            for (var i = 0; i < _participants.Count; i++)
            {
                var participant = _participants[i];
                try
                {
                    participant.Restore(data);
                }
                catch (Exception exception)
                {
                    var rollbackError = RollbackRestore(checkpoints, i);
                    var error = $"Participant '{participant.Id}' failed: {exception.Message}";
                    if (!string.IsNullOrEmpty(rollbackError))
                        error = $"{error} Rollback incomplete: {rollbackError}";

                    info = new SaveLoadInfo(
                        SaveLoadStatus.RestoreFailed,
                        info.Source,
                        info.DataVersion,
                        info.RequiresRewrite,
                        error);
                    return false;
                }
            }

            _loadedData = data;
            _hasRestored = true;
            return true;
        }

        public bool TrySaveBlocking(
            SaveReason reason,
            TimeSpan timeout,
            out string error)
        {
            EnsureOwnerThread();
            if (timeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    var task = SaveAsync(reason, cancellation.Token);
                    if (!task.Wait(timeout))
                    {
                        cancellation.Cancel();
                        _ = task.ContinueWith(
                            faulted => { _ = faulted.Exception; },
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted |
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                        error = $"Save did not complete within {timeout.TotalSeconds:0.##} seconds.";
                        return false;
                    }

                    task.GetAwaiter().GetResult();
                    error = null;
                    return true;
                }
                catch (AggregateException exception)
                {
                    error = exception.Flatten().InnerException?.Message ?? exception.Message;
                    return false;
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                    return false;
                }
            }
        }

        private void Unregister(ISaveParticipant<TData> participant)
        {
            EnsureOwnerThread();
            _participants.Remove(participant);
        }

        private bool TryCaptureRestoreCheckpoints(
            out RestoreCheckpoint[] checkpoints,
            out string error)
        {
            checkpoints = new RestoreCheckpoint[_participants.Count];
            if (_requireTransactionalRestore)
            {
                for (var i = 0; i < _participants.Count; i++)
                {
                    var participant = _participants[i];
                    if (participant is ITransactionalSaveParticipant<TData>)
                        continue;

                    error =
                        $"Participant '{participant.Id}' does not implement " +
                        $"{nameof(ITransactionalSaveParticipant<TData>)}. Restore was rejected " +
                        "before mutation. Pass requireTransactionalRestore: false only for " +
                        "explicit best-effort legacy restore.";
                    return false;
                }
            }

            for (var i = 0; i < _participants.Count; i++)
            {
                if (!(_participants[i] is ITransactionalSaveParticipant<TData> transactional))
                    continue;

                try
                {
                    checkpoints[i] = new RestoreCheckpoint(
                        transactional,
                        transactional.CaptureRestoreSnapshot());
                }
                catch (Exception exception)
                {
                    error =
                        $"Participant '{_participants[i].Id}' failed to capture a restore checkpoint: " +
                        exception.Message;
                    return false;
                }
            }

            error = null;
            return true;
        }

        private string RollbackRestore(RestoreCheckpoint[] checkpoints, int lastAttemptedIndex)
        {
            StringBuilder issues = null;
            for (var i = lastAttemptedIndex; i >= 0; i--)
            {
                var checkpoint = checkpoints[i];
                if (checkpoint.Participant == null)
                {
                    AppendRollbackIssue(
                        ref issues,
                        $"Participant '{_participants[i].Id}' has no restore rollback contract.");
                    continue;
                }

                try
                {
                    checkpoint.Participant.RollbackRestore(checkpoint.Snapshot);
                }
                catch (Exception exception)
                {
                    AppendRollbackIssue(
                        ref issues,
                        $"Participant '{_participants[i].Id}' rollback failed: {exception.Message}");
                }
            }

            return issues?.ToString();
        }

        private static void RestoreLateParticipant(
            ISaveParticipant<TData> participant,
            TData loadedData)
        {
            var transactional = participant as ITransactionalSaveParticipant<TData>;
            var snapshot = transactional?.CaptureRestoreSnapshot();
            try
            {
                participant.Restore(loadedData);
            }
            catch (Exception restoreException)
            {
                if (transactional == null)
                    throw;

                try
                {
                    transactional.RollbackRestore(snapshot);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        $"Participant '{participant.Id}' restore and rollback both failed.",
                        restoreException,
                        rollbackException);
                }

                throw;
            }
        }

        private static void AppendRollbackIssue(ref StringBuilder issues, string issue)
        {
            if (issues == null)
                issues = new StringBuilder();
            else
                issues.Append(' ');

            issues.Append(issue);
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "SaveCoordinator capture, restore and registration must run on its owner thread.");
            }
        }

        private static int CompareParticipants(
            ISaveParticipant<TData> left,
            ISaveParticipant<TData> right)
        {
            var orderComparison = left.Order.CompareTo(right.Order);
            return orderComparison != 0
                ? orderComparison
                : string.CompareOrdinal(left.Id, right.Id);
        }

        private readonly struct RestoreCheckpoint
        {
            public RestoreCheckpoint(
                ITransactionalSaveParticipant<TData> participant,
                object snapshot)
            {
                Participant = participant;
                Snapshot = snapshot;
            }

            public ITransactionalSaveParticipant<TData> Participant { get; }
            public object Snapshot { get; }
        }

        private sealed class Registration : IDisposable
        {
            private readonly object _sync = new object();
            private SaveCoordinator<TData> _owner;
            private ISaveParticipant<TData> _participant;

            public Registration(
                SaveCoordinator<TData> owner,
                ISaveParticipant<TData> participant)
            {
                _owner = owner;
                _participant = participant;
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    if (_owner == null || _participant == null)
                        return;

                    // Unregister may reject a non-owner thread. Keep the registration intact so
                    // the caller can retry Dispose from the coordinator's owner thread.
                    _owner.Unregister(_participant);
                    _participant = null;
                    _owner = null;
                }
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class SaveLifecycleRelay : MonoBehaviour
    {
        [SerializeField] private bool _dontDestroyOnLoad = true;
        [SerializeField] private bool _saveOnPause = true;
        [SerializeField] private bool _saveOnFocusLost;
        [SerializeField, Min(0.1f)] private float _quitTimeoutSeconds = 1.5f;

        private ISaveCoordinator _coordinator;
        private CancellationTokenSource _lifetimeCancellation;
        private bool _isQuitting;
        private bool _saveInFlight;

        public void Initialize(ISaveCoordinator coordinator)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        }

        private void Awake()
        {
            _lifetimeCancellation = new CancellationTokenSource();
            if (_dontDestroyOnLoad)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _saveOnPause)
                SaveFromLifecycleAsync(SaveReason.Pause);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && _saveOnFocusLost)
                SaveFromLifecycleAsync(SaveReason.Pause);
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
            _lifetimeCancellation?.Cancel();
            if (_coordinator == null)
                return;

            if (!_coordinator.TrySaveBlocking(
                    SaveReason.Quit,
                    TimeSpan.FromSeconds(_quitTimeoutSeconds),
                    out var error))
            {
                Debug.LogWarning($"[SaveLifecycleRelay] Quit save failed: {error}");
            }
        }

        private async void SaveFromLifecycleAsync(SaveReason reason)
        {
            if (_isQuitting || _saveInFlight || _coordinator == null)
                return;

            _saveInFlight = true;
            try
            {
                await _coordinator.SaveAsync(reason, _lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _saveInFlight = false;
            }
        }

        private void OnDestroy()
        {
            _lifetimeCancellation?.Cancel();
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
        }
    }
}
