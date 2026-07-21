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
        [SerializeField] private Core.Rarity rarity = Core.Rarity.Common;

        public Core.Rarity Rarity => rarity;
        [SerializeField] private GameObject visual;
        [SerializeField] private float respawnSeconds = 10f;
        [SerializeField] private float spinDegreesPerSecond = 90f;

        private readonly NetworkVariable<bool> _available = new(true,
            writePerm: NetworkVariableWritePermission.Server);

        public string DisplayName => displayName;
        public bool IsAvailable => _available.Value;
        public virtual bool RequiresInteract => false;

        private Light _glow;

        public override void OnNetworkSpawn()
        {
            EnsureGlow();
            _available.OnValueChanged += (_, isAvailable) => ApplyAvailability(isAvailable);
            ApplyAvailability(_available.Value);
        }

        /// A rarity-coloured glow so loot reads from a distance.
        private void EnsureGlow()
        {
            if (_glow != null) return;
            var go = new GameObject("Glow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.5f;
            _glow = go.AddComponent<Light>();
            _glow.type = LightType.Point;
            _glow.range = 3f;
            _glow.intensity = 1.8f;
            _glow.shadows = LightShadows.None;
            _glow.color = Core.RarityColors.Get(rarity);
        }

        private float _spin;
        private Vector3 _visualBase;
        private bool _baseCaptured;

        private void Update()
        {
            if (!_available.Value || visual == null) return;

            if (!_baseCaptured) { _visualBase = visual.transform.localPosition; _baseCaptured = true; }

            // Tilted spin + bob + breathing glow so pickups read as loot rather
            // than a cube sitting on the floor.
            _spin += spinDegreesPerSecond * Time.deltaTime;
            visual.transform.localRotation = Quaternion.AngleAxis(_spin, Vector3.up) * Quaternion.Euler(20f, 0f, 0f);
            visual.transform.localPosition = _visualBase + Vector3.up * (0.12f + 0.08f * Mathf.Sin(Time.time * 2.2f));
            if (_glow != null) _glow.intensity = 1.6f + 0.9f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.6f));
        }

        private void ApplyAvailability(bool isAvailable)
        {
            if (visual != null) visual.SetActive(isAvailable);
            if (_glow != null) _glow.enabled = isAvailable;
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
