using System.Reflection;
using System.Text.Json;
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




            // Création du fichier readme.txt
            var readmeFilePath = "/data/db/readme.txt";
            var readmeContent = @"
*******************************************************************************
*                            RdtClient Service                                 *
*******************************************************************************

This service initializes the RdtClient application upon startup.

1. Database Migration:
   - The service automatically migrates the database schema using Entity Framework Core upon startup.

2. Settings Initialization:
   - Seeds initial settings data.
   - Resets cache to ensure data consistency.

3. Example Configuration File Generation:
   - If the example configuration file does not exist, it will be generated.
   - Example configuration file path: `/data/db/instances.json.example`.
   - Format:
        {
            ""Category"": {
                ""Host"": ""http://host:port"",
                ""ApiKey"": ""api_key""
            },
            ""OtherCategory"": {
                ""Host"": ""http://other_host:port"",
                ""ApiKey"": ""other_api_key""
            }
        }

4. Version Logging:
   - Logs the current version of the application upon startup.

For further information, please refer to the source code or contact the developers.

Author: [Your Name]
Date: [Date]

*******************************************************************************
";
            await File.WriteAllTextAsync(readmeFilePath, readmeContent, cancellationToken);






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
                Category = new { Host = "http://host:port", ApiKey = "api_key" },
                OtherCategory = new { Host = "http://other_host:port", ApiKey = "other_api_key" }
            };
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(exampleConfigPath, json, cancellationToken);
        }

        Ready = true;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}