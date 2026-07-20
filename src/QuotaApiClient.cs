using System;
using System.Threading.Tasks;

namespace CodexPulse
{
    public sealed class QuotaApiClient : IDisposable
    {
        private readonly CodexAccountClient accountClient = new CodexAccountClient();

        public Task<QuotaSnapshot> FetchAsync(AppSettings settings)
        {
            if (settings.DemoMode)
            {
                return Task.FromResult(QuotaSnapshot.Demo());
            }
            return accountClient.FetchAsync();
        }

        public Task<string> BeginLoginAsync()
        {
            return accountClient.BeginLoginAsync();
        }

        public void Dispose()
        {
            accountClient.Dispose();
        }
    }
}
