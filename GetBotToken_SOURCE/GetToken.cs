using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace GetBotToken;

internal static class GetToken
{
    private const int ListenPort = 80;
    private const string TwitchAuthorizeBase = "https://id.twitch.tv/oauth2/authorize";
    private const string BotScopes = "chat:read chat:edit moderator:read:chatters";
    private static readonly TimeSpan ListenTimeout = TimeSpan.FromMinutes(5);

    private static void Main()
    {
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Clear();
        Console.WriteLine();

        string clientId = PromptWithConfirm(
            "Please input your Client ID from dev.twitch.tv: ",
            s => !string.IsNullOrWhiteSpace(s),
            "Client ID can't be empty.");

        string redirectUrl = PromptWithConfirm(
            "Please input your Redirect URL from dev.twitch.tv: ",
            IsValidRedirectUrl,
            "Redirect URL must be exactly http://localhost or http://localhost/");

        string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string authUrl =
            $"{TwitchAuthorizeBase}?response_type=token" +
            $"&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUrl)}" +
            $"&scope={Uri.EscapeDataString(BotScopes)}" +
            $"&state={Uri.EscapeDataString(state)}";

        if (!TryCreateLoopbackServer(ListenPort, state, out var server, out string bindError) || server is null)
        {
            ShowError($"ERROR: Could not bind http://localhost on port {ListenPort}.");
            Console.WriteLine(bindError);
            Console.WriteLine();
            Console.WriteLine($"Close anything else using localhost:{ListenPort} and try again.");
            Console.WriteLine("Common causes: IIS, Web Deploy, Apache, Nginx, Docker, Skype, or another token tool instance.");
            Console.ReadKey();
            return;
        }

        using (server)
        {
            Console.WriteLine();
            Console.WriteLine("Opening browser for Twitch authorization.");
            Console.WriteLine("The Redirect URL for your bot must match exactly what you entered.");
            Console.WriteLine();

            OpenBrowser(authUrl);

            if (server.WaitForToken(ListenTimeout, out string token, out string error))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: Your bot token is below:");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine(token);
                Console.WriteLine();
                Console.WriteLine("Input this into the TwitchCraft setup to use the bot.");
            }
            else
            {
                ShowError("ERROR: Failed to receive the token from localhost.");
                Console.WriteLine(error);
                Console.WriteLine();
                Console.WriteLine("Make sure the Redirect URL in your Twitch app exactly matches what you entered here.");
                Console.WriteLine("For this file, use exactly: http://localhost or http://localhost/");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to end.");
        Console.ReadKey();
    }


    private static bool IsValidRedirectUrl(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (!Uri.TryCreate(s.Trim(), UriKind.Absolute, out var uri) || uri is null) return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && (uri.IsDefaultPort || uri.Port == ListenPort)
            && (uri.AbsolutePath.Length == 0 || uri.AbsolutePath == "/");
    }

    private static string PromptWithConfirm(string prompt, Func<string, bool> validate, string error)
    {
        while (true)
        {
            Console.Write(prompt);
            string value = (Console.ReadLine() ?? string.Empty).Trim();

            if (!validate(value))
            {
                ShowError("ERROR: " + error);
                Console.WriteLine();
                continue;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("You entered: " + value);
            Console.ResetColor();

            if (PromptYesNo("Is this correct? "))
                return value;

            Console.WriteLine("Please re-enter.\n");
        }
    }

    private static bool PromptYesNo(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("Y");
            Console.ResetColor();
            Console.Write("/");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("N");
            Console.ResetColor();
            Console.Write(": ");

            switch ((Console.ReadLine() ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "Y" or "YES": return true;
                case "N" or "NO": return false;
                default: Console.WriteLine("Please type Y or N."); break;
            }
        }
    }

    private static bool TryCreateLoopbackServer(int port, string expectedState, out LoopbackTokenServer? server, out string error)
    {
        bool hasIpv4 = TryStartListener(IPAddress.Loopback, port, out var ipv4, out string ipv4Error);
        bool hasIpv6 = TryStartListener(IPAddress.IPv6Loopback, port, out var ipv6, out string ipv6Error);

        if (hasIpv4 || hasIpv6)
        {
            server = new LoopbackTokenServer(ipv4, ipv6, expectedState);
            error = string.Empty;
            return true;
        }

        server = null;
        error = CombineErrors(ipv4Error, ipv6Error);
        return false;
    }

    private static bool TryStartListener(IPAddress address, int port, out TcpListener? listener, out string error)
    {
        listener = null;
        error = string.Empty;

        try
        {
            listener = new TcpListener(address, port);
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                listener.Server.DualMode = false;

            listener.Start(20);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { listener?.Stop(); } catch { }
            listener = null;
            return false;
        }
    }

    private static string CombineErrors(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second;
        if (string.IsNullOrWhiteSpace(second) || first == second) return first;
        return $"IPv4: {first}{Environment.NewLine}IPv6: {second}";
    }

    private static void OpenBrowser(string url)
    {
        if (TryStartProcess(new ProcessStartInfo { FileName = url, UseShellExecute = true, Verb = "open" }))
            return;

        if (TryStartProcess(new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = $"/c start \"\" \"{url}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        }))
            return;

        ShowError("ERROR: Failed to open your browser. Copy/paste this URL into a browser:");
        Console.WriteLine(url);
    }

    private static bool TryStartProcess(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ShowError(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private sealed class LoopbackTokenServer : IDisposable
    {
        private const int MaxRequestBytes = 16384;
        private const int MaxBodyBytes = 8192;

        private readonly TcpListener? _ipv4;
        private readonly TcpListener? _ipv6;
        private readonly string _expectedState;
        private readonly TaskCompletionSource<string> _tokenSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Task> _workers = [];
        private int _shutdownStarted;

        public LoopbackTokenServer(TcpListener? ipv4, TcpListener? ipv6, string expectedState)
        {
            _ipv4 = ipv4;
            _ipv6 = ipv6;
            _expectedState = expectedState;

            if (_ipv4 is not null) _workers.Add(AcceptLoopAsync(_ipv4, _cts.Token));
            if (_ipv6 is not null) _workers.Add(AcceptLoopAsync(_ipv6, _cts.Token));
        }

        public bool WaitForToken(TimeSpan timeout, out string token, out string error)
        {
            try
            {
                if (_tokenSource.Task.Wait(timeout))
                {
                    token = _tokenSource.Task.GetAwaiter().GetResult();
                    error = string.Empty;
                    return true;
                }

                BeginShutdown();
                token = string.Empty;
                error = "Timed out waiting for Twitch to return the token.";
                return false;
            }
            catch (Exception ex)
            {
                BeginShutdown();
                token = string.Empty;
                error = ex is AggregateException aggregate ? aggregate.GetBaseException().Message : ex.Message;
                return false;
            }
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    _ = HandleClientAsync(client, cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    using NetworkStream stream = client.GetStream();
                    HttpRequest request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);

                    switch (request)
                    {
                        case { Method: "POST", Path: "/token" }:
                            await HandleTokenPostAsync(stream, request.Body).ConfigureAwait(false);
                            break;
                        case { Method: "GET", Path: "/favicon.ico" }:
                            await WriteResponseAsync(stream, "204 No Content", "text/plain; charset=utf-8", string.Empty, cancellationToken).ConfigureAwait(false);
                            break;
                        case { Method: "GET", Path: "/" }:
                            await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", BuildHtmlPage(), cancellationToken).ConfigureAwait(false);
                            break;
                        default:
                            await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", "Bad request.", cancellationToken).ConfigureAwait(false);
                            break;
                    }
                }
                catch
                {
                }
            }
        }

        private async Task HandleTokenPostAsync(NetworkStream stream, string body)
        {
            string accessToken = ExtractFormValue(body, "access_token");
            string state = ExtractFormValue(body, "state");

            if (!string.Equals(state, _expectedState, StringComparison.Ordinal))
            {
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", "Invalid authorization state.", CancellationToken.None).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", "Missing access token.", CancellationToken.None).ConfigureAwait(false);
                return;
            }

            await WriteResponseAsync(stream, "200 OK", "text/plain; charset=utf-8", "OK", CancellationToken.None).ConfigureAwait(false);

            if (_tokenSource.TrySetResult(accessToken))
                BeginShutdown();
        }

        private void BeginShutdown()
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;

            _cts.Cancel();
            try { _ipv4?.Stop(); } catch { }
            try { _ipv6?.Stop(); } catch { }
        }

        private static async Task<HttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            static HttpRequest Bad() => new(string.Empty, string.Empty, string.Empty);

            byte[] buffer = new byte[MaxRequestBytes];
            int total = 0;
            int headerEnd = -1;

            while (total < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken).ConfigureAwait(false);
                if (read <= 0) break;

                total += read;
                headerEnd = FindHeaderEnd(buffer, total);
                if (headerEnd >= 0) break;
            }

            if (headerEnd < 0) return Bad();

            string headerText = Encoding.UTF8.GetString(buffer, 0, headerEnd);
            string[] lines = headerText.Split(["\r\n"], StringSplitOptions.None);
            string[] first = (lines.Length > 0 ? lines[0] : string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (first.Length < 3 || first[1].Length == 0 || first[1][0] != '/') return Bad();

            string method = first[0].Trim().ToUpperInvariant();
            string rawTarget = first[1].Trim();
            int queryIndex = rawTarget.IndexOf('?');
            string path = queryIndex >= 0 ? rawTarget[..queryIndex] : rawTarget;

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) return Bad();

                string name = lines[i][..colon].Trim();
                string value = lines[i][(colon + 1)..].Trim();

                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    (!int.TryParse(value, out contentLength) || contentLength < 0 || contentLength > MaxBodyBytes))
                    return Bad();
            }

