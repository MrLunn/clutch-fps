using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Server-side spawn point picker. Attach to the NetworkManager object and
    /// assign a few empty transforms placed around the range as spawn points.
    public class PlayerSpawnPoints : MonoBehaviour
    {
        [SerializeField] private Transform[] spawnPoints;

        public Transform GetRandomSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return null;
            return spawnPoints[Random.Range(0, spawnPoints.Length)];
        }

        private void Awake()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnServerStarted += HookPlayerSpawns;
            }
        }

        private void HookPlayerSpawns()
        {
            if (!NetworkManager.Singleton.IsServer) return;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return;
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) return;
            if (client.PlayerObject == null) return;

            Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (client.PlayerObject.TryGetComponent<Player.FirstPersonMovement>(out var movement))
            {
                movement.TeleportClientRpc(point.position);
            }
        }
    }
}
