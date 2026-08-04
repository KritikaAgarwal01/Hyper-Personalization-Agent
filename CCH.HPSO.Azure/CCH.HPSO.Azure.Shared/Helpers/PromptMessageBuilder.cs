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

            var placeholders = PlaceholderHelper.ExtractPlaceholders(inputMessage.PromptText);

            // The placeholder values are now resolved by the ms_ResolveTraversalPath Dataverse custom API.
            // The custom API internally looks up the traversal path configured for each placeholder and walks
            // the relationships starting from the contact record, so the previous mapping / categorization /
            // retrieval logic (kept commented out below) is no longer required.
            ResolvePlaceholderValues(placeholders, inputMessage.ContactId, orgService);

            string updatedPromptTextJson = PlaceholderHelper.ReplacePlaceholders(inputMessage.PromptText, placeholders, inputMessage);

            // Fetch the configurable OpenAI system message from the prompt template record so it travels with the
            // message to the OpenAI service (and survives the Service Bus hop). OpenAIService falls back to its
            // built-in default when this value is empty.
            var dataverseService = new DataverseService(orgService, serviceClientFactory);
            string systemMessage = dataverseService.GetSystemMessage(inputMessage.PromptTemplateId, orgService);

            // Reconstruct the message as JSON
            string updatedMySbMsg = JsonSerializer.Serialize(new InputMessage()
            {
                PromptText = updatedPromptTextJson,
                ComplianceThreshold = inputMessage.ComplianceThreshold,
                ContactId = inputMessage.ContactId,
                ContactName = inputMessage.ContactName,
                IsPreview = inputMessage.IsPreview,
                PromptTemplateId = inputMessage.PromptTemplateId,
                PromptTemplateName = inputMessage.PromptTemplateName,
                PromptLanguage = inputMessage.PromptLanguage,
                PromptAppVersion = inputMessage.PromptAppVersion,
                PromptDeploymentName = inputMessage.PromptDeploymentName,
                Tone = inputMessage.Tone,
                SystemMessage = systemMessage
            });

            return updatedMySbMsg;

            // -------------------------------------------------------------------------------------------------
            // PREVIOUS IMPLEMENTATION - kept for reference, superseded by the ms_ResolveTraversalPath custom API.
            // -------------------------------------------------------------------------------------------------
            /*
            var dataverseService = new DataverseService(orgService, serviceClientFactory);

            if (true)
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
            */
        }

        /// <summary>
        /// Resolves the actual value for every placeholder by calling the ms_ResolveTraversalPath
        /// Dataverse custom API. The custom API resolves the traversal path configured for each
        /// placeholder starting from the supplied contact record and returns the leaf value(s).
        /// </summary>
        /// <param name="placeholders">The placeholders extracted from the prompt text.</param>
        /// <param name="contactId">The contact id used as the pivot record for the traversal.</param>
        /// <param name="orgService">The organization service used to execute the custom API.</param>
        private static void ResolvePlaceholderValues(List<PlaceHolderInformation> placeholders, string contactId, IOrganizationService orgService)
        {
            const string pivotEntityName = "contact";

            foreach (var placeholder in placeholders)
            {
                var request = new OrganizationRequest("ms_ResolveTraversalPath")
                {
                    ["PlaceholderName"] = placeholder.Placeholder,
                    ["EntityName"] = pivotEntityName,
                    ["EntityGuid"] = contactId,
                    ["ValueMode"] = "Formatted"
                };

                var response = orgService.Execute(request);

                if (response.Results.TryGetValue("TraversalPath", out var traversalPath) && traversalPath is string traversalPathText)
                {
                    placeholder.TraversalPath = traversalPathText;
                }

                string? resultJson = response.Results.TryGetValue("Result", out var resultValue) ? resultValue as string : null;

                placeholder.ActualValue = ConvertResultJsonToDisplayValue(resultJson);
            }
        }

        /// <summary>
        /// Converts the JSON string returned by the ms_ResolveTraversalPath custom API into a
        /// display string. The custom API returns a JSON scalar for a single value, a JSON array
        /// for multiple values, and the literal "null" when no value is found.
        /// </summary>
        /// <param name="resultJson">The JSON payload returned in the custom API "Result" output.</param>
        /// <returns>The resolved value as a display string.</returns>
        private static string ConvertResultJsonToDisplayValue(string? resultJson)
        {
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                return string.Empty;
            }

            using JsonDocument document = JsonDocument.Parse(resultJson);
            return ConvertJsonElementToString(document.RootElement);
        }

        /// <summary>
        /// Converts a single <see cref="JsonElement"/> to its display string representation.
        /// Arrays are flattened into a comma separated list.
        /// </summary>
        /// <param name="element">The JSON element to convert.</param>
        /// <returns>The display string representation of the element.</returns>
        private static string ConvertJsonElementToString(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString() ?? string.Empty;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return string.Empty;
                case JsonValueKind.Array:
                    return string.Join(", ", element.EnumerateArray().Select(ConvertJsonElementToString));
                default:
                    return element.GetRawText();
            }
        }

        /* ================================================================================================
         * The following helper methods implemented the previous placeholder resolution approach and are
         * no longer used now that resolution is delegated to the ms_ResolveTraversalPath custom API.
         * They are retained (commented out) for reference per the migration to the custom API.
         * ================================================================================================

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
                var templateMapping = promptTemplatesAttributeMapping.Entities.FirstOrDefault(templateAttribute => templateAttribute.GetAttributeValue<string>("ms_placeholdername")
                            .Equals(placeholder.Placeholder, StringComparison.InvariantCultureIgnoreCase));

                if (templateMapping != null)
                {
                    placeholder.TraversalPath = templateMapping.GetAttributeValue<string>("ms_traversalpath");
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
        * ================================================================================================
        */

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