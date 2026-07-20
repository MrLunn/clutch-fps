using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClutchFPS.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonMovement : NetworkBehaviour
    {
        // Public so the Esc tuning menu can drive them live.
        public float walkSpeed = 5f;
        public float sprintSpeed = 8f;
        public float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -20f;

        [Header("Feel")]
        [Tooltip("How fast you reach top speed; lower = heavier start.")]
        public float acceleration = 40f;
        [Tooltip("How fast you stop; lower = more slide.")]
        public float deceleration = 50f;
        [Tooltip("Fraction of accel/decel available mid-air.")]
        public float airControl = 0.35f;
        public float bobFrequency = 1.9f;
        public float bobAmplitude = 0.04f;
        [Tooltip("Camera dip per m/s of landing impact.")]
        public float landDipScale = 0.014f;
        public float sprintFov = 67f;
        [SerializeField] private AudioClip walkLoop;
        [SerializeField] private AudioClip runLoop;

        private float[] _tuningDefaults;

        private static readonly string[] TuningKeys =
        {
            "mv_walk", "mv_sprint", "mv_jump", "mv_accel", "mv_decel",
            "mv_air", "mv_bobf", "mv_boba", "mv_land", "mv_fov"
        };

        private float[] TuningValues
        {
            get => new[] { walkSpeed, sprintSpeed, jumpHeight, acceleration, deceleration,
                airControl, bobFrequency, bobAmplitude, landDipScale, sprintFov };
            set
            {
                walkSpeed = value[0]; sprintSpeed = value[1]; jumpHeight = value[2];
                acceleration = value[3]; deceleration = value[4]; airControl = value[5];
                bobFrequency = value[6]; bobAmplitude = value[7]; landDipScale = value[8];
                sprintFov = value[9];
            }
        }

        // Movement is game balance, not player preference: tuning persistence
        // exists only in the editor/dev builds so release players can't
        // override it (not even by editing PlayerPrefs in the registry).
        public void SaveTuning()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var values = TuningValues;
            for (int i = 0; i < TuningKeys.Length; i++) PlayerPrefs.SetFloat(TuningKeys[i], values[i]);
#endif
        }

        public void LoadTuning()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!PlayerPrefs.HasKey(TuningKeys[0])) return;
            var values = TuningValues;
            for (int i = 0; i < TuningKeys.Length; i++) values[i] = PlayerPrefs.GetFloat(TuningKeys[i], values[i]);
            TuningValues = values;
