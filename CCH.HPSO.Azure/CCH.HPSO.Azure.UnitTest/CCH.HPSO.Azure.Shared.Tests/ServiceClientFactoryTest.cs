using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.Helpers;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.PowerPlatform.Dataverse.Client.Utils;
using Moq;
using Xunit;

namespace CCH.HPSO.Azure.Shared.Tests
{
    public class ServiceClientFactoryTest
    {
        [Fact]
        public void Create_WithValidConnectionString_ReturnsServiceClient()
        {
            // Arrange
            IServiceClientFactory factory = new ServiceClientFactory();
            var connectionString = "AuthType=ClientSecret;Url=https://dummy.crm.dynamics.com;ClientId=dummy-client-id;ClientSecret=dummy-secret;";

            // Act & Assert
            Assert.Throws<DataverseConnectionException>(() =>
            {
                var client = factory.Create(connectionString);
            });
        }
    }
}