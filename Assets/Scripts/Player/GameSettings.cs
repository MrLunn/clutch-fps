using UnityEngine;

namespace ClutchFPS.Player
{
    /// Player comfort settings that aren't gameplay balance: field of view and
    /// master volume. Persisted via PlayerPrefs and applied on startup.
    public static class GameSettings
    {
        public const float MinFov = 60f;
        public const float MaxFov = 110f;

        public static float Fov
        {
            get => PlayerPrefs.GetFloat("game_fov", 90f);
            set => PlayerPrefs.SetFloat("game_fov", Mathf.Clamp(value, MinFov, MaxFov));
        }

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat("game_volume", 0.9f);
            set
            {
                float v = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat("game_volume", v);
                AudioListener.volume = v;
            }
        }

        /// Re-apply saved values on startup (volume needs pushing to the engine;
        /// FOV is read live by the camera each frame).
        public static void ApplySavedIfAny()
        {
            AudioListener.volume = MasterVolume;
        }

        /// GUILayout block for the connect-menu settings panel.
        public static void DrawControls()
        {
            GUILayout.Label($"Field of view:  {Mathf.RoundToInt(Fov)}");
            Fov = GUILayout.HorizontalSlider(Fov, MinFov, MaxFov);
            GUILayout.Space(4);
            GUILayout.Label($"Master volume:  {Mathf.RoundToInt(MasterVolume * 100f)}%");
            MasterVolume = GUILayout.HorizontalSlider(MasterVolume, 0f, 1f);
        }
    }
}
