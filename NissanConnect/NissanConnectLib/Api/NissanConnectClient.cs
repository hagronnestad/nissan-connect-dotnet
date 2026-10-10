using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NissanConnectLib.Exceptions;
using NissanConnectLib.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static NissanConnectLib.Configuration;

namespace NissanConnectLib.Api;

public class NissanConnectClient : IDisposable
{
    private const int MaxAuthenticationRedirects = 10;
    private const string HtmlAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    public event EventHandler<OAuthAccessTokenResult>? AccessTokenRefreshed;

    private readonly Uri _authBaseUri;
    private readonly Uri _redirectUri;
    private readonly string _redirectUriValue;
    private readonly string _clientId;
    private readonly string _scope;
    private readonly string _authBrand;
    private readonly string _authClient;
    private readonly string _authLocale;
    private readonly string _kamereonScope;
    private readonly string _userAdapterBaseUrl;
    private readonly string _userBaseUrl;
    private readonly string _carAdapterBaseUrl;
    private readonly ILogger<NissanConnectClient> _logger;
    private readonly HttpClient _tokenHttpClient;
    private readonly Func<HttpMessageHandler> _authenticationHandlerFactory;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _authenticationStateLock = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private OAuthAccessTokenResult? _accessToken;
    private long _authenticationVersion;
    private string? _user;
    private string? _password;
    private bool _disposed;

    /// <summary>
    /// Gets or sets the current authentication token. Assigning a different token
    /// clears credentials remembered from a previous login.
    /// </summary>
    public OAuthAccessTokenResult? AccessToken
    {
        get
        {
            lock (_authenticationStateLock)
            {
                return _accessToken;
            }
        }
        set
        {
            lock (_authenticationStateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (value is not null && ReferenceEquals(_accessToken, value))
                {
                    return;
                }

                _accessToken = value;
                _user = null;
                _password = null;
                _authenticationVersion++;
            }
        }
    }

    /// <summary>
    /// Creates a new <see cref="NissanConnectClient"/> instance for the specified region.
    /// <see cref="Region.EU"/> is the default region.
    /// </summary>
    public NissanConnectClient(Region region = Region.EU, ILogger<NissanConnectClient>? logger = default)
        : this(
            Settings[region],
            logger,
            () => new HttpClientHandler
            {
                AllowAutoRedirect = false
            },
            new HttpClientHandler())
    {
    }

    internal NissanConnectClient(
        IReadOnlyDictionary<ConfigurationKey, string> settings,
        ILogger<NissanConnectClient>? logger,
        Func<HttpMessageHandler> authenticationHandlerFactory,
        HttpMessageHandler apiHandler)
    {
        _authBaseUri = new Uri(settings[ConfigurationKey.AuthBaseUrl], UriKind.Absolute);
        _redirectUriValue = settings[ConfigurationKey.RedirectUri];
        _redirectUri = new Uri(_redirectUriValue, UriKind.Absolute);
        _clientId = settings[ConfigurationKey.ClientId];
        _scope = settings[ConfigurationKey.Scope];
        _authBrand = settings[ConfigurationKey.AuthBrand];
        _authClient = settings[ConfigurationKey.AuthClient];
        _authLocale = settings[ConfigurationKey.AuthLocale];
        _kamereonScope = settings[ConfigurationKey.KamereonScope];
        _userAdapterBaseUrl = settings[ConfigurationKey.UserAdapterBaseUrl];
        _userBaseUrl = settings[ConfigurationKey.UserBaseUrl];
        _carAdapterBaseUrl = settings[ConfigurationKey.CarAdapterBaseUrl];
        _logger = logger ?? NullLogger<NissanConnectClient>.Instance;

        _authenticationHandlerFactory = authenticationHandlerFactory;
        _tokenHttpClient = new HttpClient(authenticationHandlerFactory());
        _httpClient = new HttpClient(new AutoRefreshTokenDelegatingHandler(this, _logger)
        {
            InnerHandler = apiHandler
        });
    }

