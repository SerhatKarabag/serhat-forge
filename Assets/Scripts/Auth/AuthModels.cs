using System;

namespace Serhat.Forge.Auth
{
    public enum AuthState
    {
        Uninitialized,
        Initializing,
        LoggingInAnon,
        LoggedInAnon,
        LinkingProvider,
        UnlinkingAnon,
        LoggedInProvider,
        Error
    }

    public enum AuthErrorCode
    {
        None,
        NetworkError,
        InvalidToken,
        AccountAlreadyLinked,
        AccountNotFound,
        UnlinkFailed,
        PlatformAuthFailed,
        SecureStorageError,
        ConcurrentOperation,
        Unknown
    }

    public class AuthError
    {
        public AuthErrorCode Code { get; }
        public string UserMessage { get; }
        public string DebugMessage { get; }
        public bool IsRetryable { get; }
        public Exception InnerException { get; }

        public AuthError(AuthErrorCode code, string userMessage, string debugMessage,
            bool isRetryable = false, Exception innerException = null)
        {
            Code = code;
            UserMessage = userMessage;
            DebugMessage = debugMessage;
            IsRetryable = isRetryable;
            InnerException = innerException;
        }

        public static AuthError NetworkFailure(string details) =>
            new AuthError(AuthErrorCode.NetworkError, "İnternet bağlantısı yok. Tekrar dene.",
                details, isRetryable: true);

        public static AuthError AccountConflict() =>
            new AuthError(AuthErrorCode.AccountAlreadyLinked,
                "Bu hesap başka bir oyuncuya bağlı.", "Provider account already linked to different PlayFabId");

        public static AuthError AccountNotFound() =>
            new AuthError(AuthErrorCode.AccountNotFound,
                "Hesap bulunamadı. 'Bağla' ile hesap oluşturabilirsiniz.",
                "No PlayFab account linked to provider");

        public static AuthError InvalidAuthToken(string details) =>
            new AuthError(AuthErrorCode.InvalidToken,
                "Güvenlik doğrulaması başarısız. Tekrar giriş yapın.", details);

        public static AuthError UnlinkFailure(string details) =>
            new AuthError(AuthErrorCode.UnlinkFailed,
                "Giriş başarısız. Lütfen tekrar deneyin.",
                $"UnlinkCustomID failed: {details}", isRetryable: true);

        public static AuthError ConcurrentOperation() =>
            new AuthError(AuthErrorCode.ConcurrentOperation,
                "İşlem devam ediyor. Lütfen bekleyin.", "Auth operation already in progress");

        public static AuthError Generic(string debugMsg) =>
            new AuthError(AuthErrorCode.Unknown,
                "Giriş başarısız. Lütfen tekrar deneyin.", debugMsg);
    }

    public class Result<T>
    {
        public bool IsSuccess { get; }
        public T Value { get; }
        public AuthError Error { get; }

        private Result(T value) { IsSuccess = true; Value = value; }
        private Result(AuthError error) { IsSuccess = false; Error = error; }

        public static Result<T> Success(T value) => new Result<T>(value);
        public static Result<T> Failure(AuthError error) => new Result<T>(error);
    }

    public class AuthSessionData
    {
        public string PlayFabId { get; set; }
        public string SessionTicket { get; set; }
        public string EntityToken { get; set; }
        public bool HasProviderLink { get; set; }
    }
}
