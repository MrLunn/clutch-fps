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

        private static WeaponDatabase _instance;

        /// Shared instance for code with no serialized reference of its own
        /// (runtime-spawned loot). Weapons keep using their own field.
        public static WeaponDatabase Instance =>
            _instance != null ? _instance : _instance = Resources.Load<WeaponDatabase>("WeaponDatabase");

        /// Database index of a WeaponData asset, or -1 if it isn't listed.
        /// Lets a dropped default weapon resolve to a concrete entry for its
        /// ground label and for re-equipping on pickup.
        public int IndexOf(WeaponData weapon)
        {
            if (weapons == null || weapon == null) return -1;
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] == weapon) return i;
            }
            return -1;
        }

        public WeaponData Get(int index) =>
            weapons != null && index >= 0 && index < weapons.Length ? weapons[index] : null;
    }
}
