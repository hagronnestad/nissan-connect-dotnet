using NissanConnectLib.Exceptions;
using NissanConnectLib.Models;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using static NissanConnectLib.Configuration;

namespace NissanConnectLib
{
    public class NissanConnectClient
    {
        private readonly JsonSerializerOptions _jsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

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

        private bool _isLoggedIn = false;
        private string? _token = null;

        /// <summary>
        /// Retrieves the current authentication token.
        /// </summary>
        public string? Token => _token;

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

            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Log in using an existing authentication token.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<bool> LogIn(string token)
        {
            _isLoggedIn = true;
            _token = token;
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_token}");

            var userId = await GetUserId();

            if (userId == null || string.IsNullOrWhiteSpace(userId))
            {
                LogOut();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Log in using a username and password.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="pass"></param>
        /// <returns></returns>
        public async Task<bool> LogIn(string user, string pass)
        {
            try
            {
                var ia = await InitAuthentication();
                if (ia == null) return false;

                var ali = await Authenticate(ia, user, pass);
                if (ali == null) return false;

                var code = await Authorize();
                if (code == null) return false;

                var token = await GetAccessToken(code);
                if (token == null) return false;

                _isLoggedIn = true;
                _token = token.AccessToken;
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_token}");

                return true;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return false;
            }
        }

        /// <summary>
        /// Log out. This is a client side log out only. This method DOES NOT log you out server side.
        /// </summary>
        public void LogOut()
        {
            _isLoggedIn = false;
            _token = null;
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
        }

        /// <summary>
        /// Gets the unique user id for the logged in user.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="NotLoggedInException"></exception>
        public async Task<string?> GetUserId()
        {
            if (!_isLoggedIn) throw new NotLoggedInException();

            var r = await _httpClient.GetAsync($"{_userAdapterBaseUrl}/v1/users/current");
            var res = await r.Content.ReadFromJsonAsync<UserIdResult>(_jsonSerializerOptions);
            return res?.UserId;
        }

        /// <summary>
        /// Gets a list of all the cars owned by the specified user.
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        /// <exception cref="NotLoggedInException"></exception>
        public async Task<List<Car>?> GetCars(string userId)
        {
            if (!_isLoggedIn) throw new NotLoggedInException();

            var r = await _httpClient.GetAsync($"{_userBaseUrl}/v5/users/{userId}/cars");
            var res = await r.Content.ReadFromJsonAsync<CarsResult>(_jsonSerializerOptions);
            return res?.Data;
        }

        /// <summary>
        /// Gets the battery status of the specified car.
        /// </summary>
        /// <param name="vin"></param>
        /// <returns></returns>
        /// <exception cref="NotLoggedInException"></exception>
        public async Task<BatteryStatusResult?> GetBatteryStatus(string vin)
        {
            if (!_isLoggedIn) throw new NotLoggedInException();

            var r = await _httpClient.GetAsync($"{_carAdapterBaseUrl}/v1/cars/{vin}/battery-status");
            var res = await r.Content.ReadFromJsonAsync<BatteryStatusResultData>(_jsonSerializerOptions);
            return res?.Data;
        }


        private async Task<AuthenticateAuthIdResponse?> InitAuthentication()
        {
            var r = await _httpClient.PostAsync($"{_authBaseUrl}/json/realms/root/realms/{_realm}/authenticate", null);
            var res = await r.Content.ReadFromJsonAsync<AuthenticateAuthIdResponse>(_jsonSerializerOptions);
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
            var res = await r.Content.ReadFromJsonAsync<AuthenticateTokenIdResponse>(_jsonSerializerOptions);
            return res;
        }

        private async Task<string?> Authorize()
        {
            var r = await _httpClient.GetAsync(
                $"{_authBaseUrl}/oauth2/{_realm}/authorize?client_id={_clientId}&redirect_uri={_redirectUri}&response_type=code&scope={_scope}&nonce=sdfdsfez&state=af0ifjsldkj");

            if (r.StatusCode == System.Net.HttpStatusCode.Found)
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

            var res = await r.Content.ReadFromJsonAsync<OAuthAccessTokenResult>(_jsonSerializerOptions);
            return res;
        }
    }
}
