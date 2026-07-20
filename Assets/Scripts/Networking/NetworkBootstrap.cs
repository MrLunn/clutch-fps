using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Minimal on-screen Host/Client/Server buttons so two people can test the
    /// shooting range immediately, before any real menu UI exists.
    /// Attach to the same GameObject as the NetworkManager.
    public class NetworkBootstrap : MonoBehaviour
    {
        private string _address = "127.0.0.1";
        private string _name;

        private void Awake()
        {
            _name = Player.PlayerIdentity.LocalName;
            Player.DisplaySettings.ApplySavedIfAny();
        }

        private static GUIStyle _logoStyle;
        private static GUIStyle _tagStyle;

        /// Wordmark above the menu, drawn procedurally so it needs no art.
        private static void DrawLogo()
        {
            if (_logoStyle == null)
            {
                _logoStyle = new GUIStyle
                {
                    fontSize = 62,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _tagStyle = new GUIStyle
                {
                    fontSize = 15,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            float y = Screen.height * 0.5f - 230f;
            // Drop shadow, then the wordmark, then the tagline.
            _logoStyle.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(3, y + 3, Screen.width, 74), "CLUTCH", _logoStyle);
            _logoStyle.normal.textColor = new Color(0.95f, 0.78f, 0.25f);
            GUI.Label(new Rect(0, y, Screen.width, 74), "CLUTCH", _logoStyle);

            _tagStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            GUI.Label(new Rect(0, y + 66, Screen.width, 22), "EXTRACTION  ·  LOOT  ·  SURVIVE", _tagStyle);
        }

        private static UnityTransport GetTransport(NetworkManager networkManager)
        {
            return networkManager.NetworkConfig.NetworkTransport as UnityTransport;
        }

        // Explicitly release the transport socket when play mode ends or the
        // app quits; otherwise the editor process keeps UDP 7777 bound and the
        // next StartHost fails with a transport start failure.
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

            if (networkManager.IsClient || networkManager.IsServer) return;

            DrawLogo();
            if (StashScreen.Open)
            {
                StashScreen.Draw(Player.PlayerIdentity.LocalName);
                return;
            }

            // Centered so the buttons stay visible regardless of game-view
            // zoom/aspect cropping at the screen edges.
            float width = 240;
            float height = 430;
            GUILayout.BeginArea(new Rect(
                (Screen.width - width) / 2f, (Screen.height - height) / 2f + 40f, width, height));

            GUILayout.Label("Name:");
            // Edited freely (may be empty mid-edit); saved/sanitized only on connect.
            _name = GUILayout.TextField(_name, 20, GUILayout.Height(26));
            GUILayout.Space(8);

            if (GUILayout.Button("Host", GUILayout.Height(36)))
            {
                Player.PlayerIdentity.LocalName = _name;
                // Listen on all interfaces so LAN/VPN clients can reach us.
                GetTransport(networkManager)?.SetConnectionData("0.0.0.0", 7777, "0.0.0.0");
                networkManager.StartHost();
            }

            GUILayout.Space(8);
            GUILayout.Label("Host IP (for Client):");
            _address = GUILayout.TextField(_address, GUILayout.Height(26));
            if (GUILayout.Button("Client", GUILayout.Height(36)))
            {
                Player.PlayerIdentity.LocalName = _name;
                GetTransport(networkManager)?.SetConnectionData(_address.Trim(), 7777);
                networkManager.StartClient();
            }

            if (GUILayout.Button("Server", GUILayout.Height(30)))
            {
                GetTransport(networkManager)?.SetConnectionData("0.0.0.0", 7777, "0.0.0.0");
                networkManager.StartServer();
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Stash", GUILayout.Height(30)))
            {
                Player.PlayerIdentity.LocalName = _name;
                StashScreen.Open = true;
            }

            GUILayout.Space(10);
            Player.DisplaySettings.DrawControls();
            GUILayout.EndArea();
        }
    }
}
