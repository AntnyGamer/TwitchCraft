using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1.BotSetup;

internal readonly record struct TwitchOAuthResult(
    string Token,
    string Login,
    string Error,
    string RefreshToken = "",
    int ExpiresInSeconds = 0)
{
    public bool IsSuccess => !string.IsNullOrEmpty(Token) && string.IsNullOrEmpty(Error);
}

internal readonly record struct TwitchDeviceAuthorization(
    string DeviceCode,
    string UserCode,
    Uri VerificationUri,
    int ExpiresInSeconds,
    int IntervalSeconds);

internal static class TwitchOAuthAuthorizer
{
    internal const string RequiredScopes = "chat:read chat:edit moderator:read:chatters moderator:read:followers";
    // DO NOT CHANGE THIS UNLESS YOU ARE PLANNING ON MAKING YOUR OWN APPLICATION
    // This desktop flow intentionally has no Client Secret, so the Twitch application MUST use Client Type: Public
    internal const string TwitchCraftClientId = "291b194ilywj9gxpo919c2p9q08tdx";
    internal static bool TwitchCraftOAuthConfigured => TwitchCraftClientId.Length > 0;
    private static readonly Uri DeviceUri = new("https://id.twitch.tv/oauth2/device");
    private static readonly Uri TokenUri = new("https://id.twitch.tv/oauth2/token");
    private static readonly Uri ValidateUri = new("https://id.twitch.tv/oauth2/validate");
    private const string DeviceGrantType = "urn:ietf:params:oauth:grant-type:device_code";
    private static readonly TimeSpan MaximumAuthorizationWait = TimeSpan.FromMinutes(10);
    private static readonly string[] RequiredScopeList = RequiredScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<TwitchOAuthResult> AuthorizeAsync(string clientId, CancellationToken cancellationToken)
    {
        string normalizedClientId = (clientId ?? string.Empty).Trim();
        if (normalizedClientId.Length == 0)
            return Fail("This TwitchCraft build is missing its public Twitch Client ID.");

        TwitchDeviceAuthorization device;
        try
        {
            using FormUrlEncodedContent content = new(
            [
                new KeyValuePair<string, string>("client_id", normalizedClientId),
                new KeyValuePair<string, string>("scopes", RequiredScopes)
            ]);
            using HttpResponseMessage response = await HttpClient.PostAsync(DeviceUri, content, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Fail("Twitch could not start device authorization. " + ReadError(json));

            using JsonDocument document = JsonDocument.Parse(json);
            if (!TryReadDeviceAuth(document.RootElement, out device, out string error))
                return Fail(error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("Timed out while starting Twitch authorization.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Debug.WriteLine(ex);
            return Fail("Could not contact Twitch to start authorization.");
        }

        try
        {
            AppHelpers.OpenTarget(device.VerificationUri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            return Fail("TwitchCraft could not open the Twitch authorization page. " + ex.Message);
        }

        TimeSpan pollDelay = TimeSpan.FromSeconds(Math.Clamp(device.IntervalSeconds, 1, 30));
        TimeSpan allowedWait = TimeSpan.FromSeconds(device.ExpiresInSeconds);
        if (allowedWait > MaximumAuthorizationWait)
            allowedWait = MaximumAuthorizationWait;

        DateTimeOffset deadline = DateTimeOffset.UtcNow + allowedWait;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(pollDelay, cancellationToken).ConfigureAwait(false);
            DeviceTokenPollResult poll = await PollForTokenAsync(
                normalizedClientId,
                device.DeviceCode,
                cancellationToken).ConfigureAwait(false);

            if (poll.Status == DeviceTokenPollStatus.Pending)
                continue;

            if (poll.Status == DeviceTokenPollStatus.SlowDown)
            {
                pollDelay += TimeSpan.FromSeconds(5);
                continue;
            }

            return poll.Result;
        }

        return Fail("Timed out waiting for Twitch authorization. Click Authorize Twitch to try again.");
    }

    public static async Task<TwitchOAuthResult> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        string normalizedClientId = (clientId ?? string.Empty).Trim();
        string normalizedRefreshToken = (refreshToken ?? string.Empty).Trim();
        if (normalizedClientId.Length == 0 || normalizedRefreshToken.Length == 0)
            return Fail("The saved Twitch authorization cannot be renewed because its Client ID or refresh token is missing.");

        try
        {
            using FormUrlEncodedContent content = new(
            [
                new KeyValuePair<string, string>("client_id", normalizedClientId),
                new KeyValuePair<string, string>("refresh_token", normalizedRefreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token")
            ]);
            using HttpResponseMessage response = await HttpClient.PostAsync(TokenUri, content, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Fail("Twitch could not renew the saved authorization. " + ReadError(json));

            using JsonDocument document = JsonDocument.Parse(json);
            if (!TryReadTokens(document.RootElement, out string token, out string newRefreshToken, out int expiresIn, out string error))
                return Fail(error);

            return await ValidateTokenAsync(token, newRefreshToken, expiresIn, normalizedClientId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("Timed out while renewing Twitch authorization.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Debug.WriteLine(ex);
            return Fail("Could not contact Twitch to renew authorization.");
        }
    }

    internal static bool ShouldUseDeviceAuth(string refreshError)
    {
        if (string.IsNullOrWhiteSpace(refreshError))
            return false;

        return refreshError.Contains("invalid refresh token", StringComparison.OrdinalIgnoreCase)
            || refreshError.Contains("expired refresh token", StringComparison.OrdinalIgnoreCase)
            || refreshError.Contains("refresh token expired", StringComparison.OrdinalIgnoreCase)
            || refreshError.Contains("revoked refresh token", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsClientSecretFailure(string refreshError)
    {
        if (string.IsNullOrWhiteSpace(refreshError))
            return false;

        bool mentionsClientSecret = refreshError.Contains("client secret", StringComparison.OrdinalIgnoreCase)
            || refreshError.Contains("client_secret", StringComparison.OrdinalIgnoreCase);
        if (!mentionsClientSecret)
            return false;

        return refreshError.Contains("missing", StringComparison.OrdinalIgnoreCase)
            || refreshError.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || refreshError.Contains("required", StringComparison.OrdinalIgnoreCase)
            || refreshError.Contains("rejected", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryReadDeviceAuth(
        JsonElement root,
        out TwitchDeviceAuthorization authorization,
        out string error)
    {
        authorization = default;
        error = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Twitch returned an invalid device-authorization response.";
            return false;
        }

        string deviceCode = GetString(root, "device_code");
        string userCode = GetString(root, "user_code");
        string verificationUrl = GetString(root, "verification_uri");
        int expiresIn = GetPositiveInt(root, "expires_in");
        int interval = GetPositiveInt(root, "interval");
        if (deviceCode.Length == 0 || userCode.Length == 0 || expiresIn <= 0 || interval <= 0)
        {
            error = "Twitch returned incomplete device-authorization information.";
            return false;
        }

        if (!Uri.TryCreate(verificationUrl, UriKind.Absolute, out Uri? verificationUri) ||
            !string.Equals(verificationUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !IsTwitchHost(verificationUri.Host))
        {
            error = "Twitch returned an invalid authorization page address.";
            return false;
        }

        authorization = new(
            deviceCode,
            userCode,
            AddActivationCode(verificationUri, userCode),
            expiresIn,
            interval);
        return true;
    }

    internal static bool TryReadIdentity(
        JsonElement root,
        string expectedClientId,
        out string login,
        out string error)
    {
        login = string.Empty;
        error = string.Empty;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Twitch returned an invalid token-validation response.";
            return false;
        }

        string clientId = GetString(root, "client_id");
        login = TwitchCraftBot_V1.CommandUserHelper.NormalizeUser(GetString(root, "login"));
        string userId = GetString(root, "user_id");
        if (!string.Equals(clientId, expectedClientId, StringComparison.Ordinal))
        {
            error = "The token belongs to a different Twitch Client ID.";
            return false;
        }

        if (login.Length == 0 || userId.Length == 0)
        {
            error = "Twitch did not return a valid user account for this token.";
            return false;
        }

        if (GetPositiveInt(root, "expires_in") <= 0)
        {
            error = "The Twitch token is expired or invalid.";
            return false;
        }

        if (!root.TryGetProperty("scopes", out JsonElement scopes) || scopes.ValueKind != JsonValueKind.Array)
        {
            error = "Twitch did not return the token permissions.";
            return false;
        }

        foreach (string requiredScope in RequiredScopeList)
        {
            bool found = false;
            foreach (JsonElement scope in scopes.EnumerateArray())
            {
                if (scope.ValueKind == JsonValueKind.String &&
                    string.Equals(scope.GetString(), requiredScope, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                error = "The token is missing the required permission: " + requiredScope;
                login = string.Empty;
                return false;
            }
        }

        return true;
    }

    private static async Task<DeviceTokenPollResult> PollForTokenAsync(
        string clientId,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        try
        {
            using FormUrlEncodedContent content = new(
            [
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("scopes", RequiredScopes),
                new KeyValuePair<string, string>("device_code", deviceCode),
                new KeyValuePair<string, string>("grant_type", DeviceGrantType)
            ]);
            using HttpResponseMessage response = await HttpClient.PostAsync(TokenUri, content, cancellationToken).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string serviceError = ReadError(json);
                if (serviceError.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase) ||
                    serviceError.Contains("authorization pending", StringComparison.OrdinalIgnoreCase))
                {
                    return new(DeviceTokenPollStatus.Pending, default);
                }

                if (serviceError.Contains("slow_down", StringComparison.OrdinalIgnoreCase) ||
                    serviceError.Contains("slow down", StringComparison.OrdinalIgnoreCase))
                {
                    return new(DeviceTokenPollStatus.SlowDown, default);
                }

                return new(DeviceTokenPollStatus.Complete, Fail("Twitch authorization failed. " + serviceError));
            }

            using JsonDocument document = JsonDocument.Parse(json);
            if (!TryReadTokens(document.RootElement, out string token, out string refreshToken, out int expiresIn, out string error))
                return new(DeviceTokenPollStatus.Complete, Fail(error));

            TwitchOAuthResult validated = await ValidateTokenAsync(
                token,
                refreshToken,
                expiresIn,
                clientId,
                cancellationToken).ConfigureAwait(false);
            return new(DeviceTokenPollStatus.Complete, validated);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(DeviceTokenPollStatus.Complete, Fail("Timed out while waiting for Twitch authorization."));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Debug.WriteLine(ex);
            return new(DeviceTokenPollStatus.Complete, Fail("Could not contact Twitch while completing authorization."));
        }
    }

    private static bool TryReadTokens(
        JsonElement root,
        out string token,
        out string refreshToken,
        out int expiresIn,
        out string error)
    {
        token = GetString(root, "access_token");
        refreshToken = GetString(root, "refresh_token");
        expiresIn = GetPositiveInt(root, "expires_in");
        error = string.Empty;
        if (token.Length == 0 || refreshToken.Length == 0 || expiresIn <= 0)
        {
            token = string.Empty;
            refreshToken = string.Empty;
            expiresIn = 0;
            error = "Twitch returned incomplete renewable token information.";
            return false;
        }

        return true;
    }

    private static async Task<TwitchOAuthResult> ValidateTokenAsync(
        string token,
        string refreshToken,
        int expiresIn,
        string clientId,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, ValidateUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
            using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Fail("Twitch rejected the access token.");

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return TryReadIdentity(document.RootElement, clientId, out string login, out string error)
                ? new(token, login, string.Empty, refreshToken, expiresIn)
                : Fail(error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("Timed out while validating the token with Twitch.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            Debug.WriteLine(ex);
            return Fail("Could not validate the token with Twitch.");
        }
    }

    private static Uri AddActivationCode(Uri verificationUri, string userCode)
    {
        string query = verificationUri.Query.TrimStart('?');
        if (query.Contains("device-code=", StringComparison.OrdinalIgnoreCase))
            return verificationUri;

        UriBuilder builder = new(verificationUri);
        string codeQuery = "public=true&device-code=" + Uri.EscapeDataString(userCode);
        builder.Query = query.Length == 0 ? codeQuery : query + "&" + codeQuery;
        return builder.Uri;
    }

    private static bool IsTwitchHost(string host)
        => string.Equals(host, "twitch.tv", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase);

    private static string ReadError(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string error = GetString(root, "message");
            if (error.Length == 0)
                error = GetString(root, "error_description");
            if (error.Length == 0)
                error = GetString(root, "error");
            return Sanitize(error.Length == 0 ? "Twitch rejected the request." : error);
        }
        catch (JsonException)
        {
            return "Twitch rejected the request.";
        }
    }

    private static string GetString(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetPositiveInt(JsonElement root, string propertyName)
        => root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out JsonElement value) &&
            value.TryGetInt32(out int number) && number > 0
                ? number
                : 0;

    private static string Sanitize(string value)
        => (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static TwitchOAuthResult Fail(string error)
        => new(string.Empty, string.Empty, Sanitize(error));

    private enum DeviceTokenPollStatus
    {
        Pending,
        SlowDown,
        Complete
    }

    private readonly record struct DeviceTokenPollResult(DeviceTokenPollStatus Status, TwitchOAuthResult Result);
}
