using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Core
{
    /// Server-authoritative health. Damage is only ever applied on the server
    /// (via Weapon's ServerRpc), and the current value is synced to all clients.
    /// Also owns worn gear (body armor + helmet) so damage routes through it
    /// before touching health.
    public class Health : NetworkBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;

        private readonly NetworkVariable<float> _currentHealth = new(
            writePerm: NetworkVariableWritePermission.Server);

        // Worn gear. Class 0 = none, 1 = light, 2 = heavy. Durability drains as
        // hits are soaked; at 0 the piece breaks (class back to 0).
        private readonly NetworkVariable<byte> _armorClass = new(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _armorDurability = new(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> _helmetClass = new(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _helmetDurability = new(writePerm: NetworkVariableWritePermission.Server);

        public float CurrentHealth => _currentHealth.Value;
        public float MaxHealth => maxHealth;

        public byte ArmorClass => _armorClass.Value;
        public byte HelmetClass => _helmetClass.Value;
        public float ArmorFraction => ArmorMaxDurability(_armorClass.Value) is var m && m > 0f ? _armorDurability.Value / m : 0f;
        public float HelmetFraction => HelmetMaxDurability(_helmetClass.Value) is var m && m > 0f ? _helmetDurability.Value / m : 0f;

        public event System.Action<float, float> HealthChanged;
        public event System.Action<ulong> Died;

        /// True when the most recent damage came from an instant-kill HitZone
        /// (weapons deal 99999 on headshots) that a helmet did NOT stop.
        public bool LastDamageWasHeadshot { get; private set; }

        // --- Gear specs -----------------------------------------------------
        // Body armor: fraction of body damage soaked, and durability pool.
        private static float ArmorReduction(byte c) => c == 2 ? 0.5f : c == 1 ? 0.3f : 0f;
        private static float ArmorMaxDurability(byte c) => c == 2 ? 100f : c == 1 ? 50f : 0f;
        // Helmet: damage a stopped headshot leaks through (instead of 99999),
        // and durability pool. Each stopped headshot costs 50 durability.
        private static float HelmetBleed(byte c) => c == 2 ? 30f : c == 1 ? 48f : 0f;
        private static float HelmetMaxDurability(byte c) => c == 2 ? 100f : c == 1 ? 50f : 0f;

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

            amount = AbsorbThroughGear(amount);

            LastDamageWasHeadshot = amount >= 9000f;
            _currentHealth.Value = Mathf.Max(0f, _currentHealth.Value - amount);

            if (_currentHealth.Value <= 0f)
            {
                Died?.Invoke(attackerClientId);
            }
        }

        /// Run incoming damage through worn gear, draining durability and
        /// returning the reduced amount that reaches health. Server-only.
        private float AbsorbThroughGear(float amount)
        {
            bool headshot = amount >= 9000f;

            if (headshot)
            {
                if (_helmetClass.Value > 0 && _helmetDurability.Value > 0f)
                {
                    float leak = HelmetBleed(_helmetClass.Value);
                    _helmetDurability.Value = Mathf.Max(0f, _helmetDurability.Value - 50f);
                    if (_helmetDurability.Value <= 0f) _helmetClass.Value = 0;
                    return leak; // the helmet turned a kill into a survivable hit
                }
                return amount;
            }

            if (_armorClass.Value > 0 && _armorDurability.Value > 0f)
            {
                float soaked = amount * ArmorReduction(_armorClass.Value);
                _armorDurability.Value = Mathf.Max(0f, _armorDurability.Value - soaked);
                if (_armorDurability.Value <= 0f) _armorClass.Value = 0;
                return amount - soaked;
            }

            return amount;
        }

        /// Server-only: equip armor/helmet from a gear ItemType. A better class
        /// upgrades; the same class refills; a lower class is ignored so you
        /// never downgrade by walking over a worse plate.
        public void ServerEquipGear(ItemType gear)
        {
            if (!IsServer) return;
            switch (gear)
            {
                case ItemType.ArmorLight: EquipArmor(1); break;
                case ItemType.ArmorHeavy: EquipArmor(2); break;
                case ItemType.HelmetLight: EquipHelmet(1); break;
                case ItemType.HelmetHeavy: EquipHelmet(2); break;
            }
        }

        private void EquipArmor(byte cls)
        {
            if (cls < _armorClass.Value) return;
            _armorClass.Value = cls;
            _armorDurability.Value = ArmorMaxDurability(cls);
        }

        private void EquipHelmet(byte cls)
        {
            if (cls < _helmetClass.Value) return;
            _helmetClass.Value = cls;
            _helmetDurability.Value = HelmetMaxDurability(cls);
        }

        /// Server-only: heal without exceeding max. Used by medkits.
        public void ServerHeal(float amount)
        {
            if (!IsServer || amount <= 0f) return;
            if (_currentHealth.Value <= 0f) return;
            _currentHealth.Value = Mathf.Min(maxHealth, _currentHealth.Value + amount);
        }

        /// Server-only helper for respawns / range target resets.
        public void ResetHealth()
        {
            if (!IsServer) return;
            _currentHealth.Value = maxHealth;
        }

        /// Server-only: strip all worn gear (on death/respawn — you lose what
        /// you didn't extract with).
        public void ResetGear()
        {
            if (!IsServer) return;
            _armorClass.Value = 0;
            _armorDurability.Value = 0f;
            _helmetClass.Value = 0;
            _helmetDurability.Value = 0f;
        }
    }
}
