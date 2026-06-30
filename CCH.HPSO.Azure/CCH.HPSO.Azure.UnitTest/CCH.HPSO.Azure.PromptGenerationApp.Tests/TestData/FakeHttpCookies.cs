using Microsoft.Azure.Functions.Worker.Http;

namespace CCH.HPSO.Azure.PromptGenerationApp.Tests.TestData
{
    /// <summary>
    /// This class is a fake implementation of the HttpCookies for testing purposes.
    /// </summary>
    public class FakeHttpCookies : HttpCookies
    {
        /// <summary>
        /// The constructor for the FakeHttpCookies class.
        /// </summary>
        /// <param name="name">The name of the cookie.</param>
        /// <param name="value">The value of the cookie.</param>
        public override void Append(string name, string value) { }

        /// <summary>
        /// This method is not implemented and will throw a NotImplementedException.
        /// </summary>
        /// <param name="cookie">The name of the cookie</param>
        /// <exception cref="NotImplementedException"></exception>
        public override void Append(IHttpCookie cookie) { throw new NotImplementedException(); }

        /// <summary>
        /// This method is not implemented and will throw a NotImplementedException.
        /// </summary>
        /// <returns>This will always throw an exception.</returns>
        /// <exception cref="NotImplementedException"></exception>
        public override IHttpCookie CreateNew() => throw new NotImplementedException();
    }
}
