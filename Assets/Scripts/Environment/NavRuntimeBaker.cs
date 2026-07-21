using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Builds the NavMesh at startup so AI can path around whatever geometry
    /// the scene currently has — no editor baking step needed.
    public class NavRuntimeBaker : MonoBehaviour
    {
        // Start, not Awake: MapBuilder generates geometry in Awake, and the
        // NavMesh must be built after that geometry exists.
        private void Start()
        {
            var surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;

            // Bake from physics colliders, not render meshes. Synty props ship
            // with Read/Write disabled, so collecting render meshes throws
            // "does not allow read access" and fails in a build. The map is
            // built from primitive box colliders, so collider geometry bakes
            // cleanly and needs no mesh read access.
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

            // Enemies are in the scene at bake time; their solid hitbox capsules
            // would carve holes in the NavMesh under their own feet and leave
            // them off-mesh. Hide those colliders just for the bake.
            var hidden = new List<Collider>();
            foreach (var enemy in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            {
                foreach (var collider in enemy.GetComponentsInChildren<Collider>())
                {
                    if (collider.enabled && !collider.isTrigger)
                    {
                        collider.enabled = false;
                        hidden.Add(collider);
                    }
                }
            }

            surface.BuildNavMesh();

            foreach (var collider in hidden) collider.enabled = true;
        }
    }
}
