using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GetBotToken;

internal static class GetToken
{
    private const string TwitchAuthorizeUrl = "https://id.twitch.tv/oauth2/authorize";
    private const string TwitchValidateUrl = "https://id.twitch.tv/oauth2/validate";
    private const string BotScopes = "chat:read chat:edit moderator:read:chatters";
    private const int DefaultPort = 3000;

    private static readonly TimeSpan ListenTimeout = TimeSpan.FromMinutes(5);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static async Task Main()
    {
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Clear();
        Console.WriteLine();

        string clientId = PromptClientId();
        RedirectInfo redirect = PromptRedirectUrl();
        string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string authUrl =
            $"{TwitchAuthorizeUrl}?response_type=token" +
            $"&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirect.Url)}" +
            $"&scope={Uri.EscapeDataString(BotScopes)}" +
            $"&state={state}";

        if (!LoopbackTokenServer.TryCreate(redirect.Port, state, clientId, out var server, out string bindError))
        {
            ShowError($"ERROR: Could not bind localhost on port {redirect.Port}.");
            Console.WriteLine(bindError);
            Console.WriteLine();
            Console.WriteLine($"Close anything using localhost:{redirect.Port} and try again.");
            Console.WriteLine("Common causes include another token tool, IIS, Apache, Nginx, or Docker.");
            EndProgram();
            return;
        }

        using (server)
        {
            Console.WriteLine();
            Console.WriteLine("Opening browser for Twitch authorization.");
            Console.WriteLine($"Redirect URL: {redirect.Url}");
            Console.WriteLine("This must exactly match the Redirect URL in your Twitch app.");
            Console.WriteLine();

            OpenBrowser(authUrl);
            AuthResult result = await server.WaitForResultAsync(ListenTimeout);

            if (result.Error.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS: Your bot token is below:");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine(result.Token);
                Console.WriteLine();
                Console.WriteLine($"Authorized Twitch account: {result.Login}");
                Console.WriteLine("Input this token into the TwitchCraft setup to use the bot.");
            }
            else
            {
                ShowError("ERROR: Failed to receive a valid token.");
                Console.WriteLine(result.Error);
                Console.WriteLine();
                Console.WriteLine("Make sure the Redirect URL in your Twitch app exactly matches the URL above.");
            }
        }

        EndProgram();
    }

    private static string PromptClientId()
    {
        while (true)
        {
            Console.Write("Please input your Client ID from dev.twitch.tv: ");
            string value = (Console.ReadLine() ?? string.Empty).Trim();

            if (value.Length == 0)
            {
                ShowError("ERROR: Client ID can't be empty.");
                Console.WriteLine();
                continue;
            }

            if (Confirm(value)) return value;
            Console.WriteLine("Please re-enter.\n");
        }
    }

    private static RedirectInfo PromptRedirectUrl()
    {
        while (true)
        {
            Console.Write("Please input your Redirect URL from dev.twitch.tv: ");
            string value = (Console.ReadLine() ?? string.Empty).Trim();

            if (!TryNormalizeRedirectUrl(value, out RedirectInfo redirect))
            {
                ShowError("ERROR: Use http://localhost with an optional port from 1 to 65535. Do not add a path, query, or fragment.");
                Console.WriteLine();
                continue;
            }

            if (Confirm(redirect.Url)) return redirect;
            Console.WriteLine("Please re-enter.\n");
        }
    }

    private static bool TryNormalizeRedirectUrl(string value, out RedirectInfo redirect)
    {
        const string prefix = "http://localhost";
        redirect = default;

        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        ReadOnlySpan<char> suffix = value.AsSpan(prefix.Length);
        bool hasTrailingSlash = !suffix.IsEmpty && suffix[^1] == '/';
        if (hasTrailingSlash) suffix = suffix[..^1];

        int port = DefaultPort;
        if (!suffix.IsEmpty)
        {
            ReadOnlySpan<char> portText = suffix[1..];
            if (suffix[0] != ':' || portText.IsEmpty) return false;

            foreach (char character in portText)
                if (!char.IsAsciiDigit(character)) return false;

            if (!int.TryParse(portText, out port) || port is < 1 or > 65535)
                return false;
        }

        redirect = new($"http://localhost:{port}" + (hasTrailingSlash ? "/" : string.Empty), port);
        return true;
    }

