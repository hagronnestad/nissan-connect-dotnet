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
        AuthBrand,
        AuthClient,
        AuthLocale,
        KamereonScope,
        CarAdapterBaseUrl,
        UserAdapterBaseUrl,
        UserBaseUrl
    }

    public static Dictionary<Region, Dictionary<ConfigurationKey, string>> Settings = new()
    {
        {
            Region.EU, new Dictionary<ConfigurationKey, string>
            {
                { ConfigurationKey.ClientId, "ZM3WK7ax1OtQKYQ8Qqzcv5VgiA8a" },
                { ConfigurationKey.ClientSecret, string.Empty },
                { ConfigurationKey.Scope, "openid name profile email offline_access" },
                { ConfigurationKey.AuthBaseUrl, "https://login.mynissan-account.com" },
                { ConfigurationKey.Realm, string.Empty },
                { ConfigurationKey.RedirectUri, "com://wso2.service.nci" },
                { ConfigurationKey.AuthBrand, "Nissan" },
                { ConfigurationKey.AuthClient, "mynissanapp" },
                { ConfigurationKey.AuthLocale, "en_GB" },
                { ConfigurationKey.KamereonScope, "openid profile vehicles" },
                { ConfigurationKey.CarAdapterBaseUrl, "https://alliance-platform-caradapter-prod.apps.eu2.kamereon.io/car-adapter" },
                { ConfigurationKey.UserAdapterBaseUrl, "https://alliance-platform-usersadapter-prod.apps.eu2.kamereon.io/user-adapter" },
                { ConfigurationKey.UserBaseUrl, "https://nci-bff-web-prod.apps.eu2.kamereon.io/bff-web" }
            }
        }
    };
}
