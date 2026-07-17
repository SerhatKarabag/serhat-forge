using System.Text.RegularExpressions;
using Serhat.Forge.CloudScript.Infrastructure.Idempotency;
using Serhat.Forge.CloudScript.Infrastructure.Logging;
using Serhat.Forge.CloudScript.Infrastructure.PlayFab;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var config = context.Configuration;
        var environmentName = FirstNonEmpty(
            config["AZURE_FUNCTIONS_ENVIRONMENT"],
            config["DOTNET_ENVIRONMENT"],
            context.HostingEnvironment.EnvironmentName,
            "Production");
        var isDevelopment = IsDevelopmentEnvironment(environmentName);
        var titleId = RequireSetting(config["PLAYFAB_TITLE_ID"], "PLAYFAB_TITLE_ID");
        var secretKey = RequireSetting(config["PLAYFAB_DEV_SECRET_KEY"], "PLAYFAB_DEV_SECRET_KEY");
        var storageConnection = FirstNonEmpty(
            config["AZURE_STORAGE_CONNECTION_STRING"],
            config["AzureWebJobsStorage"]);
        var idempotencyTable = FirstNonEmpty(
            config["IDEMPOTENCY_TABLE_NAME"],
            "IdempotencyStore");

        if (!Regex.IsMatch(idempotencyTable, "^[A-Za-z][A-Za-z0-9]{2,62}$"))
        {
            throw new InvalidOperationException(
                "IDEMPOTENCY_TABLE_NAME must be a valid Azure Table name (3-63 alphanumeric characters, starting with a letter).");
        }

        if (!int.TryParse(config["IDEMPOTENCY_TTL_HOURS"] ?? "24", out var idempotencyTtlHours) ||
            idempotencyTtlHours is < 1 or > 168)
        {
            throw new InvalidOperationException("IDEMPOTENCY_TTL_HOURS must be between 1 and 168.");
        }

        if (!isDevelopment && string.IsNullOrWhiteSpace(storageConnection))
        {
            throw new InvalidOperationException(
                "AZURE_STORAGE_CONNECTION_STRING is required outside Development/Local/Test.");
        }

        if (!isDevelopment &&
            storageConnection.Contains("UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Production cannot use the Azure Storage development emulator.");
        }

        services.AddSingleton<IPlayFabServerGateway>(sp =>
            new PlayFabServerGateway(
                titleId,
                secretKey,
                sp.GetRequiredService<ILogger<PlayFabServerGateway>>()));

        if (string.IsNullOrWhiteSpace(storageConnection))
        {
            services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        }
        else
        {
            services.AddSingleton<IIdempotencyStore>(sp =>
                new TableStorageIdempotencyStore(
                    storageConnection,
                    idempotencyTable,
                    titleId,
                    TimeSpan.FromHours(idempotencyTtlHours),
                    sp.GetRequiredService<ILogger<TableStorageIdempotencyStore>>()));
        }

        services.AddSingleton<ICorrelationContext, CorrelationContext>();
    })
    .ConfigureLogging(logging =>
    {
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);
    })
    .Build();

host.Run();

static string RequireSetting(string? value, string name)
{
    var normalized = value?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(normalized) ||
        normalized.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
        normalized.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            $"{name} is required and cannot contain a template placeholder.");
    }

    return normalized;
}

static string FirstNonEmpty(params string?[] values) =>
    values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
    ?? string.Empty;

static bool IsDevelopmentEnvironment(string environmentName) =>
    string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(environmentName, "Local", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);