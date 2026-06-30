using Microsoft.Azure.Functions.Worker.Http;
using System.Collections.Specialized;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace CCH.HPSO.Azure.PromptGenerationApp.Tests.TestData
{
    /// <summary>
    /// This class is a fake implementation of the HttpRequestData for testing purposes.
    /// </summary>
    public class FakeHttpRequestData : HttpRequestData
    {
        /// <summary>
        /// The stream that contains the body of the HTTP request.
        /// </summary>
        private readonly MemoryStream _bodyStream;

        /// <summary>
        /// The constructor for the FakeHttpRequestData class.
        /// </summary>
        /// <param name="body"></param>
        public FakeHttpRequestData(string body) : base(new FakeFunctionContext())
        {
            _bodyStream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        }

        /// <summary>
        /// The body of the HTTP request.
        /// </summary>
        public override Stream Body => _bodyStream;

        /// <summary>
        /// The headers of the HTTP request.
        /// </summary>
        public override HttpHeadersCollection Headers { get; } = new();

        /// <summary>
        /// The query parameters of the HTTP request.
        /// </summary>
        public override NameValueCollection Query => new NameValueCollection();

        /// <summary>
        /// The route parameters of the HTTP request.
        /// </summary>
        public override Uri Url => new Uri("http://localhost");

        /// <summary>
        /// The user associated with the HTTP request.
        /// </summary>
        public override IEnumerable<ClaimsIdentity> Identities => Enumerable.Empty<ClaimsIdentity>();

        /// <summary>
        /// The method of the HTTP request.
        /// </summary>
        public override string Method => "POST";

        /// <summary>
        /// The cookies of the HTTP request.
        /// </summary>
        public override IReadOnlyCollection<IHttpCookie> Cookies => throw new NotImplementedException();

        /// <summary>
        /// This method is not implemented and will throw a NotImplementedException.
        /// </summary>
        /// <returns>This will always throw an exception.</returns>
        public override HttpResponseData CreateResponse()
        {
            return new FakeHttpResponseData(HttpStatusCode.OK); // or use 500 based on context
        }
    }
}
