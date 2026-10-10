# Nissan Connect Client Library

[![CI](https://github.com/hagronnestad/nissan-connect-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/hagronnestad/nissan-connect-dotnet/actions/workflows/ci.yml) [![Release](https://github.com/hagronnestad/nissan-connect-dotnet/actions/workflows/main.yml/badge.svg)](https://github.com/hagronnestad/nissan-connect-dotnet/actions/workflows/main.yml) [![Nuget](https://img.shields.io/nuget/v/NissanConnectLib)](https://www.nuget.org/packages/NissanConnectLib)

***Unofficial*** C# .NET client library for the Nissan Connect API. This library is based on the great [dartnissanconnect](https://gitlab.com/tobiaswkjeldsen/dartnissanconnect)-library by [@Tobiaswk](https://github.com/Tobiaswk).

⚠️ Supports the European NissanConnect Services API (`Region.EU`). Other regional APIs are not implemented or tested. This is not a client for the legacy NissanConnect EV/Carwings API.

---

## Example Program

[An example program can be found here.](https://github.com/hagronnestad/nissan-connect-dotnet/tree/main/NissanConnect/NissanConnectLib.Example/)

Set `Username` and `Password` in the example project's .NET user-secrets.
Keep `ForceBatteryStatusRefresh` set to `false` for read-only access; `true` sends
a remote battery-refresh action. The example saves tokens in `token.cache`;
treat that file as sensitive.

*Example program output:*

![Example program output](https://raw.githubusercontent.com/hagronnestad/nissan-connect-dotnet/main/Screenshots/03-example-output-3.png)


## Usage

Targets .NET 8; .NET 9 and .NET 10 applications can also consume the library.
`LogIn` uses MyNISSAN OneID authorization-code authentication with PKCE, then
exchanges the identity token for Kamereon API tokens.

Pass `ILogger<NissanConnectClient>` via `new NissanConnectClient(logger: logger)`
or dependency injection; logging is optional. Dispose manually created clients
when finished. The library does not log credentials, tokens, or vehicle IDs.

Set `AccessToken` to restore cached authentication and use `AccessTokenRefreshed`
to save renewed tokens. Automatic full-login fallback requires credentials from
a successful `LogIn`; assigning a different token clears remembered credentials.
`LogOut` clears local authentication only.

`GetCars` exposes `Car.Services` and the filtered `Car.ActivatedServices`.
`CarService.Feature` maps known IDs to `CarServiceFeature`; unknown IDs remain in
`CarService.Id`. For example, with `NissanConnectLib.Enums` imported:

```csharp
bool canStartCharging = car.ActivatedServices
    .Any(service => service.Feature == CarServiceFeature.ChargingStart);
```

Activated services are advertised capabilities, not guarantees that commands work.
They do not add controls to this library.

## Upgrade Notes

The public login and vehicle method signatures are retained. Legacy ForgeRock
response/callback models and `ConfigurationKey.ClientSecret`/`Realm` are removed;
code referencing them must be updated. `ConfigurationKey` numeric values also changed.

## More Information

### Website
https://heinandre.no/nissan-connect-dotnet/

### GitHub
https://github.com/hagronnestad/nissan-connect-dotnet


## Disclaimer
This library is not endorsed by, directly affiliated with, maintained, authorized, or sponsored by Nissan Motor Corporation. All product and company names are the registered trademarks of their original owners. The use of any trade name or trademark is for identification and reference purposes only and does not imply any association with the trademark holder of their product brand.
