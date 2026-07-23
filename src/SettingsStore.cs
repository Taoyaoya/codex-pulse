using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CodexPulse
{
    public sealed class SettingsStore
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "CodexPulse";
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public string DataDirectory { get; private set; }
        public string SettingsPath { get; private set; }
        public string CachePath { get; private set; }
        public string AccountsDirectory { get; private set; }

        public SettingsStore()
        {
            DataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexPulse");
            SettingsPath = Path.Combine(DataDirectory, "settings.json");
            CachePath = Path.Combine(DataDirectory, "quota-cache.json");
            AccountsDirectory = Path.Combine(DataDirectory, "accounts");
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(AccountsDirectory);
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new AppSettings();
                }
                AppSettings value = serializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                if (value == null)
                {
                    return new AppSettings();
                }
                value.RefreshSeconds = Math.Max(15, value.RefreshSeconds);
                value.Endpoint = value.Endpoint ?? string.Empty;
                value.AccessToken = value.AccessToken ?? string.Empty;
                value.ActiveAccountId = value.ActiveAccountId ?? string.Empty;
                value.Accounts = NormalizeAccounts(value.Accounts);
                if (value.DemoMode)
                {
                    value.DemoMode = false;
                    SaveSettings(value);
                }
                if (value.SettingsVersion < 2)
                {
                    value.SettingsVersion = 2;
                    value.DemoMode = false;
                    value.AlwaysOnTop = value.WidgetMode;
                    SaveSettings(value);
                }
                if (value.SettingsVersion < 3)
                {
                    value.SettingsVersion = 3;
                    value.WindowWidth = 660;
                    value.WindowHeight = 370;
                    SaveSettings(value);
                }
                if (value.SettingsVersion < 4)
                {
                    value.SettingsVersion = 4;
                    value.ActiveAccountId = string.Empty;
                    value.Accounts = new List<AccountProfile>();
                    SaveSettings(value);
                }
                if (value.GetActiveAccount() == null)
                {
                    value.ActiveAccountId = value.Accounts.Count == 0 ? string.Empty : value.Accounts[0].Id;
                }
                value.WindowWidth = Math.Max(560, value.WindowWidth);
                value.WindowHeight = Math.Max(320, value.WindowHeight);
                return value;
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void SaveSettings(AppSettings value)
        {
            Directory.CreateDirectory(DataDirectory);
            WriteAtomically(SettingsPath, serializer.Serialize(value));
        }

        public QuotaSnapshot LoadSnapshot(string accountId)
        {
            try
            {
                string cachePath = GetAccountCachePath(accountId);
                if (!File.Exists(cachePath))
                {
                    return QuotaSnapshot.Empty();
                }
                QuotaSnapshot value = serializer.Deserialize<QuotaSnapshot>(File.ReadAllText(cachePath, Encoding.UTF8));
                return value ?? QuotaSnapshot.Empty();
            }
            catch
            {
                return QuotaSnapshot.Empty();
            }
        }

        public void SaveSnapshot(string accountId, QuotaSnapshot value)
        {
            string cachePath = GetAccountCachePath(accountId);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            WriteAtomically(cachePath, serializer.Serialize(value));
        }

        public string GetAccountHomeDirectory(string accountId)
        {
            return Path.Combine(GetAccountDirectory(accountId), "codex-home");
        }

        public string GetAccountCachePath(string accountId)
        {
            return Path.Combine(GetAccountDirectory(accountId), "quota-cache.json");
        }

        public void DeleteAccountData(string accountId)
        {
            string accountDirectory = GetAccountDirectory(accountId);
            if (Directory.Exists(accountDirectory))
            {
                Directory.Delete(accountDirectory, true);
            }
        }

        public void SetAutoStart(bool enabled, string executablePath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的开机启动配置。");
                }
                if (enabled)
                {
                    key.SetValue(RunValueName, string.Format("\"{0}\" --minimized", executablePath));
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
        }

        private static void WriteAtomically(string targetPath, string content)
        {
            string temporaryPath = targetPath + ".tmp";
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            if (File.Exists(targetPath))
            {
                File.Replace(temporaryPath, targetPath, null);
            }
            else
            {
                File.Move(temporaryPath, targetPath);
            }
        }

        private string GetAccountDirectory(string accountId)
        {
            Guid parsed;
            if (string.IsNullOrWhiteSpace(accountId)
                || !Guid.TryParseExact(accountId, "N", out parsed))
            {
                throw new ArgumentException("账号标识无效。", "accountId");
            }
            return Path.Combine(AccountsDirectory, parsed.ToString("N"));
        }

        private static List<AccountProfile> NormalizeAccounts(List<AccountProfile> accounts)
        {
            List<AccountProfile> result = new List<AccountProfile>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (accounts == null)
            {
                return result;
            }
            foreach (AccountProfile account in accounts)
            {
                Guid parsed;
                if (account == null
                    || !Guid.TryParseExact(account.Id, "N", out parsed)
                    || !ids.Add(parsed.ToString("N")))
                {
                    continue;
                }
                account.Id = parsed.ToString("N");
                account.DisplayName = account.DisplayName ?? string.Empty;
                account.Email = account.Email ?? string.Empty;
                account.PlanType = account.PlanType ?? string.Empty;
                result.Add(account);
            }
            return result;
        }
    }
}
