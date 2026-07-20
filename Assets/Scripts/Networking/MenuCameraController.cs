using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Keeps a scene camera alive for the connect menu and between raids.
    /// It yields to the player's own camera while a local player exists, and
    /// takes back over on disconnect — otherwise leaving a raid despawns the
    /// player and leaves the screen with no camera at all.
    [RequireComponent(typeof(Camera), typeof(AudioListener))]
    public class MenuCameraController : MonoBehaviour
    {
        private Camera _camera;
        private AudioListener _listener;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _listener = GetComponent<AudioListener>();
        }

        private void Update()
        {
            var networkManager = NetworkManager.Singleton;
            bool playerActive = networkManager != null
                && networkManager.IsListening
                && networkManager.LocalClient?.PlayerObject != null;

            if (_camera.enabled == playerActive)
            {
                _camera.enabled = !playerActive;
                _listener.enabled = !playerActive;
            }
        }
    }
}
