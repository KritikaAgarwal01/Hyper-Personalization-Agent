using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;

namespace CCH.HPSO.Azure.Shared.Services
{
    /// <summary>
    /// The DataverseService class provides methods to interact with the Dataverse organization service.
    /// </summary>
    public class DataverseService : IDataverseService
    {
        /// <summary>
        /// The organization service instance used to interact with Dataverse. 
        /// </summary>
        private readonly IOrganizationService _orgService;

        /// <summary>
        /// The service client factory used to create a connection to the Dataverse.
        /// </summary>
        private readonly IServiceClientFactory _serviceClientFactory;

        /// <summary>
        /// The constant representing the placeholder for the contact's full name.
        /// </summary>
        private const string Fullname = "fullname";
        
        /// <summary>
        /// The constant representing the placeholder for the contact's id.
        /// </summary>
        private const string ContactId = "cch_contactid";

        /// <summary>
        /// The constant representing the logical name of the contact entity.
        /// </summary>
        private const string Contact = "contact";

        /// <summary>
        /// The constructor for the DataverseService class.
        /// </summary>
        /// <param name="orgService"></param>
        public DataverseService(IOrganizationService orgService, IServiceClientFactory serviceClientFactory)
        {
            _orgService = orgService;
            _serviceClientFactory = serviceClientFactory;
        }

        /// <summary>
        /// The method retrieves the account placeholder associated with a contact.
        /// </summary>
        /// <param name="contactId">The contactId of customer</param>
        /// <param name="accountAttributes">List of account attribute names to retrieve.</param>
        /// <param name="failureStageEnum">Specifies the failure stage for which to retrieve account placeholders.</param>
        /// <param name="promptTemplateId"> The prompt template id associated with the failure.</param>
        /// <returns>The collection of account placeholders.</returns>
        public EntityCollection RetrieveAccountPlaceholder(string promptTemplateId, string contactId, string contactName, string promptTemplateName, List<string> accountAttributes, FailureStageEnum failureStageEnum)
        {
            // Set Condition Values
            var query_cch_contactrole = 311220009;

            var query = new QueryExpression("cch_contactrole")
            {
                ColumnSet = new ColumnSet(
                    ContactId,
                    "cch_accountid"
                ),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression(ContactId, ConditionOperator.Equal, contactId),
                        new ConditionExpression("cch_contactrole", ConditionOperator.Equal, query_cch_contactrole)
                    }
                }
            };

            EntityCollection objContactRoles = _orgService.RetrieveMultiple(query);
            var accountId = objContactRoles != null && objContactRoles.Entities != null && objContactRoles.Entities.Count > 0 ? objContactRoles.Entities[0].GetAttributeValue<EntityReference>("cch_accountid")?.Id : Guid.Empty;

            EntityCollection accountCollection = new EntityCollection();
            if (accountId != Guid.Empty)
            {
                var accountQuery = new QueryExpression("account")
                {
                    ColumnSet = new ColumnSet(accountAttributes.ToArray()),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                        {
                            new ConditionExpression("accountid", ConditionOperator.Equal, accountId)
                        }
                    }
                };

