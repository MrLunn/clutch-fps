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

        public override void OnNetworkSpawn()
        {
            if (IsServer) _armedTime = Time.time + 1f;
            // Re-tint whenever the contents sync in; the values arrive as
            // separate deltas, so keying off any of them and re-reading all is
            // simplest and race-free.
            _itemId.OnValueChanged += (_, __) => ApplyTint();
            _weaponSlot.OnValueChanged += (_, __) => ApplyTint();
            _weaponVariant.OnValueChanged += (_, __) => ApplyTint();
            ApplyTint();
        }

        // Auto-collect on walk-over once armed. Server-authoritative like every
        // other pickup; a full inventory just leaves the drop on the ground.
        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || _taken || Time.time < _armedTime) return;
            if (other.TryGetComponent<PlayerWeaponController>(out var player))
            {
                ServerTryPickup(player);
            }
        }

        /// Colour the drop by what it holds — item tint, or weapon rarity — so
        /// you can tell from across a room whether it is worth the walk.
        private void ApplyTint()
        {
            if (visual == null || !visual.TryGetComponent<Renderer>(out var renderer)) return;
            Color tint = IsWeapon ? RarityColors.Get(Rarity) : Items.Get(_itemId.Value).Tint;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", tint);
            block.SetColor("_Color", tint);
            renderer.SetPropertyBlock(block);
        }

        private void Update()
        {
            if (visual != null)
            {
                visual.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
            }
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
                player.ServerGrantSlot(_weaponSlot.Value);
                if (_weaponVariant.Value >= 0) weapon.ServerSetWeaponData(_weaponVariant.Value);
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
