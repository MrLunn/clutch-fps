using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace ClutchFPS.Networking
{
    /// Server-side spawn point picker. Spreads players across the whole walkable
    /// map by sampling the runtime-baked NavMesh, so nobody clusters on top of
    /// each other. Falls back to designer-placed transforms if the NavMesh isn't
    /// available yet.
    public class PlayerSpawnPoints : MonoBehaviour
    {
        [SerializeField] private Transform[] spawnPoints;

        private Vector3[] _navPoints;

        /// A spread spawn position somewhere on the walkable map.
        public Vector3 GetRandomSpawnPosition()
        {
            EnsureNavPoints();
            if (_navPoints != null && _navPoints.Length > 0)
                return _navPoints[Random.Range(0, _navPoints.Length)];
            if (spawnPoints != null && spawnPoints.Length > 0)
                return spawnPoints[Random.Range(0, spawnPoints.Length)].position;
            return Vector3.up;
        }

        /// Kept for compatibility; prefer GetRandomSpawnPosition.
        public Transform GetRandomSpawnPoint()
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return null;
            return spawnPoints[Random.Range(0, spawnPoints.Length)];
        }

        // Thin the NavMesh vertices down to a set of well-spread, snapped points
        // across the map. Only cache once we actually get a baked mesh.
        private void EnsureNavPoints()
        {
            if (_navPoints != null && _navPoints.Length > 0) return;

            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0) return;

            var list = new List<Vector3>();
            int step = Mathf.Max(1, tri.vertices.Length / 48);
            for (int i = 0; i < tri.vertices.Length; i += step)
            {
                if (NavMesh.SamplePosition(tri.vertices[i], out var hit, 3f, NavMesh.AllAreas))
                    list.Add(hit.position + Vector3.up * 1.2f);
            }
            if (list.Count > 0) _navPoints = list.ToArray();
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
            if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)) return;
            if (client.PlayerObject == null) return;

            if (client.PlayerObject.TryGetComponent<Player.FirstPersonMovement>(out var movement))
            {
                movement.TeleportClientRpc(GetRandomSpawnPosition());
            }
        }
    }
}
