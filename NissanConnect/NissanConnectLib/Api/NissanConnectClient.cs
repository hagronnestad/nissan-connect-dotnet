using NissanConnectLib.Exceptions;
using NissanConnectLib.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Web;
using static NissanConnectLib.Configuration;

namespace NissanConnectLib.Api;

public class NissanConnectClient
{
    private readonly string _authBaseUrl;
    private readonly string _realm;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;
    private readonly string _scope;
    private readonly string _userAdapterBaseUrl;
    private readonly string _userBaseUrl;
    private readonly string _carAdapterBaseUrl;

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Retrieves the current authentication token.
    /// </summary>
    public OAuthAccessTokenResult? AccessToken { get; set; } = null;

    /// <summary>
    /// Creates a new <see cref="NissanConnectClient"> instance for the specified region.
    /// <see cref="Region.EU"/> is the default region.</cref>
    /// </summary>
    /// <param name="region"></param>
    public NissanConnectClient(Region region = Region.EU)
    {
        _authBaseUrl = Settings[region][ConfigurationKey.AuthBaseUrl];
        _realm = Settings[region][ConfigurationKey.Realm];
        _clientId = Settings[region][ConfigurationKey.ClientId];
        _clientSecret = Settings[region][ConfigurationKey.ClientSecret];
        _redirectUri = Settings[region][ConfigurationKey.RedirectUri];
        _scope = Settings[region][ConfigurationKey.Scope];
        _userAdapterBaseUrl = Settings[region][ConfigurationKey.UserAdapterBaseUrl];
        _userBaseUrl = Settings[region][ConfigurationKey.UserBaseUrl];
        _carAdapterBaseUrl = Settings[region][ConfigurationKey.CarAdapterBaseUrl];

        _httpClient = new HttpClient(new AutoRefreshTokenDelegatingHandler(this)
        {
            InnerHandler = new HttpClientHandler()
        });
    }

    /// <summary>
    /// Log in using a username and password.
    /// </summary>
    /// <param name="user"></param>
    /// <param name="pass"></param>
    /// <returns></returns>
    public async Task<bool> LogIn(string user, string pass)
    {
        var ia = await InitAuthentication();
        if (ia == null) throw new LogInException();

        var ali = await Authenticate(ia, user, pass);
        if (ali == null) throw new LogInException();

        var code = await Authorize();
        if (code == null) throw new LogInException();

        var token = await GetAccessToken(code);
        if (token == null) throw new LogInException();

        AccessToken = token;
        return true;
    }

    /// <summary>
    /// Log out. This is a client side log out only. This method DOES NOT log you out server side.
    /// </summary>
    public void LogOut()
    {
        AccessToken = null;
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
    }

    /// <summary>
    /// Gets the unique user id for the logged in user.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotLoggedInException"></exception>
    public async Task<string?> GetUserId()
    {
        var r = await _httpClient.GetFromJsonAsync<UserIdResult>($"{_userAdapterBaseUrl}/v1/users/current");
        return r?.UserId;
    }

    /// <summary>
    /// Gets a list of all the cars owned by the specified user.
    /// </summary>
    /// <param name="userId"></param>
    /// <returns></returns>
    /// <exception cref="NotLoggedInException"></exception>
    public async Task<List<Car>?> GetCars(string userId)
    {
        var r = await _httpClient.GetFromJsonAsync<CarsResult>($"{_userBaseUrl}/v5/users/{userId}/cars");
        return r?.Data;
    }

    /// <summary>
    /// Gets the battery status of the specified car.
    /// </summary>
    /// <param name="vin"></param>
    /// <returns></returns>
    /// <exception cref="NotLoggedInException"></exception>
    public async Task<AttributesBatteryStatus?> GetBatteryStatus(string vin, bool forceRefresh = false, TimeSpan? waitTime = null)
    {
        if (forceRefresh)
        {
            waitTime ??= TimeSpan.FromSeconds(30);
            await RefreshBatteryStatus(vin);
            await Task.Delay(waitTime.Value);
        }

        var r = await _httpClient.GetFromJsonAsync<ApiResult<AttributesBatteryStatus>>($"{_carAdapterBaseUrl}/v1/cars/{vin}/battery-status");
        return r?.Data?.Attributes;
    }

    /// <summary>
    /// Refresh the battery status of the specified car.
    /// </summary>
    /// <param name="vin"></param>
    /// <returns></returns>
    /// <exception cref="NotLoggedInException"></exception>
    public async Task<bool> RefreshBatteryStatus(string vin)
    {
        var data = new
        {
            data = new
            {
                type = "RefreshBatteryStatus"
            }
        };

        var r = await _httpClient.PostAsync($"{_carAdapterBaseUrl}/v1/cars/{vin}/actions/refresh-battery-status",
            JsonContent.Create(data, MediaTypeHeaderValue.Parse("application/vnd.api+json")));

        return r.IsSuccessStatusCode;
    }

    /// <summary>
    /// Gets the HVAC status of the specified car.
    /// </summary>
    /// <param name="vin"></param>
    /// <returns></returns>
    /// <exception cref="NotLoggedInException"></exception>
    public async Task<AttributesHvacStatus?> GetHvacStatus(string vin, bool forceRefresh = false, TimeSpan? waitTime = null)
    {
        if (forceRefresh)
        {
            waitTime ??= TimeSpan.FromSeconds(30);
            await RefreshHvacStatus(vin);
            await Task.Delay(waitTime.Value);
        }

        var r = await _httpClient.GetFromJsonAsync<ApiResult<AttributesHvacStatus>>($"{_carAdapterBaseUrl}/v1/cars/{vin}/hvac-status");
        return r?.Data?.Attributes;
    }

