using Azure.Messaging.ServiceBus;
using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;

namespace CCH.HPSO.Azure.PromptGenerationApp;

/// <summary>
/// Orchestrates prompt generation, text generation, evaluation, and publishing inside one function app.
/// </summary>
/// <param name="logger">The logger instance.</param>
/// <param name="messageBuilder">The message builder used to construct the prompt payload.</param>
/// <param name="openAIService">The OpenAI service used to generate email text.</param>
/// <param name="serviceClientFactory">The service client factory used to create Dataverse clients.</param>
/// <param name="evaluationAPIService">The evaluation API service.</param>
/// <param name="dataverseService">The Dataverse service.</param>
public class PromptGenerationFunction(ILogger<PromptGenerationFunction> logger, IPromptMessageBuilder messageBuilder, IOpenAIService openAIService, IServiceClientFactory serviceClientFactory, IEvaluationApiService evaluationAPIService, IDataverseService dataverseService)
{
    /// <summary>
    /// The connection string to connect to the Dataverse environment.
    /// </summary>
    private readonly string _connectionString = Environment.GetEnvironmentVariable("DataverseConnection") ?? throw new InvalidOperationException("Dataverse connection string is missing or empty.");

    /// <summary>
    /// The logger instance used for logging information and errors.
    /// </summary>
    private readonly ILogger<PromptGenerationFunction> _logger = logger;

    /// <summary>
    /// The connection string to connect to Service Bus.
    /// </summary>
    private readonly string _serviceBusConnectionString = Environment.GetEnvironmentVariable("ServiceBusConnection") ?? throw new InvalidOperationException("Service Bus connection string is missing or empty.");

    /// <summary>
    /// The input topic name for queued processing.
    /// </summary>
    private readonly string _inputTopicName = Environment.GetEnvironmentVariable("InputTopicName") ?? throw new InvalidOperationException("Input topic name is missing or empty.");

    /// <summary>
    /// The OpenAI service used to generate text.
    /// </summary>
    private readonly IOpenAIService _openAIService = openAIService;

    /// <summary>
    /// The Dataverse service used to create output records.
    /// </summary>
    private readonly IDataverseService _dataverseService = dataverseService;

    /// <summary>
    /// The service client factory used to create Dataverse clients.
    /// </summary>
    private readonly IServiceClientFactory _serviceClientFactory = serviceClientFactory;

    /// <summary>
    /// The evaluation API service used to calculate compliance.
    /// </summary>
    private readonly IEvaluationApiService _evaluationAPIService = evaluationAPIService;

