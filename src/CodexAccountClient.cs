using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexPulse
{
    public sealed class CodexLoginRequiredException : InvalidOperationException
    {
        public CodexLoginRequiredException(string message) : base(message) { }
    }

    public sealed class CodexAccountClient : IDisposable
    {
        private readonly object syncRoot = new object();
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly string accountId;
        private readonly string accountHomeDirectory;
        private Process process;
        private StreamWriter input;
        private StreamReader output;
        private int nextId = 10;
        private string lastDiagnostic = string.Empty;
        private bool disposed;

        public CodexAccountClient(string profileId, string profileHomeDirectory)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                throw new ArgumentException("账号标识不能为空。", "profileId");
            }
            if (string.IsNullOrWhiteSpace(profileHomeDirectory))
            {
                throw new ArgumentException("账号目录不能为空。", "profileHomeDirectory");
            }
            accountId = profileId;
            accountHomeDirectory = profileHomeDirectory;
        }

        public Task<QuotaSnapshot> FetchAsync()
        {
            return Task.Factory.StartNew<QuotaSnapshot>(
                new Func<QuotaSnapshot>(Fetch),
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default);
        }

        public Task<string> BeginLoginAsync()
        {
            return Task.Factory.StartNew<string>(
                new Func<string>(BeginLogin),
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default);
        }

        private QuotaSnapshot Fetch()
        {
            lock (syncRoot)
            {
                EnsureStarted();
                Dictionary<string, object> accountResult = SendRequest(
                    "account/read",
                    new Dictionary<string, object> { { "refreshToken", false } });
                Dictionary<string, object> account = GetDictionary(accountResult, "account", false);
                if (account == null)
                {
                    throw new CodexLoginRequiredException("尚未连接 ChatGPT/Codex 账号。");
                }

                string accountType = GetString(account, "type");
                if (!string.Equals(accountType, "chatgpt", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(accountType, "chatgptAuthTokens", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(accountType, "personalAccessToken", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(accountType, "agentIdentity", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CodexLoginRequiredException("当前 Codex 使用的不是 ChatGPT 账号登录，请点击“连接账号”。");
                }

                Dictionary<string, object> result = SendRequest("account/rateLimits/read", null);
                Dictionary<string, object> limits = null;
                Dictionary<string, object> allLimits = GetDictionary(result, "rateLimitsByLimitId", false);
                if (allLimits != null)
                {
                    limits = GetDictionary(allLimits, "codex", false);
                }
                if (limits == null)
                {
                    limits = GetDictionary(result, "rateLimits", false);
                }
                if (limits == null)
                {
                    throw new InvalidOperationException("Codex 服务没有返回额度窗口。");
                }

                Dictionary<string, object> primary = GetDictionary(limits, "primary", true);
                Dictionary<string, object> secondary = GetDictionary(limits, "secondary", false);
                Dictionary<string, object> weeklyWindow = SelectLongestWindow(primary, secondary);
                double usedPercent = GetDouble(weeklyWindow, "usedPercent");
                int remainingPercent = Math.Max(0, Math.Min(100, 100 - (int)Math.Round(usedPercent)));
                int windowMinutes = GetInt(weeklyWindow, "windowDurationMins", 0);
                long resetsAt = GetLong(weeklyWindow, "resetsAt", 0L);

                bool hasWeeklyTokenData;
                long weeklyTokensUsed = ReadWeeklyTokenUsage(out hasWeeklyTokenData);

                Dictionary<string, object> resetCredits = GetDictionary(result, "rateLimitResetCredits", false);
                int availableResets = resetCredits == null ? 0 : GetInt(resetCredits, "availableCount", 0);

                return new QuotaSnapshot
                {
                    RemainingPercent = remainingPercent,
                    UsedTokens = 0,
                    LimitTokens = 1,
                    ResetCardsAvailable = Math.Max(0, availableResets),
                    ResetCardsUsedThisMonth = 0,
                    UpdatedAtEpochMillis = TimeUtil.NowEpochMillis(),
                    QuotaWindowMinutes = Math.Max(0, windowMinutes),
                    ResetsAtEpochSeconds = resetsAt,
                    IsLiveAccount = true,
                    IsAvailable = true,
                    HasResetCardData = resetCredits != null,
                    WeeklyTokensUsed = weeklyTokensUsed,
                    HasWeeklyTokenData = hasWeeklyTokenData,
                    AccountEmail = GetString(account, "email"),
                    PlanType = GetString(account, "planType"),
                    AccountId = accountId
                };
            }
        }

        private string BeginLogin()
        {
            lock (syncRoot)
            {
                EnsureStarted();
                Dictionary<string, object> parameters = new Dictionary<string, object>
                {
                    { "type", "chatgpt" },
                    { "useHostedLoginSuccessPage", false },
                    { "appBrand", "codex" }
                };
                Dictionary<string, object> result = SendRequest("account/login/start", parameters);
                string authUrl = GetString(result, "authUrl");
                if (string.IsNullOrWhiteSpace(authUrl))
                {
                    throw new InvalidOperationException("Codex 登录服务没有返回授权地址。");
                }
                OpenBrowser(authUrl);
                return authUrl;
            }
        }

        private void EnsureStarted()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("CodexAccountClient");
            }
            if (IsProcessRunning())
            {
                return;
            }

            StopProcess();
            EnsureAccountHome();
            string executable = FindCodexExecutable();
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "app-server --stdio",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            start.EnvironmentVariables["RUST_LOG"] = "error";
            start.EnvironmentVariables["CODEX_HOME"] = accountHomeDirectory;
            start.EnvironmentVariables["CODEX_SQLITE_HOME"] = accountHomeDirectory;
            lastDiagnostic = string.Empty;
            process = new Process { StartInfo = start, EnableRaisingEvents = true };
            try
            {
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        lastDiagnostic = e.Data;
                    }
                };
                if (!process.Start())
                {
                    throw new InvalidOperationException("Windows 拒绝启动进程。");
                }
                process.BeginErrorReadLine();
                input = process.StandardInput;
                output = process.StandardOutput;
                input.AutoFlush = true;

                Dictionary<string, object> clientInfo = new Dictionary<string, object>
                {
                    { "name", "codex_pulse" },
                    { "title", "Codex Pulse" },
                    { "version", "2.1.2" }
                };
                SendRequest("initialize", new Dictionary<string, object> { { "clientInfo", clientInfo } });
                SendNotification("initialized", new Dictionary<string, object>());
            }
            catch (Exception error)
            {
                string detail = string.IsNullOrWhiteSpace(lastDiagnostic) ? error.Message : lastDiagnostic;
                StopProcess();
                throw new InvalidOperationException("无法启动内置 Codex 账号服务：" + detail, error);
            }
        }

        private Dictionary<string, object> SendRequest(string method, Dictionary<string, object> parameters)
        {
            int id = nextId++;
            Dictionary<string, object> request = new Dictionary<string, object>
            {
                { "method", method },
                { "id", id }
            };
            if (parameters != null)
            {
                request["params"] = parameters;
            }
            input.WriteLine(serializer.Serialize(request));
            return ReadResponse(id);
        }

        private void SendNotification(string method, Dictionary<string, object> parameters)
        {
            input.WriteLine(serializer.Serialize(new Dictionary<string, object>
            {
                { "method", method },
                { "params", parameters }
            }));
        }

        private Dictionary<string, object> ReadResponse(int id)
        {
            while (true)
            {
                Task<string> readTask = output.ReadLineAsync();
                if (!readTask.Wait(TimeSpan.FromSeconds(20)))
                {
                    StopProcess();
                    throw new TimeoutException("Codex 账号服务响应超时。");
                }
                string line = readTask.Result;
                if (line == null)
                {
                    string detail = string.IsNullOrWhiteSpace(lastDiagnostic) ? "进程已退出。" : lastDiagnostic;
                    StopProcess();
                    throw new InvalidOperationException("Codex 账号服务已停止：" + detail);
                }

                Dictionary<string, object> message;
                try
                {
                    message = serializer.Deserialize<Dictionary<string, object>>(line);
                }
                catch
                {
                    continue;
                }
                if (message == null || !message.ContainsKey("id") || Convert.ToInt32(message["id"]) != id)
                {
                    continue;
                }
                if (message.ContainsKey("error") && message["error"] != null)
                {
                    Dictionary<string, object> error = message["error"] as Dictionary<string, object>;
                    throw new InvalidOperationException(error == null ? "Codex 账号服务返回错误。" : GetString(error, "message"));
                }
                Dictionary<string, object> result = GetDictionary(message, "result", false);
                return result ?? new Dictionary<string, object>();
            }
        }

        private static string FindCodexExecutable()
        {
            string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex-runtime", "bin", "codex.exe");
            if (File.Exists(bundled))
            {
                const long ExpectedBundledSize = 341225264L;
                long actualSize = new FileInfo(bundled).Length;
                if (actualSize != ExpectedBundledSize)
                {
                    throw new InvalidDataException(string.Format(
                        "内置 Codex 运行时不完整（应为 {0} 字节，实际为 {1} 字节），请重新下载最新版。",
                        ExpectedBundledSize,
                        actualSize));
                }
                return bundled;
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim(), "codex.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch { }
            }
            throw new FileNotFoundException("未找到内置 Codex 运行时，请重新下载完整安装包。");
        }

        private void EnsureAccountHome()
        {
            Directory.CreateDirectory(accountHomeDirectory);
            string configPath = Path.Combine(accountHomeDirectory, "config.toml");
            if (!File.Exists(configPath))
            {
                File.WriteAllText(
                    configPath,
                    "cli_auth_credentials_store = \"file\"\r\n\r\n[history]\r\npersistence = \"none\"\r\n",
                    new UTF8Encoding(false));
            }
        }

        private static void OpenBrowser(string authUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(authUrl, UriKind.Absolute, out uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("Codex 登录服务返回了不安全的授权地址。");
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                Verb = "open",
                UseShellExecute = true
            });
        }

        private static Dictionary<string, object> SelectLongestWindow(
            Dictionary<string, object> primary,
            Dictionary<string, object> secondary)
        {
            if (secondary == null)
            {
                return primary;
            }
            int primaryMinutes = GetInt(primary, "windowDurationMins", 0);
            int secondaryMinutes = GetInt(secondary, "windowDurationMins", 0);
            return secondaryMinutes > primaryMinutes ? secondary : primary;
        }

        private long ReadWeeklyTokenUsage(out bool available)
        {
            available = false;
            try
            {
                Dictionary<string, object> usage = SendRequest("account/usage/read", null);
                object bucketsValue;
                if (usage == null || !usage.TryGetValue("dailyUsageBuckets", out bucketsValue) || bucketsValue == null)
                {
                    return 0L;
                }

                IEnumerable buckets = bucketsValue as IEnumerable;
                if (buckets == null)
                {
                    return 0L;
                }

                DateTime firstDate = DateTime.UtcNow.Date.AddDays(-6);
                long total = 0L;
                foreach (object item in buckets)
                {
                    Dictionary<string, object> bucket = item as Dictionary<string, object>;
                    if (bucket == null)
                    {
                        continue;
                    }
                    DateTime date;
                    if (!DateTime.TryParse(
                        GetString(bucket, "startDate"),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out date))
                    {
                        continue;
                    }
                    if (date.Date >= firstDate)
                    {
                        total += Math.Max(0L, GetLong(bucket, "tokens", 0L));
                        available = true;
                    }
                }
                return total;
            }
            catch
            {
                return 0L;
            }
        }

        private bool IsProcessRunning()
        {
            if (process == null)
            {
                return false;
            }
            try
            {
                return process.Id > 0 && !process.HasExited;
            }
            catch
            {
                StopProcess();
                return false;
            }
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> source, string key, bool required)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value))
            {
                return value as Dictionary<string, object>;
            }
            if (required)
            {
                throw new InvalidOperationException("Codex 响应缺少字段：" + key);
            }
            return null;
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : string.Empty;
        }

        private static double GetDouble(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                throw new InvalidOperationException("Codex 响应缺少字段：" + key);
            }
            return Convert.ToDouble(value);
        }

        private static int GetInt(Dictionary<string, object> source, string key, int fallback)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToInt32(value)
                : fallback;
        }

        private static long GetLong(Dictionary<string, object> source, string key, long fallback)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToInt64(value)
                : fallback;
        }

        private void StopProcess()
        {
            if (process == null)
            {
                return;
            }
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(2000);
                }
            }
            catch { }
            try { process.Dispose(); } catch { }
            process = null;
            input = null;
            output = null;
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                StopProcess();
            }
        }
    }
}
