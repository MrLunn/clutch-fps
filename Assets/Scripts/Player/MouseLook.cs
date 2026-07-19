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
            }
            else if (cameraPivot != null && cameraPivot.TryGetComponent<Camera>(out var otherCam))
            {
                otherCam.enabled = false;
            }
        }

        private void Update()
        {
            if (!IsOwner) return;

            Vector2 delta = Mouse.current?.delta.ReadValue() ?? Vector2.zero;

            float yaw = delta.x * sensitivity;
            _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, minPitch, maxPitch);

            transform.Rotate(Vector3.up * yaw);
            if (cameraPivot != null)
            {
                cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }
    }
}
