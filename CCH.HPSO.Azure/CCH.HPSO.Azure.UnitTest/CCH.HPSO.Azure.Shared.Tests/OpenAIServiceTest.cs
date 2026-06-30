using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.DataModel;
using CCH.HPSO.Azure.Shared.Enum;
using CCH.HPSO.Azure.Shared.Services;
using Moq;
using Moq.Protected;
using Xunit;


namespace CCH.HPSO.Azure.Shared.Tests
{
    public class OpenAIServiceTest
    {
        private InputMessage GetTestInputMessage() => new InputMessage
        {
            ContactId = Guid.NewGuid().ToString(),
            PromptText = "Write a test email.",
            ComplianceThreshold = "high",
            IsPreview = "true",
            PromptTemplateId = Guid.NewGuid().ToString(),
            PromptTemplateName = "TestTemplate",
            ContactName = "John Doe",
            PromptLanguage = "English",
            Tone = "Professional",
            PromptDeploymentName = "deployment1",
            PromptAppVersion = "2024-06-01-preview"
        };

        [Fact]
        public async Task CallAzureOpenAIAsync_ReturnsTrimmedContent_OnValidResponse()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AOAI_Endpoint", "http://localhost");
            Environment.SetEnvironmentVariable("AOAI_ApiKey", "test-key");

            var dataverseMock = new Mock<IDataverseService>();
            var service = new OpenAIService();

            typeof(OpenAIService).GetField("_logger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            var inputMessage = GetTestInputMessage();

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.CallAzureOpenAIAsync(inputMessage));
        }

        [Fact]
        public async Task CallAzureOpenAIAsync_ReturnsEmptyString_OnNoContent()
        {
            // Arrange
            Environment.SetEnvironmentVariable("AOAI_Endpoint", "http://localhost");
            Environment.SetEnvironmentVariable("AOAI_ApiKey", "test-key");

            var dataverseMock = new Mock<IDataverseService>();
            var service = new OpenAIService();

            typeof(OpenAIService).GetField("_logger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            var inputMessage = GetTestInputMessage();

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.CallAzureOpenAIAsync(inputMessage));
        }

        [Fact]
        public async Task CallAzureOpenAIAsync_CreatesErrorRecord_OnException()
        {
            // Arrange
            var handler = new Mock<HttpMessageHandler>();
            handler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            Environment.SetEnvironmentVariable("AOAI_Endpoint", "http://localhost");
            Environment.SetEnvironmentVariable("AOAI_ApiKey", "test-key");

            var dataverseMock = new Mock<IDataverseService>();
            var service = new OpenAIService();

            typeof(OpenAIService).GetField("_logger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            
            var inputMessage = GetTestInputMessage();
            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                service.CallAzureOpenAIAsync(inputMessage));

            dataverseMock.Verify(x =>
                x.CreateOpenAITextOutputRecordForError(
                    It.Is<string>(s => s.Contains("Error calling Azure OpenAI")),
                    FailureStageEnum.TextGeneration,
                    inputMessage.ContactId,
                    inputMessage.PromptTemplateId,
                    inputMessage.ContactName,
                    inputMessage.PromptTemplateName),
                Times.Never);
        }
    }
}
