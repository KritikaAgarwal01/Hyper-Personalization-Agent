using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using System.Net;
using System.Reflection;
using Xunit;

namespace CCH.HPSO.Azure.EvaluateAndPublishApp.Tests
{
    /// <summary>
    /// Tests for the EvaluateAndPublishFunction class.
    /// </summary>
    public class EvaluateAndPublishFunctionTest
    {
        /// <summary>
        /// The logger mock used to verify logging behavior.
        /// </summary>
        private readonly Mock<ILogger<EvaluateAndPublishFunction>> _loggerMock = new();

        /// <summary>
        /// The message builder mock used to parse input messages.
        /// </summary>
        private readonly Mock<IPromptMessageBuilder> _messageBuilderMock = new();

        /// <summary>
        /// The service client factory mock used to create service clients for various operations.
        /// </summary>
        private readonly Mock<IServiceClientFactory> _serviceClientFactoryMock = new();

        /// <summary>
        /// The evaluation API service mock used to simulate calls to the evaluation API.
        /// </summary>
        private readonly Mock<IEvaluationApiService> _evaluationAPIService = new();

        /// <summary>
        /// The evaluation API service mock used to simulate calls to the evaluation API.
        /// </summary>
        private readonly Mock<IDataverseService> _dataverseService = new();

        /// <summary>
        /// This method creates an instance of the EvaluateAndPublishFunction class with the mocked dependencies.
        /// </summary>
        /// <returns>EvaluateAndPublishFunction</returns>
        private EvaluateAndPublishFunction CreateFunction()
        {
            return new EvaluateAndPublishFunction(_loggerMock.Object, _messageBuilderMock.Object, _serviceClientFactoryMock.Object, _evaluationAPIService.Object, _dataverseService.Object);
        }

        /// <summary>
        /// Tests that the function processes a valid message, calls the evaluation API, and creates an OpenAI Text Output record.
        /// </summary>
        /// <returns>Task</returns>
        [Fact]
        public async Task RunAsync_InvalidComplianceScore_LogsError()
        {
            // Arrange
            var inputMessage = new InputMessage { PromptText = "{}", ComplianceThreshold = "0.5" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);

            // Mock HTTP call inside CallEvaluationAPI to return "not-a-decimal"
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("not-a-decimal")
                });

            var httpClient = new HttpClient(handlerMock.Object);

            // Set the required environment variable
            Environment.SetEnvironmentVariable("EvaluationAPIEndpoint", "http://localhost/api/eval");

            var function = new EvaluateAndPublishFunction(
                _loggerMock.Object,
                _messageBuilderMock.Object,
                _serviceClientFactoryMock.Object,
                _evaluationAPIService.Object,
                _dataverseService.Object
            );

            // Simulate the evaluation API throwing due to invalid JSON
            _evaluationAPIService
                .Setup(s => s.CallEvaluationApi(It.IsAny<string>()))
                .ReturnsAsync("not-a-decimal");

