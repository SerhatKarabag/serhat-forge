using System.Threading.Tasks;

namespace Serhat.Forge.Auth
{
    public interface IGoogleAuthProvider
    {
        Task<Result<string>> GetGoogleServerAuthCodeAsync(bool allowInteractive);
        bool ResetInFlightRequest(string reason);
    }

    public interface IGameCenterAuthProvider
    {
        Task<Result<GameCenterCredential>> AuthenticateAsync();
        bool ResetInFlightRequest(string reason);
    }

    public class GameCenterCredential
    {
        public string PlayerId { get; set; }
        public string PublicKeyUrl { get; set; }
        public string Signature { get; set; }
        public string Salt { get; set; }
        public string Timestamp { get; set; }
    }
}
