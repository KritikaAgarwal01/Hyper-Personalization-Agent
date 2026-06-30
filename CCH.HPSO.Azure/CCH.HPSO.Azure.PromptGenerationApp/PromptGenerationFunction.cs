using Azure.Messaging.ServiceBus;
using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace CCH.HPSO.Azure.PromptGenerationApp;

/// <summary>
/// The PromptGenerationFunction class is an Azure Function that processes input messages to generate prompts using Azure OpenAI.
/// </summary>
/// <param name="logger">The logger instance</param>
/// <param name="messageBuilder">The message builder used to construct the prompt message</param>
/// <param name="dataverseService"> The Dataverse service used to interact with the Dataverse environment</param>
/// <param name="evaluationAPIService">The Evaluation API Service</param>
/// <param name="openAIService">The OpenAI service used to call Azure OpenAI for generating prompts</param>
/// <param name="serviceClientFactory">The service client factory used to create service clients for various operations</param>
public class PromptGenerationFunction(ILogger<PromptGenerationFunction> logger, IPromptMessageBuilder messageBuilder, IOpenAIService openAIService, IServiceClientFactory serviceClientFactory, IEvaluationApiService evaluationAPIService, IDataverseService dataverseService)
{
    #region Properties

    /// <summary>
    /// The connection string to connect to the Dataverse environment.
    /// </summary>
    private readonly string connectionString = Environment.GetEnvironmentVariable("DataverseConnection") ?? throw new InvalidOperationException("Dataverse connection string is missing or empty.");

    /// <summary>
    /// The logger instance used for logging information and errors.
    /// </summary>
    private readonly ILogger<PromptGenerationFunction> _logger = logger;

    /// <summary>
    /// The output binding for the Service Bus topic where the updated message will be published.
    /// </summary>
    private readonly string _serviceBusConnectionString = Environment.GetEnvironmentVariable("ServiceBusConnection") ?? throw new InvalidOperationException("Service Bus connection string is missing or empty.");

    /// <summary>
    /// The output binding for the Service Bus topic where the updated message will be published.
    /// </summary>
    private readonly string _outputTopicName = Environment.GetEnvironmentVariable("OutputTopicName") ?? throw new InvalidOperationException("Output topic name is missing or empty.");

    /// <summary>
    /// The output binding for the Service Bus topic where the updated message will be published.
    /// </summary>
    private readonly string _inputTopicName = Environment.GetEnvironmentVariable("InputTopicName") ?? throw new InvalidOperationException("Input topic name is missing or empty.");

    /// <summary>
    /// The OpenAI service used to call Azure OpenAI for generating prompts.
    /// </summary>
    private readonly IOpenAIService _openAIService = openAIService;

    /// <summary>
    /// The OpenAI service used to call Azure OpenAI for generating prompts.
    /// </summary>
    private readonly IDataverseService _dataverseService = dataverseService;

    /// <summary>
    /// The service client factory used to create service clients for various operations.
    /// </summary>
    private readonly IServiceClientFactory _serviceClientFactory = serviceClientFactory;

    /// <summary>
    /// The Evaluation API service used to call the evaluation API for compliance scoring.
    /// </summary>
    private readonly IEvaluationApiService _evaluationAPIService = evaluationAPIService;

    #endregion

