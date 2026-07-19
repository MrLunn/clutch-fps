using ClutchFPS.Core;
using ClutchFPS.Networking;
using ClutchFPS.Weapons;
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

        private void Awake()
        {
            _health = GetComponent<Health>();
            _weapons = GetComponent<PlayerWeaponController>();
            _movement = GetComponent<FirstPersonMovement>();
        }

        public override void OnNetworkSpawn()
        {
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
            if (attackerClientId != OwnerClientId
                && NetworkManager.ConnectedClients.TryGetValue(attackerClientId, out var attacker)
                && attacker.PlayerObject != null
                && attacker.PlayerObject.TryGetComponent<PlayerRespawn>(out var attackerRespawn))
            {
                attackerRespawn.Kills.Value++;
            }
            KillFeedClientRpc(attackerClientId, OwnerClientId);
            Invoke(nameof(RespawnServer), respawnDelay);
        }

        [ClientRpc]
        private void KillFeedClientRpc(ulong attackerClientId, ulong victimClientId)
        {
            KillFeed.Add(attackerClientId, victimClientId);
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
