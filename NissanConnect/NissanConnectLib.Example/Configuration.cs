namespace NissanConnectLib.Example;

internal class Configuration
{
    public string TokenCacheFile { get; set; } = "token.cache";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool ForceBatteryStatusRefresh { get; set; } = false;
}
