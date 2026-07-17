#nullable enable

using System;
using System.Collections.Generic;
using Serhat.Backend.Monetization.Abstractions;

namespace Serhat.Backend.Monetization.Domain
{
    /// <summary>
    /// Result of a store command that does not return a payload.
    /// </summary>
    public sealed class StoreOperationResult
    {
        /// <summary>Whether the store operation reached its confirmed terminal state.</summary>
        public bool IsSuccess { get; }

        /// <summary>Store error when <see cref="IsSuccess"/> is false.</summary>
        public PurchaseError? Error { get; }

        private StoreOperationResult(bool isSuccess, PurchaseError? error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        public static StoreOperationResult Success() => new(true, null);

        public static StoreOperationResult Failure(PurchaseError error) =>
            new(false, error ?? throw new ArgumentNullException(nameof(error)));
    }

    /// <summary>
    /// Terminal state of a raw store restore operation.
    /// </summary>
    public enum StoreRestoreStatus
    {
        /// <summary>All discovered receipts were returned without error.</summary>
        Succeeded = 0,

        /// <summary>The store had no restorable purchases.</summary>
        NoPurchases = 1,

        /// <summary>Usable receipts were returned together with one or more errors.</summary>
        PartiallySucceeded = 2,

        /// <summary>No usable receipts were returned because the operation failed.</summary>
        Failed = 3
    }

    /// <summary>
    /// Discriminated result of a raw store restore operation.
    /// </summary>
    public sealed class StoreRestoreResult
    {
        /// <summary>Terminal state of the restore operation.</summary>
        public StoreRestoreStatus Status { get; }

        /// <summary>
        /// Whether the operation completed without errors. No purchases is a successful,
        /// terminal outcome.
        /// </summary>
        public bool IsSuccess =>
            Status == StoreRestoreStatus.Succeeded ||
            Status == StoreRestoreStatus.NoPurchases;

        /// <summary>Whether both usable receipts and store errors are present.</summary>
        public bool IsPartialSuccess => Status == StoreRestoreStatus.PartiallySucceeded;

        /// <summary>Usable receipts that may be verified independently.</summary>
        public IReadOnlyList<StoreReceipt> Receipts { get; }

        /// <summary>Errors encountered while restoring or translating store orders.</summary>
        public IReadOnlyList<PurchaseError> Errors { get; }

        /// <summary>The first error, or null when the operation completed without errors.</summary>
        public PurchaseError? Error => Errors.Count == 0 ? null : Errors[0];

        private StoreRestoreResult(
            StoreRestoreStatus status,
            IReadOnlyList<StoreReceipt>? receipts,
            IReadOnlyList<PurchaseError>? errors)
        {
            Status = status;
            Receipts = receipts ?? Array.Empty<StoreReceipt>();
            Errors = errors ?? Array.Empty<PurchaseError>();
        }

        public static StoreRestoreResult Success(IReadOnlyList<StoreReceipt> receipts)
        {
            if (receipts == null)
            {
                throw new ArgumentNullException(nameof(receipts));
            }

            return receipts.Count == 0
                ? NoPurchases()
                : new StoreRestoreResult(StoreRestoreStatus.Succeeded, receipts, null);
        }

        public static StoreRestoreResult NoPurchases() =>
            new(StoreRestoreStatus.NoPurchases, null, null);

        public static StoreRestoreResult Partial(
            IReadOnlyList<StoreReceipt> receipts,
            IReadOnlyList<PurchaseError> errors)
        {
            if (receipts == null)
            {
                throw new ArgumentNullException(nameof(receipts));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            if (receipts.Count == 0)
            {
                throw new ArgumentException(
                    "A partial restore result requires at least one usable receipt.",
                    nameof(receipts));
            }

            if (errors.Count == 0)
            {
                throw new ArgumentException(
                    "A partial restore result requires at least one error.",
                    nameof(errors));
            }

            return new StoreRestoreResult(
                StoreRestoreStatus.PartiallySucceeded,
                receipts,
                errors);
        }

        public static StoreRestoreResult Failure(PurchaseError error) =>
            new(
                StoreRestoreStatus.Failed,
                null,
                new[] { error ?? throw new ArgumentNullException(nameof(error)) });
    }
}
