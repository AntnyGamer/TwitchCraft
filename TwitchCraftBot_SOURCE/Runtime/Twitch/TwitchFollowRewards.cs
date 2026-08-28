using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchCraftBot_V1.BotSetup;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    internal const int DefaultFollowRewardAmount = 100;
    private const int MaxEventSubMessageBytes = 256 * 1024;
    private static readonly Uri EventSubWebSocketUri = new("wss://eventsub.wss.twitch.tv/ws");

    private async Task RunFollowRewardLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !AutomaticFollowRewardsEnabled)
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        TwitchConfig? twitch = _activeConfig?.Twitch;
        if (twitch == null)
            return;

        string clientId = (twitch.ClientID ?? string.Empty).Trim();
        string botToken = NormalizeTwitchToken(twitch.BotToken);
        string streamerName = NormalizeUser(twitch.StreamerName);
        if (clientId.Length == 0 || botToken.Length == 0 || streamerName.Length == 0)
            return;

        try
        {
            string botName = await ResolveAndPersistBotNameAsync(botToken, cancellationToken).ConfigureAwait(false);
            if (botName.Length == 0)
            {
                _shellWindow?.AddChatLogLine("Follow rewards could not start because the bot Twitch account was not resolved.");
                return;
            }

            string[] userIds = await ResolveTwitchUserIdsAsync(
                botName,
                streamerName,
                clientId,
                botToken,
                cancellationToken).ConfigureAwait(false);

            if (userIds.Length != 2)
            {
                _shellWindow?.AddChatLogLine("Follow rewards could not start because the bot/channel Twitch IDs were not resolved.");
                return;
            }

            Uri endpoint = EventSubWebSocketUri;
            bool subscribeOnWelcome = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                botToken = NormalizeTwitchToken(_activeConfig?.Twitch.BotToken);
                if (botToken.Length == 0)
                    return;

                FollowSocketOutcome outcome;
                try
                {
                    using ClientWebSocket socket = new();
                    await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                    outcome = await ProcessFollowSocketAsync(
                        socket,
                        subscribeOnWelcome,
                        clientId,
                        botToken,
                        userIds[1],
                        userIds[0],
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Twitch follow listener disconnected", ex));
                    outcome = default;
                }

                if (outcome.AuthorizationRejected)
                {
                    if (await TryRefreshTwitchCredentialsAsync(botToken, cancellationToken).ConfigureAwait(false))
                    {
                        endpoint = EventSubWebSocketUri;
                        subscribeOnWelcome = true;
                        continue;
                    }

                    _shellWindow?.AddChatLogLine(
                        "Follow rewards are off because Twitch authorization could not be renewed or lacks follower permission. Open Settings → Dangerous → Authorize Twitch once to update it.");
                    return;
                }

                if (outcome.ReconnectUri != null)
                {
                    endpoint = outcome.ReconnectUri;
                    subscribeOnWelcome = false;
                    continue;
                }

                endpoint = EventSubWebSocketUri;
                subscribeOnWelcome = true;
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLogMessage("Follow rewards could not start", ex));
        }
    }

    private async Task<FollowSocketOutcome> ProcessFollowSocketAsync(
        ClientWebSocket socket,
        bool subscribeOnWelcome,
        string clientId,
        string botToken,
        string broadcasterId,
        string moderatorId,
        CancellationToken cancellationToken)
    {
        bool welcomeReceived = false;
        TimeSpan messageTimeout = TimeSpan.FromSeconds(15);
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            string? message = await ReceiveEventSubMessageAsync(socket, messageTimeout, cancellationToken).ConfigureAwait(false);
            if (message == null)
                return default;

            JObject root;
            try
            {
                root = JObject.Parse(message);
            }
            catch (JsonException ex)
            {
                ErrorHandling.LogNonFatal("Twitch EventSub returned invalid JSON", ex);
                continue;
            }

            string messageType = (string?)root["metadata"]?["message_type"] ?? string.Empty;
            switch (messageType)
            {
                case "session_welcome":
                    if (welcomeReceived)
                        continue;

                    welcomeReceived = true;
                    int keepaliveSeconds = (int?)root["payload"]?["session"]?["keepalive_timeout_seconds"] ?? 10;
                    messageTimeout = TimeSpan.FromSeconds(Math.Clamp(keepaliveSeconds, 10, 600) + 10);
                    if (subscribeOnWelcome)
                    {
                        string sessionId = (string?)root["payload"]?["session"]?["id"] ?? string.Empty;
                        if (sessionId.Length == 0)
                            return default;

                        HttpStatusCode status = await CreateFollowSubscriptionAsync(
                            sessionId,
                            broadcasterId,
                            moderatorId,
                            clientId,
                            botToken,
                            cancellationToken).ConfigureAwait(false);

                        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                            return new(null, AuthorizationRejected: true);
                    }
                    break;

                case "notification":
                    if (TryParseFollowNotification(root, out FollowNotification notification))
                        await ApplyFollowRewardAsync(notification, cancellationToken).ConfigureAwait(false);
                    break;

                case "session_reconnect":
                    string reconnectUrl = (string?)root["payload"]?["session"]?["reconnect_url"] ?? string.Empty;
                    return TryCreateSafeReconnectUri(reconnectUrl, out Uri? reconnectUri)
                        ? new(reconnectUri, AuthorizationRejected: false)
                        : default;

                case "revocation":
                    string reason = (string?)root["payload"]?["subscription"]?["status"] ?? "unknown reason";
                    _shellWindow?.AddChatLogLine("Twitch disabled follow rewards: " + reason + ".");
                    return default;
            }
        }

        return default;
    }

    private async Task ApplyFollowRewardAsync(FollowNotification notification, CancellationToken cancellationToken)
    {
        if (!AutomaticFollowRewardsEnabled)
            return;

        int rewardAmount = FollowRewardAmount;
        FollowRewardResult result = _tokenStore.TryRewardFollowerOnce(
            notification.UserId,
            notification.UserLogin,
            notification.FollowedAt,
            rewardAmount,
            out int awardedAmount,
            MaximumTokenBalance);

        if (result != FollowRewardResult.Rewarded)
            return;

        string rewardText = awardedAmount > 0
            ? " received " + awardedAmount.ToString(CultureInfo.InvariantCulture) + " tokens."
            : " was already at the maximum token balance.";
        _shellWindow?.AddChatLogLine(notification.UserLogin + " followed and" + rewardText);
        await SendBotResponseAsync(
            "@" + notification.UserLogin + " thanks for following!" +
                (awardedAmount > 0
                    ? " You received " + awardedAmount.ToString(CultureInfo.InvariantCulture) + " tokens!"
                    : " Your token balance is already at the maximum."),
            BotResponseKind.Announcement,
            cancellationToken).ConfigureAwait(false);
    }

    internal bool AutomaticFollowRewardsEnabled
        => _activeConfig?.Settings.AutomaticFollowRewardsEnabled ?? true;

    internal int FollowRewardAmount
    {
        get
        {
            int amount = _activeConfig?.Settings.FollowRewardAmount ?? DefaultFollowRewardAmount;
            return amount is >= 1 and <= 1_000_000 ? amount : DefaultFollowRewardAmount;
        }
    }

    private static async Task<HttpStatusCode> CreateFollowSubscriptionAsync(
        string sessionId,
        string broadcasterId,
        string moderatorId,
        string clientId,
        string botToken,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            type = "channel.follow",
            version = "2",
            condition = new
            {
                broadcaster_user_id = broadcasterId,
                moderator_user_id = moderatorId
            },
            transport = new
            {
                method = "websocket",
                session_id = sessionId
            }
        };

        using HttpRequestMessage request = new(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions");
        request.Headers.TryAddWithoutValidation("Authorization", TwitchTokenHelper.BuildBearerHeader(botToken));
        request.Headers.TryAddWithoutValidation("Client-Id", clientId);
        request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return response.StatusCode;

        response.EnsureSuccessStatusCode();
        return response.StatusCode;
    }

    private static async Task<string?> ReceiveEventSubMessageAsync(
        ClientWebSocket socket,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        using CancellationTokenSource receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        receiveTimeout.CancelAfter(timeout);
        try
        {
            using System.IO.MemoryStream message = new();
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, receiveTimeout.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                if (result.MessageType != WebSocketMessageType.Text)
                    throw new WebSocketException("Twitch EventSub sent a non-text message.");

                if (message.Length + result.Count > MaxEventSubMessageBytes)
                    throw new WebSocketException("Twitch EventSub message exceeded the safe size limit.");

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Twitch EventSub stopped sending keepalive messages.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    internal static bool TryParseFollowNotification(JObject root, out FollowNotification notification)
    {
        notification = default;
        string subscriptionType = (string?)root["metadata"]?["subscription_type"]
            ?? (string?)root["payload"]?["subscription"]?["type"]
            ?? string.Empty;
        if (!string.Equals(subscriptionType, "channel.follow", StringComparison.Ordinal))
            return false;

        JToken? eventData = root["payload"]?["event"];
        string userId = ((string?)eventData?["user_id"] ?? string.Empty).Trim();
        string userLogin = NormalizeUser((string?)eventData?["user_login"]);
        JToken? followedAtToken = eventData?["followed_at"];
        if (userId.Length == 0 || userLogin.Length == 0 || !TryReadFollowedAt(followedAtToken, out DateTimeOffset followedAt))
        {
            return false;
        }

        notification = new(userId, userLogin, followedAt);
        return true;
    }

    private static bool TryReadFollowedAt(JToken? token, out DateTimeOffset followedAt)
    {
        followedAt = default;
        if (token?.Type == JTokenType.Date)
        {
            object? value = ((JValue)token).Value;
            if (value is DateTimeOffset offset)
            {
                followedAt = offset.ToUniversalTime();
                return true;
            }

            if (value is DateTime dateTime)
            {
                followedAt = new DateTimeOffset(dateTime.ToUniversalTime());
                return true;
            }
        }

        return DateTimeOffset.TryParse(
            token?.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out followedAt);
    }

    private static bool TryCreateSafeReconnectUri(string value, out Uri? reconnectUri)
    {
        reconnectUri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed) ||
            !string.Equals(parsed.Scheme, "wss", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.Host, "eventsub.wss.twitch.tv", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        reconnectUri = parsed;
        return true;
    }

    internal readonly record struct FollowNotification(string UserId, string UserLogin, DateTimeOffset FollowedAt);
    private readonly record struct FollowSocketOutcome(Uri? ReconnectUri, bool AuthorizationRejected);
}
