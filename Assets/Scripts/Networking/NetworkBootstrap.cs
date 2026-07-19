using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Minimal on-screen Host/Client/Server buttons so two people can test the
    /// shooting range immediately, before any real menu UI exists.
    /// Attach to the same GameObject as the NetworkManager.
    public class NetworkBootstrap : MonoBehaviour
    {
        private void OnGUI()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            if (networkManager.IsClient || networkManager.IsServer) return;

            GUILayout.BeginArea(new Rect(10, 10, 220, 120));
            if (GUILayout.Button("Host")) networkManager.StartHost();
            if (GUILayout.Button("Client")) networkManager.StartClient();
            if (GUILayout.Button("Server")) networkManager.StartServer();
            GUILayout.EndArea();
        }
    }
}
