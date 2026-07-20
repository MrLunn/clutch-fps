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
            go.transform.position = new Vector3(0f, 4.5f, -16f);
            go.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
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

        private Vector3 _basePosition;
        private float _driftTime;

        private void Start()
        {
            _basePosition = transform.position;
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

            if (!_camera.enabled) return;

            // Slow orbital drift so the menu backdrop reads as a live scene
            // rather than a still frame.
            _driftTime += Time.deltaTime * 0.08f;
            float radius = 3.5f;
            transform.position = _basePosition + new Vector3(
                Mathf.Sin(_driftTime) * radius,
                Mathf.Sin(_driftTime * 0.6f) * 0.6f,
                Mathf.Cos(_driftTime * 0.8f) * radius * 0.4f);
            transform.rotation = Quaternion.Euler(
                12f + Mathf.Sin(_driftTime * 0.5f) * 2f,
                Mathf.Sin(_driftTime * 0.35f) * 8f,
                0f);
        }
    }
}
