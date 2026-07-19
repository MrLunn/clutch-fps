using ClutchFPS.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Base for world loot: server-authoritative pickup with respawn.
    /// Auto pickups apply on walk-over; interact pickups (RequiresInteract)
    /// wait for the player to press E via PlayerInteractor.
    [RequireComponent(typeof(Collider))]
    public abstract class LootPickup : NetworkBehaviour
    {
        [SerializeField] private string displayName = "Item";
        [SerializeField] private GameObject visual;
        [SerializeField] private float respawnSeconds = 10f;
        [SerializeField] private float spinDegreesPerSecond = 90f;

        private readonly NetworkVariable<bool> _available = new(true,
            writePerm: NetworkVariableWritePermission.Server);

        public string DisplayName => displayName;
        public bool IsAvailable => _available.Value;
        public virtual bool RequiresInteract => false;

        public override void OnNetworkSpawn()
        {
            _available.OnValueChanged += (_, isAvailable) => ApplyAvailability(isAvailable);
            ApplyAvailability(_available.Value);
        }

        private void Update()
        {
            if (_available.Value && visual != null)
            {
                visual.transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime);
            }
        }

        private void ApplyAvailability(bool isAvailable)
        {
            if (visual != null) visual.SetActive(isAvailable);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || RequiresInteract || !_available.Value) return;
            if (!other.TryGetComponent<PlayerWeaponController>(out var player)) return;
            ApplyAndConsume(player);
        }

        /// Server-side entry point for E-press interaction.
        public bool ServerTryPickup(PlayerWeaponController player)
        {
            if (!IsServer || !_available.Value) return false;
            return ApplyAndConsume(player);
        }

        private bool ApplyAndConsume(PlayerWeaponController player)
        {
            if (!TryApplyTo(player)) return false;
            _available.Value = false;
            Invoke(nameof(RespawnServer), respawnSeconds);
            return true;
        }

        private void RespawnServer()
        {
            _available.Value = true;
        }

        /// Server-side. Return false if the pickup should not be consumed
        /// (e.g. ammo already full, weapon slot already owned).
        protected abstract bool TryApplyTo(PlayerWeaponController player);
    }
}
