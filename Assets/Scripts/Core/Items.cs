using UnityEngine;

namespace ClutchFPS.Core
{
    public enum ItemType
    {
        Medkit = 0,
        Ammo556 = 1,
        Ammo9mm = 2
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
            new() { Name = "9mm Rounds", MaxStack = 90, Tint = new Color(0.95f, 0.8f, 0.3f), Usable = false }
        };

        public static ItemInfo Get(int id) =>
            Registry[Mathf.Clamp(id, 0, Registry.Length - 1)];
    }
}
