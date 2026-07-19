using UnityEngine;

namespace ClutchFPS.Core
{
    public enum ItemType
    {
        Medkit = 0,
        AmmoPack = 1
    }

    public struct ItemInfo
    {
        public string Name;
        public int MaxStack;
        public Color Tint;
    }

    /// Code registry of item definitions; index = item id carried over the
    /// network. Move to ScriptableObjects when items need art/configs.
    public static class Items
    {
        private static readonly ItemInfo[] Registry =
        {
            new() { Name = "Medkit", MaxStack = 3, Tint = new Color(0.9f, 0.3f, 0.3f) },
            new() { Name = "Ammo Pack", MaxStack = 5, Tint = new Color(0.3f, 0.8f, 0.35f) }
        };

        public static ItemInfo Get(int id) =>
            Registry[Mathf.Clamp(id, 0, Registry.Length - 1)];
    }
}
