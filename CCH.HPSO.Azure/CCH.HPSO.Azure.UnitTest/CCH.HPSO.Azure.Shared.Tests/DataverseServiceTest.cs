using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using CCH.HPSO.Azure.Shared.Services;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Newtonsoft.Json;
using System.Reflection;
using Xunit;

namespace CCH.HPSO.Azure.Shared.Tests
{
    public class DataverseServiceTest
    {
        private readonly Mock<IOrganizationService> _orgServiceMock;
        private readonly Mock<IServiceClientFactory> _serviceClientFactoryMock;
        private readonly DataverseService _service;

        public DataverseServiceTest()
        {
            _orgServiceMock = new Mock<IOrganizationService>();
            _serviceClientFactoryMock = new Mock<IServiceClientFactory>();
            _service = new DataverseService(_orgServiceMock.Object, _serviceClientFactoryMock.Object);
        }

        [Fact]
        public void RetrieveAccountPlaceholder_ReturnsAccountCollection_WhenAccountExists()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            var contactId = Guid.NewGuid().ToString();
            var promptTemplateId = Guid.NewGuid().ToString();
            var contactName = "Test Contact";
            var promptTemplateName = "Test Template";
            var accountId = Guid.NewGuid();
            var accountAttributes = new List<string> { "name" };

            var contactRoleEntity = new Entity("ms_contactrole");
            contactRoleEntity["ms_accountid"] = new EntityReference("account", accountId);

            var contactRoles = new EntityCollection(new List<Entity> { contactRoleEntity });

            var accountEntity = new Entity("account");
            accountEntity["name"] = "Test Account";
            var accountCollection = new EntityCollection(new List<Entity> { accountEntity });

            _orgServiceMock.Setup(x => x.RetrieveMultiple(It.Is<QueryExpression>(q => q.EntityName == "ms_contactrole")))
                .Returns(contactRoles);

            _orgServiceMock.Setup(x => x.RetrieveMultiple(It.Is<QueryExpression>(q => q.EntityName == "account")))
                .Returns(accountCollection);

