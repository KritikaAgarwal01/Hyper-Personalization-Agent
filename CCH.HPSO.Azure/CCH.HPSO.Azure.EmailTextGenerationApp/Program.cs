using CCH.HPSO.Azure.Shared.Contracts;
using CCH.HPSO.Azure.Shared.Helpers;
using CCH.HPSO.Azure.Shared.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Register IServiceClientFactory
builder.Services.AddSingleton<IServiceClientFactory, ServiceClientFactory>();
builder.Services.AddScoped<IOrganizationService>(provider =>
{
    var connectionString = Environment.GetEnvironmentVariable("DataverseConnection");

    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Environment variable 'DataverseConnection' is missing or empty.");

    return new ServiceClient(connectionString);
});

// Register DataverseService using a factory that includes dependencies
builder.Services.AddScoped<IDataverseService>(provider =>
{
    var orgService = provider.GetRequiredService<IOrganizationService>();
    var factory = provider.GetRequiredService<IServiceClientFactory>();
    return new DataverseService(orgService, factory);
});

// Other services
builder.Services.AddScoped<IPromptMessageBuilder, PromptMessageBuilder>();
builder.Services.AddScoped<IOpenAIService, OpenAIService>();

await builder.Build().RunAsync();
