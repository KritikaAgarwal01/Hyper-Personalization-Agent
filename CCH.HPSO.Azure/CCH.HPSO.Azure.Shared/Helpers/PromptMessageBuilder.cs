using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using CCH.HPSO.Azure.Shared.Services;
using Microsoft.Xrm.Sdk;
using System.Globalization;
using System.Text.Json;

namespace CCH.HPSO.Azure.Shared.Helpers
{
    /// <summary>
    /// The IPromptMessageBuilder interface defines methods for building and processing prompt messages.
    /// </summary>
    public class PromptMessageBuilder : IPromptMessageBuilder
    {
        /// <summary>
        /// Cached <see cref="JsonSerializerOptions"/> instance with case-insensitive property name handling,
        /// used to improve performance by reusing the same options for all JSON serialization and deserialization operations.
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// This method builds an updated message by replacing placeholders in the input message with actual values from the Dataverse environment.
        /// </summary>
        /// <param name="inputMessage">The input message</param>
        /// <param name="connectionString">The connection string</param>
        /// <param name="serviceClientFactory">the service client</param>
        /// <param name="failureStageEnum">Failure stage enum to determine the stage of failure</param>
        /// <returns>The updated message with placeholders replaced</returns>
        public string BuildUpdatedMessage(InputMessage inputMessage, string connectionString, IServiceClientFactory serviceClientFactory, FailureStageEnum failureStageEnum = FailureStageEnum.None)
        {
            IOrganizationService orgService = serviceClientFactory.Create(connectionString);

            if (orgService == null)
            {
                return string.Empty;
            }

            var dataverseService = new DataverseService(orgService, serviceClientFactory);

            var placeholders = PlaceholderHelper.ExtractPlaceholders(inputMessage.PromptText);

            if (placeholders.Count > 0)
            {
                string contactName = string.Empty;

                var promptTemplatesAttributeMapping = dataverseService.GetPromptTemplateMappings(inputMessage.PromptTemplateId, inputMessage.ContactId, inputMessage.ContactName, inputMessage.PromptTemplateName, orgService, failureStageEnum);

                var (accountAttributes, top1SegmentOrderAttributes) = MapPlaceholdersToAttributes(placeholders, promptTemplatesAttributeMapping);

                PopulatePlaceholderValues(inputMessage.PromptTemplateId, inputMessage.PromptTemplateName, dataverseService, placeholders, inputMessage.ContactId, accountAttributes, top1SegmentOrderAttributes, orgService, failureStageEnum, ref contactName);

                string updatedPromptTextJson = PlaceholderHelper.ReplacePlaceholders(inputMessage.PromptText, placeholders, inputMessage);

                // Reconstruct the message as JSON
                string updatedMySbMsg = JsonSerializer.Serialize(new InputMessage()
                {
                    PromptText = updatedPromptTextJson,
                    ComplianceThreshold = inputMessage.ComplianceThreshold,
                    ContactId = inputMessage.ContactId,
                    ContactName = contactName,
                    IsPreview = inputMessage.IsPreview,
                    PromptTemplateId = inputMessage.PromptTemplateId,
                    PromptTemplateName = inputMessage.PromptTemplateName,
                    PromptLanguage = inputMessage.PromptLanguage,
                    PromptAppVersion = inputMessage.PromptAppVersion,
                    PromptDeploymentName = inputMessage.PromptDeploymentName
                });

                return updatedMySbMsg;
            }

            return string.Empty;
        }

        /// <summary>
        /// Maps placeholders to their corresponding account and segment order attributes.
        /// </summary>
        /// <param name="placeholders">List of placeholder information.</param>
        /// <param name="promptTemplatesAttributeMapping">The collection of prompt template attribute mappings.</param>
        /// <returns>A tuple containing lists of account attributes and top 1 segment order attributes.</returns>
        public (List<string> accountAttributes, List<string> top1SegmentOrderAttributes) MapPlaceholdersToAttributes(List<PlaceHolderInformation> placeholders, EntityCollection promptTemplatesAttributeMapping)
        {
            // Assign traversal paths to placeholders
            FindAndAssignTraversalPath(placeholders, promptTemplatesAttributeMapping);

            // Map to attribute lists
            return CategorizeAttributes(placeholders);
        }

