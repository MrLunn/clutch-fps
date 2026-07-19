using ClutchFPS.Core;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Simple range target: has Health, tips over when "killed", pops back up after a delay.
    /// Not a player or loot source — just a foundation prop to validate the weapon system.
    [RequireComponent(typeof(Health))]
    public class ShootingTarget : NetworkBehaviour
    {
        [SerializeField] private Transform visual;
        [SerializeField] private float respawnDelay = 3f;
        [SerializeField] private Vector3 knockedRotation = new(-90f, 0f, 0f);

        private Health _health;
        private Quaternion _uprightRotation;

        private void Awake()
        {
            _health = GetComponent<Health>();
            if (visual != null)
            {
                _uprightRotation = visual.localRotation;
            }
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

            SetKnockedClientRpc(true);
            Invoke(nameof(RespawnServer), respawnDelay);
        }

        private void RespawnServer()
        {
            _health.ResetHealth();
            SetKnockedClientRpc(false);
        }

        [ClientRpc]
        private void SetKnockedClientRpc(bool knocked)
        {
            if (visual == null) return;
            visual.localRotation = knocked ? Quaternion.Euler(knockedRotation) : _uprightRotation;
        }
    }
}
