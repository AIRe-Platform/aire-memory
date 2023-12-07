using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.OpenApi.Models;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker => {
        worker.UseNewtonsoftJson();
    })
    .ConfigureServices(services => {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddMvcCore().AddNewtonsoftJson(options => {
            options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        });

        services.AddSingleton<IOpenApiConfigurationOptions>(_ => {
            var options = new OpenApiConfigurationOptions {
                Info = new OpenApiInfo {
                    Version = "0.1.0",
                    Title = "AIRe Memory Module",
                    Description = "This is the reference implementation of AIRe Platform Memory module."
                },
                Servers = DefaultOpenApiConfigurationOptions.GetHostNames(),
                OpenApiVersion = OpenApiVersionType.V3,
                IncludeRequestingHostName = true,
                ForceHttp = false,
                ForceHttps = false
            };
            return options;
        });
    })
    .Build();

host.Run();
