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
        const int MaxAttempts = 3;
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
            catch (Exception ex) when (
                attempt < MaxAttempts &&
                ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Unable to clean up temporary test directory '{Path}': {ex.Message}");
                return;
            }
        }
    }
}
