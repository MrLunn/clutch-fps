using ClutchFPS.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace ClutchFPS.Environment
{
    /// Server-side: scatters armor, helmets and the odd medkit across the
    /// walkable map so gear is lootable off the ground, not only from AI kills.
    /// Seeds a batch shortly after the raid starts, then tops up periodically.
    /// Self-bootstraps — no scene wiring.
    public class MapGearSpawner : MonoBehaviour
    {
        private const int SeedBatch = 10;
        private const int TopUpBatch = 3;
        private const float SeedDelay = 3f;
        private const float TopUpInterval = 55f;

        private Vector3[] _navVerts;
        private float _next;
        private bool _seeded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<MapGearSpawner>() != null) return;
            var go = new GameObject("MapGearSpawner");
            go.AddComponent<MapGearSpawner>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer)
            {
                // Returned to menu / not hosting — re-seed next time we host.
                _seeded = false;
                return;
            }
            if (Time.time < _next) return;
            _next = Time.time + (_seeded ? TopUpInterval : SeedDelay);

            int batch = _seeded ? TopUpBatch : SeedBatch;
            for (int i = 0; i < batch; i++) ScatterGear();
            _seeded = true;
        }

        private void ScatterGear()
        {
            if (!RandomNavPoint(out var pos)) return;

            int roll = Random.Range(0, 100);
            int id = roll < 40 ? (int)ItemType.ArmorLight
                   : roll < 60 ? (int)ItemType.ArmorHeavy
                   : roll < 80 ? (int)ItemType.HelmetLight
                   : roll < 90 ? (int)ItemType.HelmetHeavy
                   : (int)ItemType.Medkit;
            LootSpawner.SpawnItem(pos, id, 1);
        }

        private bool RandomNavPoint(out Vector3 pos)
        {
            pos = Vector3.zero;
            if (_navVerts == null || _navVerts.Length == 0)
            {
                var tri = NavMesh.CalculateTriangulation();
                if (tri.vertices == null || tri.vertices.Length == 0) return false;
                _navVerts = tri.vertices;
            }

            var v = _navVerts[Random.Range(0, _navVerts.Length)];
            if (NavMesh.SamplePosition(v, out var hit, 3f, NavMesh.AllAreas))
            {
                pos = hit.position + Vector3.up * 0.4f;
                return true;
            }
            return false;
        }
    }
}
