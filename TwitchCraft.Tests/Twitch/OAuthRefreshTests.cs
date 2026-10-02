using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using TwitchCraft_V1.Setup;
using Xunit;

namespace TwitchCraft.Tests.Twitch;

public sealed class OAuthRefreshTests
{
    private const string RenewedTokens = """{"access_token":"renewed-access","refresh_token":"rotated-refresh","expires_in":3600}""";
    private const string ValidIdentity = """{"client_id":"client123","login":" BotAccount ","user_id":"42","expires_in":3600,"scopes":["chat:read","chat:edit","moderator:read:chatters","moderator:read:followers"]}""";

    [Fact]
    public async Task Refresh_ValidatesRenewedCredentialsBeforeReturningAnIdentity()
    {
        TestHttpHandler handler = new(async (request, token) =>
        {
            Assert.Equal("id.twitch.tv", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath == "/oauth2/token")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Null(request.Headers.Authorization);
                Assert.NotNull(request.Content);
                Assert.Equal("application/x-www-form-urlencoded", request.Content.Headers.ContentType!.MediaType);
                string form = await request.Content.ReadAsStringAsync(token);
                var fields = HttpUtility.ParseQueryString(form);
                Assert.Equal("client123", fields["client_id"]);
                Assert.Equal("refresh+token", fields["refresh_token"]);
                Assert.Equal("refresh_token", fields["grant_type"]);
                Assert.Null(fields["client_secret"]);
                return Response(HttpStatusCode.OK, RenewedTokens);
            }

            Assert.Equal("/oauth2/validate", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("OAuth", request.Headers.Authorization!.Scheme);
            Assert.Equal("renewed-access", request.Headers.Authorization.Parameter);
            return Response(HttpStatusCode.OK, ValidIdentity);
        });
        using HttpClient client = new(handler);

        TwitchOAuthResult result = await TwitchOAuthAuthorizer.RefreshAsync(
            " client123 ", " refresh+token ", client, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("renewed-access", result.Token);
        Assert.Equal("rotated-refresh", result.RefreshToken);
        Assert.Equal("botaccount", result.Login);
        Assert.Empty(result.Error);
        Assert.Equal(["/oauth2/token", "/oauth2/validate"], handler.Paths);
    }

    [Theory]
    [InlineData("""{"access_token":"renewed-access","refresh_token":"rotated-refresh","expires_in":0}""")]
    [InlineData("""{"access_token":"renewed-access","expires_in":3600}""")]
    public async Task Refresh_IncompleteRenewalDoesNotAttemptValidationOrExposeAnAccessToken(string json)
    {
        TestHttpHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK, json)));
        using HttpClient client = new(handler);

        TwitchOAuthResult result = await TwitchOAuthAuthorizer.RefreshAsync(
            "client123", "old-refresh", client, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Token);
        Assert.Empty(result.RefreshToken);
        Assert.Empty(result.Login);
        Assert.NotEmpty(result.Error);
        Assert.Equal(["/oauth2/token"], handler.Paths);
    }

    [Fact]
    public async Task Refresh_ValidationRejectionPreservesTheRotatedRefreshTokenForRetry()
    {
        TestHttpHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/oauth2/token"
                ? Response(HttpStatusCode.OK, RenewedTokens)
                : Response(HttpStatusCode.Unauthorized, "{}")));
        using HttpClient client = new(handler);

        TwitchOAuthResult result = await TwitchOAuthAuthorizer.RefreshAsync(
            "client123", "old-refresh", client, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Token);
        Assert.Empty(result.Login);
        Assert.NotEmpty(result.Error);
        Assert.Equal("rotated-refresh", result.RefreshToken);
        Assert.Equal(["/oauth2/token", "/oauth2/validate"], handler.Paths);
    }

    [Fact]
    public async Task Refresh_CallerCancellationDuringValidationPropagates()
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        TaskCompletionSource validationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestHttpHandler handler = new(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/oauth2/token")
                return Response(HttpStatusCode.OK, RenewedTokens);

            validationStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, ValidIdentity);
        });
        using HttpClient client = new(handler);

        Task<TwitchOAuthResult> renewal = TwitchOAuthAuthorizer.RefreshAsync("client123", "old-refresh", client, cancellation.Token);
        await validationStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            renewal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(["/oauth2/token", "/oauth2/validate"], handler.Paths);
    }

    [Fact]
    public async Task Refresh_RevokedTokenResponseCanTriggerReauthorizationWithoutProtocolLineBreaks()
    {
        TestHttpHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.BadRequest,
            """{"message":"invalid refresh token\r\ntry again"}""")));
        using HttpClient client = new(handler);

        TwitchOAuthResult result = await TwitchOAuthAuthorizer.RefreshAsync(
            "client123", "old-refresh", client, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Token);
        Assert.Empty(result.RefreshToken);
        Assert.True(TwitchOAuthAuthorizer.ShouldUseDeviceAuth(result.Error));
        Assert.DoesNotContain('\r', result.Error);
        Assert.DoesNotContain('\n', result.Error);
        Assert.Equal(["/oauth2/token"], handler.Paths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_TransportFailureDoesNotExposeCredentialsOrRequestReauthorization(bool timeout)
    {
        TestHttpHandler handler = new((_, _) => Task.FromException<HttpResponseMessage>(timeout
            ? new TaskCanceledException("test transport timeout")
            : new HttpRequestException("test transport failure with sensitive request details")));
        using HttpClient client = new(handler);

        TwitchOAuthResult result = await TwitchOAuthAuthorizer.RefreshAsync(
            "client123", "old-refresh", client, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Token);
        Assert.Empty(result.RefreshToken);
        Assert.NotEmpty(result.Error);
        Assert.DoesNotContain("sensitive", result.Error, StringComparison.Ordinal);
        Assert.False(TwitchOAuthAuthorizer.ShouldUseDeviceAuth(result.Error));
        Assert.Equal(["/oauth2/token"], handler.Paths);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json) };

    private sealed class TestHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return send(request, cancellationToken);
        }
    }
}
