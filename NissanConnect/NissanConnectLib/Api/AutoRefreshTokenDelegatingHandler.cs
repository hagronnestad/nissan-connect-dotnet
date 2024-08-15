using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net;

namespace NissanConnectLib.Api;

internal class AutoRefreshTokenDelegatingHandler : DelegatingHandler
{
    private readonly NissanConnectClient _nissanConnectClient;
    private readonly ILogger _logger;

    public AutoRefreshTokenDelegatingHandler(NissanConnectClient nissanConnectClient, ILogger logger)
    {
        _nissanConnectClient = nissanConnectClient;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogTrace($"{nameof(AutoRefreshTokenDelegatingHandler)}: {nameof(SendAsync)}");

            // Pass the request immediately if we don't have an access token 
            if (_nissanConnectClient.AccessToken is null)
            {
                _logger.LogTrace("AccessToken is null, passing request immediately");
                return await base.SendAsync(request, cancellationToken);
            }

            // Add the bearer token to the request
            _logger.LogDebug("Adding bearer token to request");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _nissanConnectClient.AccessToken.AccessToken);

            // Try to make the request
            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Request failed with status code Unauthorized");

                // Check if we have a refresh token
                if (_nissanConnectClient.AccessToken.RefreshToken is not null)
                {
                    // Refresh token
                    _logger.LogInformation("Trying to refresh access token");
                    var newToken = await _nissanConnectClient.RefreshAccessToken(_nissanConnectClient.AccessToken.RefreshToken);
                    if (newToken is null) return response;

                    _logger.LogInformation("Refreshed access token");

                    // Update the access token, the refreshed access token doesn't have a refresh token,
                    // so we can't set AccessToken directly (_nissanConnectClient.AccessToken = newToken)
                    _nissanConnectClient.AccessToken.AccessToken = newToken.AccessToken;
                    _nissanConnectClient.AccessToken.IdToken = newToken.IdToken;
                    _nissanConnectClient.AccessToken.ExpiresIn = newToken.ExpiresIn;

                    // Notify the client that the access token has been refreshed
                    _nissanConnectClient.OnAccessTokenRefreshed(_nissanConnectClient.AccessToken);

                    // Try to make the request again
                    _logger.LogDebug("Send the request again with the new access token");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _nissanConnectClient.AccessToken.AccessToken);
                    response = await base.SendAsync(request, cancellationToken);
                }
            }

            return response;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while sending request");
            throw;
        }
    }
}
