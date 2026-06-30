using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.PromptGenerationApp.Tests.TestData;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using Xunit;
using CCH.HPSO.Azure.Shared.Enum;
using System.Text.Json.Nodes;

namespace CCH.HPSO.Azure.PromptGenerationApp.Tests
{

    /// <summary>
    /// This class contains unit tests for the PromptGenerationFunction class.
    /// </summary>
    public class PromptGenerationFunctionTest
    {
        /// <summary>
        /// The logger mock used for testing the PromptGenerationFunction class.
        /// </summary>
        private readonly Mock<ILogger<PromptGenerationFunction>> _loggerMock = new();

        /// <summary>
        /// The message builder mock used for testing the PromptGenerationFunction class.
        /// </summary>
        private readonly Mock<IPromptMessageBuilder> _messageBuilderMock = new();

        /// <summary>
        /// The OpenAI service mock used for testing the PromptGenerationFunction class.
        /// </summary>
        private readonly Mock<IOpenAIService> _openAIServiceMock = new();

        /// <summary>
        /// The service client factory mock used for testing the PromptGenerationFunction class.
        /// </summary>
        private readonly Mock<IServiceClientFactory> _serviceClientFactoryMock = new();

        /// <summary>
        /// The service client factory mock used for testing the PromptGenerationFunction class.
        /// </summary>
        private readonly Mock<IEvaluationApiService> _evaluationAPIServiceMock = new();

        /// <summary>
        /// The dataverse service mock used for testing the PromptGenerationFunction class.
        /// </summary>
        private readonly Mock<IDataverseService> _dataverseServiceMock = new();

        /// <summary>
        /// This method creates an instance of the PromptGenerationFunction class with the necessary dependencies mocked.
        /// </summary>
        /// <returns>PromptGenerationFunction</returns>
        private PromptGenerationFunction CreateFunction()
        {
            return new(_loggerMock.Object, _messageBuilderMock.Object, _openAIServiceMock.Object, _serviceClientFactoryMock.Object, _evaluationAPIServiceMock.Object, _dataverseServiceMock.Object);
        }

        /// <summary>
        /// This method tests the RunHttp method of the PromptGenerationFunction class to ensure it returns an OK response for a preview request.
        /// </summary>
        /// <returns>Returns Task</returns>
        [Fact]
        public async Task RunHttp_ReturnsOk_ForPreview()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");

            var function = CreateFunction();
            var inputMessage = new InputMessage
            {
                IsPreview = "true",
                PromptLanguage = "en",
                Tone = "formal",
                PromptText = "sample prompt"
            };
            var requestBody = "{}";
            var aoaiResponse = "test-response";

            _messageBuilderMock
                .Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(inputMessage);

