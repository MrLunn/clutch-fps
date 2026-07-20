using ClutchFPS.Core;
using ClutchFPS.Weapons;
using UnityEngine;

namespace ClutchFPS.Networking
{
    /// Out-of-raid stash view: shows what the local player has banked, read
    /// straight from the saved stash file. Drawn by NetworkBootstrap.
    public static class StashScreen
    {
        public static bool Open;

        private static WeaponDatabase _database;

        private static WeaponDatabase Database
        {
            get
            {
                if (_database == null) _database = Resources.Load<WeaponDatabase>("WeaponDatabase");
                return _database;
            }
        }

        public static void Draw(string playerName)
        {
            var stash = StashService.Get(playerName);

            const float width = 520f, height = 460f;
            Rect panel = new((Screen.width - width) / 2f, (Screen.height - height) / 2f + 40f, width, height);
            UITheme.Panel3D(panel);

            GUI.Label(new Rect(panel.x + 24f, panel.y + 16f, panel.width - 48f, 24f), "STASH",
                UITheme.Style(18, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextBright));
            GUI.Label(new Rect(panel.x + 24f, panel.y + 16f, panel.width - 48f, 24f), playerName.ToUpper(),
                UITheme.Style(12, FontStyle.Bold, TextAnchor.MiddleRight, UITheme.Accent));

            float x = panel.x + 24f;
            float w = panel.width - 48f;
            float y = panel.y + 52f;

            if (stash == null)
            {
                GUI.Label(new Rect(x, y + 20f, w, 40f),
                    "Nothing banked yet.\nExtract from a raid to secure gear here.",
                    UITheme.Style(13, FontStyle.Normal, TextAnchor.UpperLeft, UITheme.TextDim));
                if (UITheme.Button(new Rect(x, panel.yMax - 54f, w, 34f), "Close")) Open = false;
                return;
            }

            UITheme.Header(new Rect(x, y, w, 18f), "Weapons");
            y += 26f;
            bool anyWeapon = false;
            for (int slot = 0; slot < 3; slot++)
            {
                if (((stash.ownedSlots >> slot) & 1) != 1) continue;
                anyWeapon = true;

                int variant = stash.weaponVariants != null && slot < stash.weaponVariants.Length
                    ? stash.weaponVariants[slot] : -1;
                WeaponData data = variant >= 0 && Database != null ? Database.Get(variant) : null;
                string label = data != null ? data.weaponName : $"Slot {slot + 1} weapon";
                var tint = data != null ? RarityColors.Get(data.rarity) : Color.white;

                Rect row = new(x, y, w, 46f);
                UITheme.Fill(row, new Color(0.10f, 0.11f, 0.12f, 0.9f));
                UITheme.Fill(new Rect(row.x, row.y, 3f, row.height), tint);
                IconLibrary.Draw(new Rect(row.x + 12f, row.y + 7f, 32f, 32f),
                    IconLibrary.Weapon(variant >= 0 ? variant : slot), tint);
                GUI.Label(new Rect(row.x + 54f, row.y, row.width - 70f, row.height), label.ToUpper(),
                    UITheme.Style(14, FontStyle.Bold, TextAnchor.MiddleLeft, tint));
                GUI.Label(new Rect(row.x, row.y, row.width - 14f, row.height), $"SLOT {slot + 1}",
                    UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleRight, UITheme.TextDim));
                y += 52f;
            }
            if (!anyWeapon)
            {
                GUI.Label(new Rect(x, y, w, 20f), "None",
                    UITheme.Style(12, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.TextDim));
                y += 26f;
            }

            y += 6f;
            UITheme.Header(new Rect(x, y, w, 18f), "Items");
            y += 26f;

            if (stash.itemIds == null || stash.itemIds.Length == 0)
            {
                GUI.Label(new Rect(x, y, w, 20f), "Empty",
                    UITheme.Style(12, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.TextDim));
            }
            else
            {
                // Item grid, three across.
                const float cellW = 152f, cellH = 44f, gap = 8f;
                for (int i = 0; i < stash.itemIds.Length; i++)
                {
                    int col = i % 3;
                    int rowIndex = i / 3;
                    Rect cell = new(x + col * (cellW + gap), y + rowIndex * (cellH + gap), cellW, cellH);
                    if (cell.yMax > panel.yMax - 60f) break;

                    var info = Items.Get(stash.itemIds[i]);
                    UITheme.Fill(cell, new Color(0.10f, 0.11f, 0.12f, 0.9f));
                    UITheme.Fill(new Rect(cell.x, cell.y, 3f, cell.height), info.Tint);
                    IconLibrary.Draw(new Rect(cell.x + 10f, cell.y + 8f, 28f, 28f),
                        IconLibrary.Item(stash.itemIds[i]), info.Tint);
                    GUI.Label(new Rect(cell.x + 46f, cell.y + 4f, cell.width - 54f, 20f), info.Name,
                        UITheme.Style(12, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextBright));
                    GUI.Label(new Rect(cell.x + 46f, cell.y + 22f, cell.width - 54f, 18f),
                        $"x{stash.itemCounts[i]}",
                        UITheme.Style(12, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.TextDim));
                }
            }

            if (UITheme.Button(new Rect(x, panel.yMax - 54f, w, 34f), "Close")) Open = false;
        }
    }
}