    internal virtual void OnAccessTokenRefreshed(OAuthAccessTokenResult e)
    {
        AccessTokenRefreshed?.Invoke(this, e);
    }

    /// <summary>
    /// Log in using a username and password.
    /// </summary>
    public async Task<bool> LogIn(string user, string pass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        ArgumentException.ThrowIfNullOrEmpty(pass);

        long version;
        lock (_authenticationStateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            version = _authenticationVersion;
        }

        _logger.LogDebug("Starting Nissan OneID login.");
        try
        {
            var token = await LogInCore(user, pass, CancellationToken.None);
            lock (_authenticationStateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (version != _authenticationVersion)
                {
                    throw new LogInException("Nissan login was superseded by another authentication change.");
                }

                _accessToken = token;
                _user = user;
                _password = pass;
                _authenticationVersion++;
            }
        }
        catch (LogInException exception)
        {
            _logger.LogWarning("Nissan OneID login failed: {Reason}", exception.Message);
            throw;
        }
        catch (HttpRequestException exception)
        {
            if (exception.StatusCode is { } status)
            {
                _logger.LogWarning("Nissan OneID login failed with HTTP status {StatusCode}.", (int)status);
            }
            else
            {
                _logger.LogWarning("Nissan OneID login failed due to a transport error.");
            }

            throw;
        }

        _logger.LogInformation("Nissan OneID login completed.");
        return true;
    }

    /// <summary>
    /// Log out. This is a client side log out only. This method DOES NOT log you out server side.
    /// </summary>
    public void LogOut()
    {
        lock (_authenticationStateLock)
        {
            _accessToken = null;
            _user = null;
            _password = null;
            _authenticationVersion++;
        }

        _logger.LogInformation("Nissan client logged out locally.");
    }

