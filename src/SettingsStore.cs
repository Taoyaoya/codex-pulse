using System;
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

        public SettingsStore()
        {
            DataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexPulse");
            SettingsPath = Path.Combine(DataDirectory, "settings.json");
            CachePath = Path.Combine(DataDirectory, "quota-cache.json");
            Directory.CreateDirectory(DataDirectory);
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

        public QuotaSnapshot LoadSnapshot()
        {
            try
            {
                if (!File.Exists(CachePath))
                {
                    return QuotaSnapshot.Demo();
                }
                QuotaSnapshot value = serializer.Deserialize<QuotaSnapshot>(File.ReadAllText(CachePath, Encoding.UTF8));
                return value ?? QuotaSnapshot.Demo();
            }
            catch
            {
                return QuotaSnapshot.Demo();
            }
        }

        public void SaveSnapshot(QuotaSnapshot value)
        {
            Directory.CreateDirectory(DataDirectory);
            WriteAtomically(CachePath, serializer.Serialize(value));
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
    }
}
