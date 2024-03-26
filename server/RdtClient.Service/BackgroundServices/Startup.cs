using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RdtClient.Data.Data;
using RdtClient.Service.Services;
using Serilog;

namespace RdtClient.Service.BackgroundServices;

public class Startup : IHostedService
{
    public static Boolean Ready { get; private set; }

    private readonly IServiceProvider _serviceProvider;

    public Startup(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        Log.Warning($"Starting host on version {version}");

        using var scope = _serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);

        var settings = scope.ServiceProvider.GetRequiredService<Settings>();
        await settings.Seed();
        await settings.ResetCache();

        var exampleConfigPath = "/data/db/instances.json.example";
        if (!File.Exists(exampleConfigPath))
        {
            var directoryPath = Path.GetDirectoryName(exampleConfigPath);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

                var defaultConfig = new
                {
                    Category = new { Host = "http://host:port", ApiKey = "api_key"},
                    OtherCategory = new { Host = "http://other_host:port", ApiKey = "other_api_key" },

                    // Exemple de valeurs pour qualityProfileId
                    // radarr
                    // curl -X 'GET' \
                    //  'http://localhost:7878/api/v3/qualityprofile' \
                    //  -H 'accept: application/json' \
                    //  -H 'X-Api-Key: api_radarr'

                    // sonarr
                    // curl -X 'GET' \
                    //  'http://localhost:8989/api/v3/qualityprofile' \
                    //  -H 'accept: application/json' \
                    //  -H 'X-Api-Key: api_sonarr'
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    IgnoreNullValues = true,
                    IgnoreReadOnlyProperties = true,
                    PropertyNameCaseInsensitive = true
                };

                var json = JsonSerializer.Serialize(defaultConfig, options);
                await File.WriteAllTextAsync(exampleConfigPath, json, cancellationToken);
            }
        Ready = true;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}