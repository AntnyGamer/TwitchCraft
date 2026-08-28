using System;
using System.Threading;
using System.Threading.Tasks;

namespace TwitchCraftBot_V1;

public sealed partial class BotMainHandler
{
    private async Task RunEmptyServerShutdownLoopAsync(CancellationToken cancellationToken)
    {
        DateTime emptySinceUtc = DateTime.UtcNow;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                int delayMinutes = _activeConfig?.Settings.EmptyServerShutdownDelayMinutes ?? 0;
                if (delayMinutes <= 0 || RemoteControlEnabled)
                {
                    emptySinceUtc = nowUtc;
                }
                else
                {
                    bool hasPlayers;
                    lock (_playerGate)
                        hasPlayers = _knownPlayers.Count > 0;

                    if (hasPlayers)
                    {
                        emptySinceUtc = nowUtc;
                    }
                    else if (_minecraftServerReady && nowUtc - emptySinceUtc >= TimeSpan.FromMinutes(delayMinutes))
                    {
                        AddServerLogLine("No players have been online for " + delayMinutes + " minutes. Pausing the Minecraft server.");
                        _ = Task.Run(PauseAsync, CancellationToken.None);
                        return;
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
