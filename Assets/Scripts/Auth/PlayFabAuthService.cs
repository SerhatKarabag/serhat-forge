using System;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Serhat.Forge.Auth
{
    public class PlayFabAuthService : IPlayFabAuthService
    {
        public Task<Result<AuthSessionData>> LoginWithCustomIDAsync(string customId, bool createAccount)
        {
            var tcs = new TaskCompletionSource<Result<AuthSessionData>>();

            var request = new LoginWithCustomIDRequest
            {
                CustomId = customId,
                CreateAccount = createAccount,
                InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
                {
                    GetUserAccountInfo = true
                }
            };

            PlayFabClientAPI.LoginWithCustomID(request,
                result => tcs.TrySetResult(Result<AuthSessionData>.Success(MapToSessionData(result))),
                error => tcs.TrySetResult(Result<AuthSessionData>.Failure(MapPlayFabError(error, "LoginWithCustomID"))));

            return tcs.Task;
        }

        public Task<Result<AuthSessionData>> LoginWithGoogleAccountAsync(string serverAuthCode, bool createAccount)
        {
            var tcs = new TaskCompletionSource<Result<AuthSessionData>>();

            var request = new LoginWithGooglePlayGamesServicesRequest
            {
                ServerAuthCode = serverAuthCode,
                CreateAccount = createAccount,
                InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
                {
                    GetUserAccountInfo = true
                }
            };

            PlayFabClientAPI.LoginWithGooglePlayGamesServices(request,
                result => tcs.TrySetResult(Result<AuthSessionData>.Success(MapToSessionData(result))),
                error => tcs.TrySetResult(Result<AuthSessionData>.Failure(MapPlayFabError(error, "LoginWithGooglePlayGamesServices"))));

            return tcs.Task;
        }

        public Task<Result<AuthSessionData>> LoginWithGameCenterAsync(GameCenterCredential credential, bool createAccount)
        {
            var tcs = new TaskCompletionSource<Result<AuthSessionData>>();

            var request = new LoginWithGameCenterRequest
            {
                PlayerId = credential.PlayerId,
                PublicKeyUrl = credential.PublicKeyUrl,
                Signature = credential.Signature,
                Salt = credential.Salt,
                Timestamp = credential.Timestamp,
                CreateAccount = createAccount,
                InfoRequestParameters = new GetPlayerCombinedInfoRequestParams
                {
                    GetUserAccountInfo = true
                }
            };

            PlayFabClientAPI.LoginWithGameCenter(request,
                result => tcs.TrySetResult(Result<AuthSessionData>.Success(MapToSessionData(result))),
                error => tcs.TrySetResult(Result<AuthSessionData>.Failure(MapPlayFabError(error, "LoginWithGameCenter"))));

            return tcs.Task;
        }

        public Task<Result<bool>> LinkGoogleAccountAsync(string serverAuthCode)
        {
            var tcs = new TaskCompletionSource<Result<bool>>();

            var request = new LinkGooglePlayGamesServicesAccountRequest
            {
                ServerAuthCode = serverAuthCode,
                ForceLink = false // Critical: prevent overwriting existing links
            };

            PlayFabClientAPI.LinkGooglePlayGamesServicesAccount(request,
                result => tcs.TrySetResult(Result<bool>.Success(true)),
                error => tcs.TrySetResult(Result<bool>.Failure(MapPlayFabError(error, "LinkGooglePlayGamesServicesAccount"))));

            return tcs.Task;
        }

        public Task<Result<bool>> LinkGameCenterAccountAsync(GameCenterCredential credential)
        {
            var tcs = new TaskCompletionSource<Result<bool>>();

            var request = new LinkGameCenterAccountRequest
            {
                GameCenterId = credential.PlayerId,
                PublicKeyUrl = credential.PublicKeyUrl,
                Signature = credential.Signature,
                Salt = credential.Salt,
                Timestamp = credential.Timestamp,
                ForceLink = false
            };

            PlayFabClientAPI.LinkGameCenterAccount(request,
                result => tcs.TrySetResult(Result<bool>.Success(true)),
                error => tcs.TrySetResult(Result<bool>.Failure(MapPlayFabError(error, "LinkGameCenterAccount"))));

            return tcs.Task;
        }

        public Task<Result<bool>> UnlinkCustomIDAsync(string customId)
        {
            var tcs = new TaskCompletionSource<Result<bool>>();

            var request = new UnlinkCustomIDRequest
            {
                CustomId = customId
            };

            PlayFabClientAPI.UnlinkCustomID(request,
                result => tcs.TrySetResult(Result<bool>.Success(true)),
                error => tcs.TrySetResult(Result<bool>.Failure(MapPlayFabError(error, "UnlinkCustomID"))));

            return tcs.Task;
        }

        private AuthSessionData MapToSessionData(LoginResult result) => new()
        {
            PlayFabId = result.PlayFabId,
            SessionTicket = result.SessionTicket,
            EntityToken = result.EntityToken?.EntityToken,
            HasProviderLink = result.InfoResultPayload?.AccountInfo?.GooglePlayGamesInfo != null ||
                             result.InfoResultPayload?.AccountInfo?.GoogleInfo != null ||
                             result.InfoResultPayload?.AccountInfo?.AppleAccountInfo != null ||
                             result.InfoResultPayload?.AccountInfo?.GameCenterInfo != null
        };

        private AuthError MapPlayFabError(PlayFabError error, string context)
        {
            // Mask sensitive data in logs
            string safeErrorMsg = error.ErrorMessage ?? "Unknown error";
            if (error.ErrorDetails != null)
            {
                safeErrorMsg = safeErrorMsg.Replace(error.ErrorDetails.ToString(), "[REDACTED]");
            }

            switch (error.Error)
            {
                case PlayFabErrorCode.InvalidParams:
                case PlayFabErrorCode.InvalidRequest:
                    return AuthError.InvalidAuthToken($"{context}: {safeErrorMsg}");

                case PlayFabErrorCode.AccountNotFound:
                    return AuthError.AccountNotFound();

                case PlayFabErrorCode.LinkedAccountAlreadyClaimed:
                    return AuthError.AccountConflict();

                case PlayFabErrorCode.ConnectionError:
                case PlayFabErrorCode.ServiceUnavailable:
                    return AuthError.NetworkFailure($"{context}: {error.Error}");

                case PlayFabErrorCode.APIClientRequestRateLimitExceeded:
                    return new AuthError(AuthErrorCode.Unknown,
                        "Çok fazla deneme. Lütfen bekleyin.",
                        $"{context}: Rate limit", isRetryable: true);

                default:
                    bool isRetryable = error.HttpCode >= 500 ||
                                      error.Error == PlayFabErrorCode.ServiceUnavailable;
                    return new AuthError(AuthErrorCode.Unknown,
                        "Giriş başarısız. Lütfen tekrar deneyin.",
                        $"{context}: {error.Error} - {safeErrorMsg}",
                        isRetryable);
            }
        }
    }
}
