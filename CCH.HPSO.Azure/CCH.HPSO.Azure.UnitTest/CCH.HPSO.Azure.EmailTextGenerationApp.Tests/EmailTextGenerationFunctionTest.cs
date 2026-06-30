using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace CCH.HPSO.Azure.EmailTextGenerationApp.Tests
{
    /// <summary>
    /// Tests for the EmailTextGenerationFunction class.
    /// </summary>
    public class EmailTextGenerationFunctionTest
    {
        /// <summary>
        /// The logger mock used to verify logging behavior.
        /// </summary>
        private readonly Mock<ILogger<EmailTextGenerationFunction>> _loggerMock = new();

        /// <summary>
        /// The message builder mock used to parse input messages.
        /// </summary>
        private readonly Mock<IPromptMessageBuilder> _messageBuilderMock = new();

        /// <summary>
        /// The OpenAI service mock used to simulate calls to Azure OpenAI.
        /// </summary>
        private readonly Mock<IOpenAIService> _openAIServiceMock = new();
        
        /// <summary>
        /// The OpenAI service mock used to simulate calls to Azure OpenAI.
        /// </summary>
        private readonly Mock<IDataverseService> _dataverseService = new();

        /// <summary>
        /// The Service Bus client mock used to simulate Service Bus operations.
        /// </summary>
        /// <returns>The EmailTextGenerationFunction instance</returns>
        private EmailTextGenerationFunction CreateFunction()
        {
            return new EmailTextGenerationFunction(_loggerMock.Object, _messageBuilderMock.Object, _openAIServiceMock.Object, _dataverseService.Object);
        }

        /// <summary>
        /// Tests that the function processes a valid message, calls Azure OpenAI, and publishes the result to Service Bus.
        /// </summary>
        /// <returns>Task</returns>
        [Fact]
        public async Task Run_ValidMessage_CallsOpenAIAndPublishesToServiceBus()
        {
            // Arrange
            var inputMessage = new InputMessage
            {
                PromptText = "prompt",
                PromptLanguage = "en",
                Tone = "friendly",
                ComplianceThreshold = "0.5",
                ContactName = "Test Name",
                ContactId = "123",
                IsPreview = "false",
                PromptTemplateId = "templateId",
                PromptTemplateName = "templateName"
            };

            var aoaiResponse = "openai-response";
            var messageJson = JsonSerializer.Serialize(inputMessage);

            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _openAIServiceMock.Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>())).ReturnsAsync(aoaiResponse);

            // Set environment variables for ServiceBus
            Environment.SetEnvironmentVariable("ServiceBusConnection", "Endpoint=sb://test/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=key");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");

            // Use a mock for ServiceBusClient to avoid real Service Bus calls
            var function = CreateFunction();

            // Act
            await function.Run(messageJson);

            // Assert
            _messageBuilderMock.Verify(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
            _openAIServiceMock.Verify(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>()), Times.Once);
            // You can also verify that logger did not log errors, or use a ServiceBusClient mock for more advanced checks
        }

        /// <summary>
        /// Tests that the function logs an error when sending a message to Service Bus fails.
        /// </summary>
        /// <returns>Task</returns>
        [Fact]
        public async Task Run_WhenSendMessageToServiceBusAsyncThrows_LogsError()
        {
            // Arrange
            var inputMessage = new InputMessage
            {
                PromptText = "prompt",
                PromptLanguage = "en",
                Tone = "friendly",
                ComplianceThreshold = "0.5",
                ContactName = "Test Name",
                ContactId = "123",
                IsPreview = "false",
                PromptTemplateId = "templateId",
                PromptTemplateName = "templateName"
            };

            var aoaiResponse = "openai-response";
            var messageJson = JsonSerializer.Serialize(inputMessage);

            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _openAIServiceMock.Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>())).ReturnsAsync(aoaiResponse);

            // Set environment variables for ServiceBus
            Environment.SetEnvironmentVariable("ServiceBusConnection", "bad-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");

            var function = CreateFunction();

            // Act
            await function.Run(messageJson);

            // Wait for the background fire-and-forget task to complete
            await Task.Delay(500); // Adjust delay as needed for your environment

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString().Contains("Failed to send message to Service Bus topic")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }

        /// <summary>
        /// Tests that the function logs an error when the message parsing fails.
        /// </summary>
        /// <returns>task</returns>
        [Fact]
        public async Task Run_LogsError_WhenParseMessageThrows()
        {
            // Arrange
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "fake-Output-Topic-Name");
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Throws(new Exception("parse error"));
            var function = CreateFunction();

            // Act
            await function.Run("bad-message");

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("An error occurred while processing the message: bad-message")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                    Times.AtLeastOnce);
        }

        /// <summary>
        /// Tests that the function logs an error when the CallAzureOpenAIAsync method throws an exception.
        /// </summary>
        /// <returns>task</returns>
        [Fact]
        public async Task Run_LogsError_WhenCallAzureOpenAIAsyncThrows()
        {
            // Arrange
            var inputMessage = new InputMessage { PromptText = "prompt", PromptLanguage = "en", Tone = "friendly" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _openAIServiceMock.Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>()))
                .ThrowsAsync(new Exception("openai error"));
            var function = CreateFunction();

            // Act
            await function.Run("test-message");

            // Assert
            _loggerMock.Verify(x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("An error occurred while processing the message: test-message")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.AtLeastOnce);
        }

        /// <summary>
        /// Tests that the function handles null or empty OpenAI responses gracefully.
        /// </summary>
        /// <returns>returns task</returns>
        [Fact]
        public async Task Run_Handles_NullOrEmpty_OpenAIResponse()
        {
            // Arrange
            var inputMessage = new InputMessage { PromptText = "prompt", PromptLanguage = "en", Tone = "friendly" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _openAIServiceMock.Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>()))
                .ReturnsAsync((string)null);
            var function = CreateFunction();

            // Act
            await function.Run("test-message");

            // Assert
            // Ensure no error was logged
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }
    }
}
