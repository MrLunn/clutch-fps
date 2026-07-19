using UnityEngine;

namespace ClutchFPS.Weapons
{
    /// Owner-side hit confirmation shared between Weapon (writes) and
    /// PlayerHUD (reads, to flash the hitmarker).
    public static class HitFeedback
    {
        public static float LastHitTime { get; private set; } = -10f;

        public static void RegisterHit()
        {
            LastHitTime = Time.time;
        }
    }
}