        /// <summary>
        /// Finds and assigns the traversal path for each placeholder from the template mapping.
        /// </summary>
        private static void FindAndAssignTraversalPath(List<PlaceHolderInformation> placeholders, EntityCollection promptTemplatesAttributeMapping)
        {
            foreach (var placeholder in placeholders)
            {
                var templateMapping = promptTemplatesAttributeMapping.Entities.FirstOrDefault(templateAttribute => templateAttribute.GetAttributeValue<string>("cch_placeholdername")
                            .Equals(placeholder.Placeholder, StringComparison.InvariantCultureIgnoreCase));

                if (templateMapping != null)
                {
                    placeholder.TraversalPath = templateMapping.GetAttributeValue<string>("cch_traversalpath");
                }
            }
        }

        /// <summary>
        /// Categorizes placeholders into account and segment order attributes based on their traversal path.
        /// </summary>
        private static (List<string> accountAttributes, List<string> top1SegmentOrderAttributes) CategorizeAttributes(List<PlaceHolderInformation> placeholders)
        {
            var accountAttributes = new List<string>();
            var top1SegmentOrderAttributes = new List<string>();

            foreach (var traversalPath in placeholders.Select(placeholder => placeholder.TraversalPath))
            {
                if (string.IsNullOrEmpty(traversalPath))
                    continue;

                var splitAttribute = traversalPath.Split('.', traversalPath.LastIndexOf('.'));
                string attributeName = splitAttribute[splitAttribute.Length - 1].ToLowerInvariant();

                if (traversalPath.Contains("contact.contactrole.account", StringComparison.OrdinalIgnoreCase) && !accountAttributes.Contains(attributeName))
                {
                    accountAttributes.Add(attributeName);
                }
                else if (traversalPath.Contains("contact.customerprofile", StringComparison.OrdinalIgnoreCase) && !top1SegmentOrderAttributes.Contains(attributeName))
                {
                    top1SegmentOrderAttributes.Add(attributeName);
                }
            }

            return (accountAttributes, top1SegmentOrderAttributes);
        }

        /// <summary>
        /// This method populates the actual values for the placeholders by retrieving the corresponding data from the Dataverse environment.
        /// </summary>
        /// <param name="promptTemplateId">Prompt template id</param>
        /// <param name="dataverseService">Dataverse service instance used to retrieve data.</param>
        /// <param name="placeholders">List of placeholder information.</param>
        /// <param name="contactId">The contact ID.</param>
        /// <param name="accountAttributes">List of account attribute names.</param>
        /// <param name="top1SegmentOrderAttributes">List of top 1 segment order attribute names.</param>
        /// <param name="orgService">The organization service instance used to retrieve data.</param>
        /// <param name="failureStageEnum">Failure stage enum to determine the stage of failure</param>
        /// <param name="contactName"> Reference to the contact name, which will be updated with the actual contact name.</param>
        public void PopulatePlaceholderValues(string promptTemplateId, string promptTemplateName, IDataverseService dataverseService, List<PlaceHolderInformation> placeholders, string contactId, List<string> accountAttributes, List<string> top1SegmentOrderAttributes, IOrganizationService orgService, FailureStageEnum failureStageEnum, ref string contactName)
        {
            var objAccountEntity = dataverseService.RetrieveAccountPlaceholder(promptTemplateId, contactId, contactName, promptTemplateName, accountAttributes, failureStageEnum);
            var objTop1SegmentOrdersEntity = dataverseService.RetrieveTop1SegmentOrdersPlaceholders(promptTemplateId, contactId, promptTemplateName, top1SegmentOrderAttributes, failureStageEnum, ref contactName);

            foreach (var placeHolder in placeholders)
            {
                if (placeHolder.TraversalPath?.Contains("contact.contactrole.account", StringComparison.OrdinalIgnoreCase) == true)
                {
                    PopulatePlaceholderValueFromEntity(promptTemplateId, contactId, contactName, promptTemplateName, dataverseService, placeHolder, objAccountEntity, "account", false, orgService, failureStageEnum);
                }
                else if (placeHolder.TraversalPath?.Contains("contact.customerprofile", StringComparison.OrdinalIgnoreCase) == true)
                {
                    PopulatePlaceholderValueFromEntity(promptTemplateId, contactId, contactName, promptTemplateName, dataverseService, placeHolder, objTop1SegmentOrdersEntity, "account", true, orgService, failureStageEnum);
                }
            }
        }

