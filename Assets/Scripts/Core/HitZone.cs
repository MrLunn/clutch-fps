using UnityEngine;

namespace ClutchFPS.Core
{
    /// Marks a collider as a special hit region (e.g. the head). The weapon
    /// raycast checks the hit collider for this to modify damage.
    public class HitZone : MonoBehaviour
    {
        [Tooltip("A hit here kills instantly regardless of weapon damage.")]
        public bool instantKill = true;

        [Tooltip("Used when Instant Kill is off.")]
        public float damageMultiplier = 2f;
    }
}
