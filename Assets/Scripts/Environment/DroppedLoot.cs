using ClutchFPS.Core;
using ClutchFPS.Player;
using ClutchFPS.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Loot a player threw on the ground. Unlike scene loot it has no fixed
    /// contents — the server writes them right after spawning — and it does not
    /// respawn: once someone takes it, it is gone.
    ///
    /// Carries either an item stack or a weapon slot, never both.
    public class DroppedLoot : NetworkBehaviour
    {
        [SerializeField] private Transform visual;
        [SerializeField] private float spinDegreesPerSecond = 70f;

        // -1 in _itemId means this drop is a weapon instead of an item stack.
        private readonly NetworkVariable<int> _itemId = new(-1);
        private readonly NetworkVariable<int> _amount = new(0);
        private readonly NetworkVariable<int> _weaponSlot = new(-1);
        private readonly NetworkVariable<int> _weaponVariant = new(-1);

        private bool _taken;

        // Brief grace period after spawning so the player who dropped it doesn't
        // instantly walk back into it and re-collect it.
        private float _armedTime;

        public bool IsWeapon => _weaponSlot.Value >= 0;

        public string DisplayName
        {
            get
            {
                if (IsWeapon)
                {
                    var data = WeaponDatabase.Instance != null
                        ? WeaponDatabase.Instance.Get(_weaponVariant.Value) : null;
                    return data != null ? data.weaponName : "Weapon";
                }
                var info = Items.Get(_itemId.Value);
                return _amount.Value > 1 ? $"{info.Name} x{_amount.Value}" : info.Name;
            }
        }

        public Rarity Rarity
        {
            get
            {
                if (!IsWeapon) return Rarity.Common;
                var data = WeaponDatabase.Instance != null
                    ? WeaponDatabase.Instance.Get(_weaponVariant.Value) : null;
                return data != null ? data.rarity : Rarity.Common;
            }
        }

        /// Server-side, called immediately after Spawn().
        public void ServerSetItem(int itemId, int amount)
        {
            _itemId.Value = itemId;
            _amount.Value = amount;
            _weaponSlot.Value = -1;
        }

        /// Server-side, called immediately after Spawn().
        public void ServerSetWeapon(int slot, int variantIndex)
        {
            _itemId.Value = -1;
            _weaponSlot.Value = slot;
            _weaponVariant.Value = variantIndex;
        }

        private float _spin;
        private Vector3 _visualBase;

        public override void OnNetworkSpawn()
        {
            if (IsServer) _armedTime = Time.time + 1f;
            _visualBase = visual != null ? visual.localPosition : Vector3.zero;
            // Re-tint whenever the contents sync in; the values arrive as
            // separate deltas, so keying off any of them and re-reading all is
            // simplest and race-free.
            _itemId.OnValueChanged += (_, __) => ApplyTint();
            _weaponSlot.OnValueChanged += (_, __) => ApplyTint();
            _weaponVariant.OnValueChanged += (_, __) => ApplyTint();
            ApplyTint();
        }

        // Items auto-collect on walk-over; weapons don't, so you never lose the
        // gun in your hands just by walking through loot — they're picked up
        // deliberately with E (see PlayerInteractor).
        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || _taken || Time.time < _armedTime || IsWeapon) return;
            if (other.TryGetComponent<PlayerWeaponController>(out var player))
            {
                ServerTryPickup(player);
            }
        }

        private Light _glow;
        private GameObject _model;
        private Transform _animTarget;

        /// Swap in the real mesh for this loot's type (built into Resources/Loot),
        /// colour a rarity glow, and — for the fallback cube only — tint it.
        private void ApplyTint()
        {
            Color tint = IsWeapon ? RarityColors.Get(Rarity) : Items.Get(_itemId.Value).Tint;

            EnsureModel();

            // Only the placeholder cube gets tinted; real models keep their
            // own textures and show rarity through the glow instead.
            if (_model == null && visual != null && visual.TryGetComponent<Renderer>(out var renderer))
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                block.SetColor("_Color", tint);
                renderer.SetPropertyBlock(block);
            }

            if (_glow == null)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.up * 0.4f;
                _glow = go.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.range = 3f;
                _glow.intensity = 2.4f;
                _glow.shadows = LightShadows.None;
            }
            _glow.color = tint;
        }

        /// The Resources/Loot model key for this loot, or null before the type
        /// has synced in.
        private string ModelKey()
        {
            if (IsWeapon) return $"Weapon_{Mathf.Clamp(_weaponSlot.Value, 0, 2)}";
            if (_itemId.Value < 0) return null;
            return _itemId.Value == (int)ItemType.Medkit ? "Medkit" : "Ammo";
        }

        private void EnsureModel()
        {
            if (_model != null) return;
            string key = ModelKey();
            if (key == null) return;
            var prefab = Resources.Load<GameObject>($"Loot/{key}");
            if (prefab == null) return; // fall back to the cube

            _model = Instantiate(prefab, transform);
            _model.transform.localPosition = _visualBase;
            foreach (var col in _model.GetComponentsInChildren<Collider>()) Destroy(col);
            if (visual != null) visual.gameObject.SetActive(false);
            _animTarget = _model.transform;
        }

        private void Update()
        {
            var target = _animTarget != null ? _animTarget : visual;
            if (target == null) return;

            // Tilted spin so it tumbles like a token, not a flat box; a gentle
            // bob floats it off the ground; the glow breathes. Reads as loot.
            _spin += spinDegreesPerSecond * Time.deltaTime;
            target.localRotation = Quaternion.AngleAxis(_spin, Vector3.up) * Quaternion.Euler(22f, 0f, 0f);
            target.localPosition = _visualBase + Vector3.up * (0.18f + 0.1f * Mathf.Sin(Time.time * 2.4f));
            if (_glow != null) _glow.intensity = 2.0f + 1.1f * (0.5f + 0.5f * Mathf.Sin(Time.time * 3f));
        }

        /// Server-side. Returns false when the taker has no room for it, so the
        /// drop stays put rather than vanishing into a full inventory.
        public bool ServerTryPickup(PlayerWeaponController player)
        {
            if (!IsServer || _taken || player == null) return false;

            if (IsWeapon)
            {
                var weapon = player.WeaponAt(_weaponSlot.Value);
                if (weapon == null) return false;

                // If this slot already holds a weapon, drop that one where the
                // player stands rather than destroying it — a true swap.
                bool replacing = player.OwnsSlot(_weaponSlot.Value);
                int oldVariant = weapon.VariantIndex;
                var oldData = weapon.Data;

                player.ServerGrantSlot(_weaponSlot.Value);
                weapon.ServerSetWeaponData(_weaponVariant.Value);

                if (replacing)
                {
                    int dropVariant = oldVariant;
                    if (dropVariant < 0 && WeaponDatabase.Instance != null)
                    {
                        dropVariant = WeaponDatabase.Instance.IndexOf(oldData);
                    }
                    LootSpawner.SpawnWeapon(
                        LootSpawner.DropPoint(player.transform), _weaponSlot.Value, dropVariant);
                }
            }
            else
            {
                if (!player.TryGetComponent<PlayerInventory>(out var inventory)) return false;
                if (!inventory.ServerAddItem(_itemId.Value, _amount.Value)) return false;
            }

            _taken = true;
            NetworkObject.Despawn();
            return true;
        }
    }
}
