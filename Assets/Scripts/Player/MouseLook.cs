using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClutchFPS.Player
{
    /// Yaw is applied to this transform (the player body), pitch to the camera pivot only,
    /// so networked position/rotation sync only needs to carry the body's yaw.
    public class MouseLook : NetworkBehaviour
    {
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float sensitivity = 0.12f;
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;

        private float _pitch;

        /// Weapon recoil: kicks the view up and slightly sideways. Owner-side only.
        public void AddRecoil(float pitchKick, float yawKick)
        {
            _pitch = Mathf.Clamp(_pitch - pitchKick, minPitch, maxPitch);
            transform.Rotate(Vector3.up * Random.Range(-yawKick, yawKick));
        }

        public override void OnNetworkSpawn()
        {
            enabled = IsOwner;
            if (IsOwner)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                if (cameraPivot != null && cameraPivot.TryGetComponent<Camera>(out var cam))
                {
                    cam.enabled = true;
                }
                if (cameraPivot != null && cameraPivot.TryGetComponent<AudioListener>(out var listener))
                {
                    listener.enabled = true;
                }
            }
            else if (cameraPivot != null)
            {
                if (cameraPivot.TryGetComponent<Camera>(out var otherCam)) otherCam.enabled = false;
                if (cameraPivot.TryGetComponent<AudioListener>(out var otherListener)) otherListener.enabled = false;
            }
        }

        private void Update()
        {
            if (!IsOwner) return;

            Vector2 delta = Mouse.current?.delta.ReadValue() ?? Vector2.zero;

            float sens = MouseSettings.Sensitivity;
            float yaw = delta.x * sens;
            float pitchDelta = delta.y * sens * (MouseSettings.InvertY ? -1f : 1f);
            _pitch = Mathf.Clamp(_pitch - pitchDelta, minPitch, maxPitch);

            transform.Rotate(Vector3.up * yaw);
            if (cameraPivot != null)
            {
                cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }
    }
}