#endif
        }

        public void ResetTuning()
        {
            foreach (var key in TuningKeys) PlayerPrefs.DeleteKey(key);
            if (_tuningDefaults != null) TuningValues = _tuningDefaults;
        }

        [Header("Crouch")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float crouchSpeedMultiplier = 0.55f;
        [SerializeField] private float standHeight = 1.8f;
        [SerializeField] private float crouchHeight = 1.2f;
        [SerializeField] private float standCameraY = 1.62f;
        [SerializeField] private float crouchCameraY = 1.05f;
        [SerializeField] private float crouchTransitionSpeed = 6f;

        public bool IsCrouching { get; private set; }

        // ADS override from the weapon controller: fov 0 = not aiming.
        private float _aimFov;
        private float _aimMoveMultiplier = 1f;

        public void SetAimState(float aimFov, float moveMultiplier)
        {
            _aimFov = aimFov;
            _aimMoveMultiplier = moveMultiplier;
        }

        [Tooltip("Body mesh transform squashed on remote clients while crouched.")]
        [SerializeField] private Transform bodyVisual;

        [Tooltip("Bullet hitboxes, resized/moved with crouch on every client (incl. server).")]
        [SerializeField] private CapsuleCollider bodyHitbox;
        [SerializeField] private Transform headHitbox;

        // Owner-written so other clients can show the crouch pose.
        private readonly NetworkVariable<bool> _crouchedSync = new(false,
            writePerm: NetworkVariableWritePermission.Owner);

        // 0 idle/air, 1 walk, 2 sprint, 3 crouch-walk. Synced so every client
        // hears every player's footsteps — sound intel is core gameplay.
        private readonly NetworkVariable<byte> _moveStateSync = new(0,
            writePerm: NetworkVariableWritePermission.Owner);

        private CharacterController _controller;
        private Vector3 _verticalVelocity;
        private Vector3 _horizontalVelocity;
        private float _pivotBaseY;
        private float _bobTime;
        private float _bobOffset;
        private float _landDip;
        private bool _wasGrounded = true;
        private float _lastFallSpeed;
        private Camera _camera;
        private float _baseFov = 60f;
        private AudioSource _footstepSource;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _pivotBaseY = standCameraY;
            if (cameraPivot != null && cameraPivot.TryGetComponent<Camera>(out var cam))
            {
                _camera = cam;
                _baseFov = cam.fieldOfView;
            }
            _footstepSource = gameObject.AddComponent<AudioSource>();
            _footstepSource.playOnAwake = false;
            _footstepSource.loop = true;
            _footstepSource.minDistance = 2f;
            _footstepSource.maxDistance = 25f;

            _tuningDefaults = TuningValues;
            LoadTuning();
        }

        public override void OnNetworkSpawn()
        {
            // Only the owning client drives its own movement input.
            enabled = IsOwner;
            _crouchedSync.OnValueChanged += (_, crouched) => ApplyBodyCrouch(crouched);
            ApplyBodyCrouch(_crouchedSync.Value);
            _moveStateSync.OnValueChanged += (_, state) => ApplyFootsteps(state);
            ApplyFootsteps(_moveStateSync.Value);
        }

        /// Runs on every client (callbacks fire even while this component is
        /// disabled for non-owners). Own steps are quiet and 2D; other
        /// players' steps are positional 3D and clearly audible.
        private void ApplyFootsteps(byte state)
        {
            if (_footstepSource == null) return;
            if (state == 0)
            {
                if (_footstepSource.isPlaying) _footstepSource.Stop();
                return;
            }

            AudioClip wanted = state == 2 ? runLoop : walkLoop;
            if (wanted == null) return;

            _footstepSource.spatialBlend = IsOwner ? 0f : 1f;
            _footstepSource.pitch = state == 3 ? 0.8f : 1f;
            float baseVolume = state == 3 ? 0.12f : state == 2 ? 0.45f : 0.3f;
            _footstepSource.volume = IsOwner ? baseVolume * 0.8f : baseVolume * 1.5f;

            if (_footstepSource.clip != wanted)
            {
                _footstepSource.clip = wanted;
                _footstepSource.Play();
            }
            else if (!_footstepSource.isPlaying)
            {
                _footstepSource.Play();
            }
        }

        private void ApplyBodyCrouch(bool crouched)
        {
            if (bodyVisual != null)
            {
                bodyVisual.localScale = new Vector3(0.8f, crouched ? 0.6f : 0.9f, 0.8f);
                bodyVisual.localPosition = new Vector3(0f, crouched ? 0.6f : 0.9f, 0f);
            }
            // Hitboxes track the pose. The sync variable fires on the server
            // too, so the authoritative raycasts see the crouched shape.
            if (bodyHitbox != null)
            {
                float height = crouched ? 1.2f : 1.8f;
                bodyHitbox.height = height;
                bodyHitbox.center = new Vector3(0f, height / 2f, 0f);
            }
            if (headHitbox != null)
            {
                headHitbox.localPosition = new Vector3(0f, crouched ? 1.15f : 1.62f, 0f);
            }
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
            _horizontalVelocity = Vector3.zero;
            _controller.enabled = true;
        }

        private PlayerRespawn _respawn;

        private void Update()
        {
            if (!IsOwner) return;
            if (_respawn == null) _respawn = GetComponent<PlayerRespawn>();
            if (_respawn != null && _respawn.IsDead) return;
            if (PlayerHUD.LocalMenuOpen) return;

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
            if (!isGrounded && _verticalVelocity.y < 0f)
            {
                _lastFallSpeed = -_verticalVelocity.y;
            }
            if (isGrounded && !_wasGrounded && _lastFallSpeed > 3f)
            {
                // Landing impact: dip the camera proportionally to fall speed.
                _landDip = Mathf.Min(_lastFallSpeed * landDipScale, 0.16f);
            }
            if (isGrounded && _verticalVelocity.y < 0f)
            {
                _verticalVelocity.y = -2f;
            }

            Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
            moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

            IsCrouching = keyboard != null && keyboard.leftCtrlKey.isPressed;
            if (_crouchedSync.Value != IsCrouching) _crouchedSync.Value = IsCrouching;
            _controller.height = IsCrouching ? crouchHeight : standHeight;
            _controller.center = new Vector3(0f, _controller.height / 2f, 0f);

            bool aiming = _aimFov > 0f;
            bool sprinting = keyboard != null && keyboard.leftShiftKey.isPressed && !IsCrouching && !aiming;
            float speed = sprinting ? sprintSpeed : walkSpeed;
            if (IsCrouching) speed *= crouchSpeedMultiplier;
            if (aiming) speed *= _aimMoveMultiplier;

            // Momentum: ease toward the target velocity instead of snapping,
            // with reduced control mid-air.
            Vector3 targetVelocity = moveDirection * speed;
            float rate = targetVelocity.sqrMagnitude > _horizontalVelocity.sqrMagnitude
                ? acceleration : deceleration;
            if (!isGrounded) rate *= airControl;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetVelocity, rate * Time.deltaTime);
            float planarSpeed = _horizontalVelocity.magnitude;

            // Headbob while moving on the ground.
            if (isGrounded && planarSpeed > 0.5f)
            {
                _bobTime += Time.deltaTime * bobFrequency * (planarSpeed / walkSpeed) * Mathf.PI * 2f;
                float targetBob = Mathf.Sin(_bobTime) * bobAmplitude * Mathf.Clamp01(planarSpeed / walkSpeed);
                _bobOffset = Mathf.Lerp(_bobOffset, targetBob, 12f * Time.deltaTime);
            }
            else
            {
                _bobOffset = Mathf.Lerp(_bobOffset, 0f, 8f * Time.deltaTime);
            }
            _landDip = Mathf.Lerp(_landDip, 0f, 9f * Time.deltaTime);

            // Camera pivot: smoothed crouch base + bob - landing dip.
            if (cameraPivot != null)
            {
                float targetY = IsCrouching ? crouchCameraY : standCameraY;
                _pivotBaseY = Mathf.MoveTowards(_pivotBaseY, targetY, crouchTransitionSpeed * Time.deltaTime);
                Vector3 pivotPosition = cameraPivot.localPosition;
                pivotPosition.y = _pivotBaseY + _bobOffset - _landDip;
                cameraPivot.localPosition = pivotPosition;
            }

            // FOV: ADS zoom wins, then the sprint kick, else base.
            if (_camera != null)
            {
                float targetFov = aiming ? _aimFov
                    : sprinting && planarSpeed > walkSpeed * 0.9f ? sprintFov
                    : _baseFov;
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, (aiming ? 12f : 8f) * Time.deltaTime);
            }

            // Footsteps: publish the movement state; ApplyFootsteps plays the
            // loop on every client (including this one) via the sync callback.
            bool moving = isGrounded && planarSpeed > 0.5f;
            byte moveState = !moving ? (byte)0
                : IsCrouching ? (byte)3
                : sprinting ? (byte)2
                : (byte)1;
            if (_moveStateSync.Value != moveState) _moveStateSync.Value = moveState;

            if (isGrounded && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                _verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            _verticalVelocity.y += gravity * Time.deltaTime;

            Vector3 motion = _horizontalVelocity + Vector3.up * _verticalVelocity.y;
            _controller.Move(motion * Time.deltaTime);
            _wasGrounded = isGrounded;
        }
    }
}