        /// <summary>
        /// This method populates the actual value for a placeholder from an entity collection based on the traversal path and attribute name.
        /// </summary>
        /// <param name="promptTemplateId">Prompt template id</param>
        /// <param name="contactId"> The contact ID associated with the placeholder.</param>
        /// <param name="dataverseService">the dataverse service instance</param>
        /// <param name="placeHolder">The placeholder</param>
        /// <param name="entityCollection">The entity collection</param>
        /// <param name="entityLogicalName">The logical entity name</param>
        /// <param name="attributeNameToLower">The attribute name in lower case</param>
        /// <param name="orgService">The organization service</param>
        public void PopulatePlaceholderValueFromEntity(string promptTemplateId, string contactId, string contactName, string promptTemplateName, IDataverseService dataverseService, PlaceHolderInformation placeHolder, EntityCollection entityCollection, string entityLogicalName, bool attributeNameToLower, IOrganizationService orgService, FailureStageEnum failureStageEnum)
        {
            if (entityCollection != null && entityCollection.Entities != null && entityCollection.Entities.Count > 0 && !string.IsNullOrEmpty(placeHolder.TraversalPath))
            {
                var placeHolderValue = placeHolder.TraversalPath.Split('.', placeHolder.TraversalPath.LastIndexOf('.'));
                var attributeLogicalName = placeHolderValue[placeHolderValue.Length - 1];
                if (attributeNameToLower)
                    attributeLogicalName = attributeLogicalName.ToLowerInvariant();

                if (entityCollection.Entities[0].Attributes.TryGetValue(attributeLogicalName, out var actualValue))
                {
                    if (actualValue != null)
                    {
                        placeHolder.ActualValue = GetActualValueString(promptTemplateId, contactId, contactName, promptTemplateName, dataverseService, actualValue, entityLogicalName, attributeLogicalName, orgService, failureStageEnum);
                    }
                    else
                    {
                        throw new InvalidDataException($"Actual value not present for {attributeLogicalName}");
                    }
                }
            }
        }

        /// <summary>
        /// This method retrieves the actual value as a string based on the type of the value.
        /// </summary>
        /// <param name="contactId"> The contact ID associated with the placeholder.</param>
        /// <param name="promptTemplateId"> The prompt template ID associated with the placeholder.</param>
        /// <param name="dataverseService">the dataverse service instance</param>
        /// <param name="actualValue">The actual value to convert.</param>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="attributeLogicalName">The logical name of the attribute.</param>
        /// <param name="orgService">The organization service instance used to retrieve data.</param>
        /// <returns>The actual value as a string.</returns>
        public string GetActualValueString(string promptTemplateId, string contactId, string contactName, string promptTemplateName, IDataverseService dataverseService, object actualValue, string entityLogicalName, string attributeLogicalName, IOrganizationService orgService, FailureStageEnum failureStageEnum)
        {
            if (actualValue == null)
            {
                return string.Empty;
            }
            else
            {
                switch (actualValue)
                {
                    case string s:
                        return s;
                    case EntityReference er:
                        return er.Name;
                    case OptionSetValue osv:
                        return dataverseService.GetOptionSetLabel(promptTemplateId, contactId, contactName, promptTemplateName, entityLogicalName, attributeLogicalName, osv.Value, failureStageEnum);
                    case Money m:
                        return m.Value.ToString("C", CultureInfo.CurrentCulture);
                    case DateTime dt:
                        return dt.ToString("o", CultureInfo.InvariantCulture);
                    default:
                        return actualValue.ToString() ?? string.Empty;

                }
            }
        }

        /// <summary>
        /// This method parses the incoming message from the Service Bus topic to extract the contact ID, prompt text in JSON format, and compliance threshold.
        /// </summary>
        /// <param name="mySbMsg">The service bus message</param>
        /// <param name="methodName">The calling method name</param>
        /// <returns>A tuple containing the contact ID, prompt text in JSON format, and compliance threshold.</returns>
        public InputMessage ParseMessage(string mySbMsg, string methodName)
        {
            if (string.IsNullOrEmpty(mySbMsg))
            {
                throw new ArgumentNullException(nameof(mySbMsg), $"Input message is null or empty at {methodName}");
            }

            InputMessage? parsedInputMessage = JsonSerializer.Deserialize<InputMessage>(mySbMsg, _jsonOptions);

            return parsedInputMessage ?? new InputMessage();
        }
    }
}