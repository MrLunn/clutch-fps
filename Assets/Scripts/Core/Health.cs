using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Core
{
    /// Server-authoritative health. Damage is only ever applied on the server
    /// (via Weapon's ServerRpc), and the current value is synced to all clients.
    public class Health : NetworkBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;

        private readonly NetworkVariable<float> _currentHealth = new(
            writePerm: NetworkVariableWritePermission.Server);

        public float CurrentHealth => _currentHealth.Value;
        public float MaxHealth => maxHealth;

        public event System.Action<float, float> HealthChanged;
        public event System.Action<ulong> Died;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _currentHealth.Value = maxHealth;
            }
            _currentHealth.OnValueChanged += (_, newValue) => HealthChanged?.Invoke(newValue, maxHealth);
        }

        public void TakeDamage(float amount, ulong attackerClientId)
        {
            if (!IsServer || amount <= 0f) return;
            if (_currentHealth.Value <= 0f) return;

            _currentHealth.Value = Mathf.Max(0f, _currentHealth.Value - amount);

            if (_currentHealth.Value <= 0f)
            {
                Died?.Invoke(attackerClientId);
            }
        }

        /// Server-only helper for respawns / range target resets.
        public void ResetHealth()
        {
            if (!IsServer) return;
            _currentHealth.Value = maxHealth;
        }
    }
}
