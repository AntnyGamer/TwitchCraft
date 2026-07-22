using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TwitchCraftBot_V1;

internal static partial class ErrorHandling
{
    private const string DefaultTitle = "TwitchCraftBot";
    private const string LogFileName = "TwitchCraftBot.log";
    private const long MaxLogBytes = 1_000_000;
    private const int MaxRetainedLogFiles = 5;
    private static readonly Lock LogGate = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly string ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    private static readonly string SessionId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    private static bool _initialized;
    private static bool _logInitialized;
    private static StreamWriter? _logWriter;

    public static void Initialize(Application? application)
    {
        if (_initialized || application == null)
            return;

        application.DispatcherUnhandledException += Application_DispatcherUnhandledException;
        application.Exit += Application_Exit;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        _initialized = true;
    }

    private static void ShowInfo(object? source, string? title, string? message)
    {
        Show(source, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ShowWarning(object? source, string? title, string? message)
    {
        Show(source, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void ShowError(object? source, string? title, string? message)
    {
        Show(source, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static MessageBoxResult ShowQuestion(
        object? source,
        string? title,
        string? message,
        MessageBoxImage image = MessageBoxImage.Question)
    {
        return Show(source, message, title, MessageBoxButton.YesNo, image);
    }

    private static MessageBoxResult Show(
        object? source,
        string? message,
        string? title,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        Window? owner = ResolveOwner(source);
        string safeMessage = message ?? string.Empty;
        string safeTitle = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title;

        return UIThread.ShowMessageBox(owner, safeMessage, safeTitle, buttons, image);
    }

    private static Window? ResolveOwner(object? source)
    {
        try
        {
            if (source is Window window)
            {
                if (window.Dispatcher.CheckAccess())
                    return window;

                return window.Dispatcher.Invoke(() => window);
            }

            if (source is FrameworkElement element)
            {
                if (element.Dispatcher.CheckAccess())
                    return Window.GetWindow(element);

                return element.Dispatcher.Invoke(() => Window.GetWindow(element));
            }

            Application? application = Application.Current;

            if (application == null)
                return null;

            if (application.Dispatcher.CheckAccess())
                return application.MainWindow;

            return application.Dispatcher.Invoke(() => application.MainWindow);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatExceptionMessage(Exception? ex)
    {
        return ex?.Message ?? "An unexpected error occurred.";
    }

    public static string FormatLogMessage(string context, Exception? ex)
    {
        return SecretRedactor.Redact(context + ": " + FormatExceptionMessage(ex));
    }

    public static void LogNonFatal(string context, Exception? ex)
    {
        Trace.TraceWarning(FormatLogMessage(context, ex));
        WriteLog("WARN", context, ex);
    }

    public static string FormatLogMessage(string context, SocketException ex)
    {
        return SecretRedactor.Redact(context + ": " + ex.SocketErrorCode);
    }

    internal static void RegisterSecrets(params string?[] secrets) => SecretRedactor.Register(secrets);

    private static void WriteLog(string level, string context, Exception? ex)
    {
        EnsureLogInitialized();

        try
        {
            var logEvent = new StructuredLogEvent
            {
                Timestamp = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
                Level = level,
                Event = SecretRedactor.Redact(context),
                ApplicationVersion = ApplicationVersion,
                SessionId = SessionId,
                ExceptionType = ex?.GetType().FullName,
                Message = SecretRedactor.Redact(ex?.Message ?? context),
                Details = ex == null ? null : SecretRedactor.Redact(ex.ToString())
            };
            string line = JsonSerializer.Serialize(logEvent, LogJsonOptions);

            lock (LogGate)
            {
                _logWriter?.WriteLine(line);
            }
        }
        catch
        {
        }
    }

    private static void EnsureLogInitialized()
    {
        if (_logInitialized)
            return;

        lock (LogGate)
        {
            if (_logInitialized)
                return;

            try
            {
                string directory = BotSetup.ConfigurationStore.WorkingDirectory;
                Directory.CreateDirectory(directory);
                string logPath = Path.Combine(directory, LogFileName);

                if (File.Exists(logPath) && new FileInfo(logPath).Length > MaxLogBytes)
                    RotateLogs(logPath);

                _logWriter = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Utf8NoBom)
                {
                    AutoFlush = true
                };
            }
            catch
            {
            }

            _logInitialized = true;
        }
    }

    private static void RotateLogs(string logPath)
    {
        for (int index = MaxRetainedLogFiles; index >= 1; index--)
        {
            string source = index == 1 ? logPath : logPath + "." + (index - 1).ToString(CultureInfo.InvariantCulture);
            if (!File.Exists(source))
                continue;

            string destination = logPath + "." + index.ToString(CultureInfo.InvariantCulture);
            File.Move(source, destination, true);
        }
    }

    private static void CloseLog()
    {
        try
        {
            lock (LogGate)
            {
                _logWriter?.Dispose();
                _logWriter = null;
            }
        }
        catch
        {
        }
    }

    private static void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Exception? ex = e.Exception;
        WriteLog("ERROR", "Unhandled UI exception", ex);
        string message;

        if (ex == null)
            message = "An unexpected error occurred.";
        else
            message = "An unexpected error occurred.\n\n" + FormatExceptionMessage(ex);

        ShowError(null, "Unexpected Error", message);
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        WriteLog("ERROR", "Unhandled application exception", e.ExceptionObject as Exception);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteLog("ERROR", "Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void Application_Exit(object sender, ExitEventArgs e)
    {
        CloseLog();
    }
}
