using UnityEngine;

namespace ClutchFPS.Core
{
    public enum ItemType
    {
        Medkit = 0,
        Ammo556 = 1,
        Ammo9mm = 2,
        // Gear: picked up like items but equipped on the spot rather than
        // stacked in the inventory (see Items.IsGear / Health.ServerEquipGear).
        ArmorLight = 3,
        ArmorHeavy = 4,
        HelmetLight = 5,
        HelmetHeavy = 6,
        Grenade = 7
    }

    public struct ItemInfo
    {
        public string Name;
        public int MaxStack;
        public Color Tint;
        public bool Usable;
    }

    /// Code registry of item definitions; index = item id carried over the
    /// network. Move to ScriptableObjects when items need art/configs.
    public static class Items
    {
        private static readonly ItemInfo[] Registry =
        {
            new() { Name = "Medkit", MaxStack = 3, Tint = new Color(0.9f, 0.3f, 0.3f), Usable = true },
            new() { Name = "5.56 Rounds", MaxStack = 90, Tint = new Color(0.3f, 0.8f, 0.35f), Usable = false },
            new() { Name = "9mm Rounds", MaxStack = 90, Tint = new Color(0.95f, 0.8f, 0.3f), Usable = false },
            new() { Name = "Light Armor", MaxStack = 1, Tint = new Color(0.45f, 0.62f, 0.85f), Usable = false },
            new() { Name = "Heavy Armor", MaxStack = 1, Tint = new Color(0.35f, 0.5f, 0.95f), Usable = false },
            new() { Name = "Light Helmet", MaxStack = 1, Tint = new Color(0.7f, 0.75f, 0.55f), Usable = false },
            new() { Name = "Heavy Helmet", MaxStack = 1, Tint = new Color(0.6f, 0.7f, 0.35f), Usable = false },
            new() { Name = "Frag Grenade", MaxStack = 3, Tint = new Color(0.35f, 0.42f, 0.3f), Usable = false }
        };

        public static ItemInfo Get(int id) =>
            Registry[Mathf.Clamp(id, 0, Registry.Length - 1)];

        /// Armor/helmet items equip on pickup instead of stacking.
        public static bool IsGear(int id) => id >= (int)ItemType.ArmorLight && id <= (int)ItemType.HelmetHeavy;
    }
}
