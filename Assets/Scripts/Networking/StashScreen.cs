using ClutchFPS.Core;
using ClutchFPS.Weapons;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Out-of-raid base screen: a STASH tab (what you've banked) and a MARKET
    /// tab (buy gear / sell loot for credits). Read/written straight through
    /// StashService, so it's host/local-authoritative. Drawn by NetworkBootstrap.
    public static class StashScreen
    {
        public static bool Open;
        private static int _tab;       // 0 = stash, 1 = market
        private static bool _confirmReset;

        private static WeaponDatabase _database;
        private static WeaponDatabase Database =>
            _database != null ? _database : _database = Resources.Load<WeaponDatabase>("WeaponDatabase");

        public static void Draw(string playerName)
        {
            // Make sure a new operator has a starter kit to look at.
            StashService.EnsureStarter(playerName);
            var stash = StashService.Get(playerName);

            const float width = 640f, height = 500f;
            Rect panel = new((Screen.width - width) / 2f, (Screen.height - height) / 2f + 30f, width, height);
            UITheme.Panel3D(panel);

            float x = panel.x + 24f, w = panel.width - 48f;

            // Title + credits.
            GUI.Label(new Rect(x, panel.y + 14f, 200f, 24f), playerName.ToUpper(),
                UITheme.Style(16, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextBright));
            GUI.Label(new Rect(panel.xMax - 224f, panel.y + 14f, 200f, 24f),
                $"{stash.credits:N0} CR",
                UITheme.Style(18, FontStyle.Bold, TextAnchor.MiddleRight, UITheme.Accent));

            // Tabs.
            _tab = UITheme.Segmented(new Rect(x, panel.y + 44f, 240f, 26f), new[] { "STASH", "MARKET" }, _tab);

            float top = panel.y + 84f;
            if (_tab == 0) DrawStashTab(panel, playerName, stash, x, w, top);
            else DrawMarketTab(panel, playerName, stash, x, w, top);

            // Footer: Fresh Start (left) and Close (right).
            float footY = panel.yMax - 46f;
            if (_confirmReset)
            {
                GUI.Label(new Rect(x, footY - 20f, w, 18f),
                    "Reset wipes your stash back to the starter kit.",
                    UITheme.Style(11, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.Danger));
                if (UITheme.Button(new Rect(x, footY, 150f, 32f), "Confirm reset"))
                {
                    StashService.ResetToStarter(playerName);
                    _confirmReset = false;
                }
                if (UITheme.Button(new Rect(x + 158f, footY, 100f, 32f), "Cancel")) _confirmReset = false;
            }
            else if (UITheme.Button(new Rect(x, footY, 150f, 32f), "Fresh Start"))
            {
                _confirmReset = true;
            }
            if (UITheme.Button(new Rect(panel.xMax - 24f - 120f, footY, 120f, 32f), "Close"))
            {
                Open = false;
                _confirmReset = false;
            }
        }

        // ---------- stash tab ----------

        /// Two columns: the safe stash (Equip to move gear right) and the raid
        /// loadout (Unequip to move it back). Only the loadout goes into a raid.
        private static void DrawStashTab(Rect panel, string playerName, StashService.StashEntry stash,
            float x, float w, float y)
        {
            float colW = (w - 20f) / 2f;
            float lx = x, rx = x + colW + 20f;
            float bottom = panel.yMax - 66f;

            UITheme.Header(new Rect(lx, y, colW, 18f), "Stash — safe");
            UITheme.Header(new Rect(rx, y, colW, 18f), "Loadout — going in");
            float ly = y + 26f, ry = y + 26f;

            // Weapons: owned-but-not-equipped on the left, equipped on the right.
            for (int slot = 0; slot < 3; slot++)
            {
                if (((stash.ownedSlots >> slot) & 1) != 1) continue;
                int variant = stash.weaponVariants != null && slot < stash.weaponVariants.Length
                    ? stash.weaponVariants[slot] : -1;
                WeaponData data = variant >= 0 && Database != null ? Database.Get(variant) : null;
                string label = (data != null ? data.weaponName : $"Slot {slot + 1} weapon").ToUpper();
                var tint = data != null ? RarityColors.Get(data.rarity) : Color.white;
                var icon = IconLibrary.Weapon(variant >= 0 ? variant : slot);
                bool equipped = ((stash.loadoutSlots >> slot) & 1) == 1;

                if (equipped)
                {
                    // Slot 0 is locked in; others can be sent home.
                    if (GearRow(new Rect(rx, ry, colW, 42f), icon, tint, label, $"Slot {slot + 1}",
                        slot == 0 ? null : "Unequip"))
                        StashService.UnequipWeapon(playerName, slot);
                    ry += 48f;
                }
                else
                {
                    if (GearRow(new Rect(lx, ly, colW, 42f), icon, tint, label, $"Slot {slot + 1}", "Equip"))
                        StashService.EquipWeapon(playerName, slot);
                    ly += 48f;
                }
            }

            // Safe items (left) — Equip moves them into the loadout.
            if (stash.itemIds != null)
            {
                for (int i = 0; i < stash.itemIds.Length && ly < bottom; i++)
                {
                    var info = Items.Get(stash.itemIds[i]);
                    if (GearRow(new Rect(lx, ly, colW, 42f), IconLibrary.Item(stash.itemIds[i]), info.Tint,
                        info.Name, $"x{stash.itemCounts[i]}", "Equip"))
                        StashService.EquipItem(playerName, stash.itemIds[i]);
                    ly += 48f;
                }
            }
            if ((stash.itemIds == null || stash.itemIds.Length == 0) && CountEquippedWeapons(stash, true) == 0)
            {
                GUI.Label(new Rect(lx, ly, colW, 20f), "Nothing spare.",
                    UITheme.Style(12, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.TextDim));
            }

            // Loadout items (right) — Unequip sends them back to the safe stash.
            if (stash.loadoutItemIds != null)
            {
                for (int i = 0; i < stash.loadoutItemIds.Length && ry < bottom; i++)
                {
                    var info = Items.Get(stash.loadoutItemIds[i]);
                    if (GearRow(new Rect(rx, ry, colW, 42f), IconLibrary.Item(stash.loadoutItemIds[i]), info.Tint,
                        info.Name, $"x{stash.loadoutItemCounts[i]}", "Unequip"))
                        StashService.UnequipItem(playerName, stash.loadoutItemIds[i]);
                    ry += 48f;
                }
            }
        }

        private static int CountEquippedWeapons(StashService.StashEntry stash, bool unequipped)
        {
            int n = 0;
            for (int slot = 0; slot < 3; slot++)
            {
                if (((stash.ownedSlots >> slot) & 1) != 1) continue;
                bool equipped = ((stash.loadoutSlots >> slot) & 1) == 1;
                if (equipped != unequipped) n++;
            }
            return n;
        }

        /// A gear row with an icon, label, sub-line and a right-side action
        /// button. A null button text renders a dim "LOCKED" tag instead.
        private static bool GearRow(Rect row, Sprite icon, Color tint, string label, string sub, string button)
        {
            UITheme.Fill(row, new Color(0.10f, 0.11f, 0.12f, 0.9f));
            UITheme.Fill(new Rect(row.x, row.y, 3f, row.height), tint);
            IconLibrary.Draw(new Rect(row.x + 8f, row.y + 7f, 28f, 28f), icon, tint);
            GUI.Label(new Rect(row.x + 42f, row.y + 4f, row.width - 110f, 20f), label,
                UITheme.Style(13, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextBright));
            if (!string.IsNullOrEmpty(sub))
            {
                GUI.Label(new Rect(row.x + 42f, row.y + 22f, row.width - 110f, 16f), sub,
                    UITheme.Style(11, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.TextDim));
            }
            if (button == null)
            {
                GUI.Label(new Rect(row.xMax - 66f, row.y, 58f, row.height), "LOCKED",
                    UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.TextDim));
                return false;
            }
            return UITheme.Button(new Rect(row.xMax - 66f, row.y + 9f, 58f, 26f), button);
        }

        // ---------- market tab ----------

        private static void DrawMarketTab(Rect panel, string playerName, StashService.StashEntry stash,
            float x, float w, float y)
        {
            float colW = (w - 20f) / 2f;
            float bx = x, sx = x + colW + 20f;

            // Buy column.
            UITheme.Header(new Rect(bx, y, colW, 18f), "Buy");
            float by = y + 26f;
            foreach (var offer in Market.BuyOffers)
            {
                Rect row = new(bx, by, colW, 44f);
                var tint = offer.IsWeapon ? RarityColors.Get(offer.Rarity) : Items.Get(offer.ItemId).Tint;
                UITheme.Fill(row, new Color(0.10f, 0.11f, 0.12f, 0.9f));
                UITheme.Fill(new Rect(row.x, row.y, 3f, row.height), tint);
                Rect icon = new(row.x + 10f, row.y + 7f, 30f, 30f);
                if (offer.IsWeapon)
                    IconLibrary.Draw(icon, IconLibrary.Weapon(offer.Variant >= 0 ? offer.Variant : offer.Slot), tint);
                else
                    IconLibrary.Draw(icon, IconLibrary.Item(offer.ItemId), tint);
                GUI.Label(new Rect(row.x + 48f, row.y + 4f, colW - 56f, 20f), offer.Name,
                    UITheme.Style(13, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextBright));
                GUI.Label(new Rect(row.x + 48f, row.y + 22f, colW - 120f, 18f), $"{offer.Price:N0} CR",
                    UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.Accent));

                bool canAfford = stash.credits >= offer.Price;
                Rect buyBtn = new(row.xMax - 66f, row.y + 9f, 58f, 26f);
                if (canAfford)
                {
                    if (UITheme.Button(buyBtn, "Buy")) Market.Buy(playerName, offer);
                }
                else
                {
                    UITheme.Fill(buyBtn, new Color(0.08f, 0.09f, 0.10f, 0.9f));
                    GUI.Label(buyBtn, "Buy", UITheme.Style(12, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.TextDim));
                }
                by += 50f;
            }

            // Sell column.
            UITheme.Header(new Rect(sx, y, colW, 18f), "Sell loot");
            float sy = y + 26f;
            bool anything = false;
            if (stash.itemIds != null)
            {
                for (int i = 0; i < stash.itemIds.Length; i++)
                {
                    anything = true;
                    int id = stash.itemIds[i], count = stash.itemCounts[i];
                    var info = Items.Get(id);
                    int value = count * Market.SellUnitPrice(id);
                    Rect row = new(sx, sy, colW, 44f);
                    UITheme.Fill(row, new Color(0.10f, 0.11f, 0.12f, 0.9f));
                    UITheme.Fill(new Rect(row.x, row.y, 3f, row.height), info.Tint);
                    IconLibrary.Draw(new Rect(row.x + 10f, row.y + 7f, 30f, 30f), IconLibrary.Item(id), info.Tint);
                    GUI.Label(new Rect(row.x + 48f, row.y + 4f, colW - 56f, 20f), $"{info.Name}  x{count}",
                        UITheme.Style(13, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextBright));
                    GUI.Label(new Rect(row.x + 48f, row.y + 22f, colW - 120f, 18f), $"+{value:N0} CR",
                        UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.Success));
                    if (UITheme.Button(new Rect(row.xMax - 66f, row.y + 9f, 58f, 26f), "Sell"))
                    {
                        Market.SellStack(playerName, id);
                    }
                    sy += 50f;
                    if (sy > panel.yMax - 70f) break;
                }
            }
            if (!anything)
            {
                GUI.Label(new Rect(sx, sy, colW, 40f), "Nothing to sell.\nExtract with loot, then trade it here.",
                    UITheme.Style(12, FontStyle.Normal, TextAnchor.UpperLeft, UITheme.TextDim));
            }
        }
    }
}
