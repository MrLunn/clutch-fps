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
        /// How long a raid lasts before the exits lock and stragglers are lost.
        public const float RaidDuration = 480f; // 8 minutes

        private PlayerInventory _inventory;
        private PlayerWeaponController _weapons;
        private PlayerRespawn _respawn;
        private Health _health;
        private string _playerName;
        private bool _timedOut;

        /// Raids only run on the actual maps; the shooting range is untimed.
        public bool RaidActive =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "ShootingRange";

        /// Seconds left in the raid (owner HUD), clamped at zero.
        public float TimeRemaining =>
            Mathf.Max(0f, RaidDuration - (Time.time - _raidStartTime));

        // Synced so the owning client's HUD can show extract state.
        private readonly NetworkVariable<bool> _extracted = new(false,
            writePerm: NetworkVariableWritePermission.Server);

        public bool HasExtracted => _extracted.Value;

        // Kill rewards are paid the instant you get the kill (banked even if you
        // die later); a real player is worth 3x an AI. Survival and time bonuses
        // are paid on extraction.
        private const int CreditsPerAiKill = 150;
        private const int CreditsPerPlayerKill = 450;
        private const int SurviveBonus = 500;
        private const int CreditsPerSecondLeft = 2;

        private int _killCreditsEarned; // this raid, for the summary breakdown

        /// Raid summary, filled on the owning client when extraction succeeds.
        public struct RaidSummary
        {
            public bool Valid;
            public float Duration;
            public int Kills;
            public int ItemsOut;
            public string[] Lines;
            public int KillCredits;
            public int SurviveCredits;
            public int TimeCredits;
            public int TotalCredits;
        }

        public RaidSummary Summary;

        private float _raidStartTime;
        private int _startingKills;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _weapons = GetComponent<PlayerWeaponController>();
            _respawn = GetComponent<PlayerRespawn>();
            _health = GetComponent<Health>();
        }

        private void Update()
        {
            // Server enforces the clock: running out of time with no extract is
            // a failed raid — you die and lose everything you were carrying,
            // exactly as if you'd been killed.
            if (!IsServer || _timedOut || _extracted.Value || !RaidActive) return;
            if (_respawn != null && _respawn.IsDead) return;
            if (Time.time - _raidStartTime < RaidDuration) return;

            _timedOut = true;
            if (_health != null)
            {
                _health.TakeDamage(999999f, OwnerClientId);
                // Overwrite the "themselves" label the suicide path just set.
                if (_respawn != null) _respawn.LastKiller.Value = "the raid clock";
            }
        }

        public override void OnNetworkSpawn()
        {
            _raidStartTime = Time.time;
            if (IsOwner)
            {
                // Clear leftovers from any previous raid in this process.
                Summary = default;
                Environment.ExtractionZone.ResetLocalState();
                KillFeed.Clear();

                if (_respawn != null) _startingKills = _respawn.Kills.Value;
                // Send the name explicitly rather than waiting on replication:
                // guessing the timing meant stashes sometimes saved under the
                // fallback "Player N" key and appeared to vanish.
                RegisterNameServerRpc(PlayerIdentity.LocalName);
            }
        }

        [ServerRpc]
        private void RegisterNameServerRpc(string playerName)
        {
            _playerName = string.IsNullOrWhiteSpace(playerName)
                ? $"Player {OwnerClientId}" : playerName.Trim();
            LoadStashServer();
        }

        private void LoadStashServer()
        {
            // First-timers get a starter kit so the stash is never empty.
            StashService.EnsureStarter(_playerName);
            if (RaidActive) StashService.RecordRaidStart(_playerName);
            var stash = StashService.Get(_playerName);

            // Check out the equipped loadout: which weapon slots to bring and
            // which items ride along (removed from the loadout, so death loses
            // them and extract deposits them back to the safe stash).
            StashService.CheckoutLoadout(_playerName, out int[] loadIds, out int[] loadCounts, out int loadoutSlots);

            if (stash != null)
            {
                _weapons.ServerApplyLoadout(loadoutSlots, stash.weaponVariants);
            }

            // A free base ammo kit each raid so you're never sent in dry...
            var ids = new System.Collections.Generic.List<int>
                { (int)ItemType.Ammo556, (int)ItemType.Ammo9mm };
            var counts = new System.Collections.Generic.List<int> { 60, 30 };

            // ...plus the equipped consumables on top.
            for (int i = 0; i < loadIds.Length; i++)
            {
                int at = ids.IndexOf(loadIds[i]);
                if (at >= 0) counts[at] += loadCounts[i];
                else { ids.Add(loadIds[i]); counts.Add(loadCounts[i]); }
            }

            _inventory.ServerImport(ids.ToArray(), counts.ToArray());
        }

        /// Server-side: pay the killer for a kill, right away. Called when an AI
        /// or another player dies to this player.
        public void ServerAwardKill(bool victimWasPlayer)
        {
            if (!IsServer || !RaidActive || string.IsNullOrEmpty(_playerName)) return;
            int amount = victimWasPlayer ? CreditsPerPlayerKill : CreditsPerAiKill;
            _killCreditsEarned += amount;
            StashService.AddCredits(_playerName, amount);
            StashService.RecordKill(_playerName);
        }

        /// Server-side: log a death against this player's lifetime stats.
        public void ServerRecordDeath()
        {
            if (!IsServer || !RaidActive || string.IsNullOrEmpty(_playerName)) return;
            StashService.RecordDeath(_playerName);
        }

        /// Called by the extraction zone on the server.
        public void ServerExtract()
        {
            if (!IsServer || _extracted.Value) return;
            if (_respawn != null && _respawn.IsDead) return;

            _inventory.ServerExport(out int[] ids, out int[] counts);
            // Deposit merges into the existing stash so hauls accumulate
            // across raids instead of overwriting each other.
            StashService.Deposit(_playerName, _weapons.OwnedSlotsMask,
                _weapons.ServerGetVariants(), ids, counts);
            _extracted.Value = true;

            // Kill credits were already banked as they happened. Extraction adds
            // a flat survival bonus plus a bonus per second left on the clock.
            // Practice range pays nothing.
            int surviveCredits = 0, timeCredits = 0;
            if (RaidActive)
            {
                surviveCredits = SurviveBonus;
                timeCredits = Mathf.RoundToInt(TimeRemaining) * CreditsPerSecondLeft;
            }
            int extractCredits = surviveCredits + timeCredits;
            if (extractCredits > 0) StashService.AddCredits(_playerName, extractCredits);
            if (RaidActive) StashService.RecordExtract(_playerName);

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
            SummaryClientRpc(string.Join("|", lines), _killCreditsEarned, surviveCredits, timeCredits);
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
        private void SummaryClientRpc(string joinedLines, int killCredits, int surviveCredits, int timeCredits)
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
                Lines = lines,
                KillCredits = killCredits,
                SurviveCredits = surviveCredits,
                TimeCredits = timeCredits,
                TotalCredits = killCredits + surviveCredits + timeCredits
            };
        }
    }
}
