using UnityEngine;

namespace ClutchFPS.Core
{
    public enum Rarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3
    }

    public static class RarityColors
    {
        public static Color Get(Rarity rarity) => rarity switch
        {
            Rarity.Uncommon => new Color(0.35f, 0.85f, 0.35f),
            Rarity.Rare => new Color(0.35f, 0.6f, 1f),
            Rarity.Epic => new Color(0.75f, 0.4f, 1f),
            _ => Color.white
        };
    }
}
