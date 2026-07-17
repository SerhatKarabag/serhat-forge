using System;
using System.Threading;
using System.Threading.Tasks;

namespace Serhat.Forge.Persistence
{
    public enum SaveReason
    {
        Manual,
        Checkpoint,
        Pause,
        Quit,
        Recovery
    }

    public enum SaveSource
    {
        None,
        Primary,
        Backup,
        Temporary
    }

    public enum SaveLoadStatus
    {
        Success,
        NotFound,
        Corrupt,
        WrongSchema,
        UnsupportedFormat,
        FutureDataVersion,
        MigrationFailed,
        DeserializationFailed,
        IoError,
        RestoreFailed
    }

    public readonly struct SaveLoadInfo
    {
        public SaveLoadInfo(
            SaveLoadStatus status,
            SaveSource source = SaveSource.None,
            int dataVersion = 0,
            bool requiresRewrite = false,
            string error = null)
        {
            Status = status;
            Source = source;
            DataVersion = dataVersion;
            RequiresRewrite = requiresRewrite;
            Error = error;
        }

        public SaveLoadStatus Status { get; }
        public SaveSource Source { get; }
        public int DataVersion { get; }
        public bool RequiresRewrite { get; }
        public string Error { get; }
        public bool IsSuccess => Status == SaveLoadStatus.Success;
    }

    /// <summary>Migrates a raw JsonUtility payload between two schema versions.</summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }
        bool TryMigrate(string sourceJson, out string migratedJson, out string error);
    }

    public interface ISaveRepository<TData> where TData : class
    {
        Task SaveAsync(TData data, CancellationToken cancellationToken = default);
        bool TryLoad(out TData data, out SaveLoadInfo info);
    }

    public interface ISaveParticipant<TData> where TData : class
    {
        string Id { get; }
        int Order { get; }
        void Capture(TData destination, SaveReason reason);
        void Restore(TData source);
    }

    /// <summary>
    /// Restore transaction contract for participants that can return to their pre-restore state.
    /// SaveCoordinator requires this contract for restores by default. Existing ISaveParticipant
    /// implementations remain valid for capture and can use explicit best-effort legacy restore.
    /// </summary>
    public interface ITransactionalSaveParticipant<TData> : ISaveParticipant<TData>
        where TData : class
    {
        /// <summary>
        /// Captures an opaque, non-mutating snapshot immediately before a restore transaction.
        /// </summary>
        object CaptureRestoreSnapshot();

        /// <summary>Restores the state captured by <see cref="CaptureRestoreSnapshot"/>.</summary>
        void RollbackRestore(object snapshot);
    }

    public interface ISaveCoordinator
    {
        Task SaveAsync(SaveReason reason, CancellationToken cancellationToken = default);
        bool TryRestore(out SaveLoadInfo info);
        bool TrySaveBlocking(SaveReason reason, TimeSpan timeout, out string error);
    }

    public interface ISaveCoordinator<TData> : ISaveCoordinator where TData : class
    {
        IDisposable Register(ISaveParticipant<TData> participant);
    }
}
