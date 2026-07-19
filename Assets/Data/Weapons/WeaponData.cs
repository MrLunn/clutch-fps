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
        public Core.Rarity rarity = Core.Rarity.Common;

        [Tooltip("Fire modes this weapon supports. B cycles through them in game.")]
        public FireMode[] availableFireModes = { FireMode.Single };

        [Header("Burst")]
        public int burstCount = 3;
        [Tooltip("Seconds between the shots inside one burst.")]
        public float burstShotInterval = 0.08f;

        [Header("Damage & Range")]
        public float damage = 20f;
        public float range = 100f;

        [Tooltip("Item id of the ammo this weapon consumes on reload (1 = 5.56, 2 = 9mm).")]
        public int ammoItemId = 1;

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

        [Header("Recoil & Feel")]
        [Tooltip("Degrees the view kicks up per shot (before scaling).")]
        public float recoilPitchKick = 0.6f;
        [Tooltip("Max random sideways kick per shot, in degrees.")]
        public float recoilYawKick = 0.2f;
        [Tooltip("Recoil multiplier on the first shot (zero bloom).")]
        public float recoilMinScale = 0.35f;
        [Tooltip("Recoil multiplier at full bloom.")]
        public float recoilMaxScale = 1.25f;
        [Tooltip("How far the weapon model snaps back per shot, in meters.")]
        public float kickbackDistance = 0.07f;
        [Tooltip("How fast the weapon eases back to rest.")]
        public float kickbackRecoverSpeed = 8f;

        [Header("Aim Down Sights")]
        [Tooltip("Camera FOV while aiming (base is 60).")]
        public float adsFov = 48f;
        [Tooltip("Multiplies max spread while aiming.")]
        public float adsSpreadMultiplier = 0.5f;
        [Tooltip("Multiplies recoil while aiming.")]
        public float adsRecoilMultiplier = 0.8f;
        [Tooltip("Multiplies move speed while aiming.")]
        public float adsMoveSpeedMultiplier = 0.6f;
        [Tooltip("View-model local position when fully aimed (centered under the camera).")]
        public Vector3 adsPosition = new(0f, -0.21f, 0.3f);

        [Header("Crouch Bonuses")]
        [Tooltip("Multiplies max spread while crouched (lower = more accurate).")]
        public float crouchSpreadMultiplier = 0.45f;
        [Tooltip("Multiplies recoil while crouched.")]
        public float crouchRecoilMultiplier = 0.7f;

        [Header("Audio (optional; falls back to procedural)")]
        public AudioClip fireSound;
        public AudioClip reloadSound;
        public AudioClip impactSound;
        [Range(0f, 1f)] public float fireVolume = 0.7f;

        [Header("Animation (optional; clips from the weapon model FBX)")]
        public AnimationClip fireAnimation;
        public AnimationClip reloadAnimation;

        [Header("VFX (optional; falls back to built-in)")]
        [Tooltip("Material for tracer lines. Must be an asset: runtime Shader.Find is stripped in builds.")]
        public Material tracerMaterial;
        public GameObject muzzleFlashPrefab;
        public GameObject fleshImpactPrefab;
        public GameObject worldImpactPrefab;
        [Tooltip("Seconds before a spawned VFX instance is destroyed.")]
        public float vfxLifetime = 2f;

        [Tooltip("Seconds bullet-hole impact marks linger on surfaces.")]
        public float impactLifetime = 10f;
    }
}
