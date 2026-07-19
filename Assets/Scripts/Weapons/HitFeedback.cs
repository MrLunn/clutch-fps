using UnityEngine;

namespace ClutchFPS.Weapons
{
    /// Owner-side hit confirmation shared between Weapon (writes) and
    /// PlayerHUD (reads, to flash the hitmarker).
    public static class HitFeedback
    {
        public static float LastHitTime { get; private set; } = -10f;
        public static float MagFullTime { get; private set; } = -10f;
        public static float NoAmmoTime { get; private set; } = -10f;
        public static float LastKillTime { get; private set; } = -10f;
        public static bool LastHitWasHeadshot { get; private set; }

        public static void RegisterNoAmmo()
        {
            NoAmmoTime = Time.time;
        }

        public static void RegisterHit(bool headshot, bool killed)
        {
            LastHitTime = Time.time;
            LastHitWasHeadshot = headshot;
            if (killed) LastKillTime = Time.time;
        }

        public static void RegisterMagFull()
        {
            MagFullTime = Time.time;
        }
    }
}
