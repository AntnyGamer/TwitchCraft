using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraft_V1;

public static partial class CommandList
{
    private static void AddTokenHandlers(
        MainHandler runtime,
        Dictionary<string, ChatCommandHandler> handlers,
        Func<string?, CancellationToken, Task> sayToChannel,
        Func<string?, CancellationToken, Task> saySuccessfulToChannel,
        Func<string?, CancellationToken, Task> sayConfirmationToChannel,
        Func<string, string, CancellationToken, Task<bool>> requireAllowed)
    {
        handlers["gambletokens"] = async (args, sender, ct) =>
        {
            string who = NormalizeUser(sender);
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
            if (runtime.Commands.IsGambleOnCooldown(who, out TimeSpan cooldownRemaining))
            {
                await sayToChannel(string.Create(CultureInfo.InvariantCulture, $"{who}, gamble is on cooldown. Try again in {runtime.FormatCooldown(cooldownRemaining)}."), ct).ConfigureAwait(false);
                return;
            }
            if (!runtime.Tokens.TryGetBalance(who, out int balance))
            {
                await sayToChannel(who + ", token data is temporarily unavailable. Try again in a moment.", ct).ConfigureAwait(false);
                return;
            }
            if (balance < amount)
            {
                await sayToChannel(string.Create(CultureInfo.InvariantCulture, $"{who}, you must have at least {amount} tokens to gamble that amount. You currently have {balance}."), ct).ConfigureAwait(false);
                return;
            }
            double winChance = 0.9 - ((risk - 1) * 0.08888888888888889);
            double payoutMul = 1.05 + ((risk - 1) * 0.21666666666666667);
            bool win = CommandRandom.Chance(winChance);
            int gain = win ? Math.Max(1, (int)Math.Round(amount * (payoutMul - 1.0))) : 0;
            TokenAdjustmentStatus gambleStatus = runtime.Tokens.TryGamble(who, amount, win ? gain : -amount, out int newBalance, out int actualDelta);
            if (gambleStatus == TokenAdjustmentStatus.Failed)
            {
                await sayToChannel(who + ", token data is temporarily unavailable. Your gamble was not charged; try again in a moment.", ct).ConfigureAwait(false);
                return;
            }
            if (gambleStatus == TokenAdjustmentStatus.Insufficient)
            {
                await sayToChannel(string.Create(CultureInfo.InvariantCulture, $"{who}, your balance changed before the gamble could be placed. You currently have {newBalance} tokens."), ct).ConfigureAwait(false);
                return;
            }
            runtime.Commands.StartGambleCooldown(who, GambleTokenCooldown);
            if (win)
            {
                int actualGain = Math.Max(0, actualDelta);
                await saySuccessfulToChannel(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{who}, you gambled {amount} {TokenLabel(amount)} at risk {risk} and WON! You gained {actualGain} {TokenLabel(actualGain)} and now have {newBalance} tokens total."),
                    ct).ConfigureAwait(false);
            }
            else
            {
                int actualLoss = Math.Max(0, -actualDelta);
                await saySuccessfulToChannel(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{who}, you gambled {amount} {TokenLabel(amount)} at risk {risk} and LOST. You lost {actualLoss} {TokenLabel(actualLoss)} and now have {newBalance} tokens total."),
                    ct).ConfigureAwait(false);
            }
        };
        handlers["givetokens"] = (args, sender, ct) =>
            AdjustTokensAsync(args, sender, "givetokens", isGive: true, ct);
        handlers["removetokens"] = (args, sender, ct) =>
            AdjustTokensAsync(args, sender, "removetokens", isGive: false, ct);
        async Task AdjustTokensAsync(string[]? args, string sender, string commandName, bool isGive, CancellationToken ct)
        {
            string verb = isGive ? "gave" : "removed";
            string action = isGive ? "give" : "remove";
            string direction = isGive ? "to" : "from";
            string usage = "Usage: !" + commandName + " [username|all|random] amount";
            string who = NormalizeUser(sender);
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
                List<string> viewers = runtime.GetViewerRosterSnapshot();
                if (viewers.Count == 0)
                {
                    await sayToChannel(who + ", there are no known viewers to " + action + " tokens " + direction + " right now.", ct).ConfigureAwait(false);
                    return;
                }
                int adjustedCount = isGive
                    ? runtime.Tokens.Award(viewers, amount)
                    : runtime.Tokens.Adjust(viewers, delta);
                string amountDescription = !isGive || runtime.Tokens.MaximumBalance > 0
                    ? "up to " + amount.ToString(CultureInfo.InvariantCulture)
                    : amount.ToString(CultureInfo.InvariantCulture);
                if (adjustedCount == viewers.Count)
                {
                    await sayConfirmationToChannel(string.Create(CultureInfo.InvariantCulture, $"{who} {verb} {amountDescription} {TokenLabel(amount)} {direction} all live viewers ({viewers.Count})."), ct).ConfigureAwait(false);
                }
                else
                {
                    int unchangedCount = viewers.Count - adjustedCount;
                    await saySuccessfulToChannel(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{who} {verb} {amountDescription} {TokenLabel(amount)} {direction} {adjustedCount} of {viewers.Count} live viewers. {unchangedCount} balance(s) were unchanged because they were already at a limit or could not be saved."),
                        ct).ConfigureAwait(false);
                }
                return;
            }
            if (targetToken.Equals("random", StringComparison.OrdinalIgnoreCase))
            {
                List<string> viewers = runtime.GetViewerRosterSnapshot();
                if (viewers.Count == 0)
                {
                    await sayToChannel(who + ", there are no known viewers to choose from right now.", ct).ConfigureAwait(false);
                    return;
                }
                string chosen = viewers[CommandRandom.Next(viewers.Count)];
                int adjusted = isGive ? runtime.Tokens.Award(chosen, amount) : runtime.Tokens.Adjust(chosen, delta);
                int actualAmount = Math.Abs(adjusted);
                await sayConfirmationToChannel(string.Create(CultureInfo.InvariantCulture, $"{who} {verb} {actualAmount} {TokenLabel(actualAmount)} {direction} random viewer {chosen}."), ct).ConfigureAwait(false);
                return;
            }
            if (!CommandUserHelper.TryNormalizeTwitchUser(targetToken, out string targetUsername))
            {
                await sayToChannel(who + ", please provide a valid Twitch username to " + action + " tokens " + direction + ".", ct).ConfigureAwait(false);
                return;
            }
            int changed = isGive ? runtime.Tokens.Award(targetUsername, amount) : runtime.Tokens.Adjust(targetUsername, delta);
            int changedAmount = Math.Abs(changed);
            await sayConfirmationToChannel(string.Create(CultureInfo.InvariantCulture, $"{who} {verb} {changedAmount} {TokenLabel(changedAmount)} {direction} {targetUsername}."), ct).ConfigureAwait(false);
        }
        handlers["tokens"] = async (args, sender, ct) =>
        {
            string whoAsked = NormalizeUser(sender);
            string queryUser = whoAsked;
            if (args is { Length: > 0 } && !CommandUserHelper.TryNormalizeTwitchUser(args[0], out queryUser))
            {
                await sayToChannel(whoAsked + ", please provide a valid Twitch username to check tokens for.", ct).ConfigureAwait(false);
                return;
            }
            if (!runtime.Tokens.TryGetBalance(queryUser, out int balance))
            {
                await sayToChannel(whoAsked + ", token data is temporarily unavailable. Try again in a moment.", ct).ConfigureAwait(false);
                return;
            }
            if (args is { Length: > 0 })
                await saySuccessfulToChannel(whoAsked + ", " + queryUser + " has " + balance.ToString(CultureInfo.InvariantCulture) + " " + TokenLabel(balance) + ".", ct).ConfigureAwait(false);
            else
                await saySuccessfulToChannel(whoAsked + ", you have " + balance.ToString(CultureInfo.InvariantCulture) + " " + TokenLabel(balance) + ".", ct).ConfigureAwait(false);
        };
        handlers["tokenleaderboard"] = async (_, sender, ct) =>
        {
            string whoAsked = NormalizeUser(sender);
            if (!runtime.Tokens.TryGetTopBalances(5, out IReadOnlyList<KeyValuePair<string, int>> leaders))
            {
                await sayToChannel(whoAsked + ", token data is temporarily unavailable. Try again in a moment.", ct).ConfigureAwait(false);
                return;
            }
            if (leaders.Count == 0)
            {
                await saySuccessfulToChannel(whoAsked + ", no viewers have tokens yet.", ct).ConfigureAwait(false);
                return;
            }

            List<string> places = new(leaders.Count);
            for (int i = 0; i < leaders.Count; i++)
            {
                KeyValuePair<string, int> leader = leaders[i];
                places.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{i + 1}. {leader.Key} ({leader.Value} {TokenLabel(leader.Value)})"));
            }
            await saySuccessfulToChannel(whoAsked + ", top token holders: " + string.Join("; ", places) + ".", ct).ConfigureAwait(false);
        };
        handlers["tokenrank"] = async (args, sender, ct) =>
        {
            string whoAsked = NormalizeUser(sender);
            string queryUser = whoAsked;
            if (args is { Length: > 0 } && !CommandUserHelper.TryNormalizeTwitchUser(args[0], out queryUser))
            {
                await sayToChannel(whoAsked + ", please provide a valid Twitch username to check the token rank for.", ct).ConfigureAwait(false);
                return;
            }

            if (!runtime.Tokens.TryGetRank(queryUser, out TokenRankResult? rank))
            {
                await sayToChannel(whoAsked + ", token data is temporarily unavailable. Try again in a moment.", ct).ConfigureAwait(false);
                return;
            }
            if (rank is not TokenRankResult result)
            {
                string subject = string.Equals(queryUser, whoAsked, StringComparison.OrdinalIgnoreCase) ? "you are" : queryUser + " is";
                await saySuccessfulToChannel(whoAsked + ", " + subject + " not ranked yet because the balance is 0 tokens.", ct).ConfigureAwait(false);
                return;
            }

            if (string.Equals(queryUser, whoAsked, StringComparison.OrdinalIgnoreCase))
            {
                await saySuccessfulToChannel(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{whoAsked}, you are #{result.Rank} with {result.Balance} {TokenLabel(result.Balance)}."), ct).ConfigureAwait(false);
            }
            else
            {
                await saySuccessfulToChannel(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{whoAsked}, {result.Username} is #{result.Rank} with {result.Balance} {TokenLabel(result.Balance)}."), ct).ConfigureAwait(false);
            }
        };
        handlers["tradetokens"] = async (args, sender, ct) =>
        {
            if (args is null || args.Length < 2)
            {
                await sayToChannel("Usage: !tradetokens username amount", ct).ConfigureAwait(false);
                return;
            }
            string rawToUser = (args[0] ?? string.Empty).Trim().Trim('@');
            if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount < 2)
            {
                await sayToChannel("Invalid amount. Usage: !tradetokens username amount", ct).ConfigureAwait(false);
                return;
            }
            string fromUser = NormalizeUser(sender);
            if (!CommandUserHelper.TryNormalizeTwitchUser(rawToUser, out string toUser))
            {
                await sayToChannel(fromUser + ", please provide a valid Twitch username to trade tokens to.", ct).ConfigureAwait(false);
                return;
            }
            if (string.Equals(toUser, fromUser, StringComparison.OrdinalIgnoreCase))
            {
                await sayToChannel(fromUser + ", you cannot trade tokens to yourself.", ct).ConfigureAwait(false);
                return;
            }
            int? received = runtime.Tokens.TryTransfer(fromUser, toUser, amount);
            if (received is null or < 0)
            {
                await sayToChannel(received is < 0 ? fromUser + ", you don't have enough tokens to trade." : fromUser + ", the token trade could not be completed. Try again.", ct).ConfigureAwait(false);
                return;
            }
            await sayConfirmationToChannel(string.Create(CultureInfo.InvariantCulture, $"{fromUser} traded {amount} tokens to {toUser}. {toUser} received {received} tokens."), ct).ConfigureAwait(false);
        };
    }
}
