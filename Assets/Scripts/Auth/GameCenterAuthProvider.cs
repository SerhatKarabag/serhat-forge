#if UNITY_IOS
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Serhat.Forge.Auth
{
    /// <summary>
    /// Game Center authentication provider for iOS.
    /// Reuses any in-flight native request and supports force-reset for stale requests.
    /// </summary>
    public class GameCenterAuthProvider : IGameCenterAuthProvider
    {
#if !UNITY_EDITOR
        [DllImport("__Internal")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool _GameCenterIsAuthenticated();

        [DllImport("__Internal")]
        private static extern void _GameCenterAuthenticate(int requestId, AuthCallback callback);

        [DllImport("__Internal")]
        private static extern void _GameCenterFetchVerificationSignature(int requestId, SignatureCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AuthCallback(int requestId, string playerId, string error);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SignatureCallback(int requestId, string jsonResult, string error);

        // Native code retains these callbacks until GameKit completes asynchronously.
        private static readonly AuthCallback AuthCallbackHandler = OnAuthComplete;
        private static readonly SignatureCallback SignatureCallbackHandler = OnSignatureComplete;
        private static readonly object SyncRoot = new object();
        private static AuthenticationOperation _activeOperation;
        private static int _nextRequestId;

        [AOT.MonoPInvokeCallback(typeof(AuthCallback))]
        private static void OnAuthComplete(int requestId, string playerId, string error)
        {
            var operation = GetActiveOperation(requestId);
            if (operation == null)
            {
                Debug.LogWarning(
                    $"[GameCenterAuth] Ignoring stale auth callback for request {requestId}");
                return;
            }

            if (operation.AuthCompletion.Task.IsCompleted)
            {
                Debug.LogWarning($"[GameCenterAuth] Ignoring duplicate auth callback for request {operation.RequestId}");
                return;
            }

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[GameCenterAuth] Authentication failed for request {operation.RequestId}: {error}");
                operation.AuthCompletion.TrySetResult(Result<string>.Failure(
                    new AuthError(
                        AuthErrorCode.PlatformAuthFailed,
                        "Game Center sign-in failed.",
                        $"Game Center auth error: {error}",
                        isRetryable: true)));
                return;
            }

            Debug.Log($"[GameCenterAuth] Authentication successful for request {operation.RequestId}");
            operation.AuthCompletion.TrySetResult(Result<string>.Success(playerId));
        }

        [AOT.MonoPInvokeCallback(typeof(SignatureCallback))]
        private static void OnSignatureComplete(int requestId, string jsonResult, string error)
        {
            var operation = GetActiveOperation(requestId);
            if (operation == null)
            {
                Debug.LogWarning(
                    $"[GameCenterAuth] Ignoring stale signature callback for request {requestId}");
                return;
            }

            if (operation.SignatureCompletion.Task.IsCompleted)
            {
                Debug.LogWarning($"[GameCenterAuth] Ignoring duplicate signature callback for request {operation.RequestId}");
                return;
            }

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[GameCenterAuth] Signature fetch failed for request {operation.RequestId}: {error}");
                operation.SignatureCompletion.TrySetResult(Result<GameCenterCredential>.Failure(
                    new AuthError(
                        AuthErrorCode.PlatformAuthFailed,
                        "Game Center verification failed.",
                        $"Game Center signature error: {error}",
                        isRetryable: true)));
                return;
            }

            try
            {
                var credential = JsonUtility.FromJson<GameCenterSignatureJson>(jsonResult);

                var result = new GameCenterCredential
                {
                    PlayerId = credential.playerId,
                    PublicKeyUrl = credential.publicKeyUrl,
                    Signature = credential.signature,
                    Salt = credential.salt,
                    Timestamp = credential.timestamp
                };

                Debug.Log($"[GameCenterAuth] Signature fetched for request {operation.RequestId}, player: {result.PlayerId}");
                operation.SignatureCompletion.TrySetResult(Result<GameCenterCredential>.Success(result));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameCenterAuth] Failed to parse signature JSON for request {operation.RequestId}: {ex.Message}");
                operation.SignatureCompletion.TrySetResult(Result<GameCenterCredential>.Failure(
                    new AuthError(
                        AuthErrorCode.PlatformAuthFailed,
                        "Game Center verification failed.",
                        $"JSON parse error: {ex.Message}",
                        innerException: ex)));
            }
        }

        public Task<Result<GameCenterCredential>> AuthenticateAsync()
        {
            lock (SyncRoot)
            {
                if (_activeOperation != null)
                {
                    Debug.Log($"[GameCenterAuth] Joining in-flight request {_activeOperation.RequestId}");
                    return _activeOperation.Completion.Task;
                }

                var operation = new AuthenticationOperation(++_nextRequestId);
                _activeOperation = operation;
                _ = RunAuthenticationAsync(operation);
                return operation.Completion.Task;
            }
        }

        public bool ResetInFlightRequest(string reason)
        {
            AuthenticationOperation operation;

            lock (SyncRoot)
            {
                operation = _activeOperation;
                if (operation == null)
                {
                    return false;
                }

                _activeOperation = null;
            }

            var resetError = new AuthError(
                AuthErrorCode.PlatformAuthFailed,
                "Game Center sign-in timed out. Please try again.",
                $"Game Center request {operation.RequestId} was reset. Reason: {reason}",
                isRetryable: true);

            Debug.LogWarning(
                $"[GameCenterAuth] Resetting in-flight request {operation.RequestId}. Reason: {reason}");
            operation.AuthCompletion.TrySetResult(Result<string>.Failure(resetError));
            operation.SignatureCompletion.TrySetResult(Result<GameCenterCredential>.Failure(resetError));
            operation.Completion.TrySetResult(Result<GameCenterCredential>.Failure(resetError));
            return true;
        }

        private static async Task RunAuthenticationAsync(AuthenticationOperation operation)
        {
            try
            {
                if (!_GameCenterIsAuthenticated())
                {
                    _GameCenterAuthenticate(operation.RequestId, AuthCallbackHandler);

                    var authResult = await operation.AuthCompletion.Task;
                    if (!authResult.IsSuccess)
                    {
                        operation.Completion.TrySetResult(Result<GameCenterCredential>.Failure(authResult.Error));
                        return;
                    }
                }

                _GameCenterFetchVerificationSignature(operation.RequestId, SignatureCallbackHandler);
                var signatureResult = await operation.SignatureCompletion.Task;
                operation.Completion.TrySetResult(signatureResult);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameCenterAuth] Unexpected error in request {operation.RequestId}: {ex.Message}");
                operation.Completion.TrySetResult(Result<GameCenterCredential>.Failure(
                    new AuthError(
                        AuthErrorCode.PlatformAuthFailed,
                        "Game Center verification failed.",
                        $"Unexpected Game Center provider error: {ex.Message}",
                        innerException: ex)));
            }
            finally
            {
                lock (SyncRoot)
                {
                    if (ReferenceEquals(_activeOperation, operation))
                    {
                        _activeOperation = null;
                    }
                }
            }
        }

        private static AuthenticationOperation GetActiveOperation(int requestId)
        {
            lock (SyncRoot)
            {
                return _activeOperation != null && _activeOperation.RequestId == requestId
                    ? _activeOperation
                    : null;
            }
        }

        [Serializable]
        private class GameCenterSignatureJson
        {
            public string playerId;
            public string publicKeyUrl;
            public string signature;
            public string salt;
            public string timestamp;
        }

        private sealed class AuthenticationOperation
        {
            public AuthenticationOperation(int requestId)
            {
                RequestId = requestId;
                Completion = new TaskCompletionSource<Result<GameCenterCredential>>(TaskCreationOptions.RunContinuationsAsynchronously);
                AuthCompletion = new TaskCompletionSource<Result<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
                SignatureCompletion = new TaskCompletionSource<Result<GameCenterCredential>>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public int RequestId { get; }
            public TaskCompletionSource<Result<GameCenterCredential>> Completion { get; }
            public TaskCompletionSource<Result<string>> AuthCompletion { get; }
            public TaskCompletionSource<Result<GameCenterCredential>> SignatureCompletion { get; }
        }
#else
        public Task<Result<GameCenterCredential>> AuthenticateAsync()
        {
            return Task.FromResult(Result<GameCenterCredential>.Failure(
                new AuthError(
                    AuthErrorCode.PlatformAuthFailed,
                    "Game Center is not available in the Editor.",
                    "Game Center requires an iOS device")));
        }

        public bool ResetInFlightRequest(string reason)
        {
            return false;
        }
#endif
    }
}
#endif
