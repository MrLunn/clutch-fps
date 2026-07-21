using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Wraps Unity Gaming Services init + player Authentication. Async work
    /// updates synchronous status flags so the IMGUI login screen can poll it.
    /// A signed-in account id is the identity everything else keys off.
    public static class AccountService
    {
        public enum State { Uninitialized, Initializing, SignedOut, Working, SignedIn, Error }

        public static State Status { get; private set; } = State.Uninitialized;
        public static string LastError { get; private set; } = "";
        public static string PlayerId { get; private set; } = "";
        public static string DisplayName { get; private set; } = "";

        public static bool IsSignedIn => Status == State.SignedIn;
        public static bool Busy => Status == State.Initializing || Status == State.Working;

        /// Initialize UGS once and, if a returning player has a cached session
        /// token, sign them straight back in. Safe to call repeatedly.
        public static async void EnsureInitialized()
        {
            if (Status != State.Uninitialized) return;
            Status = State.Initializing;
            try
            {
                await UnityServices.InitializeAsync();
                if (AuthenticationService.Instance.SessionTokenExists)
                {
                    // Cached token — sign the returning player back in silently.
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    OnSignedIn();
                }
                else
                {
                    Status = State.SignedOut;
                }
            }
            catch (System.Exception e) { Fail(e); }
        }

        public static async void SignInGuest()
        {
            if (!CanStart()) return;
            Status = State.Working;
            try { await AuthenticationService.Instance.SignInAnonymouslyAsync(); OnSignedIn(); }
            catch (System.Exception e) { Fail(e); }
        }

        public static async void CreateAccount(string username, string password)
        {
            if (!CanStart()) return;
            Status = State.Working;
            try
            {
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                DisplayName = username;
                OnSignedIn();
            }
            catch (System.Exception e) { Fail(e); }
        }

        public static async void SignIn(string username, string password)
        {
            if (!CanStart()) return;
            Status = State.Working;
            try
            {
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
                DisplayName = username;
                OnSignedIn();
            }
            catch (System.Exception e) { Fail(e); }
        }

        public static void SignOut()
        {
            try { AuthenticationService.Instance.SignOut(); } catch (System.Exception) { }
            PlayerId = "";
            DisplayName = "";
            Status = State.SignedOut;
        }

        private static bool CanStart() => Status == State.SignedOut || Status == State.Error;

        private static void OnSignedIn()
        {
            PlayerId = AuthenticationService.Instance.PlayerId;
            if (string.IsNullOrEmpty(DisplayName))
            {
                DisplayName = string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName)
                    ? $"Operator-{PlayerId.Substring(0, System.Math.Min(5, PlayerId.Length))}"
                    : AuthenticationService.Instance.PlayerName;
            }
            // Bridge to the existing name-keyed systems until Stage 2 moves the
            // stash onto the account id in Cloud Save.
            Player.PlayerIdentity.LocalName = DisplayName;
            Status = State.SignedIn;
        }

        private static void Fail(System.Exception e)
        {
            // Trim UGS's verbose messages to something a player can read.
            LastError = e is AuthenticationException auth ? auth.Message : e.Message;
            Status = State.Error;
            Debug.LogWarning($"AccountService: {e}");
        }
    }
}
