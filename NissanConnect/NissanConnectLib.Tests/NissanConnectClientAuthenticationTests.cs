using NissanConnectLib.Api;
using NissanConnectLib.Exceptions;
using NissanConnectLib.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static NissanConnectLib.Configuration;

namespace NissanConnectLib.Tests;

public class NissanConnectClientAuthenticationTests
{
    [Fact]
    public async Task LogIn_FollowsRedirectsPreservesFormAndUsesPkce()
    {
        var authentication = new ScriptedHandler();
        var api = new ScriptedHandler();
        QueueSuccessfulLogin(authentication, "kamereon-access", "kamereon-refresh");
        var client = CreateClient(authentication, api);

        var loggedIn = await client.LogIn("driver@example.com", "correct-password");

        Assert.True(loggedIn);
        Assert.Equal("kamereon-access", client.AccessToken?.AccessToken);
        Assert.Equal("kamereon-refresh", client.AccessToken?.RefreshToken);
        Assert.Empty(authentication.PendingResponses);
    }

    [Fact]
    public async Task LogIn_NativeHandlerUsesFreshCookieSessionsForLoginAndFallback()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        string? state = null;
        string? loginPageCookie = null;
        string? submittedCookie = null;
        string? redirectCookie = null;
        string? exchangeCookie = null;
        var authorizationCookies = new List<string>();
        var submittedUsers = new List<string>();

