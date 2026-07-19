using ClutchFPS.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Base for world loot: server-authoritative trigger pickup with respawn.
    /// Subclasses decide what the player actually receives. This is the seed of
    /// the loot system — weapon/attachment pickups will follow the same pattern.
    [RequireComponent(typeof(Collider))]
    public abstract class LootPickup : NetworkBehaviour
    {
        [SerializeField] private GameObject visual;
        [SerializeField] private float respawnSeconds = 10f;
        [SerializeField] private float spinDegreesPerSecond = 90f;

        private readonly NetworkVariable<bool> _available = new(true,
            writePerm: NetworkVariableWritePermission.Server);

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
            if (!IsServer || !_available.Value) return;
            if (!other.TryGetComponent<PlayerWeaponController>(out var player)) return;
            if (!TryApplyTo(player)) return;

            _available.Value = false;
            Invoke(nameof(RespawnServer), respawnSeconds);
        }

        private void RespawnServer()
        {
            _available.Value = true;
        }

        /// Server-side. Return false if the pickup should not be consumed
        /// (e.g. ammo already full).
        protected abstract bool TryApplyTo(PlayerWeaponController player);
    }
}
