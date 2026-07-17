using CCH.HPSO.Azure.PromptGenerationApp.Tests.TestData;
using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using System.Net;
using Xunit;

namespace CCH.HPSO.Azure.PromptGenerationApp.Tests;

/// <summary>
/// Tests for the consolidated <see cref="PromptGenerationFunction"/> workflow.
/// </summary>
public class PromptGenerationFunctionTest
{
    private readonly Mock<IDataverseService> _dataverseServiceMock = new();
    private readonly Mock<IEvaluationApiService> _evaluationApiServiceMock = new();
    private readonly Mock<ILogger<PromptGenerationFunction>> _loggerMock = new();
    private readonly Mock<IPromptMessageBuilder> _messageBuilderMock = new();
    private readonly Mock<IOpenAIService> _openAiServiceMock = new();
    private readonly Mock<IServiceClientFactory> _serviceClientFactoryMock = new();

    private const string PreviewEvaluationResponse = """
    {
      "details": {
        "avg_compliance_score": 0.85,
        "detailed_results": {
          "rules": []
        }
      }
    }
    """;

    private const string WorkflowEvaluationResponse = """
    {
      "details": {
        "avg_compliance_score": 0.92,
        "detailed_results": {
          "rules": []
        }
      }
    }
    """;

    /// <summary>
    /// Initializes test environment variables.
    /// </summary>
    public PromptGenerationFunctionTest()
    {
        Environment.SetEnvironmentVariable("DataverseConnection", "fake-connection-string");
        Environment.SetEnvironmentVariable("ServiceBusConnection", "fake-connection-string");
        Environment.SetEnvironmentVariable("InputTopicName", "input-topic");
        Environment.SetEnvironmentVariable("ServiceBusSubscription", "subscription");
    }

    /// <summary>
    /// Verifies preview requests return generated text and compliance score.
    /// </summary>
    [Fact]
    public async Task RunHttp_WhenPreview_ReturnsGeneratedTextAndComplianceScore()
    {
        var inputMessage = new InputMessage
        {
            ComplianceThreshold = "0.80",
            IsPreview = "true",
            PromptText = "sample prompt"
        };

        _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Returns(inputMessage);
        _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), It.IsAny<FailureStageEnum>())).Returns("formatted-message");
        _openAiServiceMock.Setup(m => m.CallAzureOpenAIAsync(It.IsAny<InputMessage>())).ReturnsAsync("generated-output");
        _evaluationApiServiceMock.Setup(m => m.CallEvaluationApi("generated-output")).ReturnsAsync(PreviewEvaluationResponse);

        var response = await CreateFunction().RunHttp(new FakeHttpRequestData("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = ((FakeHttpResponseData)response).BodyAsString();
        Assert.Contains("generated-output", body);
        Assert.Contains("0.85", body);
    }

    /// <summary>
    /// Verifies parse failures on HTTP requests are recorded against prompt generation.
    /// </summary>
    [Fact]
    public async Task RunHttp_WhenParseFails_RecordsPromptGenerationError()
    {
        _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), It.IsAny<string>())).Throws(new Exception("parse error"));

        var response = await CreateFunction().RunHttp(new FakeHttpRequestData("{}"));

        Assert.Contains("parse error", ((FakeHttpResponseData)response).BodyAsString());
        _dataverseServiceMock.Verify(
            m => m.CreateOpenAITextOutputRecordForError(
                It.Is<string>(s => s.Contains("parse error")),
                FailureStageEnum.PromptGeneration,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies the queued workflow persists the final evaluated output.
    /// </summary>
    [Fact]
    public async Task RunServiceBus_WhenWorkflowSucceeds_PersistsOutput()
    {
        var inputMessage = new InputMessage
        {
            ComplianceThreshold = "0.80",
            ContactId = Guid.NewGuid().ToString(),
            ContactName = "Contact",
            IsPreview = "false",
            PromptTemplateId = Guid.NewGuid().ToString(),
            PromptTemplateName = "Template"
        };

        _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), nameof(PromptGenerationFunction.RunServiceBus))).Returns(inputMessage);
        _messageBuilderMock.Setup(m => m.ParseMessage("formatted-message", "ExecutePipelineAsync")).Returns(inputMessage);
        _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), FailureStageEnum.PromptGeneration)).Returns("formatted-message");
        _openAiServiceMock.Setup(m => m.CallAzureOpenAIAsync(It.IsAny<InputMessage>())).ReturnsAsync("{\"subject_line\":\"Subject\"}");
        _evaluationApiServiceMock.Setup(m => m.CallEvaluationApi(It.IsAny<string>())).ReturnsAsync(WorkflowEvaluationResponse);
        _serviceClientFactoryMock.Setup(m => m.Create(It.IsAny<string>())).Returns(Mock.Of<IOrganizationService>());

        await CreateFunction().RunServiceBus("{}");

        _dataverseServiceMock.Verify(
            m => m.CreateOpenAITextOutputEntityRecord(
                It.Is<InputMessage>(message => message.PromptText == "{\"subject_line\":\"Subject\"}"),
                It.Is<APIResponse>(response => response.SubjectLine == "Subject"),
                0.92m,
                string.Empty,
                It.IsAny<IOrganizationService>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies text generation failures are recorded against the text generation stage.
    /// </summary>
    [Fact]
    public async Task RunServiceBus_WhenTextGenerationFails_RecordsTextGenerationError()
    {
        var inputMessage = new InputMessage
        {
            ContactId = Guid.NewGuid().ToString(),
            PromptTemplateId = Guid.NewGuid().ToString()
        };

        _messageBuilderMock.Setup(m => m.ParseMessage(It.IsAny<string>(), nameof(PromptGenerationFunction.RunServiceBus))).Returns(inputMessage);
        _messageBuilderMock.Setup(m => m.ParseMessage("formatted-message", "ExecutePipelineAsync")).Returns(inputMessage);
        _messageBuilderMock.Setup(m => m.BuildUpdatedMessage(It.IsAny<InputMessage>(), It.IsAny<string>(), It.IsAny<IServiceClientFactory>(), FailureStageEnum.PromptGeneration)).Returns("formatted-message");
        _openAiServiceMock.Setup(m => m.CallAzureOpenAIAsync(It.IsAny<InputMessage>())).ThrowsAsync(new Exception("openai error"));

        await CreateFunction().RunServiceBus("{}");

        _dataverseServiceMock.Verify(
            m => m.CreateOpenAITextOutputRecordForError(
                It.Is<string>(s => s.Contains("openai error")),
                FailureStageEnum.TextGeneration,
                inputMessage.ContactId,
                inputMessage.PromptTemplateId,
                inputMessage.ContactName,
                inputMessage.PromptTemplateName),
            Times.Once);
    }

    /// <summary>
    /// Creates the system under test.
    /// </summary>
    /// <returns>The function under test.</returns>
    private PromptGenerationFunction CreateFunction()
    {
        return new PromptGenerationFunction(
            _loggerMock.Object,
            _messageBuilderMock.Object,
            _openAiServiceMock.Object,
            _serviceClientFactoryMock.Object,
            _evaluationApiServiceMock.Object,
            _dataverseServiceMock.Object);
    }
}
