namespace TwitchCraftBot.Tests.TestInfrastructure;

internal sealed class TemporaryDirectory : IDisposable
{
    internal TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "TwitchCraftTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    internal string Path { get; }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        const int MaxAttempts = 10;
        Exception? lastException = null;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastException = ex;
                if (attempt < MaxAttempts)
                    Thread.Sleep(50 * attempt);
            }
        }

        throw new IOException(
            $"Unable to clean up temporary test directory '{Path}'.",
            lastException);
    }
}
