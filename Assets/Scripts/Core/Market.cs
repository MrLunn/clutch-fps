namespace ClutchFPS.Core
{
    /// The trader: fixed-price buy offers and per-unit sell prices, operating
    /// on a player's stash through StashService. Host/local-authoritative, same
    /// as the stash itself.
    public static class Market
    {
        public struct Offer
        {
            public string Name;
            public int Price;
            public bool IsWeapon;
            public int ItemId;      // items
            public int Count;       // items
            public int Slot;        // weapons
            public int Variant;     // weapons (-1 = stock)
            public Rarity Rarity;   // weapons, for tint
        }

        public static readonly Offer[] BuyOffers =
        {
            new() { Name = "Medkit",         Price = 800,   ItemId = (int)ItemType.Medkit,  Count = 1 },
            new() { Name = "5.56 x30",       Price = 450,   ItemId = (int)ItemType.Ammo556, Count = 30 },
            new() { Name = "9mm x30",        Price = 300,   ItemId = (int)ItemType.Ammo9mm, Count = 30 },
            new() { Name = "Frag x2",        Price = 700,   ItemId = (int)ItemType.Grenade, Count = 2 },
            // Gear: worn on entry, lost if you go down.
            new() { Name = "Light Armor",    Price = 1200,  ItemId = (int)ItemType.ArmorLight,  Count = 1 },
            new() { Name = "Heavy Armor",    Price = 3200,  ItemId = (int)ItemType.ArmorHeavy,  Count = 1 },
            new() { Name = "Light Helmet",   Price = 900,   ItemId = (int)ItemType.HelmetLight, Count = 1 },
            new() { Name = "Heavy Helmet",   Price = 2600,  ItemId = (int)ItemType.HelmetHeavy, Count = 1 },
            // Pistols (slot 1)
            new() { Name = "Pistol",         Price = 2000,  IsWeapon = true, Slot = 1, Variant = -1, Rarity = Rarity.Common },
            new() { Name = "Machine Pistol", Price = 3000,  IsWeapon = true, Slot = 1, Variant = 6,  Rarity = Rarity.Uncommon },
            new() { Name = "Magnum",         Price = 8000,  IsWeapon = true, Slot = 1, Variant = 7,  Rarity = Rarity.Rare },
            // ARs (slot 0)
            new() { Name = "Carbine",        Price = 3500,  IsWeapon = true, Slot = 0, Variant = 4,  Rarity = Rarity.Uncommon },
            new() { Name = "Marksman",       Price = 9000,  IsWeapon = true, Slot = 0, Variant = 5,  Rarity = Rarity.Rare },
            new() { Name = "Rare Rifle",     Price = 12000, IsWeapon = true, Slot = 0, Variant = 3,  Rarity = Rarity.Rare },
            // SMGs (slot 2)
            new() { Name = "SMG",            Price = 5000,  IsWeapon = true, Slot = 2, Variant = -1, Rarity = Rarity.Uncommon },
            new() { Name = "PDW",            Price = 4500,  IsWeapon = true, Slot = 2, Variant = 8,  Rarity = Rarity.Uncommon },
            new() { Name = "Vector",         Price = 15000, IsWeapon = true, Slot = 2, Variant = 9,  Rarity = Rarity.Epic },
        };

        /// Per-unit sell price for a stash item.
        public static int SellUnitPrice(int itemId) => (ItemType)itemId switch
        {
            ItemType.Medkit => 300,
            ItemType.Ammo556 => 5,
            ItemType.Ammo9mm => 3,
            ItemType.Grenade => 250,
            ItemType.ArmorLight => 420,
            ItemType.ArmorHeavy => 1150,
            ItemType.HelmetLight => 320,
            ItemType.HelmetHeavy => 950,
            _ => 1
        };

        public static bool Buy(string playerName, Offer offer)
        {
            if (!StashService.TrySpend(playerName, offer.Price)) return false;
            if (offer.IsWeapon) StashService.AddWeapon(playerName, offer.Slot, offer.Variant);
            else StashService.AddItem(playerName, offer.ItemId, offer.Count);
            return true;
        }

        /// Sell an entire stash stack of one item for credits.
        public static int SellStack(string playerName, int itemId)
        {
            int count = StashService.ItemCount(playerName, itemId);
            if (count <= 0) return 0;
            int value = count * SellUnitPrice(itemId);
            StashService.RemoveItem(playerName, itemId, count);
            StashService.AddCredits(playerName, value);
            return value;
        }
    }
}
