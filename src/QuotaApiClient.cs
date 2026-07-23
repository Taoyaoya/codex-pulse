using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CodexPulse
{
    public sealed class QuotaApiClient : IDisposable
    {
        private readonly object syncRoot = new object();
        private readonly SettingsStore store;
        private readonly Dictionary<string, CodexAccountClient> clients = new Dictionary<string, CodexAccountClient>();

        public QuotaApiClient(SettingsStore settingsStore)
        {
            store = settingsStore;
        }

        public Task<QuotaSnapshot> FetchAsync(AppSettings settings)
        {
            if (settings.DemoMode)
            {
                return Task.FromResult(QuotaSnapshot.Demo());
            }
            AccountProfile account = settings.GetActiveAccount();
            if (account == null)
            {
                return Faulted<QuotaSnapshot>(new CodexLoginRequiredException("尚未添加 ChatGPT/Codex 账号。"));
            }
            return GetClient(account.Id).FetchAsync();
        }

        public Task<string> BeginLoginAsync(string accountId)
        {
            return GetClient(accountId).BeginLoginAsync();
        }

        public void RemoveAccount(string accountId)
        {
            lock (syncRoot)
            {
                CodexAccountClient client;
                if (clients.TryGetValue(accountId, out client))
                {
                    clients.Remove(accountId);
                    client.Dispose();
                }
            }
        }

        private CodexAccountClient GetClient(string accountId)
        {
            lock (syncRoot)
            {
                CodexAccountClient client;
                if (!clients.TryGetValue(accountId, out client))
                {
                    client = new CodexAccountClient(accountId, store.GetAccountHomeDirectory(accountId));
                    clients.Add(accountId, client);
                }
                return client;
            }
        }

        private static Task<T> Faulted<T>(Exception error)
        {
            TaskCompletionSource<T> source = new TaskCompletionSource<T>();
            source.SetException(error);
            return source.Task;
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                foreach (CodexAccountClient client in clients.Values)
                {
                    client.Dispose();
                }
                clients.Clear();
            }
        }
    }
}