            // Act
            await function.RunAsync("test-message");

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error creating OpenAI Text Output record.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        /// <summary>
        /// Tests that the function logs an error when the message parsing fails.
        /// </summary>
        /// <returns>Task</returns>
        [Fact]
        public async Task RunAsync_LogsError_WhenParseMessageThrows()
        {
            // Arrange
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Throws(new Exception("parse error"));
            var function = new EvaluateAndPublishFunction(
                _loggerMock.Object,
                _messageBuilderMock.Object,
                _serviceClientFactoryMock.Object,
                _evaluationAPIService.Object,
                _dataverseService.Object
            );

            // Act
            await function.RunAsync("bad-message");

            // Assert
            _loggerMock.Verify(
                    x => x.Log(
                        LogLevel.Error,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error creating OpenAI Text Output record.")),
                        It.IsAny<Exception>(),
                        It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                    Times.Once);
        }

        /// <summary>
        /// Tests that the function logs an error when the evaluation API call fails.
        /// </summary>
        /// <returns>Task</returns>
        [Fact]
        public async Task RunAsync_LogsError_WhenCallEvaluationAPIThrows()
        {
            // Arrange
            var inputMessage = new InputMessage { PromptText = "{}", ComplianceThreshold = "0.5" };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _evaluationAPIService.Setup(s => s.CallEvaluationApi(It.IsAny<string>())).ThrowsAsync(new Exception("eval error"));
            var function = new EvaluateAndPublishFunction(
                _loggerMock.Object,
                _messageBuilderMock.Object,
                _serviceClientFactoryMock.Object,
                _evaluationAPIService.Object,
                _dataverseService.Object
            );

            // Act
            await function.RunAsync("test-message");

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error creating OpenAI Text Output record.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        /// <summary>
        /// Tests that the function logs an error when creating the OpenAI Text Output record fails.
        /// </summary>
        /// <returns>Task</returns>
        [Fact]
        public async Task RunAsync_WhenCreateOpenAITextOutputRecordThrows_LogsError()
        {
            // Arrange
            var apiResponse = new APIResponse
            {
                SubjectLine = "Subject",
                Headline = "Headline",
                IntroText = "Intro",
                CTAText = "CTA",
                OutroText = "Outro"
            };

            var inputMessage = new InputMessage
            {
                PromptText = JsonConvert.SerializeObject(apiResponse),
                ComplianceThreshold = "0.5",
                ContactId = Guid.NewGuid().ToString(),
                PromptTemplateId = Guid.NewGuid().ToString(),
                ContactName = "Test Contact",
                PromptTemplateName = "Test Template"
            };

            var jsonString = @"{
            ""details"": {
                    ""input_text"": ""Get ready to sip something extraordinary. Introducing Coca cola, the beverage that’s changing the game with bold flavor, natural ingredients, and a refreshingly modern twist. Here’s why you’ll love it: Made with real fruit extracts, Zero added sugar – 100% guilt-free, Lightly carbonated for that perfect fizz, Eco-friendly packaging. Whether you’re hitting the gym, relaxing by the pool, or powering through your workday – Coca cola is the drink that keeps up with your lifestyle."",
                    ""llm_completion_model"": ""cch-gpt-4o"",
                    ""avg_compliance_score"": 0.22,
                    ""compliance_rule_set_score"": {
                        ""generic_guardrails"": 1.0,
                        ""marketing_quality_rules"": 0.0,
                        ""marketing_compliance_rules"": 0.25
                    },
                    ""detailed_results"": {
                        ""generic_guardrails"": [
                            {
                                ""rule"": ""Do not include sensitive data. Text avoids personal data or confidential info."",
                                ""score"": ""pass""
                            }
                        ],
                        ""marketing_quality_rules"": [
                            {
                                ""rule"": ""Goal defined. Text clearly states the task (e.g. generate a marketing email that promotes a suggested product to an existing customer)."",
                                ""score"": ""fail""
                            },
                            {
                                ""rule"": ""Subject line, email header, body text, call to action, outro text are explicitly required."",
                                ""score"": ""fail""
                            },
                            {
                                ""rule"": ""Tone is clearly described (e.g. informal business)."",
                                ""score"": ""fail""
                            },
                            {
                                ""rule"": ""Text limits output to the 5 required components only"",
                                ""score"": ""fail""
                            }
                        ],
                        ""marketing_compliance_rules"": [
                            {
                                ""rule"": ""Includes growth stats, product relevance, or consumer trends."",
                                ""score"": ""fail""
                            },
                            {
                                ""rule"": ""Includes at least 2 motivators (e.g. health, taste, social status)."",
                                ""score"": ""pass""
                            },
                            {
                                ""rule"": ""Uses outlet type or names the outlet type directly."",
                                ""score"": ""fail""
                            },
                            {
                                ""rule"": ""Uses location or names the business location."",
                                ""score"": ""fail""
                            }
                        ]
                    }
                }
            }";

            _messageBuilderMock
                .Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(inputMessage);

            _evaluationAPIService
                .Setup(s => s.CallEvaluationApi(It.IsAny<string>()))
                .ReturnsAsync(jsonString);

            _serviceClientFactoryMock.Setup(f => f.Create(It.IsAny<string>()));

            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection");

            var function = new EvaluateAndPublishFunction(
                _loggerMock.Object,
                _messageBuilderMock.Object,
                _serviceClientFactoryMock.Object,
                _evaluationAPIService.Object,
                _dataverseService.Object
            );

            var dataverseServiceMock = new Mock<IDataverseService>();

            // Act
            await function.RunAsync("test-message");

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString().Contains("Error creating OpenAI Text Output record.")
                    ),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        /// <summary>
        /// Tests that the function logs an error when the CreateOpenAITextOutputRecordForError method throws an exception without details.
        /// </summary>
        /// <returns>Task</returns>
        [Fact]
        public async Task RunAsync_WhenCreateOpenAITextOutputRecordThrows_LogsError_WhenNoDetails()
        {
            // Arrange
            var apiResponse = new APIResponse
            {
                SubjectLine = "Subject",
                Headline = "Headline",
                IntroText = "Intro",
                CTAText = "CTA",
                OutroText = "Outro"
            };

            var inputMessage = new InputMessage
            {
                PromptText = JsonConvert.SerializeObject(apiResponse),
                ComplianceThreshold = "0.5",
                ContactId = Guid.NewGuid().ToString(),
                PromptTemplateId = Guid.NewGuid().ToString(),
                ContactName = "Test Contact",
                PromptTemplateName = "Test Template"
            };

            var jsonString = @"{}";

            _messageBuilderMock
                .Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(inputMessage);

            _evaluationAPIService
                .Setup(s => s.CallEvaluationApi(It.IsAny<string>()))
                .ReturnsAsync(jsonString);

            _serviceClientFactoryMock.Setup(f => f.Create(It.IsAny<string>()));

            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection");

            var function = new EvaluateAndPublishFunction(
                _loggerMock.Object,
                _messageBuilderMock.Object,
                _serviceClientFactoryMock.Object,
                _evaluationAPIService.Object,
                _dataverseService.Object
            );

            var dataverseServiceMock = new Mock<IDataverseService>();

            // Act
            await function.RunAsync("test-message");

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) =>
                        v.ToString().Contains("Error creating OpenAI Text Output record.")
                    ),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
            _dataverseService.Verify(
                x => x.CreateOpenAITextOutputRecordForError(
                    It.IsAny<string>(),
                    FailureStageEnum.EvaluationAndPublish,
                    inputMessage.ContactId,
                    inputMessage.PromptTemplateId,
                    inputMessage.ContactName,
                    inputMessage.PromptTemplateName),
                Times.Once);
        }

        /// <summary>
        /// Tests that the CreateOpenAITextOutputRecord method logs an error when the API response is null.
        /// </summary>
        /// <summary>
        /// Tests that the CreateOpenAITextOutputRecord method logs an error when the API response is null.
        /// </summary>
        [Fact]
        public void CreateOpenAITextOutputRecord_ApiResponseNull_LogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            var inputMessage = new InputMessage { PromptText = null };
            var function = CreateFunction();

            // Act & Assert
            var ex = Assert.Throws<ArgumentNullException>(() => function.CreateOpenAITextOutputRecord(inputMessage, 0.5m, ""));
            Assert.Equal("Input message cannot be null. (Parameter 'inputMessage')", ex.Message);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v != null && v.ToString().Contains("inputMessage is null.")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        /// <summary>
        /// Tests that the CreateOpenAITextOutputRecord method logs an error when the API response is null or default.
        /// </summary>
        [Fact]
        public void CreateOpenAITextOutputRecord_LogsError_WhenApiResponseIsNull()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            var function = CreateFunction();

            var inputMessage = new InputMessage
            {
                PromptText = "null", // This will cause deserialization to fail
                ComplianceThreshold = "0.5",
                ContactId = Guid.NewGuid().ToString(),
                PromptTemplateId = Guid.NewGuid().ToString(),
                ContactName = "Test Contact",
                PromptTemplateName = "Test Template"
            };

            // Optionally, use reflection to replace MapInputMessageToApiResponse with a delegate that returns null
            var method = typeof(EvaluateAndPublishFunction)
                .GetMethod("MapInputMessageToApiResponse", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() =>
                function.CreateOpenAITextOutputRecord(inputMessage, 0.5m, "fail reason"));

            Assert.Equal("APIResponse cannot be null.", ex.Message);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("APIResponse is null.")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);

            _dataverseService.Verify(
                x => x.CreateOpenAITextOutputRecordForError(
                    "APIResponse is null",
                    FailureStageEnum.EvaluationAndPublish,
                    inputMessage.ContactId,
                    inputMessage.PromptTemplateId,
                    inputMessage.ContactName,
                    inputMessage.PromptTemplateName),
                Times.Never);
        }

        /// <summary>
        /// Tests that the CreateOpenAITextOutputRecord method logs an error when the service client is null.
        /// </summary>
        [Fact]
        public void CreateOpenAITextOutputRecord_ServiceNull_LogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            var function = CreateFunction();
            var inputMessage = new InputMessage
            {
                PromptText = JsonConvert.SerializeObject(new APIResponse()),
                ComplianceThreshold = "0.5"
            };
            _serviceClientFactoryMock.Setup(f => f.Create(It.IsAny<string>())).Returns((ServiceClient)null);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => function.CreateOpenAITextOutputRecord(inputMessage, 0.5m, ""));
            Assert.Contains("Failed to connect to CRM", ex.Message);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to connect to CRM.")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Exactly(2));
        }

        /// <summary>
        /// Tests that the CreateOpenAITextOutputRecord method logs an error when an exception occurs during record creation.
        /// </summary>
        [Fact]
        public void CreateOpenAITextOutputRecord_Exception_LogsError()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");

            var function = CreateFunction();
            var inputMessage = new InputMessage
            {
                PromptText = JsonConvert.SerializeObject(new APIResponse()),
                ComplianceThreshold = "0.5"
            };
            _serviceClientFactoryMock.Setup(f => f.Create(It.IsAny<string>())).Throws(new Exception("factory error"));

            // Act & Assert
            var ex = Assert.Throws<Exception>(() => function.CreateOpenAITextOutputRecord(inputMessage, 0.5m, ""));
            Assert.Equal("factory error", ex.Message);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Error creating OpenAI Text Output record.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }

        /// <summary>
        /// Tests that the MapInputMessageToApiResponse method correctly maps a valid JSON string to an APIResponse object.
        /// </summary>
        [Fact]
        public void MapInputMessageToApiResponse_ValidJson_ReturnsApiResponse()
        {
            // Arrange
            var function = CreateFunction();
            var apiResponse = new APIResponse
            {
                SubjectLine = "Subject",
                Headline = "Headline",
                IntroText = "Intro",
                CTAText = "CTA",
                OutroText = "Outro"
            };
            var json = JsonConvert.SerializeObject(apiResponse);

            // Use reflection to call private method
            var method = typeof(EvaluateAndPublishFunction).GetMethod("MapInputMessageToApiResponse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var result = (APIResponse)method.Invoke(function, new object[] { new InputMessage() { PromptText = json } });

            // Assert
            Assert.Equal(apiResponse.SubjectLine, result.SubjectLine);
            Assert.Equal(apiResponse.Headline, result.Headline);
            Assert.Equal(apiResponse.IntroText, result.IntroText);
            Assert.Equal(apiResponse.CTAText, result.CTAText);
            Assert.Equal(apiResponse.OutroText, result.OutroText);
        }

        /// <summary>
        /// Tests that the MapInputMessageToApiResponse method logs an error and returns a default APIResponse when the input JSON is invalid.
        /// </summary>
        [Fact]
        public void MapInputMessageToApiResponse_InvalidJson_LogsErrorAndReturnsDefault()
        {
            // Arrange
            var function = CreateFunction();

            var method = typeof(EvaluateAndPublishFunction).GetMethod("MapInputMessageToApiResponse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var result = method?.Invoke(function, new object[] { new InputMessage() { PromptText = "" } });

            // Assert
            Assert.NotNull(result);
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to deserialize PromptText to APIResponse.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }

        /// <summary>
        /// Tests that the RunAsync method processes a message with a compliance score at the threshold and returns a success status code.
        /// </summary>
        /// <returns>Task</returns>
        [Fact]
        public async Task RunAsync_ComplianceScore_AtThreshold_StatusCodeIsSuccess()
        {
            // Arrange
            var inputMessage = new InputMessage
            {
                PromptText = "{}",
                ComplianceThreshold = "0.8",
                ContactId = Guid.NewGuid().ToString(),
                PromptTemplateId = Guid.NewGuid().ToString(),
                ContactName = "Test",
                PromptTemplateName = "Template"
            };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
            _evaluationAPIService.Setup(s => s.CallEvaluationApi(It.IsAny<string>())).ReturnsAsync("0.8");
            var function = CreateFunction();

            // Act
            await function.RunAsync("test-message");

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Failed to deserialize PromptText to APIResponse.")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
        }

        /// <summary>
        /// Tests that the RunAsync method logs an error when the avg_compliance_score is missing from the evaluation API response.
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task RunAsync_LogsError_WhenAvgComplianceScoreMissing()
        {
            // Arrange
            var inputMessage = new InputMessage
            {
                PromptText = "{}",
                ComplianceThreshold = "0.5",
                ContactId = Guid.NewGuid().ToString(),
                PromptTemplateId = Guid.NewGuid().ToString(),
                ContactName = "Test",
                PromptTemplateName = "Template"
            };
            _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);

            // Simulate evaluation API response missing avg_compliance_score
            string apiResponse = "{\"details\":{}}";
            _evaluationAPIService.Setup(s => s.CallEvaluationApi(It.IsAny<string>())).ReturnsAsync(apiResponse);

            var function = CreateFunction();

            // Act
            await function.RunAsync("test-message");

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("avg_compliance_score not found")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public void CreateOpenAITextOutputRecord_Throws_WhenDataverseConnectionMissing()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", null); // Remove variable
            var function = CreateFunction();
            var inputMessage = new InputMessage { PromptText = "{}", ComplianceThreshold = "0.5" };

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => function.CreateOpenAITextOutputRecord(inputMessage, 0.5m, ""));
            Assert.Contains("Dataverse connection string is missing or empty", ex.Message);
        }

        [Fact]
        public void CreateOpenAITextOutputRecord_Throws_WhenContactIdOrPromptTemplateIdInvalid()
        {
            // Arrange
            Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
            var function = CreateFunction();
            var apiResponse = new APIResponse
            {
                SubjectLine = "Subject",
                Headline = "Headline",
                IntroText = "Intro",
                CTAText = "CTA",
                OutroText = "Outro"
            };
            // Invalid GUIDs
            var inputMessage = new InputMessage
            {
                PromptText = JsonConvert.SerializeObject(apiResponse),
                ComplianceThreshold = "0.5",
                ContactId = "not-a-guid",
                PromptTemplateId = "not-a-guid",
                ContactName = "Test",
                PromptTemplateName = "Template"
            };
            // Return null, so the code throws before parsing GUIDs
            _serviceClientFactoryMock.Setup(f => f.Create(It.IsAny<string>())).Returns((ServiceClient)null);

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => function.CreateOpenAITextOutputRecord(inputMessage, 0.5m, ""));
            Assert.Contains("Failed to connect to CRM", ex.Message);
        }

        [Fact]
        public void MapInputMessageToApiResponse_NullInput_ReturnsDefault()
        {
            // Arrange
            var function = CreateFunction();
            var method = typeof(EvaluateAndPublishFunction).GetMethod("MapInputMessageToApiResponse", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act
            var result = (APIResponse)method.Invoke(function, new object[] { null });

            // Assert
            Assert.NotNull(result);
            Assert.Null(result.SubjectLine);
            Assert.Null(result.Headline);
            Assert.Null(result.IntroText);
            Assert.Null(result.CTAText);
            Assert.Null(result.OutroText);
        }
    }
}
