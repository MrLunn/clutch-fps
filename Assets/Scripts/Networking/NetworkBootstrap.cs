using ClutchFPS.Core;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Minimal on-screen Host/Client/Server buttons so two people can test the
    /// shooting range immediately, before any real menu UI exists.
    /// Attach to the same GameObject as the NetworkManager.
    public class NetworkBootstrap : MonoBehaviour
    {
        private string _joinCode = "";
        private string _name;

        private static readonly string[] MapNames = { "RANGE", "COMPLEX" };
        private static readonly string[] MapScenes = { "ShootingRange", "Raid_Complex" };
        private static int _mapIndex;
        private static bool _pendingHost;

        private string _loginUser = "";
        private string _loginPass = "";
        private bool _accountNameApplied;

        private void Awake()
        {
            _name = Player.PlayerIdentity.LocalName;
            Player.DisplaySettings.ApplySavedIfAny();
            Player.GameSettings.ApplySavedIfAny();
            AccountService.EnsureInitialized();
        }

        private void Start()
        {
            // Resume hosting after a map change requested from the menu.
            if (!_pendingHost) return;
            _pendingHost = false;
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;
            EnsureRuntimePrefabs(networkManager);
            ConnectionService.Host();
        }

        /// Wordmark, drawn procedurally so it needs no art: a heavy CLUTCH in
        /// accent with a lighter FPS, framed by rules and a spaced tagline.
        private static void DrawLogo()
        {
            float centreY = Screen.height * 0.44f - 118f;

            var heavy = UITheme.Style(64, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.Accent);
            var light = UITheme.Style(64, FontStyle.Normal, TextAnchor.MiddleCenter, UITheme.TextBright);

            const string main = "CLUTCH";
            const string suffix = " FPS";
            float mainWidth = heavy.CalcSize(new GUIContent(main)).x;
            float suffixWidth = light.CalcSize(new GUIContent(suffix)).x;
            float totalWidth = mainWidth + suffixWidth;
            float startX = (Screen.width - totalWidth) / 2f;

            // Shadow first, then the two-tone wordmark.
            var shadow = UITheme.Style(64, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(startX + 3f, centreY + 3f, totalWidth, 72f), main + suffix, shadow);
            GUI.Label(new Rect(startX, centreY, mainWidth, 72f),
                main, UITheme.Style(64, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.Accent));
            GUI.Label(new Rect(startX + mainWidth, centreY, suffixWidth, 72f),
                suffix, UITheme.Style(64, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.TextBright));

            // Tagline with rules either side.
            float tagY = centreY + 62f;
            const string tagline = "E X T R A C T I O N     L O O T     S U R V I V E";
            var tagStyle = UITheme.Style(12, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.TextDim);
            GUI.Label(new Rect(0, tagY, Screen.width, 20f), tagline, tagStyle);
            float tagWidth = tagStyle.CalcSize(new GUIContent(tagline)).x;
            float ruleY = tagY + 10f;
            float gap = tagWidth / 2f + 18f;
            UITheme.Fill(new Rect(Screen.width / 2f - gap - 60f, ruleY, 60f, 1f), UITheme.AccentDim);
            UITheme.Fill(new Rect(Screen.width / 2f + gap, ruleY, 60f, 1f), UITheme.AccentDim);
        }


        // Prefabs spawned at runtime (dropped loot) must be registered before
        // connecting, identically on host and clients so the spawn hashes
        // match. Done here because both peers pass through this screen. Wrapped
        // because AddNetworkPrefab throws if the prefab is already registered
        // (e.g. hosting a second time in the same session).
        private static void EnsureRuntimePrefabs(NetworkManager networkManager)
        {
            var drop = Environment.LootSpawner.Prefab;
            if (drop == null) return;
            try { networkManager.AddNetworkPrefab(drop); }
            catch (System.Exception) { /* already registered */ }
        }

        // Cleanly tear down the network session when play mode ends or the app
        // quits, so the relay allocation is released instead of lingering.
        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        private void OnApplicationQuit()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        private void OnGUI()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            // In a live session the in-game HUD owns the screen; the host's
            // join code lives in the F1 menu.
            if (networkManager.IsClient || networkManager.IsServer) return;

            DrawBackdrop();
            DrawLogo();

            // Gate everything behind an account so the stash/stats can key off it.
            if (!AccountService.IsSignedIn)
            {
                DrawLoginPanel();
                return;
            }
            // Default the operator name to the account name once signed in.
            if (!_accountNameApplied)
            {
                _name = AccountService.DisplayName;
                _accountNameApplied = true;
            }

            if (StashScreen.Open)
            {
                StashScreen.Draw(Player.PlayerIdentity.LocalName);
                return;
            }

            const float width = 380f;
            Rect panel = new((Screen.width - width) / 2f, Screen.height * 0.42f, width, 332f);
            UITheme.Panel3D(panel);

            float x = panel.x + 22f;
            float w = panel.width - 44f;
            float y = panel.y + 18f;

            UITheme.Header(new Rect(x, y, w, 18f), "Operator");
            y += 24f;
            _name = DrawField(new Rect(x, y, w, 30f), _name, 20);
            y += 44f;

            UITheme.Header(new Rect(x, y, w, 18f), "Location");
            y += 24f;
            _mapIndex = UITheme.Segmented(new Rect(x, y, w, 26f), MapNames, _mapIndex);
            y += 34f;

            bool busy = ConnectionService.Busy;

            UITheme.Header(new Rect(x, y, w, 18f), "Deploy");
            y += 26f;
            if (UITheme.Button(new Rect(x, y, w, 40f), busy ? "Connecting…" : "Host Raid", primary: true) && !busy)
            {
                Player.PlayerIdentity.LocalName = _name;
                // Load the chosen map before hosting; Netcode scene management
                // then syncs it to anyone who joins via the session.
                string wanted = MapScenes[_mapIndex];
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != wanted)
                {
                    _pendingHost = true;
                    UnityEngine.SceneManagement.SceneManager.LoadScene(wanted);
                }
                else
                {
                    EnsureRuntimePrefabs(networkManager);
                    ConnectionService.Host();
                }
            }
            y += 48f;

            // Join by code — Relay resolves it, no IP or port-forwarding needed.
            float codeWidth = w * 0.54f;
            _joinCode = DrawField(new Rect(x, y, codeWidth, 32f), _joinCode, 8).ToUpperInvariant();
            if (UITheme.Button(new Rect(x + codeWidth + 8f, y, w - codeWidth - 8f, 32f), "Join") && !busy)
            {
                Player.PlayerIdentity.LocalName = _name;
                EnsureRuntimePrefabs(networkManager);
                ConnectionService.Join(_joinCode);
            }
            y += 34f;

            // Status / hint line.
            if (ConnectionService.Status == ConnectionService.State.Error)
            {
                GUI.Label(new Rect(x, y, w, 18f), ConnectionService.LastError,
                    UITheme.Style(10, FontStyle.Normal, TextAnchor.MiddleCenter, UITheme.Danger));
            }
            else
            {
                GUI.Label(new Rect(x, y, w, 16f), "Host to get a code · enter a friend's code to join",
                    UITheme.Style(10, FontStyle.Normal, TextAnchor.MiddleCenter, UITheme.TextDim));
            }
            y += 22f;

            float half = (w - 8f) / 2f;
            if (UITheme.Button(new Rect(x, y, half, 32f), "Stash"))
            {
                Player.PlayerIdentity.LocalName = _name;
                StashScreen.Open = true;
            }
            if (UITheme.Button(new Rect(x + half + 8f, y, half, 32f),
                _settingsOpen ? "Close" : "Settings"))
            {
                _settingsOpen = !_settingsOpen;
            }

            if (_settingsOpen) DrawSettingsPanel(panel);

            GUI.Label(new Rect(0, Screen.height - 28f, Screen.width, 20f),
                "F1 CROSSHAIR   ·   TAB CHARACTER   ·   P PRACTICE   ·   E INTERACT",
                UITheme.Style(11, FontStyle.Normal, TextAnchor.MiddleCenter, UITheme.TextDim));
        }

        private bool _settingsOpen;

        private static string DrawField(Rect rect, string value, int maxLength)
        {
            UITheme.Fill(rect, new Color(0.04f, 0.045f, 0.05f, 0.95f));
            UITheme.Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), UITheme.Line);
            var style = UITheme.Style(14, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.TextBright);
            style.padding = new RectOffset(10, 10, 0, 0);
            return GUI.TextField(rect, value, maxLength, style);
        }

        private static string DrawPasswordField(Rect rect, string value, int maxLength)
        {
            UITheme.Fill(rect, new Color(0.04f, 0.045f, 0.05f, 0.95f));
            UITheme.Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), UITheme.Line);
            var style = UITheme.Style(14, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.TextBright);
            style.padding = new RectOffset(10, 10, 0, 0);
            return GUI.PasswordField(rect, value, '•', maxLength, style);
        }

        /// Account gate shown before the main menu. Sign in, create an account,
        /// or play as a guest. Async work runs through AccountService; this just
        /// reads its status flags each frame.
        private void DrawLoginPanel()
        {
            const float width = 380f;
            Rect panel = new((Screen.width - width) / 2f, Screen.height * 0.44f, width, 306f);
            UITheme.Panel3D(panel);

            float x = panel.x + 22f, w = panel.width - 44f, y = panel.y + 18f;
            UITheme.Header(new Rect(x, y, w, 18f), "Account");
            y += 28f;

            if (AccountService.Status == AccountService.State.Initializing)
            {
                GUI.Label(new Rect(x, y + 20f, w, 40f), "Connecting to Unity services…",
                    UITheme.Style(13, FontStyle.Normal, TextAnchor.MiddleCenter, UITheme.TextDim));
                return;
            }

            _loginUser = DrawField(new Rect(x, y, w, 30f), _loginUser, 20);
            y += 12f;
            GUI.Label(new Rect(x, y, w, 16f), "USERNAME",
                UITheme.Style(9, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextDim));
            y += 26f;
            _loginPass = DrawPasswordField(new Rect(x, y, w, 30f), _loginPass, 30);
            y += 12f;
            GUI.Label(new Rect(x, y, w, 16f), "PASSWORD",
                UITheme.Style(9, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextDim));
            y += 30f;

            bool busy = AccountService.Busy;
            float half = (w - 8f) / 2f;
            if (UITheme.Button(new Rect(x, y, half, 36f), "Sign In", primary: true) && !busy)
            {
                AccountService.SignIn(_loginUser.Trim(), _loginPass);
            }
            if (UITheme.Button(new Rect(x + half + 8f, y, half, 36f), "Create") && !busy)
            {
                AccountService.CreateAccount(_loginUser.Trim(), _loginPass);
            }
            y += 44f;
            if (UITheme.Button(new Rect(x, y, w, 30f), "Play as Guest") && !busy)
            {
                AccountService.SignInGuest();
            }
            y += 38f;

            if (busy)
            {
                GUI.Label(new Rect(x, y, w, 20f), "Working…",
                    UITheme.Style(12, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.Accent));
            }
            else if (AccountService.Status == AccountService.State.Error)
            {
                GUI.Label(new Rect(x, y, w, 34f), AccountService.LastError,
                    UITheme.Style(11, FontStyle.Normal, TextAnchor.UpperCenter, UITheme.Danger));
            }

            GUI.Label(new Rect(x, panel.yMax - 24f, w, 16f),
                "New? Password needs 8-30 chars, a number and a symbol.",
                UITheme.Style(10, FontStyle.Normal, TextAnchor.MiddleCenter, UITheme.TextDim));
        }

        private void DrawSettingsPanel(Rect anchor)
        {
            Rect panel = new(anchor.xMax + 14f, anchor.y, 300f, 430f);
            UITheme.Panel3D(panel);
            UITheme.Header(new Rect(panel.x + 18f, panel.y + 16f, panel.width - 36f, 18f), "Display");
            GUILayout.BeginArea(new Rect(panel.x + 18f, panel.y + 44f, panel.width - 36f, panel.height - 60f));
            Player.DisplaySettings.DrawControls();
            GUILayout.Space(10);
            Player.GameSettings.DrawControls();
            GUILayout.EndArea();
        }

        /// Darkens the live 3D scene behind the menu and frames it.
        private static void DrawBackdrop()
        {
            UITheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.025f, 0.03f, 0.55f));
            UITheme.Fill(new Rect(0, 0, Screen.width, 3f), UITheme.Accent);
            UITheme.Fill(new Rect(0, Screen.height - 3f, Screen.width, 3f), UITheme.AccentDim);
        }
    }
}
