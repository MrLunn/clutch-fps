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

            // Centered so the buttons stay visible regardless of game-view
            // zoom/aspect cropping at the screen edges.
            float width = 240;
            float height = 430;
            GUILayout.BeginArea(new Rect(
                (Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height));
            GUILayout.Label("CLUTCH FPS");

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

            GUILayout.Space(10);
            Player.DisplaySettings.DrawControls();
            GUILayout.EndArea();
        }
    }
}
