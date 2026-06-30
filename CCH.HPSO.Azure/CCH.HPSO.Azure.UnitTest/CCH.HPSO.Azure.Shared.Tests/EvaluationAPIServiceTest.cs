using CCH.HPSO.Azure.Shared.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace CCH.HPSO.Azure.Shared.Tests
{
    public class EvaluationAPIServiceTest
    {
        [Fact]
        public async Task CallEvaluationAPI_ReturnsResponse_OnSuccess()
        {
            // Arrange
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("{\"result\":\"success\"}")
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var loggerMock = new Mock<ILogger<EvaluationApiService>>();

            Environment.SetEnvironmentVariable("EvaluationAPIEndpoint", "http://test-endpoint");
            Environment.SetEnvironmentVariable("EvaluationAPIKey", "test-key");

            var service = new EvaluationApiService(httpClient, loggerMock.Object);

            // Act
            var result = await service.CallEvaluationApi("test-payload");

            // Assert
            Assert.Contains("success", result);
        }

        [Fact]
        public async Task CallEvaluationAPI_ThrowsAndLogs_OnHttpRequestException()
        {
            // Arrange
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException("Network error"));

            var httpClient = new HttpClient(handlerMock.Object);
            var loggerMock = new Mock<ILogger<EvaluationApiService>>();

            Environment.SetEnvironmentVariable("EvaluationAPIEndpoint", "http://test-endpoint");
            Environment.SetEnvironmentVariable("EvaluationAPIKey", "test-key");

            var service = new EvaluationApiService(httpClient, loggerMock.Object);

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(() => service.CallEvaluationApi("test-payload"));
            loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("HTTP request failed while calling Evaluation API.")),
                    It.IsAny<HttpRequestException>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Exactly(4));
        }

        [Fact]
        public void Constructor_ThrowsArgumentNullException_WhenHttpClientIsNull()
        {
            var loggerMock = new Mock<ILogger<EvaluationApiService>>();
            Assert.Throws<ArgumentNullException>(() => new EvaluationApiService(null, loggerMock.Object));
        }

        [Fact]
        public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
        {
            var httpClient = new HttpClient();
            Assert.Throws<ArgumentNullException>(() => new EvaluationApiService(httpClient, null));
        }
    }
}
