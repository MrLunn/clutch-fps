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

        /// Raid summary, filled on the owning client when extraction succeeds.
        public struct RaidSummary
        {
            public bool Valid;
            public float Duration;
            public int Kills;
            public int ItemsOut;
            public string[] Lines;
        }

        public RaidSummary Summary;

        private float _raidStartTime;
        private int _startingKills;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _weapons = GetComponent<PlayerWeaponController>();
            _respawn = GetComponent<PlayerRespawn>();
        }

        public override void OnNetworkSpawn()
        {
            _raidStartTime = Time.time;
            if (IsOwner && _respawn != null) _startingKills = _respawn.Kills.Value;
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

            // Build the human-readable haul for the summary screen.
            var lines = new System.Collections.Generic.List<string>();
            for (int slot = 0; slot < _weapons.SlotCount; slot++)
            {
                if (!_weapons.OwnsSlot(slot)) continue;
                var weapon = _weapons.WeaponAt(slot);
                if (weapon != null) lines.Add($"{weapon.Data.weaponName}");
            }
            for (int i = 0; i < ids.Length; i++)
            {
                lines.Add($"{Items.Get(ids[i]).Name} x{counts[i]}");
            }
            // Netcode can't serialize string[]; send one joined string.
            SummaryClientRpc(string.Join("|", lines));
            LeaveWorldClientRpc();
        }

        /// Extracted players are out of the raid: stop simulating them so they
        /// can't drift, be shot, or be chased while the summary is up.
        [ClientRpc]
        private void LeaveWorldClientRpc()
        {
            if (TryGetComponent<CharacterController>(out var controller)) controller.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }

        [ClientRpc]
        private void SummaryClientRpc(string joinedLines)
        {
            if (!IsOwner) return;
            var lines = string.IsNullOrEmpty(joinedLines)
                ? new string[0]
                : joinedLines.Split('|');
            Summary = new RaidSummary
            {
                Valid = true,
                Duration = Time.time - _raidStartTime,
                Kills = _respawn != null ? _respawn.Kills.Value - _startingKills : 0,
                ItemsOut = lines.Length,
                Lines = lines
            };
        }
    }
}
