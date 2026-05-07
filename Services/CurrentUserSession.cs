using Microsoft.JSInterop;

namespace BioTwin_AI.Services
{
    public class CurrentUserSession
    {
        private const string StorageKey = "biotwin.currentUser";

        public string? Username { get; private set; }

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Username);

        public void SignIn(string username)
        {
            Username = username;
        }

        public void SignOut()
        {
            Username = null;
        }

        public async Task RestoreAsync(IJSRuntime jsRuntime)
        {
            try
            {
                var username = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
                if (!string.IsNullOrWhiteSpace(username))
                {
                    Username = username;
                }
            }
            catch
            {
                // Ignore JS interop failures during prerender/non-interactive phases.
            }
        }

        public async Task PersistAsync(IJSRuntime jsRuntime)
        {
            if (!IsAuthenticated || string.IsNullOrWhiteSpace(Username))
            {
                return;
            }

            await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, Username);
        }

        public async Task ClearPersistedAsync(IJSRuntime jsRuntime)
        {
            try
            {
                await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            }
            catch
            {
                // Ignore JS interop failures during teardown.
            }
        }
    }
}