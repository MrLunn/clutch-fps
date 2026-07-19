using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Minimal on-screen Host/Client/Server buttons so two people can test the
    /// shooting range immediately, before any real menu UI exists.
    /// Attach to the same GameObject as the NetworkManager.
    public class NetworkBootstrap : MonoBehaviour
    {
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
            float height = 160;
            GUILayout.BeginArea(new Rect(
                (Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height));
            GUILayout.Label("CLUTCH FPS");
            if (GUILayout.Button("Host", GUILayout.Height(36))) networkManager.StartHost();
            if (GUILayout.Button("Client", GUILayout.Height(36))) networkManager.StartClient();
            if (GUILayout.Button("Server", GUILayout.Height(36))) networkManager.StartServer();
            GUILayout.EndArea();
        }
    }
}
