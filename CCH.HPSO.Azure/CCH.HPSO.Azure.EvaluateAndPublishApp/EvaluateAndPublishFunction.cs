using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using System.Text.Json;

namespace CCH.HPSO.Azure.EvaluateAndPublishApp
{
    public class EvaluateAndPublishFunction(ILogger<EvaluateAndPublishFunction> logger, IPromptMessageBuilder messageBuilder, IServiceClientFactory serviceClientFactory, IEvaluationApiService evaluationAPIService, IDataverseService dataverseService)
    {
        /// <summary>
        /// The ILogger instance used for logging information and errors.
        /// </summary>
        private readonly ILogger<EvaluateAndPublishFunction> _logger = logger;

        /// <summary>
        /// The message builder used to construct the prompt message.
        /// </summary>
        private readonly IPromptMessageBuilder _messageBuilder = messageBuilder;

        /// <summary>
        /// The service client factory used to create a connection to the Dataverse.
        /// </summary>
        private readonly IServiceClientFactory _serviceClientFactory = serviceClientFactory;

        /// <summary>
        /// The Evaluation API service used to call the evaluation API for compliance scoring.
        /// </summary>
        private readonly IEvaluationApiService _evaluationAPIService = evaluationAPIService;

        /// <summary>
        /// The Dataverse service used to update dataverse table.
        /// </summary>
        private readonly IDataverseService _dataverseService = dataverseService;

        /// <summary>
        /// This method is triggered by a Service Bus message and processes the input message.
        /// </summary>
        /// <param name="message">The message input</param>
        [Function(nameof(EvaluateAndPublishFunction))]
        public async Task RunAsync([ServiceBusTrigger("%InputTopicName%", "%ServiceBusSubscription%", Connection = "ServiceBusConnection")] string message)
        {
            InputMessage inputMessage = new InputMessage();
            decimal complianceScore = 0;
            var failureReasonString = string.Empty;
            try
            {
                inputMessage = _messageBuilder.ParseMessage(message, nameof(RunAsync));

                string result = await _evaluationAPIService.CallEvaluationApi(inputMessage.PromptText != null ? inputMessage.PromptText : string.Empty);

                // Try to extract avg_compliance_score from the JSON response
                using var doc = JsonDocument.Parse(result);
                if (doc.RootElement.TryGetProperty("details", out var details))
                {
                    if (details.TryGetProperty("avg_compliance_score", out var scoreElement) && scoreElement.TryGetDecimal(out var extractedScore))
                    {
                        complianceScore = extractedScore;

                        if (complianceScore < Convert.ToDecimal(inputMessage.ComplianceThreshold) && details.TryGetProperty("detailed_results", out var failureReason))
                        {
                            failureReasonString = failureReason.ToString();
                        }
                    }
                    else
                    {
                        _logger.LogError("avg_compliance_score not found in evaluation API response.");
                        throw new InvalidOperationException("avg_compliance_score not found in evaluation API response");
                    }
                }
                else
                {
                    _logger.LogError("details not found in evaluation API response.");
                    throw new InvalidOperationException("details not found in evaluation API response");
                }

                CreateOpenAITextOutputRecord(inputMessage, complianceScore, failureReasonString ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating OpenAI Text Output record.", ex.Message);
                _dataverseService.CreateOpenAITextOutputRecordForError(ex.Message, FailureStageEnum.EvaluationAndPublish, inputMessage.ContactId, inputMessage.PromptTemplateId, inputMessage.ContactName, inputMessage.PromptTemplateName);
            }
        }

        /// <summary>
        /// Creates a record in the OpenAI Text Output table using the provided connection string and API response.
        /// </summary>
        /// <param name="inputMessage">The API inputMessage.</param>
        /// <param name="complianceScore">The compliance score</param>
        /// <param name="failureReason"> The failure reason if any</param>
        public void CreateOpenAITextOutputRecord(InputMessage inputMessage, decimal complianceScore, string failureReason)
        {
            string connectionString = Environment.GetEnvironmentVariable("DataverseConnection") ?? throw new InvalidOperationException("Dataverse connection string is missing or empty.");
            if (inputMessage?.PromptText == null)
            {
                _logger.LogError("inputMessage is null.");
                throw new ArgumentNullException(nameof(inputMessage), "Input message cannot be null.");
            }

            try
            {
                var apiResponse = MapInputMessageToApiResponse(inputMessage);

                IOrganizationService service = _serviceClientFactory.Create(connectionString);

                if (service != null)
                {
                    _dataverseService.CreateOpenAITextOutputEntityRecord(inputMessage, apiResponse, complianceScore, failureReason, service); 
                    
                    _logger.LogInformation("OpenAI Text Output record created successfully.");
                }
                else
                {
                    _logger.LogError("Failed to connect to CRM.");
                    throw new InvalidOperationException("Failed to connect to CRM.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating OpenAI Text Output record : {message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// This method maps the input message to an APIResponse object.
        /// </summary>
        /// <param name="inputMessage">The input message string</param>
        /// <returns>The parsed API response</returns>
        private APIResponse MapInputMessageToApiResponse(InputMessage inputMessage)
        {
            // Parse PromptText JSON into APIResponse
            try
            {
                if (inputMessage != null && !string.IsNullOrWhiteSpace(inputMessage.PromptText))
                {
                    var apiResponse = JsonConvert.DeserializeObject<APIResponse>(inputMessage.PromptText);
                    if (apiResponse != null)
                    {
                        return apiResponse;
                    }
                    else
                    {
                        _logger.LogError("APIResponse is null.");
                        throw new InvalidOperationException("APIResponse cannot be null.");
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize PromptText to APIResponse.");
                throw;
            }

            return new APIResponse();
        }
    }
}