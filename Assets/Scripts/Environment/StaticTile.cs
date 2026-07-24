using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Persists per-object texture tiling for baked map geometry. MapBuilder
    /// normally sets tiling through a MaterialPropertyBlock, which is a runtime
    /// value and isn't saved in the scene — so once the map is baked into real
    /// objects, this component re-applies the tiling in both the editor and at
    /// runtime. Without it, baked floors/walls would smear a 0..1 texture across
    /// the whole surface.
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class StaticTile : MonoBehaviour
    {
        [SerializeField] private Vector4 st = new(1f, 1f, 0f, 0f);

        private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");

        public void SetTiling(Vector4 value)
        {
            st = value;
            Apply();
        }

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        private void Apply()
        {
            if (!TryGetComponent<Renderer>(out var renderer)) return;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetVector(BaseMapST, st);
            block.SetVector(MainTexST, st);
            renderer.SetPropertyBlock(block);
        }
    }
}
