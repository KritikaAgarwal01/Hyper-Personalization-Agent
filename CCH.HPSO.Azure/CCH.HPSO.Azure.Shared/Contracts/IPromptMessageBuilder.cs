using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using CCH.HPSO.Azure.Shared.Services;
using Microsoft.Xrm.Sdk;

namespace CCH.HPSO.Azure.Shared.Contracts
{
    /// <summary>
    /// The IPromptMessageBuilder interface defines methods for building and processing prompt messages.
    /// </summary>
    public interface IPromptMessageBuilder
    {
        /// <summary>
        /// This method builds an updated message based on the input message and connection string.
        /// </summary>
        /// <param name="inputMessage">The input message</param>
        /// <param name="connectionString">The connection string</param>
        /// <param name="serviceClientFactory">the service client</param>
        /// <param name="failureStageEnum">Failure stage enum to determine the stage of failure</param>
        /// <returns>The updated message with placeholders replaced</returns>
        string BuildUpdatedMessage(InputMessage inputMessage, string connectionString, IServiceClientFactory serviceClientFactory, FailureStageEnum failureStageEnum = FailureStageEnum.None);

        /* ================================================================================================
         * The following members backed the previous placeholder resolution approach and are no longer used
         * now that resolution is delegated to the ms_ResolveTraversalPath Dataverse custom API. They are
         * retained (commented out) for reference.
         * ================================================================================================

        /// <summary>
        /// This method retrieves the placeholders from the prompt template mappings.
        /// </summary>
        /// <param name="placeholders">List of placeholder information.</param>
        /// <param name="promptTemplatesAttributeMapping">The collection of prompt template attribute mappings.</param>
        /// <returns>A tuple containing lists of account attributes and top 1 segment order attributes.</returns>
        (List<string> accountAttributes, List<string> top1SegmentOrderAttributes) MapPlaceholdersToAttributes(
            List<PlaceHolderInformation> placeholders, EntityCollection promptTemplatesAttributeMapping);

        /// <summary>
        /// This method populates the placeholder values based on the provided parameters.
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
        void PopulatePlaceholderValues(string promptTemplateId, string promptTemplateName, IDataverseService dataverseService, List<PlaceHolderInformation> placeholders, string contactId, List<string> accountAttributes, List<string> top1SegmentOrderAttributes, IOrganizationService orgService, FailureStageEnum failureStageEnum, ref string contactName);

        /// <summary>
        /// This method populates the placeholder value from an entity collection.
        /// </summary>
        /// <param name="promptTemplateId">Prompt template id</param>
        /// <param name="contactId"> The contact ID associated with the placeholder.</param>
        /// <param name="dataverseService">the dataverse service instance</param>
        /// <param name="placeHolder">The placeholder</param>
        /// <param name="entityCollection">The entity collection</param>
        /// <param name="entityLogicalName">The logical entity name</param>
        /// <param name="attributeNameToLower">The attribute name in lower case</param>
        /// <param name="orgService">The organization service</param>
        void PopulatePlaceholderValueFromEntity(string promptTemplateId, string contactId, string contactName, string promptTemplateName, IDataverseService dataverseService, PlaceHolderInformation placeHolder, EntityCollection entityCollection, string entityLogicalName, bool attributeNameToLower, IOrganizationService orgService, FailureStageEnum failureStageEnum);

        /// <summary>
        /// This method retrieves the actual value string for a given placeholder.
        /// </summary>
        /// <param name="contactId"> The contact ID associated with the placeholder.</param>
        /// <param name="promptTemplateId"> The prompt template ID associated with the placeholder.</param>
        /// <param name="dataverseService">the dataverse service instance</param>
        /// <param name="actualValue">The actual value to convert.</param>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="attributeLogicalName">The logical name of the attribute.</param>
        /// <param name="orgService">The organization service instance used to retrieve data.</param>
        /// <returns>The actual value as a string.</returns>
        string GetActualValueString(string promptTemplateId, string contactId, string contactName, string promptTemplateName, IDataverseService dataverseService, object actualValue, string entityLogicalName, string attributeLogicalName, IOrganizationService orgService, FailureStageEnum failureStageEnum);
        * ================================================================================================
        */

        /// <summary>
        /// This method parses the incoming message from the Service Bus topic to extract the contact ID, prompt text in JSON format, and compliance threshold.
        /// </summary>
        /// <param name="mySbMsg">The service bus message</param>
        /// <param name="methodName">The calling method name</param>
        /// <returns>A tuple containing the contact ID, prompt text in JSON format, and compliance threshold.</returns>
        InputMessage ParseMessage(string mySbMsg, string methodName);
    }
}