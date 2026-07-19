using UnityEngine;

namespace ClutchFPS.Weapons
{
    /// Index-stable list of every weapon variant. The index is what travels
    /// over the network when loot swaps a weapon's data, so ONLY APPEND —
    /// never reorder or remove entries.
    [CreateAssetMenu(menuName = "Clutch FPS/Weapon Database", fileName = "WeaponDatabase")]
    public class WeaponDatabase : ScriptableObject
    {
        public WeaponData[] weapons;

        public WeaponData Get(int index) =>
            weapons != null && index >= 0 && index < weapons.Length ? weapons[index] : null;
    }
}
