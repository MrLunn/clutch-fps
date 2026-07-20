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

            float width = 460, height = 420;
            Rect panel = new((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(panel, $"STASH — {playerName}");
            GUILayout.BeginArea(new Rect(panel.x + 20, panel.y + 34, panel.width - 40, panel.height - 54));

            if (stash == null)
            {
                GUILayout.Label("No stash yet. Extract from a raid to bank gear.");
                GUILayout.Space(10);
                if (GUILayout.Button("Close", GUILayout.Height(32))) Open = false;
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("WEAPONS");
            for (int slot = 0; slot < 3; slot++)
            {
                bool owned = ((stash.ownedSlots >> slot) & 1) == 1;
                if (!owned) continue;

                int variant = stash.weaponVariants != null && slot < stash.weaponVariants.Length
                    ? stash.weaponVariants[slot] : -1;
                WeaponData data = variant >= 0 && Database != null ? Database.Get(variant) : null;
                string label = data != null ? data.weaponName : $"Slot {slot + 1} weapon";
                var tint = data != null ? RarityColors.Get(data.rarity) : Color.white;

                GUILayout.BeginHorizontal();
                Rect iconRect = GUILayoutUtility.GetRect(44, 40, GUILayout.Width(44));
                IconLibrary.Draw(iconRect, IconLibrary.Weapon(variant >= 0 ? variant : slot), tint);
                var previous = GUI.color;
                GUI.color = tint;
                GUILayout.Label($"  [{slot + 1}]  {label}", GUILayout.Height(40));
                GUI.color = previous;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(12);
            GUILayout.Label("ITEMS");
            if (stash.itemIds == null || stash.itemIds.Length == 0)
            {
                GUILayout.Label("   (empty)");
            }
            else
            {
                for (int i = 0; i < stash.itemIds.Length; i++)
                {
                    var info = Items.Get(stash.itemIds[i]);
                    GUILayout.BeginHorizontal();
                    Rect iconRect = GUILayoutUtility.GetRect(36, 32, GUILayout.Width(36));
                    IconLibrary.Draw(iconRect, IconLibrary.Item(stash.itemIds[i]), info.Tint);
                    GUILayout.Label($"  {info.Name}  x{stash.itemCounts[i]}", GUILayout.Height(32));
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Height(32))) Open = false;
            GUILayout.EndArea();
        }
    }
}