    /// <summary>
    /// Releases the HTTP clients and handlers owned by this client.
    /// </summary>
    public void Dispose()
    {
        lock (_authenticationStateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _accessToken = null;
            _user = null;
            _password = null;
            _authenticationVersion++;
        }

        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
        _tokenHttpClient.Dispose();
        _httpClient.Dispose();
        // An in-flight refresh may still need to release this managed semaphore.
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets the unique user id for the logged in user.
    /// </summary>
    public async Task<string?> GetUserId()
    {
        var r = await _httpClient.GetFromJsonAsync<UserIdResult>($"{_userAdapterBaseUrl}/v1/users/current");
        _logger.LogDebug("Current Nissan user request completed.");
        return r?.UserId;
    }

    /// <summary>
    /// Gets a list of all the cars owned by the specified user.
    /// </summary>
    public async Task<List<Car>?> GetCars(string userId)
    {
        var r = await _httpClient.GetFromJsonAsync<CarsResult>(
            $"{_userBaseUrl}/v5/users/{Uri.EscapeDataString(userId)}/cars");
        _logger.LogDebug("Nissan vehicle list request completed.");
        return r?.Data;
    }

    /// <summary>
    /// Gets the battery status of the specified car.
    /// </summary>
    public async Task<AttributesBatteryStatus?> GetBatteryStatus(
        string vin,
        bool forceRefresh = false,
        TimeSpan? waitTime = null)
    {
        if (forceRefresh)
        {
            waitTime ??= TimeSpan.FromSeconds(30);
            await RefreshBatteryStatus(vin);
            await Task.Delay(waitTime.Value);
        }

        var r = await _httpClient.GetFromJsonAsync<ApiResult<AttributesBatteryStatus>>(
            $"{_carAdapterBaseUrl}/v1/cars/{Uri.EscapeDataString(vin)}/battery-status");
        _logger.LogDebug("Vehicle battery status request completed.");
        return r?.Data?.Attributes;
    }

    /// <summary>
    /// Refresh the battery status of the specified car.
    /// </summary>
    public Task<bool> RefreshBatteryStatus(string vin) =>
        PostVehicleAction(vin, "refresh-battery-status", "RefreshBatteryStatus");

    /// <summary>
    /// Gets the HVAC status of the specified car.
    /// </summary>
    public async Task<AttributesHvacStatus?> GetHvacStatus(
        string vin,
        bool forceRefresh = false,
        TimeSpan? waitTime = null)
    {
        if (forceRefresh)
        {
            waitTime ??= TimeSpan.FromSeconds(30);
            await RefreshHvacStatus(vin);
            await Task.Delay(waitTime.Value);
        }

        var r = await _httpClient.GetFromJsonAsync<ApiResult<AttributesHvacStatus>>(
            $"{_carAdapterBaseUrl}/v1/cars/{Uri.EscapeDataString(vin)}/hvac-status");
        _logger.LogDebug("Vehicle HVAC status request completed.");
        return r?.Data?.Attributes;
    }

    /// <summary>
    /// Refresh the HVAC status of the specified car. (Untested!)
    /// </summary>
    public Task<bool> RefreshHvacStatus(string vin) =>
        PostVehicleAction(vin, "refresh-hvac-status", "RefreshHvacStatus");

    /// <summary>
    /// Refresh the location of the specified car. (Untested!)
    /// </summary>
    public Task<bool> RefreshLocation(string vin) =>
        PostVehicleAction(vin, "refresh-location", "RefreshLocation");

    /// <summary>
    /// Gets the cockpit status of the specified car.
    /// </summary>
    public async Task<AttributesCockpitStatus?> GetCockpitStatus(string vin)
    {
        var r = await _httpClient.GetFromJsonAsync<ApiResult<AttributesCockpitStatus>>(
            $"{_carAdapterBaseUrl}/v1/cars/{Uri.EscapeDataString(vin)}/cockpit");
        _logger.LogDebug("Vehicle cockpit status request completed.");
        return r?.Data?.Attributes;
    }

    /// <summary>
    /// Wake up the specified car (vehicle gateway?).
    /// </summary>
    public Task<bool> WakeUpVehicle(string vin) =>
        PostVehicleAction(vin, "wake-up-vehicle", "WakeUpVehicle");

    private async Task<bool> PostVehicleAction(string vin, string path, string action)
    {
        using var content = JsonContent.Create(
            new { data = new { type = action } },
            MediaTypeHeaderValue.Parse("application/vnd.api+json"));
        using var r = await _httpClient.PostAsync(
            $"{_carAdapterBaseUrl}/v1/cars/{Uri.EscapeDataString(vin)}/actions/{path}", content);

        LogVehicleActionResult(action, r.StatusCode);
        return r.IsSuccessStatusCode;
    }

    private void LogVehicleActionResult(string action, HttpStatusCode statusCode)
    {
        if ((int)statusCode is >= 200 and < 300)
        {
            _logger.LogInformation("Vehicle action {Action} accepted with HTTP status {StatusCode}.",
                action, (int)statusCode);
        }
        else
        {
            _logger.LogWarning("Vehicle action {Action} failed with HTTP status {StatusCode}.",
                action, (int)statusCode);
        }
    }

    internal (long Version, string? AccessToken) GetAuthenticationSnapshot()
    {
        lock (_authenticationStateLock)
        {
            return (_authenticationVersion, _accessToken?.AccessToken);
        }
    }

    internal async Task<bool> RefreshAfterUnauthorized(
        string failedAccessToken,
        long version,
        CancellationToken cancellationToken)
    {
        OAuthAccessTokenResult refreshedToken;
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            string? refreshToken;
            string? user;
            string? password;
            lock (_authenticationStateLock)
            {
                if (_disposed || version != _authenticationVersion)
                {
                    return false;
                }

                if (_accessToken?.AccessToken is { } currentAccessToken &&
                    !string.Equals(currentAccessToken, failedAccessToken, StringComparison.Ordinal))
                {
                    return true;
                }

                refreshToken = _accessToken?.RefreshToken;
                user = _user;
                password = _password;
            }

            OAuthAccessTokenResult? token = null;
            if (!string.IsNullOrEmpty(refreshToken))
            {
                try
                {
                    token = await RefreshAccessToken(refreshToken, cancellationToken);
                }
                catch (HttpRequestException exception)
                {
                    if (exception.StatusCode is { } status)
                    {
                        _logger.LogWarning(
                            "Kamereon token refresh failed with HTTP status {StatusCode}; attempting a full login.",
                            (int)status);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Kamereon token refresh failed due to a transport error; attempting a full login.");
                    }
                }
                catch (Exception exception) when (
                    exception is JsonException or InvalidOperationException)
                {
                    _logger.LogWarning("Kamereon token refresh returned an invalid response; attempting a full login.");
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Kamereon token refresh timed out; attempting a full login.");
                }
            }

            if (token is null && user is not null && password is not null)
            {
                lock (_authenticationStateLock)
                {
                    if (_disposed || version != _authenticationVersion)
                    {
                        return false;
                    }
                }

                token = await LogInCore(user, password, cancellationToken);
            }

            if (token is null)
            {
                _logger.LogWarning("Kamereon authentication could not be renewed; no login credentials are available.");
                return false;
            }

            lock (_authenticationStateLock)
            {
                if (_disposed || version != _authenticationVersion)
                {
                    return false;
                }

                _accessToken = token;
                refreshedToken = token;
            }
        }
        finally
        {
            _refreshLock.Release();
        }

        lock (_authenticationStateLock)
        {
            if (_disposed || version != _authenticationVersion)
            {
                return false;
            }
        }

        OnAccessTokenRefreshed(refreshedToken);
        _logger.LogInformation("Kamereon authentication renewed.");
        return true;
    }

