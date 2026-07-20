using UnityEngine;

namespace ClutchFPS.Core
{
    /// Sprites for items and weapons, used by the in-game inventory and the
    /// out-of-raid stash screen. Loaded from Resources so plain IMGUI code can
    /// reach it without wiring references through every prefab.
    [CreateAssetMenu(menuName = "Clutch FPS/Icon Library", fileName = "IconLibrary")]
    public class IconLibrary : ScriptableObject
    {
        [Tooltip("Indexed by ItemType (0 Medkit, 1 5.56, 2 9mm).")]
        public Sprite[] itemIcons;

        [Tooltip("Indexed by WeaponDatabase index.")]
        public Sprite[] weaponIcons;

        private static IconLibrary _instance;

        public static IconLibrary Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<IconLibrary>("IconLibrary");
                return _instance;
            }
        }

        public static Sprite Item(int itemId)
        {
            var library = Instance;
            if (library?.itemIcons == null) return null;
            return itemId >= 0 && itemId < library.itemIcons.Length ? library.itemIcons[itemId] : null;
        }

        public static Sprite Weapon(int weaponIndex)
        {
            var library = Instance;
            if (library?.weaponIcons == null) return null;
            return weaponIndex >= 0 && weaponIndex < library.weaponIcons.Length
                ? library.weaponIcons[weaponIndex] : null;
        }

        /// Draws an icon into a rect, falling back to a tinted block so the UI
        /// still reads if art is missing.
        public static void Draw(Rect rect, Sprite sprite, Color fallbackTint)
        {
            if (sprite != null && sprite.texture != null)
            {
                var tr = sprite.textureRect;
                var uv = new Rect(
                    tr.x / sprite.texture.width, tr.y / sprite.texture.height,
                    tr.width / sprite.texture.width, tr.height / sprite.texture.height);
                GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv);
                return;
            }
            var previous = GUI.color;
            GUI.color = fallbackTint;
            GUI.Box(rect, GUIContent.none);
            GUI.color = previous;
        }
    }
}