                accountCollection = _orgService.RetrieveMultiple(accountQuery);
            }

            if (accountCollection != null && accountCollection.Entities.Count > 0)
            {
                return accountCollection;
            }
            else
            {
                throw new ArgumentException($"No account information found for the provided contactId {contactId}");
            }
        }

        /// <summary>
        /// The method retrieves the top 1 segment orders placeholders associated with a contact.
        /// </summary>
        /// <param name="promptTemplateId"> The prompt template id associated with the failure.</param>
        /// <param name="contactId">The contact id</param>
        /// <param name="top1SegmentOrderAttributes">List of top 1 segment order attribute names to retrieve.</param>
        /// <returns>The collection of top 1 segment orders placeholders.</returns>
        /// <param name="failureStageEnum">Specifies the failure stage for which to retrieve top 1 segment orders placeholders.</param>
        /// <exception cref="ArgumentException"></exception>
        public EntityCollection RetrieveTop1SegmentOrdersPlaceholders(string promptTemplateId, string contactId, string promptTemplateName, List<string> top1SegmentOrderAttributes, FailureStageEnum failureStageEnum, ref string contactName)
        {
            if (!Guid.TryParse(contactId, out Guid contactGuid))
                throw new ArgumentException("Invalid or null contactId", nameof(contactId));

            Entity objcontactEntity = _orgService.Retrieve(Contact, contactGuid, new ColumnSet("msdynci_lookupfield_customerprofile", Fullname));
            contactName = objcontactEntity.GetAttributeValue<string>(Fullname);

            EntityCollection responseSegmentOrders = new EntityCollection();
            if (objcontactEntity.Attributes.Contains("msdynci_lookupfield_customerprofile"))
            {
                var customerProfileRef = objcontactEntity.GetAttributeValue<EntityReference>("msdynci_lookupfield_customerprofile");
                if (customerProfileRef != null)
                {
                    var query = new QueryExpression("msdynci_top1suggestedorder");
                    query.Criteria.AddCondition("msdynci_customerid", ConditionOperator.Equal, customerProfileRef.Name);
                    query.ColumnSet = new ColumnSet(top1SegmentOrderAttributes.ToArray());
                    responseSegmentOrders = _orgService.RetrieveMultiple(query);
                }
            }
            if (responseSegmentOrders.Entities.Count > 0)
            {
                return responseSegmentOrders;
            }
            else
            {
                throw new ArgumentException($"No segment orders found for the provided contactId: {contactId}, contactName: {contactName}");
            }
        }

        /// <summary>
        /// This method retrieves the label of an option set value for a given entity and attribute.
        /// </summary>
        /// <param name="promptTemplateId"> The prompt template id associated with the failure.</param>
        /// <param name="contactId">The contact id.</param>
        /// <param name="contactName"> The contact name associated with the failure.</param>
        /// <param name="promptTemplateName"> The prompt template name associated with the failure.</param>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="attributeLogicalName">The logical name of the attribute.</param>
        /// <param name="optionSetValue">The integer value of the option set.</param>
        /// <param name="failureStageEnum">Specifies the failure stage for which to retrieve the option set label.</param>
        /// <returns>The label of the option set value.</returns>
        public string GetOptionSetLabel(string promptTemplateId, string contactId, string contactName, string promptTemplateName, string entityLogicalName, string attributeLogicalName, int optionSetValue, FailureStageEnum failureStageEnum)
        {
            var retrieveAttributeRequest = new RetrieveAttributeRequest
            {
                EntityLogicalName = entityLogicalName,
                LogicalName = attributeLogicalName,
                RetrieveAsIfPublished = true
            };

            var retrieveAttributeResponse = (RetrieveAttributeResponse)_orgService.Execute(retrieveAttributeRequest);

            if (retrieveAttributeResponse != null)
            {
                var attributeMetadata = (PicklistAttributeMetadata)retrieveAttributeResponse.AttributeMetadata;

                var option = attributeMetadata.OptionSet.Options.FirstOrDefault(o => o.Value == optionSetValue);

                return option?.Label?.UserLocalizedLabel?.Label ?? string.Empty;
            }
            else
            {
                throw new ArgumentException($"No option set found for entity {entityLogicalName} and attribute {attributeLogicalName}");
            }
        }

        /// <summary>
        /// This method retrieves the prompt template mappings from the Dataverse environment.
        /// </summary>
        /// <param name="promptTemplateId"> The prompt template id associated with the failure.</param>
        /// <param name="contactId">The contact id</param>
        /// <param name="orgService">The organization service instance used to interact with Dataverse.</param>
        /// <param name="failureStageEnum">The failure stage enum</param>
        /// <returns>The collection of prompt template mappings.</returns>
        public EntityCollection GetPromptTemplateMappings(string promptTemplateId, string contactId, string contactName, string promptTemplateName, IOrganizationService orgService, FailureStageEnum failureStageEnum)
        {
            var query_statecode = 0;
            var promptTemplatesAttributeMappingQuery = new QueryExpression("cch_prompttemplateattributemapping")
            {
                ColumnSet = new ColumnSet("cch_attributedatatype", "cch_description", "cch_placeholdername", "cch_traversalpath"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("statecode", ConditionOperator.Equal, query_statecode)
                    }
                }
            };

            var response = orgService.RetrieveMultiple(promptTemplatesAttributeMappingQuery);

            if (response != null && response.Entities.Count > 0)
            {
                return response;
            }
            else
            {
                throw new ArgumentException("No prompt template mappings found in the Dataverse environment.");
            }
        }

        /// <summary>
        /// Creates a record in the OpenAI Text Output table using the provided connection string and API response for error scenario.
        /// </summary>
        /// <param name="failureReason">The failure reason.</param>
        /// <param name="failureStage">The failure stage</param>
        /// <param name="contactId">The contact id</param>
        /// <param name="promptTemplateId"> The prompt template id associated with the failure.</param>
        /// <param name="contactName"> The contact name associated with the failure.</param>
        /// <param name="promptTemplateName"> The prompt template name associated with the failure.</param>
        public void CreateOpenAITextOutputRecordForError(string failureReason, FailureStageEnum failureStage, string contactId, string promptTemplateId, string contactName, string promptTemplateName)
        {
            if (failureReason != null && failureStage != FailureStageEnum.None)
            {
                contactName = string.IsNullOrEmpty(contactName) ? GetContactNameByContactId(contactId) : contactName;
                var connectionString = Environment.GetEnvironmentVariable("DataverseConnection") ?? throw new InvalidOperationException("Dataverse connection string is missing or empty.");
                IOrganizationService service = _serviceClientFactory.Create(connectionString);
                if (service != null)
                {
                    Entity openAITextOutput = new Entity("cch_openaitextoutput");
                    openAITextOutput["cch_openaitextoutputname"] = $"{contactName} - {promptTemplateName}";
                    openAITextOutput[ContactId] = new EntityReference(Contact, new Guid(contactId));
                    openAITextOutput["cch_templateid"] = new EntityReference("cch_prompttemplate", new Guid(promptTemplateId));
                    openAITextOutput["cch_failurereason"] = failureReason;
                    openAITextOutput["cch_failurestage"] = failureStage.ToString();
                    openAITextOutput["statuscode"] = new OptionSetValue(399080001);
                    service.Create(openAITextOutput);
                }
            }
        }

        /// <summary>
        /// Creates a record in the OpenAI Text Output table using the provided connection string and API response.
        /// </summary>
        /// <param name="inputMessage">The input message</param>
        /// <param name="apiResponse">The api response</param>
        /// <param name="complianceScore">The compliance score</param>
        /// <param name="failureReason">The failure details from evaluation API</param>
        /// <param name="service">The service to store data in dataverse</param>
        public void CreateOpenAITextOutputEntityRecord(InputMessage inputMessage, APIResponse apiResponse, decimal complianceScore, string failureReason, IOrganizationService service)
        {
            var contactGuid = string.IsNullOrEmpty(inputMessage.ContactId) ? Guid.Empty : Guid.Parse(inputMessage.ContactId);
            var templateGuid = string.IsNullOrEmpty(inputMessage.PromptTemplateId) ? Guid.Empty : Guid.Parse(inputMessage.PromptTemplateId);

            Entity openAITextOutput = new Entity("cch_openaitextoutput");

            openAITextOutput["cch_openaitextoutputname"] = $"{inputMessage.ContactName} - {inputMessage.PromptTemplateName}";
            openAITextOutput[ContactId] = new EntityReference(Contact, contactGuid);
            openAITextOutput["cch_templateid"] = new EntityReference("cch_prompttemplate", templateGuid);
            openAITextOutput["cch_subjectline"] = apiResponse.SubjectLine;
            openAITextOutput["cch_headline"] = apiResponse.Headline;
            openAITextOutput["cch_introtext"] = apiResponse.IntroText;
            openAITextOutput["cch_ctatext"] = apiResponse.CTAText;
            openAITextOutput["cch_outrotext"] = apiResponse.OutroText;
            openAITextOutput["cch_compliancescore"] = complianceScore;
            openAITextOutput["cch_openairesponse"] = JsonConvert.SerializeObject(apiResponse);

            if (failureReason != null && failureReason != string.Empty)
            {
                openAITextOutput["statuscode"] = new OptionSetValue(399080001);
                openAITextOutput["cch_failurereason"] = failureReason;
                openAITextOutput["cch_failurestage"] = FailureStageEnum.EvaluationAndPublish.ToString();
            }
            else
            {
                openAITextOutput["statuscode"] = new OptionSetValue(1);
            }

            service.Create(openAITextOutput);
        }

        /// <summary>
        /// This method retrieves the contact name by contact ID.
        /// </summary>
        /// <param name="contactId">The contact id for which contact name needs to be fetched</param>
        /// <returns>The contact name for provided contact id</returns>
        /// <exception cref="ArgumentNullException">Thrown when contactId is null.</exception>
        private string GetContactNameByContactId(string contactId)
        {
            if (contactId != null)
            {
                var contact = _orgService.Retrieve(Contact, new Guid(contactId), new ColumnSet(Fullname));
                return contact.GetAttributeValue<string>(Fullname);
            }
            else
            {
                throw new ArgumentNullException(nameof(contactId), "Contact ID cannot be null.");
            }
        }
    }
}