    private async Task<OAuthAccessTokenResult> LogInCore(
        string user,
        string pass,
        CancellationToken cancellationToken)
    {
        CancellationToken disposeToken;
        lock (_authenticationStateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            disposeToken = _disposeCancellation.Token;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, disposeToken);
        cancellationToken = linkedCancellation.Token;
        using var authenticationClient = new HttpClient(_authenticationHandlerFactory());
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        var authorizationUri = BuildUri(
            new Uri(_authBaseUri, "/oauth2/authorize"),
            new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["redirect_uri"] = _redirectUriValue,
                ["client_id"] = _clientId,
                ["state"] = state,
                ["scope"] = _scope,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["locale"] = _authLocale,
                ["brand"] = _authBrand,
                ["client"] = _authClient
            });

        using var authorizationRequest = new HttpRequestMessage(HttpMethod.Get, authorizationUri);
        authorizationRequest.Headers.TryAddWithoutValidation("Accept", HtmlAccept);
        using var authorizationResult = await SendFollowingAuthenticationRedirects(
            authenticationClient,
            authorizationRequest,
            allowCallback: false,
            cancellationToken);
        EnsureAuthenticationSuccess(authorizationResult.Response, "load the Nissan login page");

        var loginForm = await ParseLoginForm(
            authorizationResult.Response,
            authorizationResult.FinalUri,
            cancellationToken)
            ?? throw new LogInException("Nissan login page did not contain the expected login form.");

        if (!IsAuthenticationTarget(loginForm.Action))
        {
            throw new LogInException("Nissan login form targeted an unexpected host.");
        }

