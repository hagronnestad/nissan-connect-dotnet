using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using NissanConnectLib.Api;
using NissanConnectLib.Models;
using System.Text.Json;

namespace NissanConnectLib.Example;

internal class NissanConnectHostedService : IHostedService
{
    private readonly NissanConnectClient _ncc;
    private readonly Configuration _config;
    private readonly ILogger<NissanConnectHostedService> _logger;

    public NissanConnectHostedService(
        NissanConnectClient ncc,
        Configuration config,
        ILogger<NissanConnectHostedService> logger)
    {
        _ncc = ncc;
        _config = config;
        _logger = logger;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "<Pending>")]
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunExample();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error running example");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Nissan Connect Hosted Service");
        return Task.CompletedTask;
    }

    private async Task RunExample()
    {
        var loggedIn = false;

        // Save token to cache file when refreshed
        _ncc.AccessTokenRefreshed += (sender, token) =>
        {
            _logger.LogInformation("Access token refreshed!");
            File.WriteAllText(_config.TokenCacheFile, JsonSerializer.Serialize(token));
        };

        // Try to use token cache file
        if (File.Exists(_config.TokenCacheFile))
        {
            var cachedToken = JsonSerializer.Deserialize<OAuthAccessTokenResult>(File.ReadAllText(_config.TokenCacheFile));
            _ncc.AccessToken = cachedToken;

            if (await _ncc.GetUserId() is null)
            {
                _logger.LogWarning("Could not get user ID using cached token, deleting cache file...");
                File.Delete(_config.TokenCacheFile);
            }
            else
            {
                _logger.LogInformation("Cached token is valid!");
                loggedIn = true;
            }
        }

        // Log in using username and password
        if (!loggedIn)
        {
            // Are we missing arguments?
            if (string.IsNullOrEmpty(_config.Username) || string.IsNullOrEmpty(_config.Password))
            {
                _logger.LogError("Configuration is missing. Specify username and password");
                return;
            }

            // Log in using a username and password
            loggedIn = await _ncc.LogIn(_config.Username, _config.Password);
            if (loggedIn)
            {
                _logger.LogInformation("Logged in using username and password. Writing token to cache file...");
                File.WriteAllText(_config.TokenCacheFile, JsonSerializer.Serialize(_ncc.AccessToken));
            }
            else
            {
                _logger.LogError("Login failed!");
                return;
            }
        }

        // Get the user id
        var userId = await _ncc.GetUserId();
        if (userId == null)
        {
            _logger.LogError("Couldn't get user!");
            return;
        }
        _logger.LogInformation($"Logged in as: {userId[..5]}**********");
        _logger.LogInformation($"Access Token: {_ncc.AccessToken?.AccessToken?[..5] ?? "null"}**********");

        // Get all cars
        var cars = await _ncc.GetCars(userId);
        if (cars == null)
        {
            _logger.LogError("Couldn't get cars!");
            return;
        }
        _logger.LogInformation($"Found {cars.Count} car(s)!");

        // List all cars and their battery status
        foreach (var car in cars)
        {
            if (car.Vin is null) continue;

            _logger.LogInformation("Cars:");
            _logger.LogInformation($"   Nickname: {car.NickName}");
            _logger.LogInformation($"   ModelName: {car.ModelName}");
            _logger.LogInformation($"   ModelCode: {car.ModelCode}");
            _logger.LogInformation($"   ModelYear: {car.ModelYear}");
            _logger.LogInformation($"   VIN: {car.Vin[..3]}**********");

            // Get battery status for car
            var bs = await _ncc.GetBatteryStatus(car.Vin, _config.ForceBatteryStatusRefresh);
            if (bs == null)
            {
                _logger.LogWarning("      Couldn't get battery status!");
                continue;
            }
            _logger.LogInformation($"   BatteryStatus");
            _logger.LogInformation($"      BatteryLevel: {bs.BatteryLevel}%");
            _logger.LogInformation($"      RangeHvacOff: {bs.RangeHvacOff} km");
            _logger.LogInformation($"      RangeHvacOn: {bs.RangeHvacOn} km");
            _logger.LogInformation($"      LastUpdateTime: {bs.LastUpdateTime}");
            _logger.LogInformation($"      BatteryStatusAge: {bs.BatteryStatusAge}");
            _logger.LogInformation($"      PlugStatus: {bs.PlugStatus}");
            _logger.LogInformation($"      PlugStatusDetail: {bs.PlugStatusDetail}");
            _logger.LogInformation($"      ChargeStatus: {bs.ChargeStatus}");
            _logger.LogInformation($"      ChargePower: {bs.ChargePower}");

            // Get HVAC status for car
            var hvacs = await _ncc.GetHvacStatus(car.Vin);
            if (hvacs == null)
            {
                _logger.LogWarning("      Couldn't get HVAC status!");
                continue;
            }
            _logger.LogInformation($"   HvacStatus");
            _logger.LogInformation($"      SocThreshold: {hvacs.SocThreshold}%");
            _logger.LogInformation($"      LastUpdateTime: {hvacs.LastUpdateTime}");
            _logger.LogInformation($"      HvacStatus: {hvacs.HvacStatus}");

            // Get cockpit status for car
            var cs = await _ncc.GetCockpitStatus(car.Vin);
            if (cs == null)
            {
                _logger.LogWarning("      Couldn't get cockpit status!");
                continue;
            }
            _logger.LogInformation($"   Cockpit");
            _logger.LogInformation($"      TotalMileage: {cs.TotalMileage} km");
        }
    }
}
