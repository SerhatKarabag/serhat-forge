using System.Threading.Tasks;

namespace Serhat.Forge.Auth
{
    public interface IPlayFabAuthService
    {
        Task<Result<AuthSessionData>> LoginWithCustomIDAsync(string customId, bool createAccount);
        Task<Result<AuthSessionData>> LoginWithGoogleAccountAsync(string serverAuthCode, bool createAccount);
        Task<Result<AuthSessionData>> LoginWithGameCenterAsync(GameCenterCredential credential, bool createAccount);

        Task<Result<bool>> LinkGoogleAccountAsync(string serverAuthCode);
        Task<Result<bool>> LinkGameCenterAccountAsync(GameCenterCredential credential);
        Task<Result<bool>> UnlinkCustomIDAsync(string customId);
    }
}
