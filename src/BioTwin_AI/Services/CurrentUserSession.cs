using Microsoft.JSInterop;

namespace BioTwin_AI.Services
{
    public enum UserRole
    {
        Candidate,      // 求职者，可上传简历
        Interviewer     // 面试官，搜索候选人
    }

    public class CurrentUserSession
    {
        private const string StorageKey = "biotwin.currentUser";
        private const string StorageRoleKey = "biotwin.userRole";

        public event Action? Changed;

        public string? Username { get; private set; }
        public UserRole Role { get; private set; } = UserRole.Candidate;

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Username);
        public bool IsInterviewer => Role == UserRole.Interviewer;
        public bool IsCandidate => Role == UserRole.Candidate;

        public void SignIn(string username, UserRole role = UserRole.Candidate)
        {
            Username = username;
            Role = role;
            NotifyStateChanged();
        }

        /// <summary>
        /// Interviewer anonymous login - generates a unique session ID
        /// </summary>
        public void InterviewerLogin()
        {
            Username = $"interviewer_{Guid.NewGuid():N}";
            Role = UserRole.Interviewer;
            NotifyStateChanged();
        }

        public void SignOut()
        {
            Username = null;
            NotifyStateChanged();
        }

        public async Task RestoreAsync(IJSRuntime jsRuntime)
        {
            try
            {
                var username = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
                var role = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageRoleKey);

                if (!string.IsNullOrWhiteSpace(username))
                {
                    Username = username;
                    Role = Enum.TryParse<UserRole>(role, out var parsedRole) ? parsedRole : UserRole.Candidate;
                    NotifyStateChanged();
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
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageRoleKey, Role.ToString());
        }

        public async Task ClearPersistedAsync(IJSRuntime jsRuntime)
        {
            try
            {
                await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKey);
                await jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageRoleKey);
            }
            catch
            {
                // Ignore JS interop failures during teardown.
            }
        }

        private void NotifyStateChanged()
        {
            Changed?.Invoke();
        }
    }
}