using UnityEngine;

namespace ClutchFPS.Player
{
    /// Resolution/window-mode presets, persisted and re-applied on startup.
    public static class DisplaySettings
    {
        public static readonly Vector2Int[] Resolutions =
        {
            new(5120, 1440), new(3440, 1440), new(2560, 1440), new(1920, 1080)
        };

        public static bool IsFullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        public static void Apply(int width, int height, bool fullscreen)
        {
            Screen.SetResolution(width, height,
                fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            PlayerPrefs.SetInt("res_w", width);
            PlayerPrefs.SetInt("res_h", height);
            PlayerPrefs.SetInt("res_fullscreen", fullscreen ? 1 : 0);
        }

        public static void ApplySavedIfAny()
        {
            if (!PlayerPrefs.HasKey("res_w")) return;
            Apply(PlayerPrefs.GetInt("res_w"), PlayerPrefs.GetInt("res_h"),
                PlayerPrefs.GetInt("res_fullscreen", 1) == 1);
        }

        /// IMGUI block shared by the connect menu and the in-game settings panel.
        public static void DrawControls()
        {
            GUILayout.Label("Display:");
            GUILayout.BeginHorizontal();
            bool fullscreen = IsFullscreen;
            if (GUILayout.Toggle(fullscreen, "Fullscreen", GUI.skin.button) && !fullscreen)
                Apply(Screen.width, Screen.height, true);
            if (GUILayout.Toggle(!fullscreen, "Windowed", GUI.skin.button) && fullscreen)
                Apply(Screen.width, Screen.height, false);
            GUILayout.EndHorizontal();

            for (int i = 0; i < Resolutions.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, Resolutions.Length); j++)
                {
                    var res = Resolutions[j];
                    if (GUILayout.Button($"{res.x}x{res.y}"))
                    {
                        Apply(res.x, res.y, IsFullscreen);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
