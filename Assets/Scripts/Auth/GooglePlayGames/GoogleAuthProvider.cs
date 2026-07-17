#if UNITY_ANDROID
using System.Threading.Tasks;
using GooglePlayGames;
using UnityEngine;
using UnityEngine.Scripting;

namespace Serhat.Forge.Auth
{
    /// <summary>
    /// Google Play Games authentication provider for Android.
    /// Reuses any in-flight auth request and supports force-reset for stale requests.
    /// </summary>
    [Preserve]
    public class GoogleAuthProvider : IGoogleAuthProvider
    {
        private bool _isInitialized;
        private readonly object _requestLock = new object();
        private InFlightRequest _silentRequest;
        private InFlightRequest _interactiveRequest;
        private int _nextRequestId;

        [Preserve]
        public GoogleAuthProvider()
        {
            InitializePlayGames();
        }

        private void InitializePlayGames()
        {
            if (_isInitialized)
            {
                return;
            }

            PlayGamesPlatform.DebugLogEnabled = Debug.isDebugBuild;
            PlayGamesPlatform.Activate();
            _isInitialized = true;

            Debug.Log("[GoogleAuthProvider] Google Play Games activated");
        }

        public Task<Result<string>> GetGoogleServerAuthCodeAsync(bool allowInteractive)
        {
            return allowInteractive
                ? GetOrStartInteractiveAuthTask()
                : GetOrStartSilentAuthTask();
        }

        public bool ResetInFlightRequest(string reason)
        {
            lock (_requestLock)
            {
                var resetAny = false;
                resetAny |= TryResetRequest(ref _silentRequest, reason);
                resetAny |= TryResetRequest(ref _interactiveRequest, reason);
                return resetAny;
            }
        }

        private Task<Result<string>> GetOrStartSilentAuthTask()
        {
            lock (_requestLock)
            {
                if (_silentRequest != null)
                {
                    Debug.Log($"[GoogleAuthProvider] Joining in-flight silent auth request {_silentRequest.RequestId}");
                    return _silentRequest.Completion.Task;
                }

                _silentRequest = new InFlightRequest(++_nextRequestId);
                _ = RunGoogleServerAuthCodeRequestAsync(_silentRequest, allowInteractive: false);
                return _silentRequest.Completion.Task;
            }
        }

        private Task<Result<string>> GetOrStartInteractiveAuthTask()
        {
            lock (_requestLock)
            {
                if (_interactiveRequest != null)
                {
                    Debug.Log($"[GoogleAuthProvider] Joining in-flight interactive auth request {_interactiveRequest.RequestId}");
                    return _interactiveRequest.Completion.Task;
                }

                _interactiveRequest = new InFlightRequest(++_nextRequestId);
                _ = RunGoogleServerAuthCodeRequestAsync(_interactiveRequest, allowInteractive: true);
                return _interactiveRequest.Completion.Task;
            }
        }

        private async Task RunGoogleServerAuthCodeRequestAsync(InFlightRequest request, bool allowInteractive)
        {
            try
            {
                if (!PlayGamesPlatform.Instance.IsAuthenticated())
                {
                    if (!allowInteractive)
                    {
                        PlayGamesPlatform.Instance.Authenticate(signInStatus =>
                        {
                            if (signInStatus == GooglePlayGames.BasicApi.SignInStatus.Success)
                            {
                                RequestServerAuthCode(request);
                            }
                            else
                            {
                                Debug.LogWarning(
                                    $"[GoogleAuthProvider] Silent authentication failed for request {request.RequestId}: {signInStatus}");
                                request.Completion.TrySetResult(Result<string>.Failure(
                                    new AuthError(
                                        AuthErrorCode.PlatformAuthFailed,
                                        "Google sign-in unavailable.",
                                        $"Google Play Games silent authentication failed: {signInStatus}")));
                            }
                        });

                        await request.Completion.Task;
                        return;
                    }

                    PlayGamesPlatform.Instance.ManuallyAuthenticate(signInStatus =>
                    {
                        if (signInStatus == GooglePlayGames.BasicApi.SignInStatus.Success)
                        {
                            RequestServerAuthCode(request);
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[GoogleAuthProvider] Interactive authentication failed for request {request.RequestId}: {signInStatus}");
                            request.Completion.TrySetResult(Result<string>.Failure(
                                new AuthError(
                                    AuthErrorCode.PlatformAuthFailed,
                                    "Google sign-in failed.",
                                    $"Google Play Games interactive authentication failed: {signInStatus}",
                                    isRetryable: true)));
                        }
                    });

                    await request.Completion.Task;
                    return;
                }

                RequestServerAuthCode(request);
                await request.Completion.Task;
            }
            finally
            {
                lock (_requestLock)
                {
                    if (allowInteractive)
                    {
                        if (ReferenceEquals(_interactiveRequest, request))
                        {
                            _interactiveRequest = null;
                        }
                    }
                    else if (ReferenceEquals(_silentRequest, request))
                    {
                        _silentRequest = null;
                    }
                }
            }
        }

        private bool TryResetRequest(ref InFlightRequest request, string reason)
        {
            if (request == null)
            {
                return false;
            }

            Debug.LogWarning(
                $"[GoogleAuthProvider] Resetting in-flight request {request.RequestId}. Reason: {reason}");
            request.Completion.TrySetResult(Result<string>.Failure(
                new AuthError(
                    AuthErrorCode.PlatformAuthFailed,
                    "Google sign-in timed out. Please try again.",
                    $"Google auth request {request.RequestId} was reset. Reason: {reason}",
                    isRetryable: true)));
            request = null;
            return true;
        }

        private void RequestServerAuthCode(InFlightRequest request)
        {
            PlayGamesPlatform.Instance.RequestServerSideAccess(
                forceRefreshToken: false,
                authCode =>
                {
                    if (string.IsNullOrEmpty(authCode))
                    {
                        Debug.LogWarning(
                            $"[GoogleAuthProvider] Server auth code is null or empty for request {request.RequestId}");
                        request.Completion.TrySetResult(Result<string>.Failure(
                            AuthError.InvalidAuthToken("Google server auth code is null")));
                    }
                    else
                    {
                        Debug.Log(
                            $"[GoogleAuthProvider] Server auth code received successfully for request {request.RequestId}");
                        request.Completion.TrySetResult(Result<string>.Success(authCode));
                    }
                });
        }

        private sealed class InFlightRequest
        {
            public InFlightRequest(int requestId)
            {
                RequestId = requestId;
                Completion = new TaskCompletionSource<Result<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public int RequestId { get; }
            public TaskCompletionSource<Result<string>> Completion { get; }
        }
    }
}
#endif