    private static bool Confirm(string value)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("You entered: " + value);
        Console.ResetColor();

        while (true)
        {
            Console.Write("Is this correct? ");
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

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ShowError("ERROR: Failed to open your browser. Copy and paste this URL into a browser:");
            Console.WriteLine(url);
        }
    }

    private static void ShowError(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void EndProgram()
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to end.");
        Console.ReadKey();
    }

    private readonly record struct RedirectInfo(string Url, int Port);
    private readonly record struct AuthResult(string Token, string Login, string Error);

    private sealed class LoopbackTokenServer : IDisposable
    {
        private const int MaxRequestBytes = 16_384;
        private const int MaxBodyBytes = 4_096;

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly TcpListener? _ipv4;
        private readonly TcpListener? _ipv6;
        private readonly string _expectedState;
        private readonly string _clientId;
        private readonly string _htmlPage;
        private readonly CancellationTokenSource _cts = new();
        private readonly TaskCompletionSource<AuthResult> _resultSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task[] _workers;
        private int _shutdownStarted;

        private LoopbackTokenServer(
            TcpListener? ipv4,
            TcpListener? ipv6,
            string expectedState,
            string clientId)
        {
            _ipv4 = ipv4;
            _ipv6 = ipv6;
            _expectedState = expectedState;
            _clientId = clientId;
            _htmlPage = BuildHtmlPage(expectedState);

            _workers = (ipv4, ipv6) switch
            {
                (not null, not null) => [AcceptLoopAsync(ipv4, _cts.Token), AcceptLoopAsync(ipv6, _cts.Token)],
                (not null, null) => [AcceptLoopAsync(ipv4, _cts.Token)],
                (null, not null) => [AcceptLoopAsync(ipv6, _cts.Token)],
                _ => []
            };
        }

        public static bool TryCreate(
            int port,
            string expectedState,
            string clientId,
            out LoopbackTokenServer server,
            out string error)
        {
            TcpListener? ipv4 = StartListener(IPAddress.Loopback, port, out string ipv4Error);
            TcpListener? ipv6 = StartListener(IPAddress.IPv6Loopback, port, out string ipv6Error);

            if (ipv4 is not null || ipv6 is not null)
            {
                server = new(ipv4, ipv6, expectedState, clientId);
                error = string.Empty;
                return true;
            }

            server = null!;
            error = string.IsNullOrWhiteSpace(ipv4Error)
                ? ipv6Error
                : string.IsNullOrWhiteSpace(ipv6Error) || ipv4Error == ipv6Error
                    ? ipv4Error
                    : $"IPv4: {ipv4Error}{Environment.NewLine}IPv6: {ipv6Error}";
            return false;
        }

        private static TcpListener? StartListener(IPAddress address, int port, out string error)
        {
            TcpListener? listener = null;

            try
            {
                listener = new(address, port);
                listener.Server.ExclusiveAddressUse = true;
                listener.Start(8);
                error = string.Empty;
                return listener;
            }
            catch (Exception ex)
            {
                StopListener(listener);
                error = ex.Message;
                return null;
            }
        }

        public async Task<AuthResult> WaitForResultAsync(TimeSpan timeout)
        {
            try
            {
                return await _resultSource.Task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                BeginShutdown();
                return new(string.Empty, string.Empty, "Timed out waiting for Twitch to return the token.");
            }
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                    await HandleClientAsync(client, cancellationToken);
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        CompleteFailure("The local token listener failed: " + ex.Message);
                    break;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(RequestTimeout);
            NetworkStream? stream = null;

            try
            {
                client.NoDelay = true;
                stream = client.GetStream();
                HttpRequest request = await ReadRequestAsync(stream, readCts.Token);

                if (request.Method == "GET" && request.Path == "/")
                {
                    await WriteResponseAsync(stream, "200 OK", "text/html; charset=utf-8", _htmlPage, cancellationToken);
                }
                else if (request.Method == "POST" && request.Path == "/token")
                {
                    await HandleTokenPostAsync(stream, request.Body, cancellationToken);
                }
                else if (request.Method == "GET" && request.Path == "/favicon.ico")
                {
                    await WriteResponseAsync(stream, "204 No Content", "text/plain; charset=utf-8", string.Empty, cancellationToken);
                }
                else
                {
                    await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", "Bad request.", cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                if (!cancellationToken.IsCancellationRequested)
                    await TryWriteErrorAsync(stream, "408 Request Timeout", "Request timed out.");
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                Debug.WriteLine(ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                await TryWriteErrorAsync(stream, "500 Internal Server Error", "Local server error.");
            }
        }

        private async Task HandleTokenPostAsync(
            NetworkStream stream,
            string body,
            CancellationToken cancellationToken)
        {
            ParseForm(body, out string token, out string state, out string authError);

            if (!string.Equals(state, _expectedState, StringComparison.Ordinal))
            {
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", "Invalid authorization state.", cancellationToken);
                return;
            }

            if (authError.Length != 0)
            {
                string error = "Twitch authorization failed: " + SanitizeMessage(authError);
                CompleteFailure(error);
                await WriteResponseAsync(stream, "200 OK", "text/plain; charset=utf-8", error, CancellationToken.None);
                return;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", "Missing access token.", cancellationToken);
                return;
            }

            TokenValidation validation = await ValidateTokenAsync(token, cancellationToken);
            if (!validation.IsValid)
            {
                CompleteFailure(validation.Error);
                await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", validation.Error, CancellationToken.None);
                return;
            }

            if (_resultSource.TrySetResult(new(token, validation.Login, string.Empty)))
                BeginShutdown();

            await WriteResponseAsync(
                stream,
                "200 OK",
                "text/plain; charset=utf-8",
                "Token successfully sent to TwitchCraft. You may close this page.",
                CancellationToken.None);
        }

        private async Task<TokenValidation> ValidateTokenAsync(
            string token,
            CancellationToken cancellationToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, TwitchValidateUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);

                using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return new(false, string.Empty, "Twitch rejected the access token.");

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                JsonElement root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return new(false, string.Empty, "Twitch returned an invalid token-validation response.");

                string validatedClientId = GetString(root, "client_id");
                string login = GetString(root, "login");
                string userId = GetString(root, "user_id");

                if (!string.Equals(validatedClientId, _clientId, StringComparison.Ordinal))
                    return new(false, string.Empty, "The token belongs to a different Twitch Client ID.");

                if (login.Length == 0 || userId.Length == 0)
                    return new(false, string.Empty, "Twitch did not return a valid user account for this token.");

                if (!root.TryGetProperty("expires_in", out JsonElement expires) ||
                    !expires.TryGetInt32(out int expiresIn) ||
                    expiresIn <= 0)
                {
                    return new(false, string.Empty, "The Twitch token is expired or invalid.");
                }

                if (!root.TryGetProperty("scopes", out JsonElement scopes) ||
                    scopes.ValueKind != JsonValueKind.Array)
                {
                    return new(false, string.Empty, "Twitch did not return the token scopes.");
                }

                bool hasChatRead = false;
                bool hasChatEdit = false;
                bool hasChattersScope = false;
                foreach (JsonElement scope in scopes.EnumerateArray())
                {
                    if (scope.ValueKind != JsonValueKind.String) continue;

                    switch (scope.GetString())
                    {
                        case "chat:read": hasChatRead = true; break;
                        case "chat:edit": hasChatEdit = true; break;
                        case "moderator:read:chatters": hasChattersScope = true; break;
                    }
                }

                string missingScope = !hasChatRead ? "chat:read"
                    : !hasChatEdit ? "chat:edit"
                    : !hasChattersScope ? "moderator:read:chatters"
                    : string.Empty;

                return missingScope.Length == 0
                    ? new(true, login, string.Empty)
                    : new(false, string.Empty, "The token is missing the required scope: " + missingScope);
            }
            catch (OperationCanceledException)
            {
                return new(false, string.Empty, "Timed out while validating the token with Twitch.");
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(ex);
                return new(false, string.Empty, "Could not connect to Twitch to validate the token.");
            }
            catch (JsonException ex)
            {
                Debug.WriteLine(ex);
                return new(false, string.Empty, "Twitch returned an invalid token-validation response.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                return new(false, string.Empty, "An unexpected error occurred while validating the Twitch token.");
            }
        }

        private static string GetString(JsonElement root, string propertyName) =>
            root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        private static void ParseForm(
            string body,
            out string token,
            out string state,
            out string authError)
        {
            token = string.Empty;
            state = string.Empty;
            authError = string.Empty;

            foreach (string part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = part.IndexOf('=');
                if (separator < 0) continue;

                string name = DecodeFormValue(part[..separator]);
                string value = DecodeFormValue(part[(separator + 1)..]);

                switch (name)
                {
                    case "access_token": token = value; break;
                    case "state": state = value; break;
                    case "error_description": authError = value; break;
                    case "error" when authError.Length == 0: authError = value; break;
                }
            }
        }

        private static string DecodeFormValue(string value) =>
            Uri.UnescapeDataString(value.Replace('+', ' '));

        private static string SanitizeMessage(string value) =>
            value.Replace('\r', ' ').Replace('\n', ' ').Trim();

        private static async Task<HttpRequest> ReadRequestAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxRequestBytes);
            int total = 0;

            try
            {
                int headerEnd = -1;
                while (total < MaxRequestBytes)
                {
                    int previousTotal = total;
                    int read = await stream.ReadAsync(
                        buffer.AsMemory(total, MaxRequestBytes - total),
                        cancellationToken);

                    if (read <= 0) break;
                    total += read;
                    headerEnd = FindHeaderEnd(buffer, Math.Max(0, previousTotal - 3), total);
                    if (headerEnd >= 0) break;
                }

                if (headerEnd < 0) return default;

                string[] lines = Encoding.ASCII.GetString(buffer, 0, headerEnd)
                    .Split("\r\n", StringSplitOptions.None);
                string[] firstLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (firstLine.Length != 3 ||
                    firstLine[1][0] != '/' ||
                    !firstLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
                {
                    return default;
                }

                int contentLength = 0;
                bool foundContentLength = false;

                for (int i = 1; i < lines.Length; i++)
                {
                    int colon = lines[i].IndexOf(':');
                    if (colon <= 0) return default;

                    string name = lines[i][..colon].Trim();
                    string value = lines[i][(colon + 1)..].Trim();

                    if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                        return default;

                    if (!name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (foundContentLength ||
                        !int.TryParse(value, out contentLength) ||
                        contentLength is < 0 or > MaxBodyBytes)
                    {
                        return default;
                    }

                    foundContentLength = true;
                }

                int bodyStart = headerEnd + 4;
                int requiredBytes = bodyStart + contentLength;
                if (requiredBytes > MaxRequestBytes) return default;

                while (total < requiredBytes)
                {
                    int read = await stream.ReadAsync(
                        buffer.AsMemory(total, requiredBytes - total),
                        cancellationToken);

                    if (read <= 0) return default;
                    total += read;
                }

                string target = firstLine[1];
                int queryStart = target.IndexOf('?');
                string path = queryStart < 0 ? target : target[..queryStart];
                string body = contentLength == 0
                    ? string.Empty
                    : Encoding.UTF8.GetString(buffer, bodyStart, contentLength);

                return new(firstLine[0].ToUpperInvariant(), path, body);
            }
            finally
            {
                if (total > 0)
                    CryptographicOperations.ZeroMemory(buffer.AsSpan(0, total));
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static int FindHeaderEnd(byte[] data, int start, int end)
        {
            for (int i = start; i <= end - 4; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n' &&
                    data[i + 2] == '\r' && data[i + 3] == '\n')
                {
                    return i;
                }
            }

            return -1;
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            string status,
            string contentType,
            string body,
            CancellationToken cancellationToken)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            byte[] headerBytes = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\n" +
                "Connection: close\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Cache-Control: no-store, no-cache, must-revalidate\r\n" +
                "Pragma: no-cache\r\n" +
                "X-Content-Type-Options: nosniff\r\n" +
                "Referrer-Policy: no-referrer\r\n" +
                "Cross-Origin-Opener-Policy: same-origin\r\n" +
                "Content-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'\r\n\r\n");

            await stream.WriteAsync(headerBytes, cancellationToken);
            if (bodyBytes.Length != 0)
                await stream.WriteAsync(bodyBytes, cancellationToken);
        }

        private static async Task TryWriteErrorAsync(
            NetworkStream? stream,
            string status,
            string message)
        {
            if (stream is null) return;

            try
            {
                await WriteResponseAsync(
                    stream,
                    status,
                    "text/plain; charset=utf-8",
                    message,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private static string BuildHtmlPage(string expectedState) => $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>TwitchCraft</title></head><body>
            <div id="msg" style="font-family:Arial,sans-serif;padding:18px;white-space:pre-wrap;font-size:34px;line-height:1.2"></div>
            <script>
            (async()=>{
              const msg=document.getElementById("msg"),set=t=>msg.textContent=t;
              try{
                const hash=new URLSearchParams(location.hash.slice(1));
                const query=new URLSearchParams(location.search);
                const token=hash.get("access_token")||"";
                const state=hash.get("state")||query.get("state")||"";
                const error=query.get("error_description")||query.get("error")||hash.get("error_description")||hash.get("error")||"";
                history.replaceState(null,"","/");

                if(!token&&!error){set("Waiting for Twitch authorization...");return;}
                if(state!=="{{expectedState}}"){set("Invalid Twitch authorization response. Please close this page and try again.");return;}

                const form=new URLSearchParams({state});
                if(token)form.set("access_token",token);
                if(error)form.set("error_description",error);

                for(let attempt=0;attempt<6;attempt++){
                  try{
                    set(token?(attempt?"Received token. Retrying app handoff...":"Received token. Sending it to the app..."):"Sending the authorization result to the app...");
                    const response=await fetch("/token",{method:"POST",body:form,cache:"no-store"});
                    const text=await response.text();
                    set(text||(response.ok?"Authorization completed. You may close this page.":"The app rejected the authorization response."));
                    return;
                  }catch{
                    if(attempt<5)await new Promise(resolve=>setTimeout(resolve,500));
                  }
                }

                set(token?"Token found, but failed to send it to the app. Your bot token is below:\n\n"+token+"\n\nInput this into the TwitchCraft setup to use the bot.":"Twitch authorization failed: "+error);
              }catch{
                history.replaceState(null,"","/");
                set("Error parsing the Twitch authorization response.");
              }
            })();
            </script></body></html>
            """;

        private void CompleteFailure(string error)
        {
            if (_resultSource.TrySetResult(new(string.Empty, string.Empty, error)))
                BeginShutdown();
        }

        private void BeginShutdown()
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0) return;
            _cts.Cancel();
            StopListener(_ipv4);
            StopListener(_ipv6);
        }

        private static void StopListener(TcpListener? listener)
        {
            try
            {
                listener?.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public void Dispose()
        {
            BeginShutdown();

            try
            {
                Task.WaitAll(_workers, TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            _cts.Dispose();
        }

        private readonly record struct HttpRequest(string Method, string Path, string Body);
        private readonly record struct TokenValidation(bool IsValid, string Login, string Error);
    }
}
