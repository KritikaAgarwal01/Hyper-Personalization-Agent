using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using Microsoft.Xrm.Sdk;

namespace CCH.HPSO.Azure.Shared.Contracts
{
    public interface IDataverseService
    {
        /// <summary>
        /// The method retrieves the account placeholder associated with a contact.
        /// </summary>
        /// <param name="contactId">The contactId of customer</param>
        /// <param name="accountAttributes">List of account attribute names to retrieve.</param>
        /// <param name="failureStageEnum">Specifies the failure stage for which to retrieve account placeholders.</param>
        /// <returns>The collection of account placeholders.</returns>
        EntityCollection RetrieveAccountPlaceholder(string promptTemplateId, string contactId, string contactName, string promptTemplateName, List<string> accountAttributes, FailureStageEnum failureStageEnum);

        /// <summary>
        /// The method retrieves the top 1 segment orders placeholders associated with a contact.
        /// </summary>
        /// <param name="contactId">The contact id</param>
        /// <param name="top1SegmentOrderAttributes">List of top 1 segment order attribute names to retrieve.</param>
        /// <param name="contactName">The contact name</param>
        /// <returns>The collection of top 1 segment orders placeholders.</returns>
        /// <param name="failureStageEnum">Specifies the failure stage for which to retrieve top 1 segment orders placeholders.</param>
        /// <exception cref="ArgumentException"></exception>
        EntityCollection RetrieveTop1SegmentOrdersPlaceholders(string promptTemplateId, string contactId, string promptTemplateName, List<string> top1SegmentOrderAttributes, FailureStageEnum failureStageEnum, ref string contactName);

        /// <summary>
        /// This method retrieves the label of an option set value for a given entity and attribute.
        /// </summary>
        /// <param name="service">The organization service instance used to retrieve data.</param>
        /// <param name="entityLogicalName">The logical name of the entity.</param>
        /// <param name="attributeLogicalName">The logical name of the attribute.</param>
        /// <param name="optionSetValue">The integer value of the option set.</param>
        /// <param name="failureStageEnum">Specifies the failure stage for which to retrieve the option set label.</param>
        /// <returns>The label of the option set value.</returns>
        string GetOptionSetLabel(string promptTemplateId, string contactId, string contactName, string promptTemplateName, string entityLogicalName, string attributeLogicalName, int optionSetValue, FailureStageEnum failureStageEnum);

        /// <summary>
        /// This method retrieves the prompt template mappings from the Dataverse environment.
        /// </summary>
        /// <param name="orgService">The organization service instance used to interact with Dataverse.</param>
        /// <param name="failureStageEnum">Specifies the failure stage for which to retrieve prompt template mappings.</param>
        /// <returns>The collection of prompt template mappings.</returns>
        EntityCollection GetPromptTemplateMappings(string promptTemplateId, string contactId, string contactName, string promptTemplateName, IOrganizationService orgService, FailureStageEnum failureStageEnum);

        /// <summary>
        /// Creates a record in the OpenAI Text Output table using the provided connection string and API response.
        /// </summary>
        /// <param name="failureReason">The failure reason.</param>
        /// <param name="failureStage">The failure stage</param>
        /// <param name="contactId"> The contact ID associated with the failure.</param>
        /// <param name="promptTemplateId"> The prompt template ID associated with the failure.</param>
        void CreateOpenAITextOutputRecordForError(string failureReason, FailureStageEnum failureStage, string contactId, string promptTemplateId, string contactName, string promptTemplateName);

        /// <summary>
        /// Creates a record in the OpenAI Text Output table using the provided connection string and API response.
        /// </summary>
        /// <param name="inputMessage">The input message</param>
        /// <param name="apiResponse">The api response</param>
        /// <param name="complianceScore">The compliance score</param>
        /// <param name="failureReason">The failure details from evaluation API</param>
        /// <param name="service">The service to store data in dataverse</param>
        void CreateOpenAITextOutputEntityRecord(InputMessage inputMessage, APIResponse apiResponse, decimal complianceScore, string failureReason, IOrganizationService service);
    }
}
