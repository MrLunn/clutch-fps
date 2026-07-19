using UnityEngine;

namespace ClutchFPS.Weapons
{
    public enum FireMode
    {
        Single,
        Burst,
        Automatic
    }

    /// Data-driven weapon stats. Create one asset per weapon (Rifle, Pistol, ...)
    /// via Assets > Create > Clutch FPS > Weapon Data, no code changes needed for new weapons.
    [CreateAssetMenu(menuName = "Clutch FPS/Weapon Data", fileName = "NewWeaponData")]
    public class WeaponData : ScriptableObject
    {
        public string weaponName = "Weapon";

        [Tooltip("Fire modes this weapon supports. B cycles through them in game.")]
        public FireMode[] availableFireModes = { FireMode.Single };

        [Header("Burst")]
        public int burstCount = 3;
        [Tooltip("Seconds between the shots inside one burst.")]
        public float burstShotInterval = 0.08f;

        [Header("Damage & Range")]
        public float damage = 20f;
        public float range = 100f;

        [Header("Fire Rate & Ammo")]
        [Tooltip("Shots per second while trigger/fire input is held (Automatic) or per press (Single).")]
        public float fireRate = 6f;
        public int magazineSize = 12;
        public float reloadTime = 1.4f;

        [Header("Accuracy")]
        [Tooltip("Spread in degrees at full bloom. First shots have (near) zero spread.")]
        public float spreadDegrees = 1.5f;

        [Tooltip("How much one shot adds to bloom (0-1 scale; 0.25 = full bloom after 4 quick shots).")]
        public float bloomPerShot = 0.22f;

        [Tooltip("Bloom recovered per second while not firing.")]
        public float bloomRecoverPerSecond = 2.2f;
    }
}