    /// <summary>
    /// This method is triggered by an HTTP request and handles preview or queued processing.
    /// </summary>
    /// <param name="req">The request data.</param>
    /// <returns>The preview response or an accepted workflow response.</returns>
    [Function("PromptGenerationFunction_Http")]
    public async Task<HttpResponseData> RunHttp([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        bool isPreview = false;
        InputMessage inputMessage = new();

        try
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            inputMessage = messageBuilder.ParseMessage(requestBody, nameof(RunHttp));
            isPreview = string.Equals(inputMessage.IsPreview, "true", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"inputMessage: {inputMessage}");
            string generatedText = string.Empty;
            decimal complianceScore = 0;
            string failureReason = string.Empty;

            if (isPreview)
            {
                var previewResult = await ProcessPreviewAsync(inputMessage);
                generatedText = previewResult.GeneratedText;
                complianceScore = previewResult.ComplianceScore;
                failureReason = previewResult.FailureReason;
            }
            else
            {
                _ = SendMessageToServiceBusAsync(requestBody, _inputTopicName);
            }

            var response = req.CreateResponse();
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    reasonCode = response.StatusCode.ToString(),
                    testOutput = generatedText,
                    testComplianceScore = complianceScore.ToString(),
                    testOpenAIResponse = failureReason
                }),
                Encoding.UTF8);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing the request.");
            _dataverseService.CreateOpenAITextOutputRecordForError(ex.Message, isPreview ? FailureStageEnum.None : FailureStageEnum.PromptGeneration, inputMessage.ContactId, inputMessage.PromptTemplateId, inputMessage.ContactName, inputMessage.PromptTemplateName);

            var errorResponse = req.CreateResponse();
            await errorResponse.WriteStringAsync($"An error occurred while processing the request: {ex.Message}", Encoding.UTF8);
            return errorResponse;
        }
    }

    /// <summary>
    /// This method is triggered by a Service Bus message and runs the full workflow inside this function app.
    /// </summary>
    /// <param name="message">The service bus message.</param>
    [Function("PromptGenerationFunction_ServiceBus")]
    public async Task RunServiceBus([ServiceBusTrigger("%InputTopicName%", "%ServiceBusSubscription%", Connection = "ServiceBusConnection")] string message)
    {
        InputMessage inputMessage = new();

        try
        {
            inputMessage = messageBuilder.ParseMessage(message, nameof(RunServiceBus));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing the Service Bus message.");
            _dataverseService.CreateOpenAITextOutputRecordForError(ex.Message, FailureStageEnum.PromptGeneration, inputMessage.ContactId, inputMessage.PromptTemplateId, inputMessage.ContactName, inputMessage.PromptTemplateName);
            return;
        }

        try
        {
            await ExecutePipelineAsync(inputMessage);
        }
        catch (PipelineStageException ex)
        {
            _logger.LogError(ex, "Error processing the Service Bus message during {FailureStage}.", ex.FailureStage);
            _dataverseService.CreateOpenAITextOutputRecordForError(ex.InnerException?.Message ?? ex.Message, ex.FailureStage, inputMessage.ContactId, inputMessage.PromptTemplateId, inputMessage.ContactName, inputMessage.PromptTemplateName);
        }
    }

    /// <summary>
    /// Processes preview requests end to end without persisting output.
    /// </summary>
    /// <param name="inputMessage">The preview input message.</param>
    /// <returns>The generated text and evaluation result.</returns>
    private async Task<PreviewResult> ProcessPreviewAsync(InputMessage inputMessage)
    {
        try
        {
            var promptInput = BuildPromptInput(inputMessage, nameof(ProcessPreviewAsync));
            string generatedText = await _openAIService.CallAzureOpenAIAsync(promptInput);

            // Evaluation layer disabled for this use case - the generated text is no longer evaluated via the evaluation API.
            // var evaluationResult = await EvaluateGeneratedTextAsync(inputMessage, generatedText);
            // return new PreviewResult(generatedText, evaluationResult.ComplianceScore, evaluationResult.FailureReason);
            return new PreviewResult(generatedText, 0, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing preview.");
            throw;
        }
    }

    /// <summary>
    /// Executes the queued workflow inside the prompt function app.
    /// </summary>
    /// <param name="inputMessage">The queued workflow message.</param>
    private async Task ExecutePipelineAsync(InputMessage inputMessage)
    {
        var promptInput = BuildPromptInputWithStage(inputMessage, nameof(ExecutePipelineAsync), FailureStageEnum.PromptGeneration);
        string generatedText = await GenerateEmailTextAsync(promptInput);
        promptInput.PromptText = generatedText;

        // Evaluation layer disabled for this use case - the generated text is no longer evaluated via the evaluation API.
        // var evaluationResult = await EvaluateGeneratedTextAsync(promptInput, generatedText, FailureStageEnum.EvaluationAndPublish);
        // CreateOpenAiTextOutputRecord(promptInput, evaluationResult.ComplianceScore, evaluationResult.FailureReason);
        CreateOpenAiTextOutputRecord(promptInput, 0, string.Empty);
    }

    /// <summary>
    /// Builds the prompt payload with placeholders resolved.
    /// </summary>
    /// <param name="inputMessage">The input message.</param>
    /// <param name="operationName">The caller name for parsing context.</param>
    /// <returns>The prompt payload.</returns>
    private InputMessage BuildPromptInput(InputMessage inputMessage, string operationName)
    {
        string updatedMessage = messageBuilder.BuildUpdatedMessage(inputMessage, _connectionString, _serviceClientFactory);
        return messageBuilder.ParseMessage(updatedMessage, operationName);
    }

    /// <summary>
    /// Builds the prompt payload and maps errors to the specified stage.
    /// </summary>
    /// <param name="inputMessage">The input message.</param>
    /// <param name="operationName">The caller name for parsing context.</param>
    /// <param name="failureStage">The failure stage to associate with exceptions.</param>
    /// <returns>The prompt payload.</returns>
    private InputMessage BuildPromptInputWithStage(InputMessage inputMessage, string operationName, FailureStageEnum failureStage)
    {
        try
        {
            string updatedMessage = messageBuilder.BuildUpdatedMessage(inputMessage, _connectionString, _serviceClientFactory, failureStage);
            return messageBuilder.ParseMessage(updatedMessage, operationName);
        }
        catch (Exception ex)
        {
            throw new PipelineStageException(failureStage, "Error building the prompt payload.", ex);
        }
    }

    /// <summary>
    /// Generates email text from the resolved prompt payload.
    /// </summary>
    /// <param name="inputMessage">The resolved prompt payload.</param>
    /// <returns>The generated text.</returns>
    private async Task<string> GenerateEmailTextAsync(InputMessage inputMessage)
    {
        try
        {
            return await _openAIService.CallAzureOpenAIAsync(inputMessage);
        }
        catch (Exception ex)
        {
            throw new PipelineStageException(FailureStageEnum.TextGeneration, "Error generating email text.", ex);
        }
    }

    // Evaluation layer disabled for this use case - the following method is retained (commented out) for reference.
    /*
    /// <summary>
    /// Evaluates generated text and extracts compliance details.
    /// </summary>
    /// <param name="inputMessage">The workflow message.</param>
    /// <param name="generatedText">The generated email text.</param>
    /// <param name="failureStage">The stage to associate with evaluation failures.</param>
    /// <returns>The evaluation result.</returns>
    private async Task<EvaluationResult> EvaluateGeneratedTextAsync(InputMessage inputMessage, string generatedText, FailureStageEnum? failureStage = null)
    {
        try
        {
            string result = await _evaluationAPIService.CallEvaluationApi(generatedText);
            using var doc = JsonDocument.Parse(result);

            if (!doc.RootElement.TryGetProperty("details", out var details))
            {
                throw new InvalidOperationException("details not found in evaluation API response.");
            }

            if (!details.TryGetProperty("avg_compliance_score", out var scoreElement) || !scoreElement.TryGetDecimal(out var complianceScore))
            {
                throw new InvalidOperationException("avg_compliance_score not found in evaluation API response.");
            }

            string failureReason = string.Empty;
            if (complianceScore < Convert.ToDecimal(inputMessage.ComplianceThreshold) && details.TryGetProperty("detailed_results", out var failureReasonElement))
            {
                failureReason = failureReasonElement.ToString();
            }

            return new EvaluationResult(complianceScore, failureReason);
        }
        catch (Exception ex) when (failureStage.HasValue)
        {
            throw new PipelineStageException(failureStage.Value, "Error evaluating generated text.", ex);
        }
    }
    */

    /// <summary>
    /// Persists the generated output and evaluation result to Dataverse.
    /// </summary>
    /// <param name="inputMessage">The workflow message with generated text.</param>
    /// <param name="complianceScore">The compliance score.</param>
    /// <param name="failureReason">The failure reason, if any.</param>
    private void CreateOpenAiTextOutputRecord(InputMessage inputMessage, decimal complianceScore, string failureReason)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inputMessage.PromptText))
            {
                throw new ArgumentNullException(nameof(inputMessage), "Input message prompt text cannot be null or empty.");
            }

            var apiResponse = JsonConvert.DeserializeObject<APIResponse>(inputMessage.PromptText)
                ?? throw new InvalidOperationException("APIResponse cannot be null.");

            IOrganizationService service = _serviceClientFactory.Create(_connectionString)
                ?? throw new InvalidOperationException("Failed to connect to CRM.");

            _dataverseService.CreateOpenAITextOutputEntityRecord(inputMessage, apiResponse, complianceScore, failureReason, service);
        }
        catch (Exception ex)
        {
            throw new PipelineStageException(FailureStageEnum.EvaluationAndPublish, "Error publishing generated text to Dataverse.", ex);
        }
    }

    /// <summary>
    /// Sends a queued request to Service Bus.
    /// </summary>
    /// <param name="message">The message payload.</param>
    /// <param name="topicName">The topic name.</param>
    private async Task SendMessageToServiceBusAsync(string message, string topicName)
    {
        try
        {
            await using var client = new ServiceBusClient(_serviceBusConnectionString);
            ServiceBusSender sender = client.CreateSender(topicName);
            ServiceBusMessage sbMessage = new(message);
            await sender.SendMessageAsync(sbMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to Service Bus topic: {Topic}", topicName);
            throw;
        }
    }

    /// <summary>
    /// Represents a preview response.
    /// </summary>
    /// <param name="GeneratedText">The generated text.</param>
    /// <param name="ComplianceScore">The compliance score.</param>
    /// <param name="FailureReason">The failure reason.</param>
    private sealed record PreviewResult(string GeneratedText, decimal ComplianceScore, string FailureReason);

    /// <summary>
    /// Represents an evaluation result.
    /// </summary>
    /// <param name="ComplianceScore">The compliance score.</param>
    /// <param name="FailureReason">The failure reason.</param>
    private sealed record EvaluationResult(decimal ComplianceScore, string FailureReason);

    /// <summary>
    /// Exception used to associate a workflow failure with a specific stage.
    /// </summary>
    private sealed class PipelineStageException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PipelineStageException"/> class.
        /// </summary>
        /// <param name="failureStage">The failing stage.</param>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The underlying exception.</param>
        public PipelineStageException(FailureStageEnum failureStage, string message, Exception innerException)
            : base(message, innerException)
        {
            FailureStage = failureStage;
        }

        /// <summary>
        /// Gets the failing stage.
        /// </summary>
        public FailureStageEnum FailureStage { get; }
    }
}