            _messageBuilderMock
                .Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), It.IsAny<FailureStageEnum>()))
                .Returns("formatted-message");

            _openAIServiceMock
                .Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>()))
                .ReturnsAsync(aoaiResponse);

            _evaluationAPIServiceMock
                .Setup(s => s.CallEvaluationApi("sample prompt"))
                .ReturnsAsync("0.85");

            var req = new FakeHttpRequestData(requestBody);

            // Act
            var response = await function.RunHttp(req);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = ((FakeHttpResponseData)response).BodyAsString();

            Assert.Contains("An error occurred while processing the request: Value cannot be null. (Parameter 'json')", body);
        }

        /// <summary>
        /// This method tests the RunHttp method of the PromptGenerationFunction class to ensure it returns an OK response for a non-preview request.
        /// </summary>
        /// <returns>Returns Task</returns>
        [Fact]
        public async Task RunHttp_ReturnsOk_ForNonPreview()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");

            var function = CreateFunction();
            var inputMessage = new InputMessage { IsPreview = "false" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);

            var req = new FakeHttpRequestData("{}");

            // Act
            var response = await function.RunHttp(req);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// This method tests the RunHttp method of the PromptGenerationFunction class to ensure it returns an InternalServerError response when an exception occurs.
        /// </summary>
        /// <returns>Returns task</returns>
        [Fact]
        public async Task RunHttp_ReturnsError_OnException()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Throws(new Exception("test error"));

            var req = new FakeHttpRequestData("{}");

            // Act
            var response = await function.RunHttp(req);

            // Assert
            var body = ((FakeHttpResponseData)response).BodyAsString();
            Assert.Contains("test error", body);

            _dataverseServiceMock.Verify(
                x => x.CreateOpenAITextOutputRecordForError(
                    It.Is<string>(s => s.Contains("test error")),
                    FailureStageEnum.PromptGeneration,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Once);
        }

        /// <summary>
        /// This method tests the RunHttp method of the PromptGenerationFunction class to ensure it logs an error when compliance score parsing fails.
        /// </summary>
        /// <returns>The task</returns>
        [Fact]
        public async Task RunHttp_LogsError_WhenComplianceScoreParseFails()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            var inputMessage = new InputMessage { IsPreview = "true", PromptText = "sample prompt", PromptLanguage = "en", Tone = "formal" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), It.IsAny<FailureStageEnum>())).Returns("formatted-message");
            _openAIServiceMock.Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>())).ReturnsAsync("test-response");
            _evaluationAPIServiceMock.Setup(s => s.CallEvaluationApi("sample prompt")).ReturnsAsync("not-a-decimal");
            var req = new FakeHttpRequestData("{}");

            // Act
            await function.RunHttp(req);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error processing the request")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            _dataverseServiceMock.Verify(
                x => x.CreateOpenAITextOutputRecordForError(
                    It.IsAny<string>(),
                    FailureStageEnum.PromptGeneration,
                    inputMessage.ContactId,
                    inputMessage.PromptTemplateId,
                    inputMessage.ContactName,
                    inputMessage.PromptTemplateName),
                Times.Never);
        }        

        /// <summary>
        /// This method tests the RunHttp method of the PromptGenerationFunction class to ensure it logs an error when the evaluation API call throws an exception.
        /// </summary>
        /// <returns>return task</returns>
        [Fact]
        public async Task RunHttp_LogsError_WhenCallEvaluationAPIThrows()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            var inputMessage = new InputMessage { IsPreview = "true", PromptText = "sample prompt", PromptLanguage = "en", Tone = "formal" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), It.IsAny<FailureStageEnum>())).Returns("formatted-message");
            _openAIServiceMock.Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>())).ReturnsAsync("test-response");
            _evaluationAPIServiceMock.Setup(s => s.CallEvaluationApi("sample prompt")).ThrowsAsync(new Exception("eval error"));
            var req = new FakeHttpRequestData("{}");

            // Act
            var response = await function.RunHttp(req);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error processing the request")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        /// <summary>
        /// This method tests the RunServiceBus method of the PromptGenerationFunction class to ensure it logs an error when ProcessAndSendToServiceBusAsync throws an exception.
        /// </summary>
        [Fact]
        public void RunServiceBus_LogsError_WhenProcessAndSendToServiceBusAsyncThrows()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            var inputMessage = new InputMessage();
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            // Simulate exception in BuildUpdatedMessage (which is called by ProcessAndSendToServiceBusAsync)
            _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), FailureStageEnum.PromptGeneration))
                .Throws(new Exception("process error"));

            // Act
            function.RunServiceBus("{}");

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error processing the Service Bus message:")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        /// <summary>
        /// This method tests the RunServiceBus method of the PromptGenerationFunction class to ensure it processes a valid message without throwing an exception.
        /// </summary>
        [Fact]
        public void RunServiceBus_ValidMessage_NoException()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            var inputMessage = new InputMessage() { ComplianceThreshold = "0.7", ContactId = "new string", ContactName = "Name", PromptAppVersion = "2025-07-22", IsPreview = "true" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);

            // Act
            function.RunServiceBus("{ ComplianceThreshold = \"0.7\", ContactId = \"new string\", ContactName = \"Name\", PromptAppVersion = \"2025-07-22\", IsPreview = \"true\" }");

            // Assert
            _loggerMock.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((o, t) => true), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Exactly(2));
        }

        /// <summary>
        /// This method tests the RunServiceBus method of the PromptGenerationFunction class to ensure it logs an error when a parse exception occurs.
        /// </summary>
        [Fact]
        public void RunServiceBus_LogsError_OnParseException()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Throws(new Exception("parse error"));

            // Act
            function.RunServiceBus("{}");

            // Assert
            _loggerMock.Verify(x => x.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((o, t) => true), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);

            _dataverseServiceMock.Verify(
                x => x.CreateOpenAITextOutputRecordForError(
                    It.Is<string>(s => s.Contains("parse error")),
                    FailureStageEnum.PromptGeneration,
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()),
                Times.Once);
        }

        /// <summary>
        /// This method tests the ProcessPreviewAsync method of the PromptGenerationFunction class to ensure it returns a valid AOAI response.
        /// </summary>
        /// <returns>returns task</returns>
        [Fact]
        public async Task ProcessPreviewAsync_ReturnsAOAIResponse()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");

            var function = CreateFunction();
            var inputMessage = new InputMessage { PromptLanguage = "en", Tone = "friendly" };
            _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(inputMessage, It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), FailureStageEnum.PromptGeneration))
                .Returns("msg");
            _openAIServiceMock.Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>())).ReturnsAsync("AOAI-RESULT");

            // Act
            var method = typeof(PromptGenerationFunction).GetMethod("ProcessPreviewAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = (Task<string>)method.Invoke(function, new object[] { inputMessage });

            // Assert
            Assert.Equal("AOAI-RESULT", await result);
        }

        /// <summary>
        /// This method tests the ProcessPreviewAsync method of the PromptGenerationFunction class to ensure it throws an exception and logs an error when an error occurs during message building.
        /// </summary>
        /// <returns>Returns task</returns>
        [Fact]
        public async Task ProcessPreviewAsync_Throws_AndLogs()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            var inputMessage = new InputMessage();
            _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), It.IsAny<FailureStageEnum>()))
                .Throws(new Exception("build error"));

            // Act
            var method = typeof(PromptGenerationFunction).GetMethod("ProcessPreviewAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var ex = await Assert.ThrowsAsync<Exception>(() => (Task<string>)method.Invoke(function, new object[] { inputMessage }));

            // Assert
            Assert.Equal("build error", ex.Message);
        }

        /// <summary>
        /// This method tests the RunHttp method to ensure it returns InternalServerError when BuildUpdatedMessage throws.
        /// </summary>
        [Fact]
        public async Task RunHttp_ReturnsError_WhenBuildUpdatedMessageThrows()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            var inputMessage = new InputMessage { IsPreview = "true", PromptText = "sample prompt" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), It.IsAny<FailureStageEnum>()))
                .Throws(new Exception("build error"));
            var req = new FakeHttpRequestData("{}");

            // Act
            var response = await function.RunHttp(req);

            // Assert
            var body = ((FakeHttpResponseData)response).BodyAsString();
            Assert.Contains("build error", body);
            _loggerMock.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("build error")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Exactly(2)); 
        }

        /// <summary>
        /// This method tests the RunHttp method to ensure it returns InternalServerError when CallAzureOpenAIAsync returns null.
        /// </summary>
        [Fact]
        public async Task RunHttp_ReturnsError_WhenOpenAIResponseIsNull()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            var inputMessage = new InputMessage { IsPreview = "true", PromptText = "sample prompt" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), It.IsAny<FailureStageEnum>()))
                .Returns("formatted-message");
            _openAIServiceMock.Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>()))
                .ReturnsAsync((string)null);
            var req = new FakeHttpRequestData("{}");

            // Act
            var response = await function.RunHttp(req);

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// This method tests the RunServiceBus method to ensure it does not throw when BuildUpdatedMessage returns null.
        /// </summary>
        [Fact]
        public void RunServiceBus_DoesNotThrow_WhenBuildUpdatedMessageReturnsNull()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            var function = CreateFunction();
            var inputMessage = new InputMessage();
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), FailureStageEnum.PromptGeneration))
                .Returns((string)null);

            // Act
            function.RunServiceBus("{}");

            // Assert
            _loggerMock.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error processing the Service Bus message:")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }

        /// <summary>
        /// This method tests the RunServiceBus method to ensure it logs error when OpenAIService throws.
        /// </summary>
        [Fact]
        public void RunServiceBus_LogsError_WhenOpenAIServiceThrows()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
            Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
            Environment.SetEnvironmentVariable("EvaluationAPIUrl", "https://fake-evaluation-api.com");
            Environment.SetEnvironmentVariable("ServiceBusConnection", "Endpoint=sb://test/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=key");
            Environment.SetEnvironmentVariable("OutputTopicName", "output-topic");
            var function = CreateFunction();
            var inputMessage = new InputMessage();
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _openAIServiceMock.Setup(s => s.CallAzureOpenAIAsync(It.IsAny<InputMessage>()))
                .ThrowsAsync(new Exception("openai error"));

            // Act
            function.RunServiceBus("{}");

            // Assert
            _loggerMock.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error processing the Service Bus message:")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
        }
    }
}