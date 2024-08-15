namespace NissanConnectLib;

public static class Configuration
{
    public enum Region
    {
        EU = 0,
    }

    public enum ConfigurationKey
    {
        ClientId,
        ClientSecret,
        Scope,
        AuthBaseUrl,
        Realm,
        RedirectUri,
        CarAdapterBaseUrl,
        UserAdapterBaseUrl,
        UserBaseUrl
    }

    public static Dictionary<Region, Dictionary<ConfigurationKey, string>> Settings = new()
    {
        {
            Region.EU, new Dictionary<ConfigurationKey, string>
            {
                { ConfigurationKey.ClientId, "a-ncb-nc-android-prod" },
                { ConfigurationKey.ClientSecret, "6GKIax7fGT5yPHuNmWNVOc4q5POBw1WRSW39ubRA8WPBmQ7MOxhm75EsmKMKENem" },
                { ConfigurationKey.Scope, "openid profile vehicles" },
                { ConfigurationKey.AuthBaseUrl, "https://prod.eu2.auth.kamereon.org/kauth" },
                { ConfigurationKey.Realm, "a-ncb-prod" },
                { ConfigurationKey.RedirectUri, "org.kamereon.service.nci:/oauth2redirect" },
                { ConfigurationKey.CarAdapterBaseUrl, "https://alliance-platform-caradapter-prod.apps.eu2.kamereon.io/car-adapter" },
                { ConfigurationKey.UserAdapterBaseUrl, "https://alliance-platform-usersadapter-prod.apps.eu2.kamereon.io/user-adapter" },
                { ConfigurationKey.UserBaseUrl, "https://nci-bff-web-prod.apps.eu2.kamereon.io/bff-web" }
            }
        }
    };
}
