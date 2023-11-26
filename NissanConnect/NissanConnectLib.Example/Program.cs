using NissanConnectLib.Api;
using NissanConnectLib.Models;
using System.Text.Json;

namespace NissanConnectLib.Example
{
    internal class Program
    {
        private const string TOKEN_CACHE_FILE = "token.cache";

        static async Task Main(string[] args)
        {
            // Instantiate client
            var ncc = new NissanConnectClient(Configuration.Region.EU);
            var loggedIn = false;

            // Try to use token cache file
            if (File.Exists(TOKEN_CACHE_FILE))
            {
                var cachedToken = JsonSerializer.Deserialize<OAuthAccessTokenResult>(File.ReadAllText(TOKEN_CACHE_FILE));
                ncc.AccessToken = cachedToken;

                if (await ncc.GetUserId() is null)
                {
                    Console.WriteLine("Could not get user ID using cached token, deleting cache file...");
                    File.Delete(TOKEN_CACHE_FILE);
                }
                else
                {
                    Console.WriteLine("Cached token is valid!");
                    loggedIn = true;
                }
            }

            // Log in using username and password
            if (!loggedIn)
            {
                // Are we missing arguments?
                if (args.Length == 0)
                {
                    Console.WriteLine("Command line arguments are missing. Specify username and password");
                    return;
                }

                // Log in using a username and password
                if (args.Length == 2)
                {
                    loggedIn = await ncc.LogIn(args[0], args[1]);
                    if (loggedIn)
                    {
                        Console.WriteLine("Logged in using username and password. Writing token to cache file...");
                        File.WriteAllText(TOKEN_CACHE_FILE, JsonSerializer.Serialize(ncc.AccessToken));
                    }
                    else
                    {
                        Console.WriteLine("Login failed!");
                        Console.ReadLine();
                        return;
                    }
                }
            }

            // Get the user id
            var userId = await ncc.GetUserId();
            if (userId == null)
            {
                Console.WriteLine("Couldn't get user!");
                Console.ReadLine();
                return;
            }
            Console.WriteLine($"Logged in as: {userId}");
            Console.WriteLine($"Access Token: {ncc.AccessToken?.AccessToken ?? "null"}");

            // Get all cars
            var cars = await ncc.GetCars(userId);
            if (cars == null)
            {
                Console.WriteLine("Couldn't get cars!");
                Console.ReadLine();
                return;
            }
            Console.WriteLine($"Found {cars.Count} car(s)!");

            // List all cars and their battery status
            foreach (var car in cars)
            {
                if (car.Vin is null) continue;

                Console.WriteLine("\nCars:");
                Console.WriteLine($"   Nickname: {car.NickName}");
                Console.WriteLine($"   ModelName: {car.ModelName}");
                Console.WriteLine($"   ModelCode: {car.ModelCode}");
                Console.WriteLine($"   ModelYear: {car.ModelYear}");
                Console.WriteLine($"   VIN: {car.Vin}");

                // Get battery status for car
                var bs = await ncc.GetBatteryStatus(car.Vin);
                if (bs == null)
                {
                    Console.WriteLine("      Couldn't get battery status!");
                    continue;
                }
                Console.WriteLine($"   BatteryStatus");
                Console.WriteLine($"      BatteryLevel: {bs.BatteryLevel}%");
                Console.WriteLine($"      RangeHvacOff: {bs.RangeHvacOff} km");
                Console.WriteLine($"      RangeHvacOn: {bs.RangeHvacOn} km");
                Console.WriteLine($"      LastUpdateTime: {bs.LastUpdateTime}");
                Console.WriteLine($"      BatteryStatusAge: {bs.BatteryStatusAge}");
                Console.WriteLine($"      PlugStatus: {bs.PlugStatus}");
                Console.WriteLine($"      PlugStatusDetail: {bs.PlugStatusDetail}");
                Console.WriteLine($"      ChargeStatus: {bs.ChargeStatus}");
                Console.WriteLine($"      ChargePower: {bs.ChargePower}");

                // Get HVAC status for car
                var hvacs = await ncc.GetHvacStatus(car.Vin);
                if (hvacs == null)
                {
                    Console.WriteLine("      Couldn't get HVAC status!");
                    continue;
                }
                Console.WriteLine($"   HvacStatus");
                Console.WriteLine($"      SocThreshold: {hvacs.SocThreshold}%");
                Console.WriteLine($"      LastUpdateTime: {hvacs.LastUpdateTime}");
                Console.WriteLine($"      HvacStatus: {hvacs.HvacStatus}");

                // Get cockpit status for car
                var cs = await ncc.GetCockpitStatus(car.Vin);
                if (cs == null)
                {
                    Console.WriteLine("      Couldn't get cockpit status!");
                    continue;
                }
                Console.WriteLine($"   Cockpit");
                Console.WriteLine($"      TotalMileage: {cs.TotalMileage} km");
            }
        }
    }
}
