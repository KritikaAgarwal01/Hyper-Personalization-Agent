using Azure.Messaging.ServiceBus;
using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CCH.HPSO.Azure.EmailTextGenerationApp;

public class EmailTextGenerationFunction(ILogger<EmailTextGenerationFunction> logger, IPromptMessageBuilder messageBuilder, IOpenAIService openAIService, IDataverseService dataverseService)
{
    /// <summary>
    /// The Logger instance used for logging information and errors.
    /// </summary>
    private readonly ILogger<EmailTextGenerationFunction> _logger = logger;

    /// <summary>
    /// The message builder used to construct the prompt message.
    /// </summary>
    private readonly IPromptMessageBuilder _messageBuilder = messageBuilder;

    /// <summary>
    /// The open AI service used to call Azure OpenAI for generating text.
    /// </summary>
    private readonly IOpenAIService _openAIService = openAIService;

    /// <summary>
    /// The connection string to connect to the Service Bus.
    /// </summary>
    private readonly string _serviceBusConnectionString = Environment.GetEnvironmentVariable("ServiceBusConnection") ?? throw new InvalidOperationException("Service Bus connection string is not set.");

    /// <summary>
    /// The name of the output topic from which messages are received.
    /// </summary>
    private readonly string _outputTopicName = Environment.GetEnvironmentVariable("OutputTopicName") ?? throw new InvalidOperationException("Output topic name is not set.");

    /// <summary>
    /// The OpenAI service used to call Azure OpenAI for generating prompts.
    /// </summary>
    private readonly IDataverseService _dataverseService = dataverseService;

    /// <summary>
    /// The function that processes incoming messages from the Service Bus topic to generate email text using Azure OpenAI.
    /// </summary>
    /// <param name="message">The input from service bus</param>
    [Function(nameof(EmailTextGenerationFunction))]
    public async Task Run(
        [ServiceBusTrigger("%InputTopicName%", "%ServiceBusSubscription%", Connection = "ServiceBusConnection")]
        string message)
    {
        InputMessage inputMessage = new InputMessage();
        try
        {
            inputMessage = _messageBuilder.ParseMessage(message, nameof(Run));

            // Call Azure OpenAI
            string AOAIResponse = await _openAIService.CallAzureOpenAIAsync(inputMessage);

            // Create the output object
            var output = new InputMessage()
            {
                PromptText = AOAIResponse,
                ComplianceThreshold = inputMessage.ComplianceThreshold,
                ContactName = inputMessage.ContactName,
                ContactId = inputMessage.ContactId,
                IsPreview = inputMessage.IsPreview,
                PromptTemplateId = inputMessage.PromptTemplateId,
                PromptTemplateName = inputMessage.PromptTemplateName,
                PromptLanguage = inputMessage.PromptLanguage
            };

            // Serialize to JSON
            string outputJson = JsonSerializer.Serialize(output);

            // Publish response to Service Bus
            // Fire and forget Service Bus send, with error logging
            _ = Task.Run(async () =>
            {
                await SendMessageToServiceBusAsync(outputJson, _outputTopicName);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while processing the message: {Message}", message);
            _dataverseService.CreateOpenAITextOutputRecordForError(ex.Message, FailureStageEnum.TextGeneration, inputMessage.ContactId, inputMessage.PromptTemplateId, inputMessage.ContactName, inputMessage.PromptTemplateName);
        }
    }

    /// <summary>
    /// This method sends the updated message to the Service Bus topic.
    /// </summary>
    /// <param name="message">The updated message</param>
    /// <param name="topicName">The topic name</param>
    /// <returns>Task to be resolved on operation complete</returns>
    private async Task SendMessageToServiceBusAsync(string message, string topicName)
    {
        await using var client = new ServiceBusClient(_serviceBusConnectionString);
        ServiceBusSender sender = client.CreateSender(topicName);
        ServiceBusMessage sbMessage = new ServiceBusMessage(message);
        await sender.SendMessageAsync(sbMessage);
    }
}