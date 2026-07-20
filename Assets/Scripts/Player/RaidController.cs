using ClutchFPS.Core;
using ClutchFPS.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Player
{
    /// Ties a player to their persistent stash across a raid:
    /// - On spawn (server), loads the stash into loadout + inventory. First-
    ///   time players get a starter kit.
    /// - On extraction, writes the current gear back to the stash (kept).
    /// - On death, the stash is NOT updated, so everything carried is lost.
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerWeaponController))]
    public class RaidController : NetworkBehaviour
    {
        private PlayerInventory _inventory;
        private PlayerWeaponController _weapons;
        private PlayerRespawn _respawn;
        private string _playerName;

        // Synced so the owning client's HUD can show extract state.
        private readonly NetworkVariable<bool> _extracted = new(false,
            writePerm: NetworkVariableWritePermission.Server);

        public bool HasExtracted => _extracted.Value;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _weapons = GetComponent<PlayerWeaponController>();
            _respawn = GetComponent<PlayerRespawn>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            // The name variable may not be replicated yet; poll briefly.
            Invoke(nameof(LoadStashServer), 0.5f);
        }

        private void LoadStashServer()
        {
            _playerName = _respawn != null ? _respawn.ResolvedName : $"Player {OwnerClientId}";
            var stash = StashService.Get(_playerName);

            if (stash == null)
            {
                // Starter kit for a brand-new player.
                _inventory.ServerImport(
                    new[] { (int)ItemType.Ammo556, (int)ItemType.Ammo9mm },
                    new[] { 60, 30 });
                return;
            }

            _weapons.ServerApplyLoadout(stash.ownedSlots, stash.weaponVariants);
            _inventory.ServerImport(stash.itemIds, stash.itemCounts);
        }

        /// Called by the extraction zone on the server.
        public void ServerExtract()
        {
            if (!IsServer || _extracted.Value) return;
            if (_respawn != null && _respawn.IsDead) return;

            _inventory.ServerExport(out int[] ids, out int[] counts);
            StashService.Save(new StashService.StashEntry
            {
                playerName = _playerName,
                ownedSlots = _weapons.OwnedSlotsMask,
                weaponVariants = _weapons.ServerGetVariants(),
                itemIds = ids,
                itemCounts = counts
            });
            _extracted.Value = true;
            FreezeExtractedClientRpc();
        }

        [ClientRpc]
        private void FreezeExtractedClientRpc()
        {
            // Owner stops receiving gameplay input via PlayerHUD.LocalMenuOpen-
            // style gating is handled by the HUD reading HasExtracted.
        }
    }
}