            // Act
            var result = _service.RetrieveAccountPlaceholder(promptTemplateId, contactId, contactName, promptTemplateName, accountAttributes, FailureStageEnum.PromptGeneration);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Entities);
            Assert.Equal("Test Account", result.Entities[0]["name"]);
        }

        [Fact]
        public void RetrieveAccountPlaceholder_ThrowsArgumentException_WhenNoAccountFound()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            var contactId = Guid.NewGuid().ToString();
            var promptTemplateId = Guid.NewGuid().ToString();
            var contactName = "Test Contact";
            var promptTemplateName = "Test Template";
            var accountAttributes = new List<string> { "name" };

            var contactRoles = new EntityCollection(new List<Entity>());
            _orgServiceMock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(contactRoles);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                _service.RetrieveAccountPlaceholder(promptTemplateId, contactId, contactName, promptTemplateName, accountAttributes, FailureStageEnum.PromptGeneration));
            Assert.Contains("No account information found", ex.Message);
        }

        [Fact]
        public void RetrieveTop1SegmentOrdersPlaceholders_ReturnsSegmentOrders_WhenFound()
        {
            // Arrange
            var contactId = Guid.NewGuid().ToString();
            var promptTemplateId = Guid.NewGuid().ToString();
            var promptTemplateName = "Test Template";
            var top1SegmentOrderAttributes = new List<string> { "name" };
            var contactName = "";

            var contactEntity = new Entity("contact");
            contactEntity["fullname"] = "Test Contact";
            contactEntity["msdynci_lookupfield_customerprofile"] = new EntityReference("profile", Guid.NewGuid()) { Name = "profileName" };

            _orgServiceMock.Setup(x => x.Retrieve("contact", It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                .Returns(contactEntity);

            var segmentOrderEntity = new Entity("msdynci_top1suggestedorder");
            segmentOrderEntity["name"] = "SegmentOrder";
            var segmentOrders = new EntityCollection(new List<Entity> { segmentOrderEntity });

            _orgServiceMock.Setup(x => x.RetrieveMultiple(It.Is<QueryExpression>(q => q.EntityName == "msdynci_top1suggestedorder")))
                .Returns(segmentOrders);

            // Act
            var result = _service.RetrieveTop1SegmentOrdersPlaceholders(promptTemplateId, contactId, promptTemplateName, top1SegmentOrderAttributes, FailureStageEnum.TextGeneration, ref contactName);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Entities);
            Assert.Equal("SegmentOrder", result.Entities[0]["name"]);
            Assert.Equal("Test Contact", contactName);
        }

        [Fact]
        public void RetrieveTop1SegmentOrdersPlaceholders_ThrowsArgumentException_WhenNoSegmentOrdersFound()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            var contactId = Guid.NewGuid().ToString();
            var promptTemplateId = Guid.NewGuid().ToString();
            var promptTemplateName = "Test Template";
            var top1SegmentOrderAttributes = new List<string> { "name" };
            var contactName = "";

            var contactEntity = new Entity("contact");
            contactEntity["fullname"] = "Test Contact";
            contactEntity["msdynci_lookupfield_customerprofile"] = new EntityReference("profile", Guid.NewGuid()) { Name = "profileName" };

            _orgServiceMock.Setup(x => x.Retrieve("contact", It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                .Returns(contactEntity);

            var segmentOrders = new EntityCollection(new List<Entity>());
            _orgServiceMock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(segmentOrders);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                _service.RetrieveTop1SegmentOrdersPlaceholders(promptTemplateId, contactId, promptTemplateName, top1SegmentOrderAttributes, FailureStageEnum.TextGeneration, ref contactName));
            Assert.Contains("No segment orders found", ex.Message);
        }

        [Fact]
        public void RetrieveTop1SegmentOrdersPlaceholders_ThrowsArgumentException_WhenInvalid_ContactId()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            var contactId = "";
            var promptTemplateId = Guid.NewGuid().ToString();
            var promptTemplateName = "Test Template";
            var top1SegmentOrderAttributes = new List<string> { "name" };
            var contactName = "";

            var contactEntity = new Entity("contact");
            contactEntity["fullname"] = "Test Contact";
            contactEntity["msdynci_lookupfield_customerprofile"] = new EntityReference("profile", Guid.NewGuid()) { Name = "profileName" };

            _orgServiceMock.Setup(x => x.Retrieve("contact", It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                .Returns(contactEntity);

            var segmentOrders = new EntityCollection(new List<Entity>());
            _orgServiceMock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(segmentOrders);

            // Act & Assert
            var ex = Assert.Throws<ArgumentException>(() =>
                _service.RetrieveTop1SegmentOrdersPlaceholders(promptTemplateId, contactId, promptTemplateName, top1SegmentOrderAttributes, FailureStageEnum.TextGeneration, ref contactName));
            Assert.Contains("Invalid or null contactId", ex.Message);
        }

        [Fact]
        public void GetOptionSetLabel_ReturnsLabel_WhenOptionExists()
        {
            // Arrange
            var promptTemplateId = Guid.NewGuid().ToString();
            var contactId = Guid.NewGuid().ToString();
            var contactName = "Test Contact";
            var promptTemplateName = "Test Template";
            var entityLogicalName = "account";
            var attributeLogicalName = "type";
            var optionSetValue = 1;

            var userLocalizedLabel = new Microsoft.Xrm.Sdk.LocalizedLabel("TestLabel", 1033);
            var label = new Microsoft.Xrm.Sdk.Label("TestLabel", 1033)
            {
                UserLocalizedLabel = userLocalizedLabel
            };

            var picklistMetadata = new PicklistAttributeMetadata
            {
                OptionSet = new OptionSetMetadata
                {
                    Options = {
                new OptionMetadata(label, optionSetValue)
            }
                }
            };

            var response = new RetrieveAttributeResponse
            {
                Results = { ["AttributeMetadata"] = picklistMetadata }
            };

            _orgServiceMock.Setup(x => x.Execute(It.IsAny<RetrieveAttributeRequest>()))
                .Returns(response);

            // Act
            var resultLabel = _service.GetOptionSetLabel(
                promptTemplateId, contactId, contactName, promptTemplateName,
                entityLogicalName, attributeLogicalName, optionSetValue, FailureStageEnum.PromptGeneration);

            // Assert
            Assert.Equal("TestLabel", resultLabel);
        }

        [Fact]
        public void GetOptionSetLabel_ReturnsEmpty_WhenOptionNotFound()
        {
            // Arrange
            var promptTemplateId = Guid.NewGuid().ToString();
            var contactId = Guid.NewGuid().ToString();
            var contactName = "Test Contact";
            var promptTemplateName = "Test Template";
            var entityLogicalName = "account";
            var attributeLogicalName = "type";
            var optionSetValue = 99;

            var picklistMetadata = new PicklistAttributeMetadata
            {
                OptionSet = new OptionSetMetadata
                {
                    Options = {
                        new OptionMetadata(new Microsoft.Xrm.Sdk.Label("TestLabel", 1033), 1)
                    }
                }
            };

            var response = new RetrieveAttributeResponse
            {
                Results = { ["AttributeMetadata"] = picklistMetadata }
            };

            _orgServiceMock.Setup(x => x.Execute(It.IsAny<RetrieveAttributeRequest>()))
                .Returns(response);

            // Act
            var label = _service.GetOptionSetLabel(promptTemplateId, contactId, contactName, promptTemplateName, entityLogicalName, attributeLogicalName, optionSetValue, FailureStageEnum.PromptGeneration);

            // Assert
            Assert.Equal(string.Empty, label);
        }

        [Fact]
        public void GetPromptTemplateMappings_ReturnsMappings_WhenFound()
        {
            // Arrange
            var promptTemplateId = Guid.NewGuid().ToString();
            var contactId = Guid.NewGuid().ToString();
            var contactName = "Test Contact";
            var promptTemplateName = "Test Template";
            var orgService = _orgServiceMock.Object;

            var mappingEntity = new Entity("ms_prompttemplateattributemapping");
            mappingEntity["ms_placeholdername"] = "Placeholder";
            var mappings = new EntityCollection(new List<Entity> { mappingEntity });

            _orgServiceMock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(mappings);

            // Act
            var result = _service.GetPromptTemplateMappings(promptTemplateId, contactId, contactName, promptTemplateName, orgService, FailureStageEnum.PromptGeneration);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.Entities);
            Assert.Equal("Placeholder", result.Entities[0]["ms_placeholdername"]);
        }

        [Fact]
        public void GetPromptTemplateMappings_ThrowsArgumentException_WhenNotFound()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var promptTemplateId = Guid.NewGuid().ToString();
            var contactId = Guid.NewGuid().ToString();
            var contactName = "Test Contact";
            var promptTemplateName = "Test Template";
            var orgService = _orgServiceMock.Object;

            var mappings = new EntityCollection(new List<Entity>());
            _orgServiceMock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryExpression>()))
                .Returns(mappings);

            // Act
            var ex = Assert.Throws<ArgumentException>(() =>
                _service.GetPromptTemplateMappings(promptTemplateId, contactId, contactName, promptTemplateName, orgService, FailureStageEnum.PromptGeneration));

            // Assert
            Assert.Contains("No prompt template mappings found", ex.Message);
        }

        [Fact]
        public void CreateOpenAITextOutputRecordForError_DoesNothing_WhenFailureReasonNullOrStageNone()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");

            var contactId = Guid.NewGuid().ToString();
            var promptTemplateId = Guid.NewGuid().ToString();
            var contactName = "Test Contact";
            var promptTemplateName = "Test Template";

            // Act
            _service.CreateOpenAITextOutputRecordForError("no reason", FailureStageEnum.None, contactId, promptTemplateId, contactName, promptTemplateName);
            _service.CreateOpenAITextOutputRecordForError("reason", FailureStageEnum.PromptGeneration, contactId, promptTemplateId, contactName, promptTemplateName);

            // Assert
            _serviceClientFactoryMock.Verify(x => x.Create(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void GetContactNameByContactId_ReturnsFullName_WhenContactIdIsValid()
        {
            // Arrange
            var orgServiceMock = new Mock<IOrganizationService>();
            var contactId = Guid.NewGuid().ToString();
            var entity = new Entity("contact");
            entity["fullname"] = "John Doe";
            orgServiceMock
                .Setup(s => s.Retrieve("contact", It.IsAny<Guid>(), It.Is<ColumnSet>(cs => cs.Columns.Contains("fullname"))))
                .Returns(entity);

            var service = new DataverseService(orgServiceMock.Object, Mock.Of<IServiceClientFactory>());
            var method = typeof(DataverseService).GetMethod("GetContactNameByContactId", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act
            var result = method?.Invoke(service, new object[] { contactId });

            // Assert
            Assert.Equal("John Doe", result);
        }

        [Fact]
        public void GetContactNameByContactId_ThrowsArgumentNullException_WhenContactIdIsNull()
        {
            // Arrange
            var service = new DataverseService(Mock.Of<IOrganizationService>(), Mock.Of<IServiceClientFactory>());
            var method = typeof(DataverseService).GetMethod("GetContactNameByContactId", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act & Assert
            var ex = Assert.Throws<TargetInvocationException>(() => method?.Invoke(service, new object?[] { null }));
            Assert.IsType<ArgumentNullException>(ex.InnerException);
            Assert.Equal("Contact ID cannot be null. (Parameter 'contactId')", ex.InnerException.Message);
        }

        [Fact]
        public void GetOptionSetLabel_ReturnsError_WhenOptionExists()
        {
            // Arrange
            var promptTemplateId = Guid.NewGuid().ToString();
            var contactId = Guid.NewGuid().ToString();
            var contactName = "Test Contact";
            var promptTemplateName = "Test Template";
            var entityLogicalName = "account";
            var attributeLogicalName = "type";
            var optionSetValue = 1;

            var userLocalizedLabel = new LocalizedLabel("TestLabel", 1033);
            var label = new Label("TestLabel", 1033)
            {
                UserLocalizedLabel = userLocalizedLabel
            };

            _orgServiceMock.Setup(x => x.Execute(It.IsAny<RetrieveAttributeRequest>()));

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                _service.GetOptionSetLabel(
                promptTemplateId, contactId, contactName, promptTemplateName,
                entityLogicalName, attributeLogicalName, optionSetValue, FailureStageEnum.PromptGeneration));

        }

        [Fact]
        public void CreateOpenAITextOutputEntityRecord_PopulatesEntityAndCallsCreate_WithComplianceFailed()
        {
            // Arrange
            var inputMessage = new InputMessage
            {
                ContactId = Guid.NewGuid().ToString(),
                PromptTemplateId = Guid.NewGuid().ToString(),
                ContactName = "Test Contact",
                PromptTemplateName = "Test Template",
                ComplianceThreshold = "0.7m"
            };

            var apiResponse = new APIResponse
            {
                SubjectLine = "Subject",
                Headline = "Headline",
                IntroText = "Intro",
                CTAText = "CTA",
                OutroText = "Outro"
            };

            decimal complianceScore = 0.65m;
            string failureReason = "Some failure reason";

            var mockOrgService = new Mock<IOrganizationService>();
            Entity? capturedEntity = null;
            mockOrgService.Setup(s => s.Create(It.IsAny<Entity>())).Callback<Entity>(e => capturedEntity = e);

            var dataverseService = new DataverseService(mockOrgService.Object, Mock.Of<IServiceClientFactory>());

            // Act
            dataverseService.CreateOpenAITextOutputEntityRecord(inputMessage, apiResponse, complianceScore, failureReason, mockOrgService.Object);

            // Assert
            Assert.NotNull(capturedEntity);
            Assert.Equal("ms_openaitextoutput", capturedEntity.LogicalName);
            Assert.Equal($"{inputMessage.ContactName} - {inputMessage.PromptTemplateName}", capturedEntity["ms_openaitextoutputname"]);
            Assert.Equal(new Guid(inputMessage.ContactId), ((EntityReference)capturedEntity["ms_contactid"]).Id);
            Assert.Equal(new Guid(inputMessage.PromptTemplateId), ((EntityReference)capturedEntity["ms_templateid"]).Id);
            Assert.Equal(apiResponse.SubjectLine, capturedEntity["ms_subjectline"]);
            Assert.Equal(apiResponse.Headline, capturedEntity["ms_headline"]);
            Assert.Equal(apiResponse.IntroText, capturedEntity["ms_introtext"]);
            Assert.Equal(apiResponse.CTAText, capturedEntity["ms_ctatext"]);
            Assert.Equal(apiResponse.OutroText, capturedEntity["ms_outrotext"]);
            Assert.Equal(complianceScore, capturedEntity["ms_compliancescore"]);
            Assert.Equal(JsonConvert.SerializeObject(apiResponse), capturedEntity["ms_openairesponse"]);
            Assert.Equal(399080001, ((OptionSetValue)capturedEntity["statuscode"]).Value);
            Assert.Equal(failureReason, capturedEntity["ms_failurereason"]);
            Assert.Equal(FailureStageEnum.EvaluationAndPublish.ToString(), capturedEntity["ms_failurestage"]);
        }

        [Fact]
        public void CreateOpenAITextOutputEntityRecord_PopulatesEntityAndCallsCreate()
        {
            // Arrange
            var inputMessage = new InputMessage
            {
                ContactId = Guid.NewGuid().ToString(),
                PromptTemplateId = Guid.NewGuid().ToString(),
                ContactName = "Test Contact",
                PromptTemplateName = "Test Template",
                ComplianceThreshold = "0.7m"
            };

            var apiResponse = new APIResponse
            {
                SubjectLine = "Subject",
                Headline = "Headline",
                IntroText = "Intro",
                CTAText = "CTA",
                OutroText = "Outro"
            };

            decimal complianceScore = 0.95m;

            var mockOrgService = new Mock<IOrganizationService>();
            Entity? capturedEntity = null;
            mockOrgService.Setup(s => s.Create(It.IsAny<Entity>())).Callback<Entity>(e => capturedEntity = e);

            var dataverseService = new DataverseService(mockOrgService.Object, Mock.Of<IServiceClientFactory>());

            // Act
            dataverseService.CreateOpenAITextOutputEntityRecord(inputMessage, apiResponse, complianceScore, string.Empty, mockOrgService.Object);

            // Assert
            Assert.NotNull(capturedEntity);
            Assert.Equal("ms_openaitextoutput", capturedEntity.LogicalName);
            Assert.Equal($"{inputMessage.ContactName} - {inputMessage.PromptTemplateName}", capturedEntity["ms_openaitextoutputname"]);
            Assert.Equal(new Guid(inputMessage.ContactId), ((EntityReference)capturedEntity["ms_contactid"]).Id);
            Assert.Equal(new Guid(inputMessage.PromptTemplateId), ((EntityReference)capturedEntity["ms_templateid"]).Id);
            Assert.Equal(apiResponse.SubjectLine, capturedEntity["ms_subjectline"]);
            Assert.Equal(apiResponse.Headline, capturedEntity["ms_headline"]);
            Assert.Equal(apiResponse.IntroText, capturedEntity["ms_introtext"]);
            Assert.Equal(apiResponse.CTAText, capturedEntity["ms_ctatext"]);
            Assert.Equal(apiResponse.OutroText, capturedEntity["ms_outrotext"]);
            Assert.Equal(complianceScore, capturedEntity["ms_compliancescore"]);
            Assert.Equal(JsonConvert.SerializeObject(apiResponse), capturedEntity["ms_openairesponse"]);
            Assert.Equal(1, ((OptionSetValue)capturedEntity["statuscode"]).Value);
        }
    }
}