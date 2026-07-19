using ClutchFPS.Core;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Range target: has Health, vanishes when killed, reappears after a delay.
    /// Colliders are disabled while dead so shots pass through the empty spot.
    [RequireComponent(typeof(Health))]
    public class ShootingTarget : NetworkBehaviour
    {
        /// Server-side broadcast whenever any range target dies: (attacker, headshot).
        public static event System.Action<ulong, bool> TargetKilled;

        [SerializeField] private Transform visual;
        [SerializeField] private float respawnDelay = 2f;

        private Health _health;
        private Collider[] _colliders;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        public override void OnNetworkSpawn()
        {
            _health.Died += OnDied;
        }

        public override void OnNetworkDespawn()
        {
            _health.Died -= OnDied;
        }

        private void OnDied(ulong attackerClientId)
        {
            if (!IsServer) return;
            TargetKilled?.Invoke(attackerClientId, _health.LastDamageWasHeadshot);
            SetDeadClientRpc(true);
            Invoke(nameof(RespawnServer), respawnDelay);
        }

        private void RespawnServer()
        {
            _health.ResetHealth();
            SetDeadClientRpc(false);
        }

        [ClientRpc]
        private void SetDeadClientRpc(bool dead)
        {
            if (visual != null) visual.gameObject.SetActive(!dead);
            foreach (var collider in _colliders)
            {
                collider.enabled = !dead;
            }
        }
    }
}
