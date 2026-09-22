using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace TwitchCraft_V1.Frames;

public partial class Setup : UserControl
{
    private static async Task<JObject> LoadVersionAsync(string versionID, Uri detailUri, CancellationToken cancellationToken)
    {
        string localPath = Environment.ExpandEnvironmentVariables($@"%APPDATA%\.minecraft\versions\{versionID}\{versionID}.json");
        if (File.Exists(localPath))
        {
            try
            {
                JObject detail = JObject.Parse(await File.ReadAllTextAsync(localPath, cancellationToken));
                string? serverUrl = (string?)detail["downloads"]?["server"]?["url"];
                string? serverSHA = (string?)detail["downloads"]?["server"]?["sha1"];
                if (string.Equals((string?)detail["id"], versionID, StringComparison.OrdinalIgnoreCase)
                    && IsValidSHA1Hex(serverSHA)
                    && Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? serverUri)
                    && serverUri.Scheme == Uri.UriSchemeHttps)
                {
                    return detail;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException)
            {
                ErrorHandling.LogNonFatal("Local Minecraft version detail manifest could not be used", ex);
            }
        }

        return JObject.Parse(await SetupHttpClient.GetStringAsync(detailUri, cancellationToken));
    }

    private static async Task EnsureServerJarAsync(HttpClient http, string serverUrl, string jarPath, string expectedSHA, long? expectedSize, CancellationToken cancellationToken)
    {
        if (!IsValidSHA1Hex(expectedSHA))
            throw new InvalidOperationException("The selected Minecraft server checksum was missing or invalid.");

        Uri downloadUri = CreateHttpsUri(serverUrl, "Minecraft server download");

        if (File.Exists(jarPath))
        {
            bool existingJarValid = await Task.Run(() => ServerJarMatches(jarPath, expectedSHA, expectedSize), cancellationToken).ConfigureAwait(false);
            if (existingJarValid)
                return;
        }

        string tempPath = FileSystemHelper.GetUniqueTempPath(jarPath);
        string backupPath = jarPath + ".bak";

        try
        {
            await DownloadJarAsync(http, downloadUri, tempPath, cancellationToken).ConfigureAwait(false);
            await Task.Run(() => VerifyServerJar(tempPath, expectedSHA, expectedSize), cancellationToken).ConfigureAwait(false);
            FileReplaceMode replaceMode = FileSystemHelper.ReplaceFile(tempPath, jarPath, backupPath, "Atomic server jar replace failed; falling back to copy");
            if (replaceMode == FileReplaceMode.Fallback)
                await Task.Run(() => VerifyServerJar(jarPath, expectedSHA, expectedSize), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is HttpRequestException || ex is InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Could not create a verified Minecraft server jar. Check the network, disk permissions, and whether another process is using the jar, then try again.\n\nJar path: " + jarPath,
                ex);
        }
        finally
        {
            FileSystemHelper.DeleteFileSafe(tempPath);
        }
    }

    private static async Task DownloadJarAsync(HttpClient http, Uri downloadUri, string outputPath, CancellationToken cancellationToken)
    {
        using CancellationTokenSource downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        downloadCts.CancelAfter(TimeSpan.FromMinutes(15));
        cancellationToken = downloadCts.Token;
        using HttpResponseMessage response = await http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && (contentLength.Value <= 0 || contentLength.Value > MaxServerJarDownloadBytes))
            throw new InvalidOperationException("The Minecraft server download size was invalid.");

        using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, DownloadBufferSize, FileOptions.SequentialScan);
        await CopyLimitedAsync(input, output, MaxServerJarDownloadBytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyLimitedAsync(Stream input, Stream output, long maxBytes, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(DownloadBufferSize);
        long totalBytes = 0;

        try
        {
            while (true)
            {
                int bytesRead = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    return;

                totalBytes += bytesRead;
                if (totalBytes > maxBytes)
                    throw new InvalidOperationException("The Minecraft server download was larger than expected.");

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Uri CreateHttpsUri(string? url, string description)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The " + description + " URL was not a valid HTTPS URL.");

        return uri;
    }

    private static bool IsValidSHA1Hex(string? expectedSHA)
    {
        if (expectedSHA == null || expectedSHA.Length != SHA1HexLength)
            return false;

        for (int i = 0; i < expectedSHA.Length; i++)
        {
            char c = expectedSHA[i];
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        return true;
    }

    private static bool ServerJarMatches(string filePath, string expectedSHA, long? expectedSize = null)
    {
        try
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, DownloadBufferSize, FileOptions.SequentialScan);
            if (expectedSize.HasValue && fs.Length != expectedSize.Value)
                return false;

            string actual = Convert.ToHexStringLower(SHA1.HashData(fs));
            return string.Equals(actual, expectedSHA, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException)
        {
            ErrorHandling.LogNonFatal("Failed to verify existing Minecraft server jar", ex);
            return false;
        }
    }

    private static void VerifyServerJar(string filePath, string expectedSHA, long? expectedSize = null)
    {
        if (!ServerJarMatches(filePath, expectedSHA, expectedSize))
            throw new InvalidOperationException("The downloaded server jar did not match the expected size or SHA-1 checksum.");
    }
}
