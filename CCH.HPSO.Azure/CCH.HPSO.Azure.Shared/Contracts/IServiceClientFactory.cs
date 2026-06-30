using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace CCH.HPSO.Azure.Shared.Contracts
{
    /// <summary>
    /// This interface defines a factory for creating instances of ServiceClient.
    /// </summary>
    public interface IServiceClientFactory
    {
        /// <summary>
        /// The method creates a ServiceClient instance using the provided connection string.
        /// </summary>
        /// <param name="connectionString">The connection string used to establish a connection with the service.</param>
        /// <returns>A ServiceClient instance.</returns>
        IOrganizationService Create(string connectionString);
    }
}