using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using CCH.HPSO.Azure.Shared.Helpers;
using CCH.HPSO.Azure.Shared.Services;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Moq;
using System.Globalization;
using Xunit;

namespace CCH.HPSO.Azure.Shared.Tests
{
    public class PromptMessageBuilderTest
    {
        private delegate void RetrieveTop1SegmentOrdersPlaceholdersCallback(string a, string b, string c, List<string> d, FailureStageEnum e, ref string f);

        [Fact]
        public void BuildUpdatedMessage_ReturnsEmptyString_WhenServiceClientIsNotReady()
        {
            // Arrange
            var mockFactory = new Mock<IServiceClientFactory>();
            var mockServiceClient = new Mock<ServiceClient>(It.IsAny<string>());

            // Pass the wrapper to the builder (assume constructor or property injection)
            var builder = new PromptMessageBuilder();

            var inputMessage = new InputMessage
            {
                PromptText = "Test",
                ComplianceThreshold = "0.5",
                ContactId = "contactId",
                ContactName = "contactName",
                IsPreview = "false",
                PromptTemplateId = "templateId",
                PromptTemplateName = "templateName",
                PromptLanguage = "en",
                PromptAppVersion = "1.0",
                PromptDeploymentName = "deployment"
            };

            // Act
            var result = builder.BuildUpdatedMessage(inputMessage, "fake-connection-string", mockFactory.Object);

            // Assert
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void MapPlaceholdersToAttributes_MapsCorrectly()
        {
            var placeholders = new List<PlaceHolderInformation>
            {
                new PlaceHolderInformation { Placeholder = "name" }
            };
            var entity = new Entity();
            entity.Attributes["ms_placeholdername"] = "name";
            entity.Attributes["ms_traversalpath"] = "contact.contactrole.account.name";
            var mapping = new EntityCollection(new List<Entity> { entity });

            var builder = new PromptMessageBuilder();
            var (accountAttrs, segmentAttrs) = builder.MapPlaceholdersToAttributes(placeholders, mapping);

            Assert.Contains("name", accountAttrs);
            Assert.Empty(segmentAttrs);
        }
        
        [Fact]
        public void MapPlaceholdersToAttributes_MapsCorrectlyCustomerProfile()
        {
            // Arrange
            var placeholders = new List<PlaceHolderInformation>
            {
                new PlaceHolderInformation { Placeholder = "name" }
            };
            var entity = new Entity();
            entity.Attributes["ms_placeholdername"] = "name";
            entity.Attributes["ms_traversalpath"] = "contact.customerprofile.name";
            var mapping = new EntityCollection(new List<Entity> { entity });

            var builder = new PromptMessageBuilder();

            // Act
            var (accountAttrs, segmentAttrs) = builder.MapPlaceholdersToAttributes(placeholders, mapping);

            // Assert
            Assert.Contains("name", segmentAttrs);
            Assert.Empty(accountAttrs);
        }

        [Fact]
        public void PopulatePlaceholderValues_PopulatesValues()
        {
            // Arrange
            var dataverseServiceMock = new Mock<IDataverseService>();

            dataverseServiceMock
                .Setup(s => s.RetrieveAccountPlaceholder(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<FailureStageEnum>()))
                .Returns(new EntityCollection(new List<Entity>
                {
            new Entity { Attributes = { { "name", "Test Account" } } }
                }));

            string contactName = "Original Contact"; // MUST match ref in setup

            RetrieveTop1SegmentOrdersPlaceholdersCallback value = (
                    string a, string b, string c, List<string> d, FailureStageEnum e, ref string f) =>
                {
                    f = "Updated Contact";
                };
            dataverseServiceMock
                .Setup(s => s.RetrieveTop1SegmentOrdersPlaceholders(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<List<string>>(),
                    It.IsAny<FailureStageEnum>(),
                    ref contactName))
                .Returns(new EntityCollection(new List<Entity>
                {
            new Entity { Attributes = { { "segment", "Test Segment" } } }
                }))
                .Callback(new RetrieveTop1SegmentOrdersPlaceholdersCallback(value));

            var builder = new PromptMessageBuilder();
            var placeholders = new List<PlaceHolderInformation>
            {
                new PlaceHolderInformation { Placeholder = "account_name", TraversalPath = "contact.contactrole.account.name" },
                new PlaceHolderInformation { Placeholder = "segment", TraversalPath = "contact.customerprofile.segment" }
            };

            var accountAttrs = new List<string> { "name" };
            var segmentAttrs = new List<string> { "segment" };

            // Act
            builder.PopulatePlaceholderValues(
                "templateId",
                "Template",
                dataverseServiceMock.Object,
                placeholders,
                "contactId",
                accountAttrs,
                segmentAttrs,
                Mock.Of<IOrganizationService>(),
                FailureStageEnum.None,
                ref contactName);

            // Assert
            Assert.Equal("Updated Contact", contactName);
            Assert.Equal("Test Account", placeholders[0].ActualValue);
            Assert.Equal("Test Segment", placeholders[1].ActualValue);
        }


        [Fact]
        public void PopulatePlaceholderValueFromEntity_SetsActualValue()
        {
            var dataverseServiceMock = new Mock<DataverseService>(Mock.Of<IOrganizationService>(), Mock.Of<IServiceClientFactory>());
            var entity = new Entity();
            entity.Attributes["name"] = "John Doe";
            var entityCollection = new EntityCollection(new List<Entity> { entity });
            var placeholder = new PlaceHolderInformation { TraversalPath = "contact.contactrole.account.name" };

            var builder = new PromptMessageBuilder();
            builder.PopulatePlaceholderValueFromEntity("templateId", "contactId", "Contact", "Template", dataverseServiceMock.Object, placeholder, entityCollection, "account", false, Mock.Of<IOrganizationService>(), FailureStageEnum.None);

            Assert.Equal("John Doe", placeholder.ActualValue);
        }

        [Fact]
        public void PopulatePlaceholderValueFromEntity_ThrowsException()
        {
            // Arrange
            var dataverseServiceMock = new Mock<DataverseService>(Mock.Of<IOrganizationService>(), Mock.Of<IServiceClientFactory>());
            var entity = new Entity();
            entity.Attributes["name"] = null;
            var entityCollection = new EntityCollection(new List<Entity> { entity });
            var placeholder = new PlaceHolderInformation { TraversalPath = "contact.contactrole.account.name" };

            var builder = new PromptMessageBuilder();

            // Act & Assert
            Assert.Throws<InvalidDataException>(() =>
                builder.PopulatePlaceholderValueFromEntity(
                    "templateId",
                    "contactId",
                    "Contact",
                    "Template",
                    dataverseServiceMock.Object,
                    placeholder,
                    entityCollection,
                    "account",
                    false,
                    Mock.Of<IOrganizationService>(),
                    FailureStageEnum.None));
        }

        [Fact]
        public void GetActualValueString_ReturnsString_ForVariousTypes()
        {
            var dataverseServiceMock = new Mock<IDataverseService>();
            var builder = new PromptMessageBuilder();

            Assert.Equal("abc", builder.GetActualValueString("templateId", "contactId", "Contact", "Template", dataverseServiceMock.Object, "abc", "entity", "attr", Mock.Of<IOrganizationService>(), FailureStageEnum.None));
            Assert.Equal("Name", builder.GetActualValueString("templateId", "contactId", "Contact", "Template", dataverseServiceMock.Object, new EntityReference("entity", Guid.NewGuid()) { Name = "Name" }, "entity", "attr", Mock.Of<IOrganizationService>(), FailureStageEnum.None));
            dataverseServiceMock.Setup(s => s.GetOptionSetLabel(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<FailureStageEnum>())).Returns("Label");
            Assert.Equal("Label", builder.GetActualValueString("templateId", "contactId", "Contact", "Template", dataverseServiceMock.Object, new OptionSetValue(1), "entity", "attr", Mock.Of<IOrganizationService>(), FailureStageEnum.None));

            // Set culture to en-US for currency test, skip if not available
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                try
                {
                    CultureInfo.CurrentCulture = new CultureInfo("en-US");
                }
                catch (CultureNotFoundException)
                {
                    // Skip the test if en-US is not available (e.g., globalization-invariant mode)
                    return;
                }
                var moneyResult = builder.GetActualValueString("templateId", "contactId", "Contact", "Template", dataverseServiceMock.Object, new Money(123.45m), "entity", "attr", Mock.Of<IOrganizationService>(), FailureStageEnum.None);
                Assert.Contains("$", moneyResult);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }

            var dateResult = builder.GetActualValueString("templateId", "contactId", "Contact", "Template", dataverseServiceMock.Object, DateTime.UtcNow, "entity", "attr", Mock.Of<IOrganizationService>(), FailureStageEnum.None);
            Assert.Contains("T", dateResult);
        }

