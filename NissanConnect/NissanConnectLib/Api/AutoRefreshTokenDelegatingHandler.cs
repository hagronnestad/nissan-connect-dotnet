using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;

namespace NissanConnectLib.Api;

internal sealed class AutoRefreshTokenDelegatingHandler : DelegatingHandler
{
    private readonly NissanConnectClient _nissanConnectClient;
    private readonly ILogger _logger;

    public AutoRefreshTokenDelegatingHandler(NissanConnectClient nissanConnectClient, ILogger logger)
    {
        _nissanConnectClient = nissanConnectClient;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var authentication = _nissanConnectClient.GetAuthenticationSnapshot();
        var accessToken = authentication.AccessToken;
        if (string.IsNullOrEmpty(accessToken))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var retryRequest = await HttpRequestCloner.CloneAsync(request, cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        _logger.LogDebug("Kamereon request returned HTTP 401; renewing authentication before retry.");
        try
        {
            var renewed = await _nissanConnectClient.RefreshAfterUnauthorized(
                accessToken, authentication.Version, cancellationToken);
            var currentAuthentication = _nissanConnectClient.GetAuthenticationSnapshot();
            if (currentAuthentication.Version != authentication.Version)
            {
                _logger.LogDebug("Authentication changed while the request was pending; not retrying.");
                return response;
            }

            if (!renewed || string.IsNullOrEmpty(currentAuthentication.AccessToken))
            {
                _logger.LogWarning("Kamereon request could not be retried because authentication was not renewed.");
                return response;
            }

            response.Dispose();
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", currentAuthentication.AccessToken);
            var retryResponse = await base.SendAsync(retryRequest, cancellationToken);
            if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("Kamereon request returned HTTP 401 after authentication renewal.");
            }

            return retryResponse;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}
