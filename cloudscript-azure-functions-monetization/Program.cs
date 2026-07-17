using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serhat.Forge.CloudScript.Framework.Monetization.Configuration;
using Serhat.Forge.CloudScript.Infrastructure.Logging;
using Serhat.Forge.CloudScript.Infrastructure.Telemetry;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddApplicationInsightsTelemetryProcessor<GooglePlayPurchaseTokenTelemetryProcessor>();
        services.AddApplicationInsightsTelemetryProcessor<AppleTransactionIdTelemetryProcessor>();

        services.AddMonetization();
        services.AddSingleton<ICorrelationContext, CorrelationContext>();
    })
    .ConfigureLogging(logging =>
    {
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
    })
    .Build();

host.Run();
