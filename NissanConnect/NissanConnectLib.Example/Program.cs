using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            config.AddUserSecrets<Program>();
        })
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "[dd.MM.yyyy HH:mm:ss] ";
                options.SingleLine = true;
            });
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