    /// <summary>
    /// Refresh the HVAC status of the specified car. (Untested!)
    /// </summary>
    /// <param name="vin"></param>
    /// <returns></returns>
    /// <exception cref="NotLoggedInException"></exception>
    public async Task<bool> RefreshHvacStatus(string vin)
    {
        var data = new
        {
            data = new
            {
                type = "RefreshHvacStatus"
            }
        };

        var r = await _httpClient.PostAsync($"{_carAdapterBaseUrl}/v1/cars/{vin}/actions/refresh-hvac-status",
            JsonContent.Create(data, MediaTypeHeaderValue.Parse("application/vnd.api+json")));

        return r.IsSuccessStatusCode;
    }

    /// <summary>
    /// Refresh the location of the specified car. (Untested!)
    /// </summary>
    /// <param name="vin"></param>
    /// <returns></returns>
    /// <exception cref="NotLoggedInException"></exception>
    public async Task<bool> RefreshLocation(string vin)
    {
        var data = new
        {
            data = new
            {
                type = "RefreshLocation"
            }
        };

        var r = await _httpClient.PostAsync($"{_carAdapterBaseUrl}/v1/cars/{vin}/actions/refresh-location",
            JsonContent.Create(data, MediaTypeHeaderValue.Parse("application/vnd.api+json")));

        return r.IsSuccessStatusCode;
    }

    /// <summary>
    /// Gets the cockpit status of the specified car.
    /// </summary>
    /// <param name="vin"></param>
    /// <returns></returns>
    /// <exception cref="NotLoggedInException"></exception>
    public async Task<AttributesCockpitStatus?> GetCockpitStatus(string vin)
    {
        var r = await _httpClient.GetFromJsonAsync<ApiResult<AttributesCockpitStatus>>($"{_carAdapterBaseUrl}/v1/cars/{vin}/cockpit");
        return r?.Data?.Attributes;
    }

    /// <summary>
    /// Wake up the specified car (vehicle gateway?).
    /// </summary>
    /// <param name="vin"></param>
    /// <returns></returns>
    /// <exception cref="NotLoggedInException"></exception>
    public async Task<bool> WakeUpVehicle(string vin)
    {
        var data = new
        {
            data = new
            {
                type = "WakeUpVehicle"
            }
        };

        var r = await _httpClient.PostAsync($"{_carAdapterBaseUrl}/v1/cars/{vin}/actions/wake-up-vehicle",
            JsonContent.Create(data, MediaTypeHeaderValue.Parse("application/vnd.api+json")));

        return r.IsSuccessStatusCode;
    }


    private async Task<AuthenticateAuthIdResponse?> InitAuthentication()
    {
        var r = await _httpClient.PostAsync($"{_authBaseUrl}/json/realms/root/realms/{_realm}/authenticate", null);
        var res = await r.Content.ReadFromJsonAsync<AuthenticateAuthIdResponse>();
        return res;
    }

    private async Task<AuthenticateTokenIdResponse?> Authenticate(AuthenticateAuthIdResponse initResponse, string user, string password)
    {
        var userInput = initResponse.Callbacks?.FirstOrDefault(x => x.Type == "NameCallback")?.Input?.First();
        var passwordInput = initResponse.Callbacks?.FirstOrDefault(x => x.Type == "PasswordCallback")?.Input?.First();

        if (userInput != null && passwordInput != null)
        {
            userInput.Value = user;
            passwordInput.Value = password;
        }

        var r = await _httpClient.PostAsJsonAsync($"{_authBaseUrl}/json/realms/root/realms/{_realm}/authenticate", initResponse);
        var res = await r.Content.ReadFromJsonAsync<AuthenticateTokenIdResponse>();
        return res;
    }

    private async Task<string?> Authorize()
    {
        var r = await _httpClient.GetAsync(
            $"{_authBaseUrl}/oauth2/{_realm}/authorize?client_id={_clientId}&redirect_uri={_redirectUri}&response_type=code&scope={_scope}&nonce=sdfdsfez&state=af0ifjsldkj");

        if (r.StatusCode == HttpStatusCode.Found)
        {
            var location = r.Headers.Location;
            if (location != null)
            {
                var oauthCode = HttpUtility.ParseQueryString(location.Query)["code"];
                return oauthCode;
            }
        }

        return null;
    }

    private async Task<OAuthAccessTokenResult?> GetAccessToken(string code)
    {
        var r = await _httpClient.PostAsync(
            $"{_authBaseUrl}/oauth2/{_realm}/access_token?code={code}&client_id={_clientId}&client_secret={_clientSecret}&redirect_uri={_redirectUri}&grant_type=authorization_code", null);

        var res = await r.Content.ReadFromJsonAsync<OAuthAccessTokenResult>();
        return res;
    }

    internal async Task<OAuthAccessTokenResult?> RefreshAccessToken(string refreshToken)
    {
        var data = new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "refresh_token", refreshToken },
            { "client_id", _clientId },
            { "client_secret", _clientSecret }
        };

        var r = await _httpClient.PostAsync($"{_authBaseUrl}/oauth2/{_realm}/access_token",
            new FormUrlEncodedContent(data));

        var res = await r.Content.ReadFromJsonAsync<OAuthAccessTokenResult>();
        return res;
    }
}