        loginForm.Fields["userName"] = user;
        loginForm.Fields["username"] = loginForm.Fields.TryGetValue("regionCode", out var regionCode) &&
                                       !string.IsNullOrEmpty(regionCode)
            ? $"{regionCode}/{user}"
            : user;
        loginForm.Fields["password"] = pass;

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginForm.Action)
        {
            Content = new FormUrlEncodedContent(loginForm.Fields)
        };
        loginRequest.Headers.TryAddWithoutValidation("Accept", HtmlAccept);
        loginRequest.Headers.Referrer = authorizationResult.FinalUri;
        loginRequest.Headers.TryAddWithoutValidation(
            "Origin",
            $"{loginForm.Action.Scheme}://{loginForm.Action.Authority}");

        using var loginResult = await SendFollowingAuthenticationRedirects(
            authenticationClient,
            loginRequest,
            allowCallback: true,
            cancellationToken);

        if (loginResult.Callback is null)
        {
            if (loginResult.Response.IsSuccessStatusCode &&
                await ParseLoginForm(loginResult.Response, loginResult.FinalUri, cancellationToken) is not null)
            {
                throw new LogInException("Invalid Nissan username or password.");
            }

            throw new LogInException(
                "Nissan login did not complete. Check MyNISSAN for pending terms, consent, or verification prompts.");
        }

        var callbackParameters = ParseQuery(loginResult.Callback.Query);
        if (!callbackParameters.TryGetValue("state", out var callbackState) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state),
                Encoding.UTF8.GetBytes(callbackState)))
        {
            throw new LogInException("Invalid Nissan OAuth state.");
        }

        if (!callbackParameters.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
        {
            throw new LogInException("Nissan login did not return an authorization code.");
        }

        var oneIdToken = await ExchangeAuthorizationCode(authenticationClient, code, verifier, cancellationToken);
        if (string.IsNullOrEmpty(oneIdToken.IdToken))
        {
            throw new LogInException("Nissan OneID token response did not include an ID token.");
        }

        return await ExchangeIdentityToken(oneIdToken.IdToken, cancellationToken);
    }

    private async Task<OAuthAccessTokenResult> ExchangeAuthorizationCode(
        HttpClient authenticationClient,
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_authBaseUri, "/oauth2/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["redirect_uri"] = _redirectUriValue,
                ["grant_type"] = "authorization_code",
                ["client_id"] = _clientId,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["scope"] = _scope
            })
        };
        using var response = await authenticationClient.SendAsync(request, cancellationToken);
        return await ReadTokenResponse(response, "Nissan OneID token", cancellationToken);
    }

    private async Task<OAuthAccessTokenResult> ExchangeIdentityToken(
        string idToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateKamereonTokenRequest(
            $"{_userBaseUrl}/v1/oauth2/access_token?platform=Android",
            idToken,
            content: new ByteArrayContent(Array.Empty<byte>()));
        using var response = await _tokenHttpClient.SendAsync(request, cancellationToken);
        return await ReadTokenResponse(response, "Kamereon access token", cancellationToken);
    }

    private async Task<OAuthAccessTokenResult> RefreshAccessToken(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var content = JsonContent.Create(new { scope = _kamereonScope });
        using var request = CreateKamereonTokenRequest(
            $"{_userBaseUrl}/v1/oauth2/refresh-token?platform=Android",
            refreshToken,
            content);
        using var response = await _tokenHttpClient.SendAsync(request, cancellationToken);
        var token = await ReadTokenResponse(response, "Kamereon refresh token", cancellationToken);
        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            token.RefreshToken = refreshToken;
        }

        return token;
    }

    private static HttpRequestMessage CreateKamereonTokenRequest(
        string uri,
        string authorization,
        HttpContent content)
    {
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/vnd.api+json");
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<AuthNavigationResult> SendFollowingAuthenticationRedirects(
        HttpClient authenticationClient,
        HttpRequestMessage initialRequest,
        bool allowCallback,
        CancellationToken cancellationToken)
    {
        var request = await HttpRequestCloner.CloneAsync(initialRequest, cancellationToken);
        try
        {
            for (var redirectCount = 0; redirectCount <= MaxAuthenticationRedirects; redirectCount++)
            {
                var requestUri = request.RequestUri!;
                var response = await authenticationClient.SendAsync(request, cancellationToken);
                if (!IsRedirect(response.StatusCode) || response.Headers.Location is null)
                {
                    return new AuthNavigationResult(response, requestUri, null);
                }

                HttpRequestMessage nextRequest;
                try
                {
                    var target = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(requestUri, response.Headers.Location);

                    if (allowCallback && IsCallbackTarget(target))
                    {
                        return new AuthNavigationResult(response, target, target);
                    }

                    if (!IsAuthenticationTarget(target))
                    {
                        throw new LogInException("Nissan login redirected to an unexpected host.");
                    }

                    nextRequest = await CreateRedirectRequest(
                        request, response.StatusCode, target, cancellationToken);
                }
                catch
                {
                    response.Dispose();
                    throw;
                }

                request.Dispose();
                response.Dispose();
                request = nextRequest;
            }

            throw new LogInException("Nissan login exceeded the redirect limit.");
        }
        finally
        {
            request.Dispose();
        }
    }

    private static async Task<HttpRequestMessage> CreateRedirectRequest(
        HttpRequestMessage request,
        HttpStatusCode statusCode,
        Uri target,
        CancellationToken cancellationToken)
    {
        var preserveMethod = statusCode is HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
        var redirectedRequest = await HttpRequestCloner.CloneAsync(
            request, cancellationToken, includeContent: preserveMethod);
        redirectedRequest.RequestUri = target;
        if (!preserveMethod)
        {
            redirectedRequest.Method = HttpMethod.Get;
            redirectedRequest.Headers.Remove("Origin");
        }

        return redirectedRequest;
    }

    private static async Task<LoginForm?> ParseLoginForm(
        HttpResponseMessage response,
        Uri pageUri,
        CancellationToken cancellationToken)
    {
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = await new HtmlParser().ParseDocumentAsync(html, cancellationToken);

        foreach (var form in document.QuerySelectorAll("form"))
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var input in form.QuerySelectorAll("input[name]"))
            {
                fields[input.GetAttribute("name")!] = input.GetAttribute("value") ?? string.Empty;
            }

            if (!fields.ContainsKey("sessionDataKey") || !fields.ContainsKey("password"))
            {
                continue;
            }

            var action = form.GetAttribute("action") ?? string.Empty;
            return new LoginForm(new Uri(pageUri, action), fields);
        }

        return null;
    }

    private static async Task<OAuthAccessTokenResult> ReadTokenResponse(
        HttpResponseMessage response,
        string tokenName,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Unable to obtain {tokenName}; server returned {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        var token = await response.Content.ReadFromJsonAsync<OAuthAccessTokenResult>(
            cancellationToken: cancellationToken);
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
        {
            throw new InvalidOperationException($"Unable to obtain {tokenName}.");
        }

        if (token.ExpiresIn <= 0)
        {
            token.ExpiresIn = 3600;
        }

        token.TokenType ??= "Bearer";
        return token;
    }

    private static void EnsureAuthenticationSuccess(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new LogInException(
                $"Unable to {operation}; server returned {(int)response.StatusCode}.");
        }
    }

    private bool IsAuthenticationTarget(Uri uri)
    {
        return string.Equals(uri.Scheme, _authBaseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.Host, _authBaseUri.Host, StringComparison.OrdinalIgnoreCase) &&
               uri.Port == _authBaseUri.Port;
    }

    private bool IsCallbackTarget(Uri uri)
    {
        return string.Equals(uri.Scheme, _redirectUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(uri.Host, _redirectUri.Host, StringComparison.OrdinalIgnoreCase) &&
               uri.Port == _redirectUri.Port;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static Uri BuildUri(Uri baseUri, IReadOnlyDictionary<string, string> parameters)
    {
        var query = string.Join(
            "&",
            parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        var builder = new UriBuilder(baseUri)
        {
            Query = query
        };
        return builder.Uri;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0].Replace('+', ' ')),
                part => part.Length > 1
                    ? Uri.UnescapeDataString(part[1].Replace('+', ' '))
                    : string.Empty,
                StringComparer.Ordinal);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record LoginForm(Uri Action, Dictionary<string, string> Fields);

    private sealed record AuthNavigationResult(
        HttpResponseMessage Response,
        Uri FinalUri,
        Uri? Callback) : IDisposable
    {
        public void Dispose()
        {
            Response.Dispose();
        }
    }
}
