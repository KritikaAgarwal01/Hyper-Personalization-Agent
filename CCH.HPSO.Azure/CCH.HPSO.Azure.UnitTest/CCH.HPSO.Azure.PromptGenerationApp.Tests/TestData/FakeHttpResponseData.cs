using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text;

namespace CCH.HPSO.Azure.PromptGenerationApp.Tests.TestData
{
    /// <summary>
    /// This class is a fake implementation of the HttpResponseData for testing purposes.
    /// </summary>
    public class FakeHttpResponseData : HttpResponseData
    {
        /// <summary>
        /// The stream that contains the body of the HTTP response.
        /// </summary>
        private readonly MemoryStream _body = new();

        /// <summary>
        /// The constructor for the FakeHttpResponseData class.
        /// </summary>
        /// <param name="statusCode">The status code of the HTTP response.</param>
        public FakeHttpResponseData(HttpStatusCode statusCode) : base(new FakeFunctionContext())
        {
            StatusCode = statusCode;
            Headers = new HttpHeadersCollection(); // Properly settable
            Cookies = new FakeHttpCookies();
        }

        /// <summary>
        /// The status code of the HTTP response.
        /// </summary>
        public override HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// The headers of the HTTP response.
        /// </summary>
        public override HttpHeadersCollection Headers { get; set; }

        /// <summary>
        /// The cookies of the HTTP response.
        /// </summary>
        public override HttpCookies Cookies { get; }

        /// <summary>
        /// The body of the HTTP response.
        /// </summary>
        public override Stream Body { get => _body; set => throw new NotImplementedException(); }

        /// <summary>
        /// This method is not implemented and will throw a NotImplementedException.
        /// </summary>
        /// <returns>This will always throw an exception.</returns>
        public string BodyAsString()
        {
            _body.Position = 0;
            using var reader = new StreamReader(_body, Encoding.UTF8, leaveOpen: true);
            return reader.ReadToEnd();
        }
    }
}
