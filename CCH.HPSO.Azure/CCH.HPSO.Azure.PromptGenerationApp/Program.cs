using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.Helpers;
using CCH.HPSO.Azure.Shared.Services;
using Microsoft.Azure.Functions.Worker.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        services.AddSingleton<IServiceClientFactory, ServiceClientFactory>();
        services.AddScoped<IOrganizationService>(provider =>
        {
            var connectionString = Environment.GetEnvironmentVariable("DataverseConnection");
            return new ServiceClient(connectionString);
        });
        services.AddScoped<IDataverseService, DataverseService>();
        services.AddSingleton<IOpenAIService, OpenAIService>();
        services.AddSingleton<IPromptMessageBuilder, PromptMessageBuilder>();
        services.AddSingleton<IEvaluationApiService, EvaluationApiService>();
    })
    .Build();

await host.RunAsync();