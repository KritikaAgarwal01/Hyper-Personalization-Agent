using CCH.HPSO.Azure.Shared.Contracts;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

namespace CCH.HPSO.Azure.Shared.Helpers
{
    /// <summary>
    /// This class implements the IServiceClientFactory interface to create instances of ServiceClient.
    /// </summary>
    public class ServiceClientFactory : IServiceClientFactory
    {
        /// <summary>
        /// This method creates a ServiceClient instance using the provided connection string.
        /// </summary>
        /// <param name="connectionString">The connection string used to establish a connection with the service.</param>
        /// <returns>A ServiceClient instance.</returns>
        IOrganizationService IServiceClientFactory.Create(string connectionString)
        {
            return new ServiceClient(connectionString);
        }
    }
}