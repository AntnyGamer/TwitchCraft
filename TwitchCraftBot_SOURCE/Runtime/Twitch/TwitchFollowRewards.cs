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

    private async Task RunFollowRewardsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !AutomaticFollowRewardsEnabled)
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        TwitchConfig? twitch = _activeConfig?.Twitch;
        if (twitch == null)
            return;

        string clientId = (twitch.ClientID ?? string.Empty).Trim();
        string botToken;
        string streamerName = NormalizeUser(twitch.StreamerName);
        if (clientId.Length == 0 || streamerName.Length == 0)
            return;

        try
        {
        ResolveFollowUsers:
            botToken = NormalizeToken(_activeConfig?.Twitch.BotToken);
            if (botToken.Length == 0) return;
            string botName = await ResolveBotAsync(botToken, cancellationToken).ConfigureAwait(false);
            if (botName.Length == 0)
            {
                _shellWindow?.AddChatLogLine("Follow rewards could not start because the bot Twitch account was not resolved.");
                return;
            }

            string[] userIds = await ResolveUsersAsync(
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

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!string.Equals(botToken, NormalizeToken(_activeConfig?.Twitch.BotToken), StringComparison.Ordinal)) goto ResolveFollowUsers;

                FollowSocketOutcome outcome;
                ClientWebSocket socket = new();
                try
                {
                    await socket.ConnectAsync(EventSubWebSocketUri, cancellationToken).ConfigureAwait(false);
                    outcome = await RunFollowSocketAsync(
                        socket,
                        true,
                        clientId,
                        botToken,
                        userIds[1],
                        userIds[0],
                        cancellationToken).ConfigureAwait(false);

                    while (outcome.ReconnectUri is Uri reconnectUri)
                    {
                        using CancellationTokenSource handoff = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        Task drain = DrainFollowNotificationsAsync(socket, handoff.Token);
                        ClientWebSocket replacement = new();
                        try
                        {
                            await replacement.ConnectAsync(reconnectUri, cancellationToken).ConfigureAwait(false);
                            TaskCompletionSource<bool> welcomed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                            Task<FollowSocketOutcome> replacementListener = RunFollowSocketAsync(
                                replacement,
                                false,
                                clientId,
                                botToken,
                                userIds[1],
                                userIds[0],
                                cancellationToken,
                                welcomed);
                            await Task.WhenAny(welcomed.Task, replacementListener).ConfigureAwait(false);
                            if (!welcomed.Task.IsCompleted)
                            {
                                outcome = await replacementListener.ConfigureAwait(false);
                                if (outcome == default) throw new WebSocketException();
                                replacement.Dispose();
                                break;
                            }

                            handoff.Cancel();
                            await drain.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                            socket.Dispose();
                            socket = replacement;
                            outcome = await replacementListener.ConfigureAwait(false);
                        }
                        catch (Exception) when (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
                        {
                            replacement.Dispose();
                            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        finally
                        {
                            handoff.Cancel();
                            await drain.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("Twitch follow listener disconnected", ex));
                    outcome = default;
                }
                finally
                {
                    socket.Dispose();
                }

                if (outcome.Stop)
                    return;

                if (outcome.AuthorizationRejected)
                {
                    if (await TryRefreshAuthAsync(botToken, cancellationToken).ConfigureAwait(false))
                        continue;

                    _shellWindow?.AddChatLogLine(
                        "Follow rewards are off because Twitch authorization could not be renewed or lacks follower permission. Open Settings → Dangerous → Authorize Twitch once to update it.");
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _shellWindow?.AddChatLogLine(ErrorHandling.FormatLog("Follow rewards could not start", ex));
        }
    }

    private async Task<FollowSocketOutcome> RunFollowSocketAsync(
        ClientWebSocket socket,
        bool subscribeOnWelcome,
        string clientId,
        string botToken,
        string broadcasterId,
        string moderatorId,
        CancellationToken cancellationToken,
        TaskCompletionSource<bool>? welcomeSignal = null)
    {
        bool welcomeReceived = false;
        TimeSpan messageTimeout = TimeSpan.FromSeconds(15);
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            string? message = await ReceiveEventAsync(socket, messageTimeout, cancellationToken).ConfigureAwait(false);
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
                    welcomeSignal?.TrySetResult(true);
                    int keepaliveSeconds = (int?)root["payload"]?["session"]?["keepalive_timeout_seconds"] ?? 10;
                    messageTimeout = TimeSpan.FromSeconds(Math.Clamp(keepaliveSeconds, 10, 600) + 10);
                    if (subscribeOnWelcome)
                    {
                        string sessionId = (string?)root["payload"]?["session"]?["id"] ?? string.Empty;
                        if (sessionId.Length == 0)
                            return default;

                        HttpStatusCode status = await SubscribeToFollowsAsync(
                            sessionId,
                            broadcasterId,
                            moderatorId,
                            clientId,
                            botToken,
                            cancellationToken).ConfigureAwait(false);

                        if (status == HttpStatusCode.Unauthorized)
                            return new(null, AuthorizationRejected: true);

                        if (status == HttpStatusCode.Forbidden)
                        {
                            _shellWindow?.AddChatLogLine("Follow rewards are off because the bot account lacks follower permission or moderator status.");
                            return new(null, AuthorizationRejected: false, Stop: true);
                        }
                    }
                    break;

                case "notification":
                    if (TryParseFollow(root, out FollowNotification notification))
                        await RewardFollowerAsync(notification, cancellationToken).ConfigureAwait(false);
                    break;

                case "session_reconnect":
                    string reconnectUrl = (string?)root["payload"]?["session"]?["reconnect_url"] ?? string.Empty;
                    return TryGetReconnectUri(reconnectUrl, out Uri? reconnectUri)
                        ? new(reconnectUri, AuthorizationRejected: false)
                        : default;

                case "revocation":
                    string reason = (string?)root["payload"]?["subscription"]?["status"] ?? "unknown reason";
                    _shellWindow?.AddChatLogLine("Twitch revoked the follow subscription: " + reason + ".");
                    return new(null, AuthorizationRejected: reason == "authorization_revoked", Stop: reason != "authorization_revoked");
            }
        }

        return default;
    }

    private async Task DrainFollowNotificationsAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            string? message = await ReceiveEventAsync(socket, TimeSpan.FromSeconds(40), cancellationToken).ConfigureAwait(false);
            if (message == null)
                return;

            try
            {
                JObject root = JObject.Parse(message);
                if ((string?)root["metadata"]?["message_type"] == "notification" &&
                    TryParseFollow(root, out FollowNotification notification))
                {
                    await RewardFollowerAsync(notification, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (JsonException ex)
            {
                ErrorHandling.LogNonFatal("Twitch EventSub returned invalid JSON during reconnect", ex);
            }
        }
    }

    private async Task RewardFollowerAsync(FollowNotification notification, CancellationToken cancellationToken)
    {
        if (!AutomaticFollowRewardsEnabled)
            return;

        int rewardAmount = FollowRewardAmount;
        FollowRewardResult result = _tokenStore.TryRewardFollower(
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
        await SendReplyAsync(
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

    private static async Task<HttpStatusCode> SubscribeToFollowsAsync(
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

    private static async Task<string?> ReceiveEventAsync(
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

    internal static bool TryParseFollow(JObject root, out FollowNotification notification)
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
        if (userId.Length == 0 || userLogin.Length == 0 || !TryReadFollowTime(followedAtToken, out DateTimeOffset followedAt))
        {
            return false;
        }

        notification = new(userId, userLogin, followedAt);
        return true;
    }

    private static bool TryReadFollowTime(JToken? token, out DateTimeOffset followedAt)
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

    private static bool TryGetReconnectUri(string value, out Uri? reconnectUri)
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
    private readonly record struct FollowSocketOutcome(Uri? ReconnectUri, bool AuthorizationRejected, bool Stop = false);
}
