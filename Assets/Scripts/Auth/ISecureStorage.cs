using System.Threading.Tasks;

namespace Serhat.Forge.Auth
{
    public interface ISecureStorage
    {
        /// <summary>Get existing key or create new GUID-based persistent key</summary>
        Task<Result<string>> GetOrCreatePersistentKeyAsync();

        Task<Result<string>> GetKeyAsync(string keyName);
        Task<Result<bool>> SetKeyAsync(string keyName, string value);
        Task<Result<bool>> DeleteKeyAsync(string keyName);
    }
}
