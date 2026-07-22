using System.Collections.Generic;
using ClutchFPS.Core;
using ClutchFPS.Networking;
using ClutchFPS.Weapons;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Player
{
    /// Server-authoritative player death and respawn. On death the player is
    /// frozen and hidden everywhere; after the delay the server refills the
    /// loadout, resets health, and teleports the owner to a spawn point.
    [RequireComponent(typeof(Health))]
    public class PlayerRespawn : NetworkBehaviour
    {
        [SerializeField] private float respawnDelay = 4f;
        [SerializeField] private MeshRenderer bodyRenderer;

        private Health _health;
        private PlayerWeaponController _weapons;
        private FirstPersonMovement _movement;

        private readonly NetworkVariable<bool> _isDead = new(false,
            writePerm: NetworkVariableWritePermission.Server);

        public bool IsDead => _isDead.Value;

        public readonly NetworkVariable<int> Kills = new(0,
            writePerm: NetworkVariableWritePermission.Server);
        public readonly NetworkVariable<int> Deaths = new(0,
            writePerm: NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<FixedString32Bytes> DisplayName = new(default,
            writePerm: NetworkVariableWritePermission.Owner);

        // Who landed the killing blow, shown on the death screen.
        public readonly NetworkVariable<FixedString32Bytes> LastKiller = new(default,
            writePerm: NetworkVariableWritePermission.Server);

        public string ResolvedName =>
            DisplayName.Value.IsEmpty ? $"Player {OwnerClientId}" : DisplayName.Value.ToString();

        // Recent incoming-damage sources for the local player, used by the HUD
        // to draw directional indicators. Static because only one player is
        // local; entries age out after a short window.
        public struct DamageHit { public Vector3 Source; public float Time; }
        private static readonly List<DamageHit> _localDamage = new();

        /// When the local player last took a hit — the HUD flashes red off this.
        public static float LastHitTime { get; private set; } = -10f;
        public static IReadOnlyList<DamageHit> LocalDamage
        {
            get
            {
                _localDamage.RemoveAll(h => Time.time - h.Time > 1.2f);
                return _localDamage;
            }
        }

        /// Server-side: tell the victim's owner where a hit came from.
        public void ServerReportDamage(Vector3 source)
        {
            if (!IsServer) return;
            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
            ReportDamageClientRpc(source, target);
        }

        [ClientRpc]
        private void ReportDamageClientRpc(Vector3 source, ClientRpcParams _ = default)
        {
            if (!IsOwner) return;
            _localDamage.Add(new DamageHit { Source = source, Time = Time.time });
            LastHitTime = Time.time;
            CameraShake.Add(0.32f);
        }

        private void Awake()
        {
            _health = GetComponent<Health>();
            _weapons = GetComponent<PlayerWeaponController>();
            _movement = GetComponent<FirstPersonMovement>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner)
            {
                DisplayName.Value = PlayerIdentity.LocalName;
            }
            _health.Died += OnDiedServer;
            _isDead.OnValueChanged += (_, dead) => ApplyDeadState(dead);
            ApplyDeadState(_isDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            _health.Died -= OnDiedServer;
        }

        private void OnDiedServer(ulong attackerClientId)
        {
            if (!IsServer) return;
            _isDead.Value = true;
            Deaths.Value++;
            if (TryGetComponent<RaidController>(out var raid)) raid.ServerRecordDeath();
            // AI kills show as "Enemy"; unknown attackers fall back to the victim.
            string attackerName = attackerClientId == Environment.EnemyAI.AiClientId
                ? "Enemy" : ResolvedName;
            if (attackerClientId != OwnerClientId
                && NetworkManager.ConnectedClients.TryGetValue(attackerClientId, out var attacker)
                && attacker.PlayerObject != null
                && attacker.PlayerObject.TryGetComponent<PlayerRespawn>(out var attackerRespawn))
            {
                attackerRespawn.Kills.Value++;
                attackerName = attackerRespawn.ResolvedName;
                // Killing a real player pays triple an AI.
                if (attacker.PlayerObject.TryGetComponent<RaidController>(out var attackerRaid))
                {
                    attackerRaid.ServerAwardKill(true);
                }
            }
            LastKiller.Value = attackerClientId == OwnerClientId ? "themselves" : attackerName;
            KillFeedClientRpc(attackerName, ResolvedName, attackerClientId == OwnerClientId);
            Invoke(nameof(RespawnServer), respawnDelay);
        }

        [ClientRpc]
        private void KillFeedClientRpc(string attackerName, string victimName, bool suicide)
        {
            KillFeed.Add(attackerName, victimName, suicide);
        }

        private void RespawnServer()
        {
            _weapons.ServerRefillAllAmmo();
            _health.ResetHealth();
            _isDead.Value = false;

            var spawnPoints = FindFirstObjectByType<PlayerSpawnPoints>();
            var point = spawnPoints != null ? spawnPoints.GetRandomSpawnPoint() : null;
            if (point != null && _movement != null)
            {
                _movement.TeleportClientRpc(point.position);
            }
        }

        private void ApplyDeadState(bool dead)
        {
            // Body: visible only when alive, and never to the owning player.
            if (bodyRenderer != null)
            {
                bodyRenderer.enabled = !dead && !IsOwner;
            }

            // Weapons: all hidden while dead; restore the active slot on respawn.
            if (_weapons != null)
            {
                if (dead)
                {
                    for (int i = 0; i < _weapons.SlotCount; i++)
                    {
                        _weapons.WeaponAt(i)?.SetHolstered(true);
                    }
                }
                else
                {
                    _weapons.RefreshVisibleWeapon();
                }
            }
        }
    }
}