        server.MapGet("/oauth2/authorize", context =>
        {
            state = context.Request.Query["state"].ToString();
            var cookie = context.Request.Headers.Cookie.ToString();
            authorizationCookies.Add(cookie);
            if (cookie.Contains("SSO=session", StringComparison.Ordinal))
            {
                context.Response.Redirect($"com://wso2.service.nci?code=sso-code&state={state}");
                return Task.CompletedTask;
            }

            context.Response.Headers.Append("Set-Cookie", "JSESSIONID=initial; Path=/; HttpOnly");
            context.Response.Redirect("/authenticationendpoint/login.do");
            return Task.CompletedTask;
        });
        server.MapGet("/authenticationendpoint/login.do", async context =>
        {
            loginPageCookie = context.Request.Headers.Cookie.ToString();
            context.Response.Headers.Append("Set-Cookie", "JSESSIONID=updated; Path=/; HttpOnly");
            context.Response.Headers.Append("Set-Cookie", "AFFINITY=server; Path=/; HttpOnly");
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(LoginFormHtml);
        });
        server.MapPost("/commonauth", async context =>
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, 1024, leaveOpen: true);
            var form = Form(await reader.ReadToEndAsync());
            submittedUsers.Add(form["userName"]);
            submittedCookie = context.Request.Headers.Cookie.ToString();
            context.Response.Redirect("/oauth2/resume");
        });
        server.MapGet("/oauth2/resume", context =>
        {
            redirectCookie = context.Request.Headers.Cookie.ToString();
            context.Response.Headers.Append("Set-Cookie", "SSO=session; Path=/; HttpOnly");
            context.Response.Redirect($"com://wso2.service.nci?code=authorization-code&state={state}");
            return Task.CompletedTask;
        });
        server.MapPost("/oauth2/token", async context =>
        {
            exchangeCookie = context.Request.Headers.Cookie.ToString();
            await context.Response.WriteAsJsonAsync(new
            {
                access_token = "oneid-access",
                id_token = "oneid-id-token"
            });
        });
        server.MapPost("/bff-web/v1/oauth2/access_token", context =>
            context.Response.WriteAsJsonAsync(new
            {
                access_token = $"kamereon-access-{submittedUsers.Count}",
                refresh_token = "kamereon-refresh"
            }));
        server.MapPost("/bff-web/v1/oauth2/refresh-token", context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });
        await server.StartAsync();

        var baseUrl = server.Urls.Single();
        var settings = Settings[Region.EU].ToDictionary(entry => entry.Key, entry => entry.Value);
        settings[ConfigurationKey.AuthBaseUrl] = baseUrl;
        settings[ConfigurationKey.UserBaseUrl] = $"{baseUrl}/bff-web";
        var api = new ScriptedHandler();
        api.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        api.Enqueue(_ => JsonResponse("""{"userId":"user-123"}"""));
        using var client = new NissanConnectClient(settings, null,
            () => new HttpClientHandler { AllowAutoRedirect = false }, api);

        Assert.True(await client.LogIn("driver@example.com", "correct-password"));
        Assert.Equal("kamereon-access-1", client.AccessToken?.AccessToken);
        Assert.Contains("JSESSIONID=initial", loginPageCookie);
        foreach (var cookie in new[] { submittedCookie, redirectCookie, exchangeCookie })
        {
            Assert.Contains("JSESSIONID=updated", cookie);
            Assert.Contains("AFFINITY=server", cookie);
            Assert.DoesNotContain("JSESSIONID=initial", cookie);
        }

        Assert.Contains("SSO=session", exchangeCookie);
        Assert.True(await client.LogIn("other-driver@example.com", "another-password"));
        client.LogOut();
        Assert.True(await client.LogIn("driver@example.com", "correct-password"));
        Assert.Equal("user-123", await client.GetUserId());
        Assert.Equal("kamereon-access-4", client.AccessToken?.AccessToken);
        Assert.Equal(new[] { "driver@example.com", "other-driver@example.com",
            "driver@example.com", "driver@example.com" }, submittedUsers);
        Assert.Equal(4, authorizationCookies.Count);
        Assert.All(authorizationCookies, cookie => Assert.Empty(cookie));
    }

    [Fact]
    public void PublicConstructor_AcceptsLoggerDirectlyAndViaDependencyInjection()
    {
        var logger = new RecordingLogger();
        using var directClient = new NissanConnectClient(logger: logger);
        using var services = new ServiceCollection()
            .AddSingleton<ILogger<NissanConnectClient>>(logger)
            .AddSingleton<NissanConnectClient>()
            .BuildServiceProvider();

        services.GetRequiredService<NissanConnectClient>().LogOut();
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Information &&
            entry.Message == "Nissan client logged out locally.");
    }

    [Fact]
    public void Dispose_ReleasesBothOwnedHttpHandlersAndClearsTokens()
    {
        var authentication = new ScriptedHandler();
        var api = new ScriptedHandler();
        var client = new NissanConnectClient(Settings[Region.EU], null, () => authentication, api);
        client.AccessToken = new OAuthAccessTokenResult
        {
            AccessToken = "access-token",
            RefreshToken = "refresh-token"
        };

        client.Dispose();
        client.Dispose();

        Assert.True(authentication.IsDisposed);
        Assert.True(api.IsDisposed);
        Assert.Null(client.AccessToken);
    }

    [Fact]
    public async Task LogIn_UsesSuppliedLoggerWithoutLoggingCredentialsOrTokens()
    {
        var authentication = new ScriptedHandler();
        QueueSuccessfulLogin(authentication, "kamereon-access", "kamereon-refresh");
        var logger = new RecordingLogger();
        using var client = CreateClient(authentication, new ScriptedHandler(), logger);

        await client.LogIn("driver@example.com", "correct-password");
        client.LogOut();

        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Information &&
            entry.Message == "Nissan OneID login completed.");
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Information &&
            entry.Message == "Nissan client logged out locally.");
        Assert.DoesNotContain(logger.Entries, entry =>
            new[] { "driver@example.com", "correct-password", "kamereon-access",
                "kamereon-refresh", "oneid-id-token" }
                .Any(secret => entry.Message.Contains(secret, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LogIn_RejectsInvalidCredentialsWithoutExchangingTokens()
    {
        var authentication = new ScriptedHandler();
        authentication.Enqueue(request =>
        {
            Assert.Equal("/oauth2/authorize", request.Uri.AbsolutePath);
            return HtmlResponse(LoginFormHtml);
        });
        authentication.Enqueue(request =>
        {
            Assert.Equal("/commonauth", request.Uri.AbsolutePath);
            return HtmlResponse(LoginFormHtml);
        });
        var logger = new RecordingLogger();
        using var client = CreateClient(authentication, new ScriptedHandler(), logger);

        var exception = await Assert.ThrowsAsync<LogInException>(
            () => client.LogIn("driver@example.com", "wrong-password"));

        Assert.Contains("Invalid Nissan username or password", exception.Message);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("Invalid Nissan username or password", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("wrong-password", StringComparison.Ordinal) ||
            entry.Message.Contains("driver@example.com", StringComparison.Ordinal));
        Assert.Empty(authentication.PendingResponses);
        Assert.Null(client.AccessToken);
    }

    [Fact]
    public async Task LogIn_RejectsMismatchedOAuthState()
    {
        var authentication = new ScriptedHandler();
        authentication.Enqueue(_ => HtmlResponse(LoginFormHtml));
        authentication.Enqueue(_ => RedirectResponse("com://wso2.service.nci?code=auth-code&state=wrong-state"));
        var client = CreateClient(authentication, new ScriptedHandler());

        var exception = await Assert.ThrowsAsync<LogInException>(
            () => client.LogIn("driver@example.com", "correct-password"));

        Assert.Contains("OAuth state", exception.Message);
        Assert.Empty(authentication.PendingResponses);
    }

    [Fact]
    public async Task LogIn_RejectsUnexpectedFormTarget()
    {
        const string html = """
            <html><body>
              <form action="https://attacker.example/commonauth">
                <input type="hidden" name="sessionDataKey" value="session-key">
                <input type="password" name="password">
              </form>
            </body></html>
            """;
        var authentication = new ScriptedHandler();
        authentication.Enqueue(_ => HtmlResponse(html));
        var client = CreateClient(authentication, new ScriptedHandler());

        var exception = await Assert.ThrowsAsync<LogInException>(
            () => client.LogIn("driver@example.com", "correct-password"));

        Assert.Contains("unexpected host", exception.Message);
        Assert.Empty(authentication.PendingResponses);
    }

    [Fact]
    public async Task UnauthorizedRequest_RefreshesAndPreservesRotatedRefreshToken()
    {
        var authentication = new ScriptedHandler();
        authentication.Enqueue(request =>
        {
            Assert.Equal("/bff-web/v1/oauth2/refresh-token", request.Uri.AbsolutePath);
            Assert.Equal("Android", Query(request.Uri)["platform"]);
            Assert.Equal("old-refresh", request.Authorization);
            Assert.Equal("application/vnd.api+json", request.ContentType);
            Assert.Equal("openid profile vehicles", JsonDocument.Parse(request.Body).RootElement
                .GetProperty("scope").GetString());
            return JsonResponse("""
                {
                  "access_token": "new-access",
                  "refresh_token": "rotated-refresh",
                  "token_type": "Bearer",
                  "expires_in": 900
                }
                """);
        });

        var api = new ScriptedHandler();
        api.Enqueue(request =>
        {
            Assert.Equal("Bearer old-access", request.Authorization);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        api.Enqueue(request =>
        {
            Assert.Equal("Bearer new-access", request.Authorization);
            return JsonResponse("""{"userId":"user-123"}""");
        });

        var logger = new RecordingLogger();
        using var client = CreateClient(authentication, api, logger);
        client.AccessToken = new OAuthAccessTokenResult
        {
            AccessToken = "old-access",
            RefreshToken = "old-refresh"
        };
        OAuthAccessTokenResult? refreshedToken = null;
        client.AccessTokenRefreshed += (_, token) => refreshedToken = token;

        var userId = await client.GetUserId();

        Assert.Equal("user-123", userId);
        Assert.Equal("new-access", client.AccessToken.AccessToken);
        Assert.Equal("rotated-refresh", client.AccessToken.RefreshToken);
        Assert.Same(client.AccessToken, refreshedToken);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Information &&
            entry.Message == "Kamereon authentication renewed.");
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("old-refresh", StringComparison.Ordinal) ||
            entry.Message.Contains("rotated-refresh", StringComparison.Ordinal));
        Assert.Empty(authentication.PendingResponses);
        Assert.Empty(api.PendingResponses);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UnauthorizedRequest_PreservesRefreshTokenWhenReplacementIsMissingOrEmpty(
        string? replacement)
    {
        var authentication = new ScriptedHandler();
        authentication.Enqueue(_ => JsonResponse(JsonSerializer.Serialize(new
        {
            access_token = "new-access",
            refresh_token = replacement
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })));
        var api = new ScriptedHandler();
        api.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        api.Enqueue(_ => JsonResponse("""{"userId":"user-123"}"""));
        using var client = CreateClient(authentication, api);
        client.AccessToken = new OAuthAccessTokenResult
        {
            AccessToken = "old-access",
            RefreshToken = "previous-refresh"
        };

        Assert.Equal("user-123", await client.GetUserId());
        Assert.Equal("previous-refresh", client.AccessToken?.RefreshToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnauthorizedRequest_DoesNotRestoreOrReplayAfterAuthenticationChanges(
        bool replaceToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new CallbackHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return JsonResponse("""{"access_token":"stale-access","refresh_token":"stale-refresh"}""");
        });
        var apiCalls = 0;
        var api = new CallbackHandler((_, _) =>
        {
            Interlocked.Increment(ref apiCalls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        using var client = CreateClient(authentication, api);
        client.AccessToken = new OAuthAccessTokenResult
        {
            AccessToken = "old-access",
            RefreshToken = "old-refresh"
        };
        var events = 0;
        client.AccessTokenRefreshed += (_, _) => events++;
        var pendingRequest = client.GetUserId();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        OAuthAccessTokenResult? replacement = null;
        if (replaceToken)
        {
            replacement = new OAuthAccessTokenResult { AccessToken = "other-account-access" };
            client.AccessToken = replacement;
        }
        else
        {
            client.LogOut();
        }

        release.SetResult();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => pendingRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Same(replacement, client.AccessToken);
        Assert.Equal(0, events);
        Assert.Equal(1, apiCalls);
    }

    [Fact]
    public async Task LogIn_DoesNotRestoreAuthenticationAfterLogout()
    {
        var scripted = new ScriptedHandler();
        QueueSuccessfulLogin(scripted, "stale-access", "stale-refresh");
        using var transport = new HttpMessageInvoker(scripted);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new CallbackHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth2/authorize")
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await transport.SendAsync(request, cancellationToken);
        });
        using var client = CreateClient(authentication, new ScriptedHandler());
        var pendingLogin = client.LogIn("driver@example.com", "correct-password");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.LogOut();
        release.SetResult();

        var exception = await Assert.ThrowsAsync<LogInException>(() => pendingLogin);
        Assert.Contains("superseded", exception.Message);
        Assert.Null(client.AccessToken);
    }

    [Fact]
    public async Task LogIn_DoesNotOverwriteANewerCompletedLogin()
    {
        var scripted = new ScriptedHandler();
        QueueSuccessfulLogin(scripted, "newer-access", "newer-refresh", "other-driver@example.com");
        QueueSuccessfulLogin(scripted, "older-access", "older-refresh");
        using var transport = new HttpMessageInvoker(scripted);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var authentication = new CallbackHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth2/authorize" &&
                Interlocked.Increment(ref attempts) == 1)
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return await transport.SendAsync(request, cancellationToken);
        });
        using var client = CreateClient(authentication, new ScriptedHandler());
        var olderLogin = client.LogIn("driver@example.com", "correct-password");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await client.LogIn("other-driver@example.com", "correct-password"));
        release.SetResult();

        await Assert.ThrowsAsync<LogInException>(() => olderLogin);
        Assert.Equal("newer-access", client.AccessToken?.AccessToken);
        Assert.Equal("newer-refresh", client.AccessToken?.RefreshToken);
    }

    [Fact]
    public async Task UnauthorizedRequest_DoesNotRetryUnderANewlyLoggedInAccount()
    {
        var authentication = new ScriptedHandler();
        QueueSuccessfulLogin(authentication, "new-account-access", "new-account-refresh", "other-driver@example.com");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiCalls = 0;
        var api = new CallbackHandler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref apiCalls);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        using var client = CreateClient(authentication, api);
        client.AccessToken = new OAuthAccessTokenResult { AccessToken = "old-account-access" };
        var pendingRequest = client.GetUserId();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.LogIn("other-driver@example.com", "correct-password");
        release.SetResult();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => pendingRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Equal("new-account-access", client.AccessToken?.AccessToken);
        Assert.Equal(1, apiCalls);
        Assert.Empty(authentication.PendingResponses);
    }

    [Fact]
    public async Task UnauthorizedRequest_DoesNotReplayWhenRefreshSubscriberLogsOut()
    {
        var authentication = new ScriptedHandler();
        authentication.Enqueue(_ => JsonResponse("""{"access_token":"new-access"}"""));
        var api = new ScriptedHandler();
        api.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(authentication, api);
        client.AccessToken = new OAuthAccessTokenResult
        {
            AccessToken = "old-access",
            RefreshToken = "old-refresh"
        };
        client.AccessTokenRefreshed += (_, _) => client.LogOut();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetUserId());
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Null(client.AccessToken);
        Assert.Empty(api.PendingResponses);
    }

    [Fact]
    public async Task UnauthorizedRequest_RefreshSubscriberCanReenterWithoutDeadlock()
    {
        var authentication = new ScriptedHandler();
        authentication.Enqueue(_ => JsonResponse("""{"access_token":"first-refresh"}"""));
        authentication.Enqueue(_ => JsonResponse("""{"access_token":"second-refresh"}"""));
        var api = new ScriptedHandler();
        api.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        api.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        api.Enqueue(_ => JsonResponse("""{"userId":"nested-user"}"""));
        api.Enqueue(_ => JsonResponse("""{"userId":"outer-user"}"""));
        using var client = CreateClient(authentication, api);
        client.AccessToken = new OAuthAccessTokenResult
        {
            AccessToken = "old-access",
            RefreshToken = "old-refresh"
        };
        string? nestedUser = null;
        void Refreshed(object? sender, OAuthAccessTokenResult token)
        {
            client.AccessTokenRefreshed -= Refreshed;
            nestedUser = client.GetUserId().GetAwaiter().GetResult();
        }

        client.AccessTokenRefreshed += Refreshed;
        var outerUser = await Task.Run(client.GetUserId).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("outer-user", outerUser);
        Assert.Equal("nested-user", nestedUser);
        Assert.Equal("second-refresh", client.AccessToken?.AccessToken);
        Assert.Empty(authentication.PendingResponses);
        Assert.Empty(api.PendingResponses);
    }

    [Fact]
    public async Task AssigningAnotherCachedToken_DoesNotReusePreviousLoginCredentials()
    {
        var authentication = new ScriptedHandler();
        QueueSuccessfulLogin(authentication, "logged-in-access", "logged-in-refresh");
        authentication.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var api = new ScriptedHandler();
        api.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(authentication, api);
        await client.LogIn("driver@example.com", "correct-password");
        var cached = new OAuthAccessTokenResult
        {
            AccessToken = "cached-account-access",
            RefreshToken = "cached-account-refresh"
        };
        client.AccessToken = cached;

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetUserId());
        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Same(cached, client.AccessToken);
        Assert.Empty(authentication.PendingResponses);
    }

    [Fact]
    public async Task Dispose_CancelsAnInFlightLogin()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new CallbackHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("A canceled login must not continue.");
        });
        using var client = CreateClient(authentication, new ScriptedHandler());
        var pendingLogin = client.LogIn("driver@example.com", "correct-password");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        client.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pendingLogin.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(client.AccessToken);
    }

    [Fact]
    public async Task UnauthorizedRequest_FallsBackAfterRefreshTimeout()
    {
        var scripted = new ScriptedHandler();
        QueueSuccessfulLogin(scripted, "initial-access", "initial-refresh");
        QueueSuccessfulLogin(scripted, "fallback-access", "fallback-refresh");
        using var transport = new HttpMessageInvoker(scripted);
        var authentication = new CallbackHandler((request, cancellationToken) =>
            request.RequestUri!.AbsolutePath.EndsWith("/refresh-token", StringComparison.Ordinal)
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("Mock refresh timeout"))
                : transport.SendAsync(request, cancellationToken));
        var api = new ScriptedHandler();
        api.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        api.Enqueue(_ => JsonResponse("""{"userId":"user-123"}"""));
        using var client = CreateClient(authentication, api);
        await client.LogIn("driver@example.com", "correct-password");

        Assert.Equal("user-123", await client.GetUserId());
        Assert.Equal("fallback-access", client.AccessToken?.AccessToken);
        Assert.Empty(scripted.PendingResponses);
    }

    [Fact]
    public async Task Refresh_CallerCancellationDoesNotFallBackToLogin()
    {
        var scripted = new ScriptedHandler();
        QueueSuccessfulLogin(scripted, "initial-access", "initial-refresh");
        using var transport = new HttpMessageInvoker(scripted);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = new CallbackHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/refresh-token", StringComparison.Ordinal))
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("A canceled refresh must not continue.");
            }

            return await transport.SendAsync(request, cancellationToken);
        });
        using var client = CreateClient(authentication, new ScriptedHandler());
        await client.LogIn("driver@example.com", "correct-password");
        var authenticationState = client.GetAuthenticationSnapshot();
        var refresh = client.RefreshAfterUnauthorized(
            authenticationState.AccessToken!, authenticationState.Version, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Equal("initial-access", client.AccessToken?.AccessToken);
        Assert.Empty(scripted.PendingResponses);
    }

    [Fact]
    public async Task ReassigningTheRefreshedToken_DoesNotDiscardLoginCredentials()
    {
        var authentication = new ScriptedHandler();
        QueueSuccessfulLogin(authentication, "initial-access", "initial-refresh");
        authentication.Enqueue(_ => JsonResponse("""{"access_token":"refreshed-access"}"""));
        authentication.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        QueueSuccessfulLogin(authentication, "fallback-access", "fallback-refresh");
        var api = new ScriptedHandler();
        api.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        api.Enqueue(_ => JsonResponse("""{"userId":"user-123"}"""));
        api.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        api.Enqueue(_ => JsonResponse("""{"userId":"user-123"}"""));
        using var client = CreateClient(authentication, api);
        await client.LogIn("driver@example.com", "correct-password");
        client.AccessTokenRefreshed += (_, token) => client.AccessToken = token;

        Assert.Equal("user-123", await client.GetUserId());
        Assert.Equal("refreshed-access", client.AccessToken?.AccessToken);
        Assert.Equal("user-123", await client.GetUserId());
        Assert.Equal("fallback-access", client.AccessToken?.AccessToken);
        Assert.Empty(authentication.PendingResponses);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task UnauthorizedPost_RetriesOnceWithTheOriginalBody(
        HttpStatusCode retryStatus, bool accepted)
    {
        var refreshes = 0;
        var authentication = new CallbackHandler((_, _) =>
        {
            Interlocked.Increment(ref refreshes);
            return Task.FromResult(JsonResponse("""{"access_token":"new-access"}"""));
        });
        string? body = null;
        var api = new ScriptedHandler();
        api.Enqueue(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            body = request.Body;
            Assert.Equal("application/vnd.api+json", request.ContentType);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        api.Enqueue(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(body, request.Body);
            Assert.Equal("application/vnd.api+json", request.ContentType);
            Assert.Equal("Bearer new-access", request.Authorization);
            return new HttpResponseMessage(retryStatus);
        });
        using var client = CreateClient(authentication, api);
        client.AccessToken = new OAuthAccessTokenResult
        {
            AccessToken = "old-access",
            RefreshToken = "old-refresh"
        };

        Assert.Equal(accepted, await client.RefreshBatteryStatus("test-vin"));
        Assert.Equal(1, refreshes);
        Assert.Empty(api.PendingResponses);
    }

    [Fact]
    public async Task UnauthorizedRequest_FallsBackToFullLoginWhenRefreshFails()
    {
        var authentication = new ScriptedHandler();
        QueueSuccessfulLogin(authentication, "initial-access", "expired-refresh");
        authentication.Enqueue(request =>
        {
            Assert.Equal("/bff-web/v1/oauth2/refresh-token", request.Uri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        QueueSuccessfulLogin(authentication, "fallback-access", "fallback-refresh");

        var api = new ScriptedHandler();
        api.Enqueue(request =>
        {
            Assert.Equal("Bearer initial-access", request.Authorization);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        api.Enqueue(request =>
        {
            Assert.Equal("Bearer fallback-access", request.Authorization);
            return JsonResponse("""{"userId":"user-123"}""");
        });

        var client = CreateClient(authentication, api);
        await client.LogIn("driver@example.com", "correct-password");
        var refreshEvents = 0;
        client.AccessTokenRefreshed += (_, _) => refreshEvents++;

        var userId = await client.GetUserId();

        Assert.Equal("user-123", userId);
        Assert.Equal("fallback-access", client.AccessToken?.AccessToken);
        Assert.Equal("fallback-refresh", client.AccessToken?.RefreshToken);
        Assert.Equal(1, refreshEvents);
        Assert.Empty(authentication.PendingResponses);
        Assert.Empty(api.PendingResponses);
    }

    [Fact]
    public async Task ConcurrentUnauthorizedRequests_PerformSingleRefresh()
    {
        var refreshCalls = 0;
        var authentication = new CallbackHandler(async (request, cancellationToken) =>
        {
            var snapshot = await RequestSnapshot.Create(request, cancellationToken);
            Assert.Equal("/bff-web/v1/oauth2/refresh-token", snapshot.Uri.AbsolutePath);
            Interlocked.Increment(ref refreshCalls);
            await Task.Delay(50, cancellationToken);
            return JsonResponse("""
                {
                  "access_token": "new-access",
                  "refresh_token": "new-refresh",
                  "expires_in": 900
                }
                """);
        });

        var oldTokenRequests = 0;
        var api = new CallbackHandler(async (request, cancellationToken) =>
        {
            var snapshot = await RequestSnapshot.Create(request, cancellationToken);
            if (snapshot.Authorization == "Bearer old-access")
            {
                Interlocked.Increment(ref oldTokenRequests);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            Assert.Equal("Bearer new-access", snapshot.Authorization);
            return JsonResponse("""{"userId":"user-123"}""");
        });

        var client = CreateClient(authentication, api);
        client.AccessToken = new OAuthAccessTokenResult
        {
            AccessToken = "old-access",
            RefreshToken = "old-refresh"
        };

        var results = await Task.WhenAll(client.GetUserId(), client.GetUserId());

        Assert.All(results, userId => Assert.Equal("user-123", userId));
        Assert.Equal(2, oldTokenRequests);
        Assert.Equal(1, refreshCalls);
    }

    [Fact]
    public async Task VehicleAction_LogsFailureStatusWithoutVehicleIdentifier()
    {
        var api = new ScriptedHandler();
        api.Enqueue(request =>
        {
            Assert.Equal("/car-adapter/v1/cars/test-vin/actions/refresh-battery-status",
                request.Uri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        });
        var logger = new RecordingLogger();
        using var client = CreateClient(new ScriptedHandler(), api, logger);

        var accepted = await client.RefreshBatteryStatus("test-vin");

        Assert.False(accepted);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message.Contains("RefreshBatteryStatus", StringComparison.Ordinal) &&
            entry.Message.Contains("429", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("test-vin", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("refresh-battery-status", "RefreshBatteryStatus")]
    [InlineData("refresh-hvac-status", "RefreshHvacStatus")]
    [InlineData("refresh-location", "RefreshLocation")]
    [InlineData("wake-up-vehicle", "WakeUpVehicle")]
    public async Task VehicleActions_PreserveRequestShapeAndLogAcceptedStatus(
        string path,
        string action)
    {
        var api = new ScriptedHandler();
        api.Enqueue(request =>
        {
            Assert.Equal($"/car-adapter/v1/cars/test-vin/actions/{path}", request.Uri.AbsolutePath);
            Assert.Equal("application/vnd.api+json", request.ContentType);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal(action, body.RootElement.GetProperty("data").GetProperty("type").GetString());
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var logger = new RecordingLogger();
        using var client = CreateClient(new ScriptedHandler(), api, logger);

        var accepted = action switch
        {
            "RefreshBatteryStatus" => await client.RefreshBatteryStatus("test-vin"),
            "RefreshHvacStatus" => await client.RefreshHvacStatus("test-vin"),
            "RefreshLocation" => await client.RefreshLocation("test-vin"),
            "WakeUpVehicle" => await client.WakeUpVehicle("test-vin"),
            _ => throw new InvalidOperationException("Unexpected action")
        };

        Assert.True(accepted);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Information &&
            entry.Message.Contains(action, StringComparison.Ordinal) &&
            entry.Message.Contains("202", StringComparison.Ordinal));
        Assert.Empty(api.PendingResponses);
    }

    [Fact]
    public async Task VehicleAndUserIdentifiers_AreEncodedAsUrlPathSegments()
    {
        var api = new ScriptedHandler();
        api.Enqueue(request =>
        {
            Assert.Equal("/bff-web/v5/users/user%3Fother%3D1/cars", request.Uri.AbsolutePath);
            Assert.Empty(request.Uri.Query);
            return JsonResponse("""{"data":[]}""");
        });
        api.Enqueue(request =>
        {
            Assert.Equal("/car-adapter/v1/cars/vin%3Fother%3D1/battery-status", request.Uri.AbsolutePath);
            Assert.Empty(request.Uri.Query);
            return JsonResponse("""{"data":{"attributes":{"batteryLevel":89}}}""");
        });
        using var client = CreateClient(new ScriptedHandler(), api);

        var cars = await client.GetCars("user?other=1");
        var battery = await client.GetBatteryStatus("vin?other=1");

        Assert.Empty(cars!);
        Assert.Equal(89, battery?.BatteryLevel);
        Assert.Empty(api.PendingResponses);
    }

    private static NissanConnectClient CreateClient(
        HttpMessageHandler authenticationHandler,
        HttpMessageHandler apiHandler,
        ILogger<NissanConnectClient>? logger = null)
    {
        var settings = Settings[Region.EU].ToDictionary(entry => entry.Key, entry => entry.Value);
        Func<HttpMessageHandler> authenticationHandlerFactory = authenticationHandler switch
        {
            ScriptedHandler scripted => scripted.CreateHandler,
            CallbackHandler callback => callback.CreateHandler,
            _ => throw new ArgumentException("Unsupported test authentication handler.", nameof(authenticationHandler))
        };
        return new NissanConnectClient(settings, logger, authenticationHandlerFactory, apiHandler);
    }

    private static void QueueSuccessfulLogin(
        ScriptedHandler authentication,
        string accessToken,
        string refreshToken,
        string user = "driver@example.com")
    {
        string? state = null;
        string? challenge = null;

        authentication.Enqueue(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("login.mynissan-account.com", request.Uri.Host);
            Assert.Equal("/oauth2/authorize", request.Uri.AbsolutePath);
            var query = Query(request.Uri);
            Assert.Equal("code", query["response_type"]);
            Assert.Equal("ZM3WK7ax1OtQKYQ8Qqzcv5VgiA8a", query["client_id"]);
            Assert.Equal("openid name profile email offline_access", query["scope"]);
            Assert.Equal("com://wso2.service.nci", query["redirect_uri"]);
            Assert.Equal("S256", query["code_challenge_method"]);
            Assert.Equal("en_GB", query["locale"]);
            Assert.Equal("Nissan", query["brand"]);
            Assert.Equal("mynissanapp", query["client"]);
            state = query["state"];
            challenge = query["code_challenge"];
            Assert.NotEmpty(state);
            Assert.NotEmpty(challenge);
            return RedirectResponse("/authenticationendpoint/login.do");
        });

        authentication.Enqueue(request =>
        {
            Assert.Equal("/authenticationendpoint/login.do", request.Uri.AbsolutePath);
            return HtmlResponse(LoginFormHtml);
        });

        authentication.Enqueue(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/commonauth", request.Uri.AbsolutePath);
            Assert.Equal("https://login.mynissan-account.com", request.Origin);
            Assert.Equal(
                "https://login.mynissan-account.com/authenticationendpoint/login.do",
                request.Referrer);
            var form = Form(request.Body);
            Assert.Equal("session-key", form["sessionDataKey"]);
            Assert.Equal("preserved-value", form["preserved"]);
            Assert.Equal("EU", form["regionCode"]);
            Assert.Equal(user, form["userName"]);
            Assert.Equal($"EU/{user}", form["username"]);
            Assert.Equal("correct-password", form["password"]);
            return RedirectResponse(
                $"com://wso2.service.nci?code=authorization-code&state={Uri.EscapeDataString(state!)}");
        });

        authentication.Enqueue(request =>
        {
            Assert.Equal("/oauth2/token", request.Uri.AbsolutePath);
            var form = Form(request.Body);
            Assert.Equal("authorization_code", form["grant_type"]);
            Assert.Equal("authorization-code", form["code"]);
            Assert.Equal("com://wso2.service.nci", form["redirect_uri"]);
            Assert.Equal("ZM3WK7ax1OtQKYQ8Qqzcv5VgiA8a", form["client_id"]);
            Assert.Equal("openid name profile email offline_access", form["scope"]);
            Assert.False(form.ContainsKey("client_secret"));
            Assert.Equal(challenge, PkceChallenge(form["code_verifier"]));
            return JsonResponse("""
                {
                  "access_token": "oneid-access",
                  "id_token": "oneid-id-token",
                  "token_type": "Bearer",
                  "expires_in": 3600
                }
                """);
        });

        authentication.Enqueue(request =>
        {
            Assert.Equal("/bff-web/v1/oauth2/access_token", request.Uri.AbsolutePath);
            Assert.Equal("Android", Query(request.Uri)["platform"]);
            Assert.Equal("oneid-id-token", request.Authorization);
            Assert.Equal("application/vnd.api+json", request.ContentType);
            Assert.Equal("application/json", request.Accept);
            Assert.Empty(request.Body);
            return JsonResponse($$"""
                {
                  "access_token": "{{accessToken}}",
                  "refresh_token": "{{refreshToken}}",
                  "token_type": "Bearer",
                  "expires_in": 900
                }
                """);
        });
    }

    private static HttpResponseMessage HtmlResponse(string html)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage RedirectResponse(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private static Dictionary<string, string> Query(Uri uri)
    {
        return ParseUrlEncoded(uri.Query.TrimStart('?'));
    }

    private static Dictionary<string, string> Form(string body)
    {
        return ParseUrlEncoded(body);
    }

    private static Dictionary<string, string> ParseUrlEncoded(string value)
    {
        return value.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0].Replace('+', ' ')),
                part => part.Length > 1
                    ? Uri.UnescapeDataString(part[1].Replace('+', ' '))
                    : string.Empty);
    }

    private static string PkceChallenge(string verifier)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private const string LoginFormHtml = """
        <html><body>
          <form action="../../commonauth">
            <input type="hidden" name="sessionDataKey" value="session-key">
            <input type="hidden" name="preserved" value="preserved-value">
            <input type="hidden" name="regionCode" value="EU">
            <input type="text" name="userName">
            <input type="text" name="username">
            <input type="password" name="password">
          </form>
        </body></html>
        """;

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Func<RequestSnapshot, HttpResponseMessage>> _responses;

        public ScriptedHandler() : this(new())
        {
        }

        private ScriptedHandler(ConcurrentQueue<Func<RequestSnapshot, HttpResponseMessage>> responses)
        {
            _responses = responses;
        }

        public HttpMessageHandler CreateHandler() => new ScriptedHandler(_responses);

        public IReadOnlyCollection<Func<RequestSnapshot, HttpResponseMessage>> PendingResponses => _responses;
        public bool IsDisposed { get; private set; }

        public void Enqueue(Func<RequestSnapshot, HttpResponseMessage> response)
        {
            _responses.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!_responses.TryDequeue(out var response))
            {
                throw new InvalidOperationException($"Unexpected HTTP request: {request.Method} {request.RequestUri}");
            }

            return response(await RequestSnapshot.Create(request, cancellationToken));
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class CallbackHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _callback;

        public CallbackHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        {
            _callback = callback;
        }

        public HttpMessageHandler CreateHandler() => new CallbackHandler(_callback);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _callback(request, cancellationToken);
        }
    }

    private sealed class RecordingLogger : ILogger<NissanConnectClient>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Enqueue((logLevel, formatter(state, exception)));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri Uri,
        string Body,
        string? Authorization,
        string? Origin,
        string? Referrer,
        string? ContentType,
        string? Accept)
    {
        public static async Task<RequestSnapshot> Create(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return new RequestSnapshot(
                request.Method,
                request.RequestUri!,
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("Authorization", out var authorization)
                    ? authorization.Single()
                    : null,
                request.Headers.TryGetValues("Origin", out var origin) ? origin.Single() : null,
                request.Headers.Referrer?.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Accept.FirstOrDefault()?.MediaType);
        }
    }
}
