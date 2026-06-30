using CCH.HPSO.Azure.Shared.Contracts;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;

namespace CCH.HPSO.Azure.Shared.Services
{
    /// <summary>
    /// The EvaluationApiService class is responsible for calling another Azure Function via HTTP POST.
    /// </summary>
    public class EvaluationApiService : IEvaluationApiService
    {
        /// <summary>
        /// The media type for JSON content.
        /// </summary>
        private const string MediaType = "application/json";

        /// <summary>
        /// The environment variable name for the retry count.
        /// </summary>
        private const string RetryCountConstant = "3";

        /// <summary>
        /// The HttpClient instance used to make HTTP requests.
        /// </summary>
        private readonly HttpClient _httpClient;

        /// <summary>
        /// The ILogger instance used for logging information and errors.
        /// </summary>
        private readonly ILogger<EvaluationApiService> _logger;

        /// <summary>
        /// The constructor for the EvaluationApiService class.
        /// </summary>
        /// <param name="httpClient">The http client</param>
        /// <param name="logger">The logger</param>
        /// <exception cref="ArgumentNullException"></exception>
        public EvaluationApiService(HttpClient httpClient, ILogger<EvaluationApiService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            // Set the timeout for the HttpClient from environment variable or default to 100 seconds
            var timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("HttpClientTimeoutSeconds"), out var timeout) ? timeout : 100;
            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Calls another Azure Function via HTTP POST.
        /// </summary>
        /// <param name="payload">The object to send as JSON.</param>
        /// <returns>The response string from the called function.</returns>
        public async Task<string> CallEvaluationApi(object payload)
        {
            var endpoint = Environment.GetEnvironmentVariable("EvaluationAPIEndpoint");
            var apiKey = Environment.GetEnvironmentVariable("EvaluationAPIKey");
            var json = JsonConvert.SerializeObject(new { input_text = payload });
            var content = new StringContent(json, Encoding.UTF8, MediaType);

            // Prepare headers only once
            if (!_httpClient.DefaultRequestHeaders.Contains("api-key"))
                _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
            if (!_httpClient.DefaultRequestHeaders.Contains("accept"))
                _httpClient.DefaultRequestHeaders.Add("accept", MediaType);

            int attempt = 0;
            while (true)
            {
                try
                {
                    HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);
                    response.EnsureSuccessStatusCode();
                    string responseBody = await response.Content.ReadAsStringAsync();
                    return responseBody;
                }
                catch (HttpRequestException ex)
                {
                    attempt++;
                    int retryCount = int.Parse(Environment.GetEnvironmentVariable("RetryCount") ?? RetryCountConstant);
                    _logger.LogError(ex, "HTTP request failed while calling Evaluation API. Attempt {Attempt} of {MaxRetries}.", attempt, retryCount);

                    if (attempt > retryCount)
                        throw;

                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt)); // Exponential backoff
                }
            }
        }
    }
}
