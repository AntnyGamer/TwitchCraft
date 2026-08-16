using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
namespace TwitchCraftBot_V1;

public static partial class CommandList
{
    private static void AddTokenHandlers(
        BotMainHandler runtime,
        Dictionary<string, ChatCommandHandler> handlers,
        Func<string?, CancellationToken, Task> sayToChannel,
        Func<string, string, CancellationToken, Task<bool>> requireAllowed)
    {
        handlers["gambletokens"] = async (args, sender, ct) =>
        {
            string who = NormalizeCommandUser(sender);
            if (args is null || args.Length < 1)
            {
                await sayToChannel(who + ", usage: !gambletokens amount risk (1-10) — example: !gambletokens 20 5", ct).ConfigureAwait(false);
                return;
            }
            if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount < 5)
            {
                await sayToChannel(who + ", invalid amount. Minimum gamble amount is 5 tokens.", ct).ConfigureAwait(false);
                return;
            }
            if (amount > 150)
            {
                await sayToChannel(who + ", maximum gamble amount is 150 tokens per bet.", ct).ConfigureAwait(false);
                return;
            }
            int risk = 5;
            if (args.Length >= 2 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRisk))
                risk = parsedRisk;
            risk = Math.Clamp(risk, 1, 10);
            if (runtime.IsGambleOnCooldown(who, out TimeSpan cooldownRemaining))
            {
                await sayToChannel(string.Create(CultureInfo.InvariantCulture, $"{who}, gamble is on cooldown. Try again in {FormatMinutesSeconds(cooldownRemaining)}."), ct).ConfigureAwait(false);
                return;
            }
            int balance = runtime.GetTokens(who);
            if (!runtime.TrySpendTokens(who, amount))
            {
                await sayToChannel(string.Create(CultureInfo.InvariantCulture, $"{who}, you must have at least {amount} tokens to gamble that amount. You currently have {balance}."), ct).ConfigureAwait(false);
                return;
            }
            double winChance = 0.9 - ((risk - 1) * 0.08888888888888889);
            double payoutMul = 1.05 + ((risk - 1) * 0.21666666666666667);
            runtime.StartGambleCooldown(who, GambleTokenCooldown);
            bool win = BotMainHandler.SecureRandomChance(winChance);
            if (win)
            {
                int gain = (int)Math.Round(amount * (payoutMul - 1.0));
                if (gain <= 0)
                    gain = 1;
                runtime.AdjustTokens(who, amount + gain); // Gamble win payout restores bet plus profit.
                int newBalance = runtime.GetTokens(who);
                await sayToChannel(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{who}, you gambled {amount} {CommandTokenWord(amount)} at risk {risk} and WON! You gained {gain} {CommandTokenWord(gain)} and now have {newBalance} tokens total."),
                    ct).ConfigureAwait(false);
            }
            else
            {
                int newBalance = runtime.GetTokens(who);
                await sayToChannel(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{who}, you gambled {amount} {CommandTokenWord(amount)} at risk {risk} and LOST. You lost {amount} {CommandTokenWord(amount)} and now have {newBalance} tokens total."),
                    ct).ConfigureAwait(false);
            }
        };
        handlers["givetokens"] = (args, sender, ct) =>
            HandleTokenAdjustmentCommandAsync(args, sender, "givetokens", isGive: true, ct);
        handlers["removetokens"] = (args, sender, ct) =>
            HandleTokenAdjustmentCommandAsync(args, sender, "removetokens", isGive: false, ct);
        async Task HandleTokenAdjustmentCommandAsync(string[]? args, string sender, string commandName, bool isGive, CancellationToken ct)
        {
            string verb = isGive ? "gave" : "removed";
            string action = isGive ? "give" : "remove";
            string direction = isGive ? "to" : "from";
            string usage = "Usage: !" + commandName + " [username|all|random] amount";
            string who = NormalizeCommandUser(sender);
            if (!await requireAllowed(sender, commandName, ct).ConfigureAwait(false))
                return;
            if (args is null || args.Length < 1)
            {
                await sayToChannel(usage, ct).ConfigureAwait(false);
                return;
            }
            string targetToken = args.Length == 1 ? who : (args[0] ?? string.Empty).Trim().TrimStart('@');
            string amountToken = args.Length == 1 ? args[0] ?? string.Empty : args[1] ?? string.Empty;
            if (!int.TryParse(amountToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
            {
                await sayToChannel("Invalid amount. " + usage, ct).ConfigureAwait(false);
                return;
            }
            if (string.IsNullOrWhiteSpace(targetToken))
                targetToken = who;
            int delta = isGive ? amount : -amount;
            if (targetToken.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                List<string> chatters = runtime.GetKnownChattersSnapshot();
                if (chatters.Count == 0)
                {
                    await sayToChannel(who + ", there are no known viewers to " + action + " tokens " + direction + " right now.", ct).ConfigureAwait(false);
                    return;
                }
                runtime.AdjustTokens(chatters, delta);
                await sayToChannel(string.Create(CultureInfo.InvariantCulture, $"{who} {verb} {amount} {CommandTokenWord(amount)} {direction} all tracked viewers ({chatters.Count})."), ct).ConfigureAwait(false);
                return;
            }
            if (targetToken.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                List<string> chatters = runtime.GetKnownChattersSnapshot();
                if (chatters.Count == 0)
                {
                    await sayToChannel(who + ", there are no known viewers to choose from right now.", ct).ConfigureAwait(false);
                    return;
                }
                string chosen = chatters[BotMainHandler.SecureRandomInt(chatters.Count)];
                runtime.AdjustTokens(chosen, delta);
                await sayToChannel(string.Create(CultureInfo.InvariantCulture, $"{who} {verb} {amount} {CommandTokenWord(amount)} {direction} random viewer {chosen}."), ct).ConfigureAwait(false);
                return;
            }
            if (!CommandUserHelper.TryNormalizeTwitchUsername(targetToken, out string targetUsername))
            {
                await sayToChannel(who + ", please provide a valid Twitch username to " + action + " tokens " + direction + ".", ct).ConfigureAwait(false);
                return;
            }
            runtime.AdjustTokens(targetUsername, delta);
            await sayToChannel(string.Create(CultureInfo.InvariantCulture, $"{who} {verb} {amount} {CommandTokenWord(amount)} {direction} {targetUsername}."), ct).ConfigureAwait(false);
        }
        handlers["tokens"] = async (args, sender, ct) =>
        {
            string whoAsked = NormalizeCommandUser(sender);
            string queryUser = whoAsked;
            if (args is { Length: > 0 } && !CommandUserHelper.TryNormalizeTwitchUsername(args[0], out queryUser))
            {
                await sayToChannel(whoAsked + ", please provide a valid Twitch username to check tokens for.", ct).ConfigureAwait(false);
                return;
            }
            int balance = runtime.GetTokens(queryUser);
            if (args is { Length: > 0 })
                await sayToChannel(whoAsked + ", " + queryUser + " has " + balance.ToString(CultureInfo.InvariantCulture) + " " + CommandTokenWord(balance) + ".", ct).ConfigureAwait(false);
            else
                await sayToChannel(whoAsked + ", you have " + balance.ToString(CultureInfo.InvariantCulture) + " " + CommandTokenWord(balance) + ".", ct).ConfigureAwait(false);
        };
        handlers["tradetokens"] = async (args, sender, ct) =>
        {
            if (args is null || args.Length < 2)
            {
                await sayToChannel("Usage: !tradetokens username amount", ct).ConfigureAwait(false);
                return;
            }
            string rawToUser = (args[0] ?? string.Empty).Trim().Trim('@');
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount <= 0)
            {
                await sayToChannel("Invalid amount. Usage: !tradetokens username amount", ct).ConfigureAwait(false);
                return;
            }
            string fromUser = NormalizeCommandUser(sender);
            if (!CommandUserHelper.TryNormalizeTwitchUsername(rawToUser, out string toUser))
            {
                await sayToChannel(fromUser + ", please provide a valid Twitch username to trade tokens to.", ct).ConfigureAwait(false);
                return;
            }
            if (string.Equals(toUser, fromUser, StringComparison.OrdinalIgnoreCase))
            {
                await sayToChannel(fromUser + ", you cannot trade tokens to yourself.", ct).ConfigureAwait(false);
                return;
            }
            if (!runtime.TrySpendTokens(fromUser, amount))
            {
                await sayToChannel(fromUser + ", you don't have enough tokens to trade.", ct).ConfigureAwait(false);
                return;
            }
            int received = amount / 2;
            if (received > 0)
                runtime.AdjustTokens(toUser, received);
            await sayToChannel(string.Create(CultureInfo.InvariantCulture, $"{fromUser} traded {amount} tokens to {toUser}. {toUser} received {received} tokens (50%)."), ct).ConfigureAwait(false);
        };
    }
}
