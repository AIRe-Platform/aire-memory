using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Aire.Memory;
using Aire.Sdk.Auth.Extensions;
using Aire.Sdk.Auth.Models;
using Aire.Sdk.AI;
using Aire.Sdk.Platform;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker => {
        worker.UseNewtonsoftJson();
        worker.UseJwtAuth(new JwtTokenServiceConfiguration() {
            SigningKey = AireEnvironment.TokenSigningKey,
            EncryptionKey = AireEnvironment.TokenEncryptionKey
        });
    })
    .ConfigureServices(services => {
        services.AddHttpClient();
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
                Servers = [
                    new OpenApiServer { Url = AireEnvironment.OpenApiHost ?? "/api" }
                ],
                OpenApiVersion = OpenApiVersionType.V3,
                IncludeRequestingHostName = false,
                ForceHttp = false,
                ForceHttps = false
            };
            return options;
        });

        services.AddDbContext<DatabaseContext>();

        services
            .Configure<AirePlatformServiceConfiguration>(o => {
                o.ServiceUrl = AireEnvironment.PlatformServiceUrl;
                o.ServiceKey = AireEnvironment.ServiceKey;
            })
            .AddSingleton<IAirePlatformService, AirePlatformService>()
            .AddScoped<IAireAiService, AireAiService>();

    })
    .Build();

host.Run();
