using System.Net.Http.Headers;
using System.Net;
using NissanConnectLib.Exceptions;

namespace NissanConnectLib.Api;

internal class AutoRefreshTokenDelegatingHandler : DelegatingHandler
{
    private readonly NissanConnectClient _nissanConnectClient;

    public AutoRefreshTokenDelegatingHandler(NissanConnectClient nissanConnectClient)
    {
        _nissanConnectClient = nissanConnectClient;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            // Pass the request immediately if we don't have an access token 
            if (_nissanConnectClient.AccessToken is null)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            // Set the bearer token
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _nissanConnectClient.AccessToken.AccessToken);

            // Try to make the request
            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {

                // The request failed, let's try to refresh our access token
                if (_nissanConnectClient.AccessToken.RefreshToken is not null)
                {
                    // Refresh token
                    var newToken = await _nissanConnectClient.RefreshAccessToken(_nissanConnectClient.AccessToken.RefreshToken);
                    if (newToken is null) return response;

                    _nissanConnectClient.AccessToken = newToken;
                }

                // Try to make the request again
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _nissanConnectClient.AccessToken.AccessToken);
                response = await base.SendAsync(request, cancellationToken);
            }

            return response;
        }
        catch
        {
            throw new NotLoggedInException();
        }
    }
}
