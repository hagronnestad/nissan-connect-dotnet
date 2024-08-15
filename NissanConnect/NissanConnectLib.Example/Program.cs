using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Hosting;
using NissanConnectLib.Api;

namespace NissanConnectLib.Example;

internal class Program
{
    static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)

        .ConfigureAppConfiguration((hostingContext, config) =>
        {
            config.AddEnvironmentVariables();
            config.AddCommandLine(args);
            config.AddUserSecrets<Program>();
        })
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole(options =>
            {
                options.FormatterName = nameof(CustomConsoleFormatter);
            });
            logging.AddConsoleFormatter<CustomConsoleFormatter, ConsoleFormatterOptions>();
        })
        .ConfigureServices((context, services) =>
        {
            var config = context.Configuration.Get<Configuration>() ??
                new Configuration();

            services.AddHostedService<NissanConnectHostedService>();
            services.AddSingleton<Configuration>(config);
            services.AddSingleton<NissanConnectClient>();
        });

        await host.Build().RunAsync();
    }
}
