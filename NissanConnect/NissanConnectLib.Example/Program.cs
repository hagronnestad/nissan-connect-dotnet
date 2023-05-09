namespace NissanConnectLib.Example
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Are we missing arguments?
            if (args.Length == 0)
            {
                Console.WriteLine("Command line arguments are missing. Specify either an existing token or a username and password");
                return;
            }

            // Instantiate client
            var ncc = new NissanConnectClient(Configuration.Region.EU);
            var loggedIn = false;

            // Log in using an existing token
            if (args.Length == 1)
            {
                loggedIn = await ncc.LogIn(args[0]);
                Console.WriteLine($"Logged in using token: {ncc.Token}");
            }

            // Log in using a username and password
            if (args.Length == 2)
            {
                loggedIn = await ncc.LogIn(args[0], args[1]);
            }

            // Are we logged in?
            if (!loggedIn)
            {
                Console.WriteLine("Login failed!");
                Console.ReadLine();
                return;
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
                if (bs?.Attributes == null)
                {
                    Console.WriteLine("      Couldn't get battery status!");
                    continue;
                }
                Console.WriteLine($"      Battery: {bs.Attributes.BatteryLevel}%");
                Console.WriteLine($"      Range: {bs.Attributes.RangeHvacOff} km");
                Console.WriteLine($"      LastUpdateTime: {bs.Attributes.LastUpdateTime}");
            }
        }
    }
}
