using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace TwitchCraft_V1;

internal sealed class RollingJsonLogWriter : IDisposable
{
    private readonly Lock _gate = new();
    private readonly string _logPath;
    private readonly long _maxBytes;
    private readonly int _maxRetainedFiles;
    private readonly Encoding _encoding;
    private readonly int _newLineByteCount;
    private StreamWriter? _writer;
    private long _currentBytes;
    private bool _disposed;
    private bool _retentionCleaned;

    internal RollingJsonLogWriter(
        string logPath,
        long maxBytes,
        int maxRetainedFiles,
        Encoding encoding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedFiles);
        ArgumentNullException.ThrowIfNull(encoding);

        _logPath = logPath;
        _maxBytes = maxBytes;
        _maxRetainedFiles = maxRetainedFiles;
        _encoding = encoding;
        _newLineByteCount = encoding.GetByteCount(Environment.NewLine);
    }

    internal bool TryWriteLine(string line)
    {
        if (line == null)
            return false;

        lock (_gate)
        {
            if (_disposed)
                return false;

            try
            {
                EnsureWriter();
                long pendingBytes = _encoding.GetByteCount(line) + _newLineByteCount;

                if (_currentBytes >= _maxBytes ||
                    (_currentBytes > 0 && pendingBytes > _maxBytes - _currentBytes))
                {
                    Rotate();
                }

                _writer!.WriteLine(line);
                _currentBytes += pendingBytes;
                return true;
            }
            catch
            {
                CloseWriter();
                return false;
            }
        }
    }

    private void EnsureWriter()
    {
        if (_writer != null)
            return;

        string? directory = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (!_retentionCleaned)
        {
            CleanupOldLogs(directory);
            _retentionCleaned = true;
        }

        FileStream stream = new(_logPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            if (stream.Length > 0)
            {
                stream.Position = stream.Length - 1;
                if (stream.ReadByte() != '\n') RepairPartialLine(stream);
            }
            stream.Position = _currentBytes = stream.Length;
            _writer = new StreamWriter(stream, _encoding) { AutoFlush = true };
        }
        catch { try { stream.Dispose(); } catch { } throw; }
    }

    private void RepairPartialLine(FileStream stream)
    {
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.Position = 0;
        stream.ReadExactly(bytes);
        int cut = Array.LastIndexOf(bytes, (byte)'\n') + 1;
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(cut));
            stream.Position = stream.Length;
            string missingNewline = bytes[^1] == (byte)'\r' && Environment.NewLine.StartsWith('\r')
                ? "\n"
                : Environment.NewLine;
            stream.Write(_encoding.GetBytes(missingNewline));
        }
        catch (System.Text.Json.JsonException) { stream.SetLength(cut); }
    }

    private void Rotate()
    {
        CloseWriter();

        if (_maxRetainedFiles == 0)
        {
            File.Delete(_logPath);
            EnsureWriter();
            return;
        }

        for (int index = _maxRetainedFiles; index >= 1; index--)
        {
            string source = index == 1
                ? _logPath
                : GetLogPath(index - 1);
            if (!File.Exists(source))
                continue;

            File.Move(source, GetLogPath(index), true);
        }

        EnsureWriter();
    }

    private void CleanupOldLogs(string? directory)
    {
        try
        {
            string searchDirectory = string.IsNullOrWhiteSpace(directory)
                ? Environment.CurrentDirectory
                : directory;
            string fileName = Path.GetFileName(_logPath);

            foreach (string path in Directory.EnumerateFiles(searchDirectory, fileName + ".old*"))
            {
                string suffix = Path.GetFileName(path)[(fileName.Length + 4)..];
                if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
                    index > _maxRetainedFiles)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
    }

    private string GetLogPath(int index) =>
        _logPath + ".old" + index.ToString(CultureInfo.InvariantCulture);

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _writer = null;
            _currentBytes = 0;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            CloseWriter();
        }
    }
}
