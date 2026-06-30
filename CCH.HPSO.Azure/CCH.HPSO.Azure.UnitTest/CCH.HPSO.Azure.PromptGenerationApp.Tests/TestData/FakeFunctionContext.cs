using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace CCH.HPSO.Azure.PromptGenerationApp.Tests.TestData
{
    /// <summary>
    /// This class is a fake implementation of the FunctionContext for testing purposes.
    /// </summary>
    public class FakeFunctionContext : FunctionContext
    {
        /// <summary>
        /// The constructor for the FakeFunctionContext class.
        /// </summary>
        public override string InvocationId => Guid.NewGuid().ToString();

        /// <summary>
        /// The function ID for the fake function context.
        /// </summary>
        public override string FunctionId => Guid.NewGuid().ToString();

        /// <summary>
        /// The TraceContext for the fake function context.
        /// </summary>
        public override TraceContext TraceContext => null;

        /// <summary>
        /// The IServiceProvider instance used to resolve services.
        /// </summary>
        public override IServiceProvider InstanceServices { get; set; } = new ServiceCollection().BuildServiceProvider();

        /// <summary>
        /// The FunctionDefinition for the fake function context.
        /// </summary>
        public override FunctionDefinition FunctionDefinition => null;

        /// <summary>
        /// The FunctionDirectory for the fake function context.
        /// </summary>
        public override RetryContext RetryContext => throw new NotImplementedException();

        /// <summary>
        /// The FunctionAppDirectory for the fake function context.
        /// </summary>
        public override IDictionary<object, object> Items { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        /// <summary>
        /// The BindingContext for the fake function context.
        /// </summary>
        public override BindingContext BindingContext => throw new NotImplementedException();

        /// <summary>
        /// The Features for the fake function context.
        /// </summary>
        public override IInvocationFeatures Features => throw new NotImplementedException();
    }
}
