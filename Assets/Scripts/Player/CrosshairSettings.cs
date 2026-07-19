using UnityEngine;

namespace ClutchFPS.Player
{
    public enum CrosshairStyle
    {
        Cross,
        Dot,
        Circle
    }

    /// Player-tunable crosshair, persisted via PlayerPrefs.
    public static class CrosshairSettings
    {
        public static readonly Color[] Colors =
        {
            Color.white, Color.green, Color.cyan, Color.red, Color.yellow, Color.magenta
        };
        public static readonly string[] ColorNames =
        {
            "White", "Green", "Cyan", "Red", "Yellow", "Magenta"
        };

        public static CrosshairStyle Style
        {
            get => (CrosshairStyle)PlayerPrefs.GetInt("crosshair_style", 0);
            set => PlayerPrefs.SetInt("crosshair_style", (int)value);
        }

        public static float Size
        {
            get => PlayerPrefs.GetFloat("crosshair_size", 12f);
            set => PlayerPrefs.SetFloat("crosshair_size", Mathf.Clamp(value, 4f, 40f));
        }

        public static int ColorIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt("crosshair_color", 0), 0, Colors.Length - 1);
            set => PlayerPrefs.SetInt("crosshair_color", Mathf.Clamp(value, 0, Colors.Length - 1));
        }

        public static Color Color => Colors[ColorIndex];
    }
}
