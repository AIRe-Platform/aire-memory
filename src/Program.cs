using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Aire.Memory;
using Aire.Sdk.Auth;
using Aire.Sdk.Auth.Extensions;
using Aire.Sdk.Platform;
using Aire.Sdk.Platform.Clients;
using Azure.Storage.Queues;
using Aire.Sdk.Azure;

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

        services
            .AddSingleton<ITableStorageService, TableStorageService>()
            .Configure<TableStorageConfiguration>(o => {
                o.ConnectionString = AireEnvironment.StorageConnectionString;
            });

        services.AddAzureClients(builder => {
            builder.AddQueueServiceClient(AireEnvironment.StorageConnectionString)
                .ConfigureOptions(options => {
                    options.MessageEncoding = QueueMessageEncoding.Base64;
                })
                .WithName("queue-client");
        });

        services.AddSingleton<IOpenApiConfigurationOptions>(_ => {
            var options = new OpenApiConfigurationOptions {
                Info = new OpenApiInfo {
                    Version = "0.1.0",
                    Title = "AIRe Memory Module",
                    Description = "This is the reference implementation of the AIRe Platform Memory module."
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

        // TODO: Remove after migration to Azure Storage
        services.AddDbContext<DatabaseContext>();

        services
            .Configure<AirePlatformServiceConfiguration>(o => {
                o.ServiceUrl = AireEnvironment.PlatformServiceUrl;
                o.ServiceKey = AireEnvironment.PlatformServiceKey;
            })
            .AddSingleton<IAirePlatformService, AirePlatformService>()
            .AddScoped<IAireClientFactory, AireClientFactory>();

    })
    .Build();

host.Run();
