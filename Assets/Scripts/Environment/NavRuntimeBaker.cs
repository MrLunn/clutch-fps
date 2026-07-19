using Unity.AI.Navigation;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Builds the NavMesh at startup so AI can path around whatever geometry
    /// the scene currently has — no editor baking step needed.
    public class NavRuntimeBaker : MonoBehaviour
    {
        private void Awake()
        {
            var surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();
        }
    }
}
