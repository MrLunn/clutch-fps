using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClutchFPS.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonMovement : NetworkBehaviour
    {
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float sprintSpeed = 8f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -20f;

        private CharacterController _controller;
        private Vector3 _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            // Only the owning client drives its own movement input.
            enabled = IsOwner;
        }

        /// Movement is owner-authoritative (ClientNetworkTransform), so spawn
        /// placement must happen on the owning client — the server calls this.
        [ClientRpc]
        public void TeleportClientRpc(Vector3 position)
        {
            if (!IsOwner) return;
            _controller.enabled = false;
            transform.position = position;
            _verticalVelocity = Vector3.zero;
            _controller.enabled = true;
        }

        private void Update()
        {
            if (!IsOwner) return;

            Vector2 moveInput = Vector2.zero;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed) moveInput.y += 1f;
                if (keyboard.sKey.isPressed) moveInput.y -= 1f;
                if (keyboard.dKey.isPressed) moveInput.x += 1f;
                if (keyboard.aKey.isPressed) moveInput.x -= 1f;
            }

            bool isGrounded = _controller.isGrounded;
            if (isGrounded && _verticalVelocity.y < 0f)
            {
                _verticalVelocity.y = -2f;
            }

            Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
            moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

            bool sprinting = keyboard != null && keyboard.leftShiftKey.isPressed;
            float speed = sprinting ? sprintSpeed : walkSpeed;

            if (isGrounded && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                _verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            _verticalVelocity.y += gravity * Time.deltaTime;

            Vector3 motion = moveDirection * speed + Vector3.up * _verticalVelocity.y;
            _controller.Move(motion * Time.deltaTime);
        }
    }
}