        [Fact]
        public void ParseMessage_ParsesValidJson()
        {
            // Arrange
            var builder = new PromptMessageBuilder();
            var json = @"{
                ""ContactId"": ""abc"",
                ""PromptText"": ""Hello"",
                ""ComplianceThreshold"": ""high"",
                ""IsPreview"": ""true"",
                ""PromptTemplateId"": ""template"",
                ""PromptTemplateName"": ""TestTemplate"",
                ""ContactName"": ""John Doe"",
                ""PromptLanguage"": ""English"",
                ""Tone"": ""Professional"",
                ""PromptDeploymentName"": ""deployment1"",
                ""PromptAppVersion"": ""2024-06-01-preview""
            }";

            // Act
            var result = builder.ParseMessage(json, "Method");

            // Assert
            Assert.Equal("abc", result.ContactId);
            Assert.Equal("Hello", result.PromptText);
        }

        [Fact]
        public void ParseMessage_ThrowsArgumentException_OnNullOrEmpty()
        {
            var builder = new PromptMessageBuilder();
            Assert.Throws<ArgumentNullException>(() => builder.ParseMessage(null, "Method"));
        }

        [Fact]
        public void ParseMessage_ThrowsException_WhenInvalidJson()
        {
            var builder = new PromptMessageBuilder();
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => builder.ParseMessage("{not valid json}", "Method"));
        }

        [Fact]
        public void GetActualValueString_ReturnsFormattedCurrency_ForMoney()
        {
            // Arrange
            var money = new Money(1234.56m);
            var expected = money.Value.ToString("C", CultureInfo.CurrentCulture);
            var builder = new PromptMessageBuilder();
            var dataverseServiceMock = new Mock<IDataverseService>();

            // Act
            var result = builder.GetActualValueString(
                "ptid", "cid", "cname", "ptname",
                dataverseServiceMock.Object, money, "account", "revenue",
                Mock.Of<IOrganizationService>(), FailureStageEnum.PromptGeneration);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetActualValueString_ReturnsIsoString_ForDateTime()
        {
            // Arrange
            var date = new DateTime(2024, 7, 23, 15, 30, 45, DateTimeKind.Utc);
            var expected = date.ToString("o", CultureInfo.InvariantCulture);
            var builder = new PromptMessageBuilder();
            var dataverseServiceMock = new Mock<IDataverseService>();

            // Act
            var result = builder.GetActualValueString(
                "ptid", "cid", "cname", "ptname",
                dataverseServiceMock.Object, date, "account", "createdon",
                Mock.Of<IOrganizationService>(), FailureStageEnum.PromptGeneration);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetActualValueString_ReturnsToString_ForDefaultType()
        {
            // Arrange
            var value = 42;
            var expected = value.ToString();
            var builder = new PromptMessageBuilder();
            var dataverseServiceMock = new Mock<IDataverseService>();

            // Act
            var result = builder.GetActualValueString(
                "ptid", "cid", "cname", "ptname",
                dataverseServiceMock.Object, value, "account", "number",
                Mock.Of<IOrganizationService>(), FailureStageEnum.PromptGeneration);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetActualValueString_ReturnsEmptyString_ForNullValue()
        {
            // Arrange
            var builder = new PromptMessageBuilder();
            var dataverseServiceMock = new Mock<IDataverseService>();

            // Act
            var result = builder.GetActualValueString("ptid", "cid", "cname", "ptname",dataverseServiceMock.Object, null, "account", "number",
                Mock.Of<IOrganizationService>(), FailureStageEnum.PromptGeneration);

            // Assert
            Assert.Equal("", result);
        }
    }
}