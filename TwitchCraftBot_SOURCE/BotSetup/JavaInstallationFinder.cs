using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TwitchCraftBot_V1.BotSetup;

internal static partial class JavaInstallationFinder
{
    [GeneratedRegex("version\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex JavaVersionRegex();

    public static (string JavaExe, string JavaHome) FindMatching(int javaVersion)
    {
        HashSet<string> seenJavaHomes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? candidate in GetJavaHomeCandidates())
        {
            string javaHome = NormalizeJavaHomeCandidate(candidate);
            if (javaHome.Length == 0 || !seenJavaHomes.Add(javaHome))
                continue;

            if (TryGetJavaExecutable(javaHome, out string javaExe)
                && TryGetJavaMajorVersion(javaExe, out int major)
                && major == javaVersion)
            {
                return (javaExe, javaHome);
            }
        }

        return (string.Empty, string.Empty);
    }

    private static string NormalizeJavaHomeCandidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        string trimmed = path.Trim();
        try
        {
            string fullPath = Path.GetFullPath(trimmed);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            return fullPath.Length > root.Length
                ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : fullPath;
        }
        catch
        {
            return trimmed;
        }
    }

    private static IEnumerable<string?> GetJavaHomeCandidates()
    {
        yield return Environment.GetEnvironmentVariable("JAVA_HOME");
        yield return Environment.GetEnvironmentVariable("JAVA_HOME", EnvironmentVariableTarget.User);
        yield return Environment.GetEnvironmentVariable("JAVA_HOME", EnvironmentVariableTarget.Machine);

        foreach (string? home in GetPathJavaHomes())
            yield return home;

        foreach (string? home in GetCommonJavaHomes())
            yield return home;
    }

    private static IEnumerable<string?> GetPathJavaHomes()
    {
        string?[] pathValues =
        [
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
        ];

        foreach (string? pathValue in pathValues)
        {
            if (string.IsNullOrWhiteSpace(pathValue))
                continue;

            foreach (string segment in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string javaHome;
                try
                {
                    if (!File.Exists(Path.Combine(segment, "java.exe")) && !File.Exists(Path.Combine(segment, "javaw.exe")))
                        continue;

                    DirectoryInfo directory = new(segment);
                    javaHome = string.Equals(directory.Name, "bin", StringComparison.OrdinalIgnoreCase)
                        ? directory.Parent?.FullName ?? string.Empty
                        : directory.FullName;
                }
                catch
                {
                    continue;
                }

                if (javaHome.Length > 0)
                    yield return javaHome;
            }
        }
    }

    private static IEnumerable<string?> GetCommonJavaHomes()
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        ];

        string[] vendors = ["Java", "Eclipse Adoptium", "Microsoft", "BellSoft", "Zulu", "Amazon Corretto", "Semeru", "Oracle"];

        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            foreach (string vendor in vendors)
            {
                string vendorRoot = Path.Combine(root, vendor);
                if (!Directory.Exists(vendorRoot))
                    continue;

                yield return vendorRoot;

                IEnumerable<string> homes;
                try
                {
                    homes = Directory.EnumerateDirectories(vendorRoot);
                }
                catch
                {
                    continue;
                }

                foreach (string home in homes)
                    yield return home;
            }
        }
    }

    private static bool TryGetJavaExecutable(string? path, out string javaExe)
    {
        javaExe = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            string home = path.Trim();
            string[] candidates =
            [
                Path.Combine(home, "bin", "javaw.exe"),
                Path.Combine(home, "bin", "java.exe"),
                Path.Combine(home, "javaw.exe"),
                Path.Combine(home, "java.exe")
            ];

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    javaExe = candidate;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryGetJavaMajorVersion(string javaExe, out int major)
    {
        major = 0;

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = javaExe,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(psi);
            if (process == null)
                return false;

            StringBuilder output = new();
            object outputGate = new();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (outputGate)
                        output.AppendLine(e.Data);
                }
            };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    lock (outputGate)
                        output.AppendLine(e.Data);
                }
            };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            if (!process.WaitForExit(3000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            process.WaitForExit();
            string versionOutput;
            lock (outputGate)
                versionOutput = output.ToString();

            Match match = JavaVersionRegex().Match(versionOutput);
            if (!match.Success)
                return false;

            ReadOnlySpan<char> version = match.Groups[1].Value.AsSpan().Trim();
            version = version.StartsWith("1.", StringComparison.Ordinal) ? version[2..] : version;
            int dot = version.IndexOf('.');
            return int.TryParse(dot >= 0 ? version[..dot] : version, out major) && major > 0;
        }
        catch
        {
            return false;
        }
    }
}
