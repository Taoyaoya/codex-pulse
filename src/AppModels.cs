using System;
using System.Collections.Generic;

namespace CodexPulse
{
    public sealed class AppSettings
    {
        public int SettingsVersion { get; set; }
        public bool DemoMode { get; set; }
        public string Endpoint { get; set; }
        public string AccessToken { get; set; }
        public int RefreshSeconds { get; set; }
        public bool AutoStart { get; set; }
        public bool StartMinimized { get; set; }
        public bool WidgetMode { get; set; }
        public bool AlwaysOnTop { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public string ActiveAccountId { get; set; }
        public List<AccountProfile> Accounts { get; set; }

        public AppSettings()
        {
            SettingsVersion = 4;
            DemoMode = false;
            Endpoint = string.Empty;
            AccessToken = string.Empty;
            RefreshSeconds = 60;
            AutoStart = false;
            StartMinimized = false;
            WidgetMode = false;
            AlwaysOnTop = false;
            WindowWidth = 660;
            WindowHeight = 370;
            ActiveAccountId = string.Empty;
            Accounts = new List<AccountProfile>();
        }

        public AppSettings Clone()
        {
            return new AppSettings
            {
                SettingsVersion = SettingsVersion,
                DemoMode = DemoMode,
                Endpoint = Endpoint,
                AccessToken = AccessToken,
                RefreshSeconds = RefreshSeconds,
                AutoStart = AutoStart,
                StartMinimized = StartMinimized,
                WidgetMode = WidgetMode,
                AlwaysOnTop = AlwaysOnTop,
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                ActiveAccountId = ActiveAccountId,
                Accounts = CloneAccounts(Accounts)
            };
        }

        public AccountProfile GetActiveAccount()
        {
            if (Accounts == null || string.IsNullOrWhiteSpace(ActiveAccountId))
            {
                return null;
            }
            return Accounts.Find(delegate(AccountProfile account)
            {
                return account != null && string.Equals(account.Id, ActiveAccountId, StringComparison.Ordinal);
            });
        }

        private static List<AccountProfile> CloneAccounts(List<AccountProfile> accounts)
        {
            List<AccountProfile> result = new List<AccountProfile>();
            if (accounts == null)
            {
                return result;
            }
            foreach (AccountProfile account in accounts)
            {
                if (account != null)
                {
                    result.Add(account.Clone());
                }
            }
            return result;
        }
    }

    public sealed class AccountProfile
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public string PlanType { get; set; }
        public long CreatedAtEpochMillis { get; set; }
        public long LastUsedAtEpochMillis { get; set; }

        public AccountProfile()
        {
            Id = string.Empty;
            DisplayName = string.Empty;
            Email = string.Empty;
            PlanType = string.Empty;
            CreatedAtEpochMillis = TimeUtil.NowEpochMillis();
            LastUsedAtEpochMillis = CreatedAtEpochMillis;
        }

        public string Label
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName;
                if (!string.IsNullOrWhiteSpace(Email)) return Email;
                return "未连接账号";
            }
        }

        public AccountProfile Clone()
        {
            return new AccountProfile
            {
                Id = Id,
                DisplayName = DisplayName,
                Email = Email,
                PlanType = PlanType,
                CreatedAtEpochMillis = CreatedAtEpochMillis,
                LastUsedAtEpochMillis = LastUsedAtEpochMillis
            };
        }
    }

    public sealed class QuotaSnapshot
    {
        public int RemainingPercent { get; set; }
        public long UsedTokens { get; set; }
        public long LimitTokens { get; set; }
        public int ResetCardsAvailable { get; set; }
        public int ResetCardsUsedThisMonth { get; set; }
        public long UpdatedAtEpochMillis { get; set; }
        public int QuotaWindowMinutes { get; set; }
        public long ResetsAtEpochSeconds { get; set; }
        public bool IsLiveAccount { get; set; }
        public bool IsAvailable { get; set; }
        public bool HasResetCardData { get; set; }
        public long WeeklyTokensUsed { get; set; }
        public bool HasWeeklyTokenData { get; set; }
        public string AccountEmail { get; set; }
        public string PlanType { get; set; }
        public string AccountId { get; set; }

        public static QuotaSnapshot Demo()
        {
            return new QuotaSnapshot
            {
                RemainingPercent = 78,
                UsedTokens = 22000,
                LimitTokens = 100000,
                ResetCardsAvailable = 3,
                ResetCardsUsedThisMonth = 1,
                UpdatedAtEpochMillis = TimeUtil.NowEpochMillis(),
                QuotaWindowMinutes = 300,
                ResetsAtEpochSeconds = TimeUtil.NowEpochSeconds() + 7200,
                IsLiveAccount = false,
                IsAvailable = true,
                HasResetCardData = true,
                WeeklyTokensUsed = 34820,
                HasWeeklyTokenData = true,
                AccountEmail = string.Empty,
                PlanType = "demo",
                AccountId = string.Empty
            };
        }

        public static QuotaSnapshot Empty()
        {
            return new QuotaSnapshot
            {
                RemainingPercent = 0,
                UsedTokens = 0,
                LimitTokens = 1,
                ResetCardsAvailable = 0,
                UpdatedAtEpochMillis = TimeUtil.NowEpochMillis(),
                IsAvailable = false,
                HasWeeklyTokenData = false,
                AccountEmail = string.Empty,
                PlanType = string.Empty,
                AccountId = string.Empty
            };
        }
    }

    internal static class TimeUtil
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long NowEpochMillis()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
        }

        public static long NowEpochSeconds()
        {
            return (long)(DateTime.UtcNow - Epoch).TotalSeconds;
        }

        public static DateTime FromEpochMillis(long value)
        {
            if (value <= 0)
            {
                return DateTime.Now;
            }
            return Epoch.AddMilliseconds(value).ToLocalTime();
        }

        public static DateTime FromEpochSeconds(long value)
        {
            if (value <= 0)
            {
                return DateTime.Now;
            }
            return Epoch.AddSeconds(value).ToLocalTime();
        }
    }
}
