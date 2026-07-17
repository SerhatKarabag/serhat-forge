using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Persistence
{
    /// <summary>
    /// JsonUtility repository with versioning, checksum validation and primary/backup/temp recovery.
    /// DTO roots and nested DTOs must be marked Serializable and must not contain dictionaries.
    /// </summary>
    public sealed class VersionedJsonSaveRepository<TData> : ISaveRepository<TData>
        where TData : class
    {
        private const int EnvelopeFormatVersion = 1;
        private const int BufferSize = 16 * 1024;

        private readonly string _primaryPath;
        private readonly string _backupPath;
        private readonly string _temporaryPath;
        private readonly string _schemaId;
        private readonly int _currentDataVersion;
        private readonly Dictionary<int, ISaveMigration> _migrations;
        private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);

        [Serializable]
        private sealed class SaveEnvelope
        {
            public int formatVersion;
            public string schemaId;
            public int dataVersion;
            public long generation;
            public long savedUtcTicks;
            public string payloadJson;
            public string checksum;
        }

        private sealed class Candidate
        {
            public TData Data;
            public SaveSource Source;
            public int DataVersion;
            public long Generation;
            public bool Migrated;
        }

        public VersionedJsonSaveRepository(
            string absolutePath,
            string schemaId,
            int currentDataVersion,
            IEnumerable<ISaveMigration> migrations = null)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
                throw new ArgumentException("Save path is required.", nameof(absolutePath));
            if (string.IsNullOrWhiteSpace(schemaId))
                throw new ArgumentException("Schema id is required.", nameof(schemaId));
            if (currentDataVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(currentDataVersion));
            if (!typeof(TData).IsSerializable)
                throw new ArgumentException($"{typeof(TData).FullName} must have [Serializable].");
            if (typeof(UnityEngine.Object).IsAssignableFrom(typeof(TData)))
                throw new ArgumentException("UnityEngine.Object cannot be a save root.");

            _primaryPath = Path.GetFullPath(absolutePath);
            _backupPath = _primaryPath + ".bak";
            _temporaryPath = _primaryPath + ".tmp";
            _schemaId = schemaId;
            _currentDataVersion = currentDataVersion;
            _migrations = BuildMigrationMap(migrations, currentDataVersion);
        }

        public async Task SaveAsync(TData data, CancellationToken cancellationToken = default)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            cancellationToken.ThrowIfCancellationRequested();
            var payloadJson = JsonUtility.ToJson(data, false);
            if (string.IsNullOrWhiteSpace(payloadJson))
                throw new InvalidOperationException("JsonUtility produced an empty payload.");

            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var envelope = new SaveEnvelope
                {
                    formatVersion = EnvelopeFormatVersion,
                    schemaId = _schemaId,
                    dataVersion = _currentDataVersion,
                    generation = GetNextGenerationFailClosed(),
                    savedUtcTicks = DateTime.UtcNow.Ticks,
                    payloadJson = payloadJson
                };
                envelope.checksum = ComputeChecksum(envelope);

                await WriteAtomicAsync(
                        JsonUtility.ToJson(envelope, false),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public bool TryLoad(out TData data, out SaveLoadInfo info)
        {
            data = null;
            Candidate best = null;
            var anyFile = false;
            var anyFailure = false;
            var primaryFailureSet = false;
            var futureVersionFound = false;
            SaveLoadInfo firstFailure = default;
            SaveLoadInfo primaryFailure = default;
            SaveLoadInfo futureFailure = default;

            EvaluateCandidate(_primaryPath, SaveSource.Primary, ref best, ref anyFile,
                ref anyFailure, ref firstFailure, ref primaryFailureSet, ref primaryFailure,
                ref futureVersionFound, ref futureFailure);
            EvaluateCandidate(_backupPath, SaveSource.Backup, ref best, ref anyFile,
                ref anyFailure, ref firstFailure, ref primaryFailureSet, ref primaryFailure,
                ref futureVersionFound, ref futureFailure);
            EvaluateCandidate(_temporaryPath, SaveSource.Temporary, ref best, ref anyFile,
                ref anyFailure, ref firstFailure, ref primaryFailureSet, ref primaryFailure,
                ref futureVersionFound, ref futureFailure);

            // A downgraded build must never overwrite a valid newer-version save.
            if (futureVersionFound)
            {
                info = futureFailure;
                return false;
            }

            if (best != null)
            {
                data = best.Data;
                info = new SaveLoadInfo(
                    SaveLoadStatus.Success,
                    best.Source,
                    best.DataVersion,
                    best.Migrated || best.Source != SaveSource.Primary);
                return true;
            }

            if (!anyFile)
            {
                info = new SaveLoadInfo(SaveLoadStatus.NotFound);
                return false;
            }

            info = primaryFailureSet
                ? primaryFailure
                : anyFailure ? firstFailure : new SaveLoadInfo(SaveLoadStatus.Corrupt);
            return false;
        }

        private void EvaluateCandidate(
            string path,
            SaveSource source,
            ref Candidate best,
            ref bool anyFile,
            ref bool anyFailure,
            ref SaveLoadInfo firstFailure,
            ref bool primaryFailureSet,
            ref SaveLoadInfo primaryFailure,
            ref bool futureVersionFound,
            ref SaveLoadInfo futureFailure)
        {
            if (!File.Exists(path))
                return;

            anyFile = true;
            if (TryReadCandidate(path, source, out var candidate, out var failure))
            {
                if (best == null || candidate.Generation > best.Generation ||
                    candidate.Generation == best.Generation &&
                    GetSourcePriority(candidate.Source) > GetSourcePriority(best.Source))
                {
                    best = candidate;
                }
                return;
            }

            if (!anyFailure)
            {
                anyFailure = true;
                firstFailure = failure;
            }

            if (source == SaveSource.Primary)
            {
                primaryFailureSet = true;
                primaryFailure = failure;
            }

            if (failure.Status == SaveLoadStatus.FutureDataVersion)
            {
                futureVersionFound = true;
                futureFailure = failure;
            }
        }

        private bool TryReadCandidate(
            string path,
            SaveSource source,
            out Candidate candidate,
            out SaveLoadInfo failure)
        {
            candidate = null;
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var envelope = JsonUtility.FromJson<SaveEnvelope>(json);
                if (envelope == null || string.IsNullOrWhiteSpace(envelope.payloadJson) ||
                    envelope.generation < 0 || envelope.dataVersion < 0)
                {
                    failure = Failure(SaveLoadStatus.Corrupt, source, "Invalid envelope.");
                    return false;
                }
                if (envelope.formatVersion != EnvelopeFormatVersion)
                {
                    failure = Failure(SaveLoadStatus.UnsupportedFormat, source,
                        $"Envelope format {envelope.formatVersion} is unsupported.");
                    return false;
                }
                if (!string.Equals(envelope.schemaId, _schemaId, StringComparison.Ordinal))
                {
                    failure = Failure(SaveLoadStatus.WrongSchema, source,
                        $"Expected schema '{_schemaId}', found '{envelope.schemaId}'.");
                    return false;
                }
                if (!VerifyChecksum(envelope))
                {
                    failure = Failure(SaveLoadStatus.Corrupt, source, "Checksum mismatch.");
                    return false;
                }
                if (envelope.dataVersion > _currentDataVersion)
                {
                    failure = new SaveLoadInfo(
                        SaveLoadStatus.FutureDataVersion,
                        source,
                        envelope.dataVersion,
                        error: $"Save version {envelope.dataVersion} is newer than supported {_currentDataVersion}.");
                    return false;
                }

                var payloadJson = envelope.payloadJson;
                var dataVersion = envelope.dataVersion;
                var migrated = false;
                while (dataVersion < _currentDataVersion)
                {
                    if (!_migrations.TryGetValue(dataVersion, out var migration))
                    {
                        failure = new SaveLoadInfo(
                            SaveLoadStatus.MigrationFailed,
                            source,
                            dataVersion,
                            error: $"No migration is registered from version {dataVersion}.");
                        return false;
                    }

                    if (!migration.TryMigrate(payloadJson, out var nextJson, out var error) ||
                        string.IsNullOrWhiteSpace(nextJson))
                    {
                        failure = new SaveLoadInfo(
                            SaveLoadStatus.MigrationFailed,
                            source,
                            dataVersion,
                            error: error ?? $"Migration from version {dataVersion} failed.");
                        return false;
                    }

                    payloadJson = nextJson;
                    dataVersion = migration.ToVersion;
                    migrated = true;
                }

                var parsed = JsonUtility.FromJson<TData>(payloadJson);
                if (parsed == null)
                {
                    failure = Failure(
                        SaveLoadStatus.DeserializationFailed,
                        source,
                        "Payload deserialized to null.");
                    return false;
                }

                candidate = new Candidate
                {
                    Data = parsed,
                    Source = source,
                    DataVersion = dataVersion,
                    Generation = envelope.generation,
                    Migrated = migrated
                };
                failure = default;
                return true;
            }
            catch (IOException exception)
            {
                failure = Failure(SaveLoadStatus.IoError, source, exception.Message);
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                failure = Failure(SaveLoadStatus.IoError, source, exception.Message);
                return false;
            }
            catch (Exception exception)
            {
                failure = Failure(SaveLoadStatus.Corrupt, source, exception.Message);
                return false;
            }
        }

        private async Task WriteAtomicAsync(string content, CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(_primaryPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("Save path has no parent directory.");

            Directory.CreateDirectory(directory);
            var bytes = Encoding.UTF8.GetBytes(content);
            var preserveTemporary = false;
            try
            {
                using (var stream = new FileStream(
                           _temporaryPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None,
                           BufferSize,
                           true))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                        .ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Flush(true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    CommitTemporaryFile();
                }
                catch
                {
                    preserveTemporary = true;
                    throw;
                }
            }
            finally
            {
                if (!preserveTemporary)
                    TryDelete(_temporaryPath);
            }
        }

        private void CommitTemporaryFile()
        {
            if (!File.Exists(_primaryPath))
            {
                File.Move(_temporaryPath, _primaryPath);
                return;
            }

            try
            {
                File.Replace(_temporaryPath, _primaryPath, _backupPath, true);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
            catch (IOException) when (File.Exists(_temporaryPath) && File.Exists(_primaryPath)) { }

            File.Copy(_primaryPath, _backupPath, true);
            File.Delete(_primaryPath);
            File.Move(_temporaryPath, _primaryPath);
        }

        private long GetNextGenerationFailClosed()
        {
            long highest = 0;
            InspectEnvelopeGeneration(_primaryPath, ref highest);
            InspectEnvelopeGeneration(_backupPath, ref highest);
            InspectEnvelopeGeneration(_temporaryPath, ref highest);
            if (highest == long.MaxValue)
                throw new InvalidOperationException("Save generation overflow.");
            return highest + 1;
        }

        private void InspectEnvelopeGeneration(string path, ref long highest)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var envelope = JsonUtility.FromJson<SaveEnvelope>(File.ReadAllText(path, Encoding.UTF8));
                if (envelope == null || envelope.formatVersion != EnvelopeFormatVersion ||
                    !string.Equals(envelope.schemaId, _schemaId, StringComparison.Ordinal) ||
                    !VerifyChecksum(envelope))
                    return;

                if (envelope.dataVersion > _currentDataVersion)
                    throw new InvalidOperationException(
                        $"Refusing to overwrite future save version {envelope.dataVersion}.");

                highest = Math.Max(highest, envelope.generation);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // Corrupt candidates do not influence generation.
            }
        }

        private static Dictionary<int, ISaveMigration> BuildMigrationMap(
            IEnumerable<ISaveMigration> migrations,
            int currentDataVersion)
        {
            var result = new Dictionary<int, ISaveMigration>();
            if (migrations == null)
                return result;

            foreach (var migration in migrations)
            {
                if (migration == null || migration.FromVersion < 0 ||
                    migration.ToVersion <= migration.FromVersion ||
                    migration.ToVersion > currentDataVersion)
                    throw new ArgumentException("Migration chain contains an invalid step.", nameof(migrations));
                if (!result.TryAdd(migration.FromVersion, migration))
                    throw new ArgumentException(
                        $"Multiple migrations start at version {migration.FromVersion}.",
                        nameof(migrations));
            }
            return result;
        }

        private static SaveLoadInfo Failure(SaveLoadStatus status, SaveSource source, string error) =>
            new SaveLoadInfo(status, source, error: error);

        private static int GetSourcePriority(SaveSource source)
        {
            switch (source)
            {
                case SaveSource.Primary: return 3;
                case SaveSource.Temporary: return 2;
                case SaveSource.Backup: return 1;
                default: return 0;
            }
        }

        private static string ComputeChecksum(SaveEnvelope envelope)
        {
            var canonical =
                envelope.formatVersion.ToString(CultureInfo.InvariantCulture) + "\n" +
                envelope.schemaId + "\n" +
                envelope.dataVersion.ToString(CultureInfo.InvariantCulture) + "\n" +
                envelope.generation.ToString(CultureInfo.InvariantCulture) + "\n" +
                envelope.savedUtcTicks.ToString(CultureInfo.InvariantCulture) + "\n" +
                envelope.payloadJson;

            using (var sha256 = SHA256.Create())
                return Convert.ToBase64String(sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
        }

        private static bool VerifyChecksum(SaveEnvelope envelope)
        {
            if (string.IsNullOrEmpty(envelope.checksum))
                return false;

            var expected = ComputeChecksum(envelope);
            if (expected.Length != envelope.checksum.Length)
                return false;

            var difference = 0;
            for (var i = 0; i < expected.Length; i++)
                difference |= expected[i] ^ envelope.checksum[i];
            return difference == 0;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Cleanup failure must not hide the original operation.
            }
        }
    }
}
