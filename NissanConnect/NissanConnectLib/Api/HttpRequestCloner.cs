namespace NissanConnectLib.Api;

internal static class HttpRequestCloner
{
    public static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        bool includeContent = true)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };
        try
        {
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var option in request.Options)
            {
                clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
            }

            if (includeContent && request.Content is not null)
            {
                var content = new ByteArrayContent(
                    await request.Content.ReadAsByteArrayAsync(cancellationToken));
                clone.Content = content;
                foreach (var header in request.Content.Headers)
                {
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
        catch
        {
            clone.Dispose();
            throw;
        }
    }
}
