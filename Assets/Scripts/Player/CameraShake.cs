using UnityEngine;

namespace ClutchFPS.Player
{
    /// Trauma-based camera shake on the local player's camera pivot. Systems call
    /// CameraShake.Add(amount); the shake decays and reads as recoil/impact.
    /// The pivot's position (movement) and rotation (MouseLook) are rewritten
    /// absolutely every Update, so this just layers an offset on top in
    /// LateUpdate — the next Update wipes it, so no base/undo bookkeeping.
    public class CameraShake : MonoBehaviour
    {
        public static CameraShake Current;

        private float _trauma;
        private float _seed;

        private void Awake()
        {
            Current = this;
            _seed = Random.value * 100f;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        /// Add trauma (0..1). Fire is small, hits/explosions bigger.
        public static void Add(float amount)
        {
            if (Current != null) Current._trauma = Mathf.Clamp01(Current._trauma + amount);
        }

        private void LateUpdate()
        {
            if (_trauma <= 0.0001f) { _trauma = 0f; return; }

            // Slightly super-linear falloff: big hits still dominate, but small
            // trauma stays visible (squaring made per-shot kick imperceptible).
            float s = _trauma * Mathf.Lerp(0.55f, 1f, _trauma);
            float t = Time.time * 28f;
            float nx = (Mathf.PerlinNoise(_seed, t) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(_seed + 11f, t) - 0.5f) * 2f;
            float nz = (Mathf.PerlinNoise(_seed + 23f, t) - 0.5f) * 2f;

            transform.localPosition += new Vector3(nx, ny, 0f) * (s * 0.12f);
            transform.localRotation *= Quaternion.Euler(ny * s * 6f, nx * s * 6f, nz * s * 9f);

            _trauma = Mathf.MoveTowards(_trauma, 0f, Time.deltaTime * 2.2f);
        }
    }
}
