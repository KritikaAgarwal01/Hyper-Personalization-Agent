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
builder.Services.AddSingleton<IServiceClientFactory, ServiceClientFactory>();
builder.Services.AddScoped<IOrganizationService>(provider =>
{
    var connectionString = Environment.GetEnvironmentVariable("DataverseConnection");
    return new ServiceClient(connectionString);
});
builder.Services.AddScoped<IDataverseService, DataverseService>();
builder.Services.AddScoped<IPromptMessageBuilder, PromptMessageBuilder>();
builder.Services.AddScoped<IEvaluationApiService, EvaluationApiService>();

await builder.Build().RunAsync();
