using ClutchFPS.Environment;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClutchFPS.Player
{
    /// Timed target-practice run: P starts a countdown, target kills score
    /// points (headshots extra), best result is kept locally. Timer and score
    /// are server-authoritative; each player can run independently.
    public class PracticeMode : NetworkBehaviour
    {
        [SerializeField] private float runDuration = 60f;
        [SerializeField] private int killScore = 100;
        [SerializeField] private int headshotScore = 150;

        private readonly NetworkVariable<bool> _active = new(false,
            writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _timeRemaining = new(0f,
            writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _score = new(0,
            writePerm: NetworkVariableWritePermission.Server);

        public bool IsActive => _active.Value;
        public float TimeRemaining => _timeRemaining.Value;
        public int Score => _score.Value;

        public int LastScore { get; private set; }
        public float RunEndedAt { get; private set; } = -100f;

        public int BestScore
        {
            get => PlayerPrefs.GetInt("practice_best", 0);
            private set => PlayerPrefs.SetInt("practice_best", value);
        }

        private PlayerRespawn _respawn;

        public override void OnNetworkSpawn()
        {
            _respawn = GetComponent<PlayerRespawn>();
            if (IsServer)
            {
                ShootingTarget.TargetKilled += OnTargetKilledServer;
            }
            _active.OnValueChanged += (wasActive, isActive) =>
            {
                if (wasActive && !isActive)
                {
                    LastScore = _score.Value;
                    RunEndedAt = Time.time;
                    if (IsOwner && LastScore > BestScore)
                    {
                        BestScore = LastScore;
                    }
                }
            };
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                ShootingTarget.TargetKilled -= OnTargetKilledServer;
            }
        }

        private void Update()
        {
            if (IsServer && _active.Value)
            {
                _timeRemaining.Value -= Time.deltaTime;
                if (_timeRemaining.Value <= 0f)
                {
                    _timeRemaining.Value = 0f;
                    _active.Value = false;
                }
            }

            if (!IsOwner || _active.Value) return;
            if (PlayerHUD.LocalMenuOpen) return;
            if (_respawn != null && _respawn.IsDead) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
            {
                StartRunServerRpc();
            }
        }

        [ServerRpc]
        private void StartRunServerRpc()
        {
            if (_active.Value) return;
            _score.Value = 0;
            _timeRemaining.Value = runDuration;
            _active.Value = true;
        }

        private void OnTargetKilledServer(ulong attackerClientId, bool headshot)
        {
            if (!_active.Value || attackerClientId != OwnerClientId) return;
            _score.Value += headshot ? headshotScore : killScore;
        }
    }
}