            int bodyStart = headerEnd + 4;
            int bufferedBodyBytes = Math.Max(0, total - bodyStart);
            byte[] bodyBytes = contentLength > 0 ? new byte[contentLength] : [];

            if (contentLength > 0)
            {
                if (bufferedBodyBytes > 0)
                {
                    int copied = Math.Min(bufferedBodyBytes, contentLength);
                    Buffer.BlockCopy(buffer, bodyStart, bodyBytes, 0, copied);
                    bufferedBodyBytes = copied;
                }

                while (bufferedBodyBytes < contentLength)
                {
                    int read = await stream.ReadAsync(bodyBytes.AsMemory(bufferedBodyBytes, contentLength - bufferedBodyBytes), cancellationToken).ConfigureAwait(false);
                    if (read <= 0) return Bad();
                    bufferedBodyBytes += read;
                }
            }

            return new(method, path, bodyBytes.Length > 0 ? Encoding.UTF8.GetString(bodyBytes) : string.Empty);
        }

        private static int FindHeaderEnd(byte[] data, int count)
        {
            for (int i = 0; i <= count - 4; i++)
                if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
                    return i;

            return -1;
        }

        private static string ExtractFormValue(string formBody, string key)
        {
            if (string.IsNullOrEmpty(formBody) || string.IsNullOrEmpty(key)) return string.Empty;

            foreach (string part in formBody.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = part.IndexOf('=');
                if (equals < 0) continue;

                string name = Uri.UnescapeDataString(part[..equals].Replace('+', ' '));
                if (!string.Equals(name, key, StringComparison.Ordinal)) continue;

                return Uri.UnescapeDataString(part[(equals + 1)..].Replace('+', ' '));
            }

            return string.Empty;
        }

        private static async Task WriteResponseAsync(NetworkStream stream, string status, string contentType, string body, CancellationToken cancellationToken)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            byte[] headerBytes = Encoding.UTF8.GetBytes(
                "HTTP/1.1 " + status + "\r\n" +
                "Connection: close\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + bodyBytes.Length + "\r\n" +
                "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
                "Pragma: no-cache\r\n\r\n");

            await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            if (bodyBytes.Length > 0)
                await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        }

        private string BuildHtmlPage()
        {
            return "<!DOCTYPE html><html><head><meta charset='utf-8'><title>TwitchCraft</title></head><body>"
                + "<div id='msg' style='font-family:Inter;padding:18px;white-space:pre-wrap;font-size:34px;line-height:1.2'></div>"
                + "<script>"
                + "(function(){"
                + "var expectedState='" + _expectedState + "';"
                + "var e=document.getElementById('msg');"
                + "function set(t){e.textContent=t;}"
                + "function showToken(t){set('Token found, but failed to send it to the app. Your bot token is below:\\n\\n'+t+'\\n\\nInput this into the TwitchCraft setup to use the bot.');}"
                + "function send(t,s,tries){"
                + "set(tries<5?'Received token. Retrying app handoff...':'Received token. Sending it to the app...');"
                + "fetch('/token',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:'access_token='+encodeURIComponent(t)+'&state='+encodeURIComponent(s)})"
                + ".then(function(r){if(!r.ok) throw new Error('HTTP '+r.status); set('Your bot token is: '+t+'\\n\\nInput this into the TwitchCraft setup to use the bot!');})"
                + ".catch(function(){if(tries>0){setTimeout(function(){send(t,s,tries-1);},500);return;}showToken(t);});"
                + "}"
                + "try{"
                + "var h=window.location.hash||'';"
                + "var p=new URLSearchParams(h.charAt(0)==='#'?h.substring(1):h);"
                + "var t=p.get('access_token');"
                + "var s=p.get('state')||'';"
                + "var err=p.get('error_description')||p.get('error');"
                + "if(err){set('Twitch returned an error: '+err);return;}"
                + "if(!t){set('Waiting for Twitch token...');return;}"
                + "if(s!==expectedState){set('Invalid Twitch authorization response. Please close this page and try again.');return;}"
                + "history.replaceState(null,'','/');"
                + "send(t,s,5);"
                + "}catch(ex){set('Error parsing token.');}"
                + "})();"
                + "</script></body></html>";
        }

        public void Dispose()
        {
            BeginShutdown();
            try { Task.WaitAll([.. _workers], TimeSpan.FromSeconds(1)); } catch { }
            _cts.Dispose();
        }

        private readonly record struct HttpRequest(string Method, string Path, string Body);
    }
}