    /// <summary>
    /// This method is triggered by an HTTP request and processes the input message to generate a prompt using Azure OpenAI.
    /// </summary>
    /// <param name="req">The request data</param>
    /// <returns>Returns the generated response from AOAI</returns>
    [Function("PromptGenerationFunction_Http")]
    public async Task<HttpResponseData> RunHttp(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        bool isPreview = false;
        InputMessage inputMessage = new InputMessage();
        try
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            inputMessage = messageBuilder.ParseMessage(requestBody, nameof(RunHttp));

            string AOAIResponse = string.Empty;
            isPreview = string.Equals(inputMessage.IsPreview, "true", StringComparison.OrdinalIgnoreCase);
            decimal complianceScore = 0; // Default value
            var failureReasonString = string.Empty;

            if (isPreview)
            {
                AOAIResponse = await ProcessPreviewAsync(inputMessage);

                // Call evaluation API and extract compliance score
                string result = await _evaluationAPIService.CallEvaluationApi(AOAIResponse);
                                
                using var doc = JsonDocument.Parse(result);
                if (doc.RootElement.TryGetProperty("details", out var details))
                {
                    if (details.TryGetProperty("avg_compliance_score", out var scoreElement) && scoreElement.TryGetDecimal(out var extractedScore))
                        complianceScore = extractedScore;

                    if (complianceScore < Convert.ToDecimal(inputMessage.ComplianceThreshold))
                    {
                        details.TryGetProperty("detailed_results", out var failureReason);
                        failureReasonString = failureReason.ToString();
                    }
                }
            }
            else
            {
                // Publish the message to Service Bus for further processing (fire and forget)
                _ = SendMessageToServiceBusAsync(requestBody, _inputTopicName);
            }

            var response = req.CreateResponse();
            var resultResponse = new
            {
                reasonCode = response.StatusCode.ToString(),
                testOutput = AOAIResponse,
                testComplianceScore = complianceScore.ToString(),
                testOpenAIResponse = failureReasonString
            };

            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(resultResponse), Encoding.UTF8);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error processing the request: {Message}", ex.Message);
            _dataverseService.CreateOpenAITextOutputRecordForError(ex.Message, isPreview ? FailureStageEnum.None : FailureStageEnum.PromptGeneration, inputMessage.ContactId, inputMessage.PromptTemplateId, inputMessage.ContactName, inputMessage.PromptTemplateName);
            var errorResponse = req.CreateResponse();
            await errorResponse.WriteStringAsync("An error occurred while processing the request: " + ex.Message, Encoding.UTF8);
            return errorResponse;
        }
    }

    /// <summary>
    /// This method is triggered by a Service Bus message and processes the input message to generate a prompt and send it to another Service Bus topic.
    /// </summary>
    /// <param name="message">The service bus message</param>
    [Function("PromptGenerationFunction_ServiceBus")]
    public void RunServiceBus([ServiceBusTrigger("%InputTopicName%", "%ServiceBusSubscription%", Connection = "ServiceBusConnection")] string message)
    {
        InputMessage inputMessage = new InputMessage();
        try
        {
            inputMessage = messageBuilder.ParseMessage(message, nameof(RunServiceBus));

            _ = ProcessAndSendToServiceBusAsync(inputMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error processing the Service Bus message: {Message}", ex.Message);
            _dataverseService.CreateOpenAITextOutputRecordForError(ex.Message, FailureStageEnum.PromptGeneration, inputMessage.ContactId, inputMessage.PromptTemplateId, inputMessage.ContactName, inputMessage.PromptTemplateName);
        }
    }

    /// <summary>
    /// This method processes the input message to generate a prompt using Azure OpenAI for preview purposes.
    /// </summary>
    /// <param name="inputMessage">The input message</param>
    /// <returns>The AOAI response</returns>
    private async Task<string> ProcessPreviewAsync(InputMessage inputMessage)
    {
        try
        {
            var updatedMySbMsg = messageBuilder.BuildUpdatedMessage(inputMessage, connectionString, _serviceClientFactory);

            return await _openAIService.CallAzureOpenAIAsync(messageBuilder.ParseMessage(updatedMySbMsg, nameof(ProcessPreviewAsync)));
        }
        catch (Exception ex)
        {
            _logger.LogError("Error processing preview: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// This method processes the input message to generate a prompt and sends the updated message to the Service Bus topic.
    /// </summary>
    /// <param name="inputMessage">The input message</param>
    /// <returns>The response task</returns>
    private async Task ProcessAndSendToServiceBusAsync(InputMessage inputMessage)
    {
        try
        {
            var updatedMySbMsg = messageBuilder.BuildUpdatedMessage(inputMessage, connectionString, _serviceClientFactory, FailureStageEnum.PromptGeneration);
            await SendMessageToServiceBusAsync(updatedMySbMsg, _outputTopicName);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error processing the Service Bus message: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// This method sends the updated message to the Service Bus topic.
    /// </summary>
    /// <param name="message">The updated message</param>
    /// <param name="topicName">The topic name</param>
    /// <param name="contactId"> The contact ID</param>
    /// <param name="promptTemplateId"> The prompt template ID</param> 
    /// <returns>Task to be resolved on operation complete</returns>
    private async Task SendMessageToServiceBusAsync(string message, string topicName)
    {
        try
        {
            await using var client = new ServiceBusClient(_serviceBusConnectionString);
            ServiceBusSender sender = client.CreateSender(topicName);
            ServiceBusMessage sbMessage = new ServiceBusMessage(message);
            await sender.SendMessageAsync(sbMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to send message to Service Bus topic: {Topic}, exception message: {Message}", topicName, ex.Message);
            throw;
        }
    }
}