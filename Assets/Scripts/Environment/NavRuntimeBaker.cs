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
            surface.BuildNavMesh();
        }
    }
}
