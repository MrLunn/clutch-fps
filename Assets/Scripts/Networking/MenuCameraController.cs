using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Guarantees a camera exists for the connect menu and between raids.
    ///
    /// The only gameplay camera lives on the player prefab, so before hosting
    /// (and after leaving a raid) the scene would otherwise have no camera at
    /// all — "Display 1 No cameras rendering", which also hides the menu.
    /// This bootstraps itself at runtime rather than relying on a scene object,
    /// so it can't be lost to a scene re-save.
    public class MenuCameraController : MonoBehaviour
    {
        private Camera _camera;
        private AudioListener _listener;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (FindFirstObjectByType<MenuCameraController>() != null) return;

            var go = new GameObject("MenuCamera (runtime)");
            go.transform.position = new Vector3(0f, 6f, -14f);
            go.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            go.AddComponent<MenuCameraController>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null) _camera = gameObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.Skybox;
            _camera.fieldOfView = 55f;
            // Sort behind the player camera so it never wins while in a raid.
            _camera.depth = -10f;

            _listener = GetComponent<AudioListener>();
            if (_listener == null) _listener = gameObject.AddComponent<AudioListener>();
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
