using ClutchFPS.Core;
using ClutchFPS.Weapons;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClutchFPS.Player
{
    /// Minimal OnGUI-based HUD: crosshair (F1 to customize), health, ammo, fire mode.
    /// Same throwaway style as NetworkBootstrap — replace with real UI later.
    public class PlayerHUD : NetworkBehaviour
    {
        [SerializeField] private PlayerWeaponController weaponController;
        [SerializeField] private Health health;

        private bool _settingsOpen;
        private static Texture2D _pixel;

        private void Update()
        {
            if (!IsOwner) return;
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                _settingsOpen = !_settingsOpen;
                Cursor.lockState = _settingsOpen ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = _settingsOpen;
            }
        }

        private void OnGUI()
        {
            if (!IsOwner) return;

            DrawCrosshair();
            DrawStatus();
            if (_settingsOpen) DrawSettings();
        }

        private static Texture2D Pixel
        {
            get
            {
                if (_pixel == null)
                {
                    _pixel = new Texture2D(1, 1);
                    _pixel.SetPixel(0, 0, Color.white);
                    _pixel.Apply();
                }
                return _pixel;
            }
        }

        private void DrawCrosshair()
        {
            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;
            float size = CrosshairSettings.Size;
            Color previous = GUI.color;
            GUI.color = CrosshairSettings.Color;

            switch (CrosshairSettings.Style)
            {
                case CrosshairStyle.Dot:
                    GUI.DrawTexture(new Rect(cx - 2, cy - 2, 4, 4), Pixel);
                    break;
                case CrosshairStyle.Circle:
                    // Approximate a circle with 4 short arcs of pixels.
                    int steps = 24;
                    for (int i = 0; i < steps; i++)
                    {
                        float angle = i * Mathf.PI * 2f / steps;
                        GUI.DrawTexture(new Rect(
                            cx + Mathf.Cos(angle) * size - 1,
                            cy + Mathf.Sin(angle) * size - 1, 2, 2), Pixel);
                    }
                    break;
                default: // Cross
                    float gap = 4f;
                    GUI.DrawTexture(new Rect(cx - size, cy - 1, size - gap, 2), Pixel);
                    GUI.DrawTexture(new Rect(cx + gap, cy - 1, size - gap, 2), Pixel);
                    GUI.DrawTexture(new Rect(cx - 1, cy - size, 2, size - gap), Pixel);
                    GUI.DrawTexture(new Rect(cx - 1, cy + gap, 2, size - gap), Pixel);
                    break;
            }
            GUI.color = previous;
        }

        private void DrawStatus()
        {
            if (health != null)
            {
                GUI.Box(new Rect(20, Screen.height - 50, 160, 30),
                    $"HP  {Mathf.CeilToInt(health.CurrentHealth)} / {Mathf.CeilToInt(health.MaxHealth)}");
            }

            var weapon = weaponController != null ? weaponController.ActiveWeapon : null;
            if (weapon != null)
            {
                string ammoText = weapon.IsReloading
                    ? $"{weapon.Data.weaponName} [{weapon.CurrentFireMode}]  Reloading..."
                    : $"{weapon.Data.weaponName} [{weapon.CurrentFireMode}]  {weapon.CurrentAmmo} / {weapon.Data.magazineSize}";
                GUI.Box(new Rect(Screen.width - 240, Screen.height - 50, 220, 30), ammoText);
            }
        }

        private void DrawSettings()
        {
            Rect panel = new(Screen.width / 2f - 150, Screen.height / 2f - 110, 300, 220);
            GUI.Box(panel, "Crosshair Settings  (F1 to close)");

            GUILayout.BeginArea(new Rect(panel.x + 15, panel.y + 30, panel.width - 30, panel.height - 45));

            GUILayout.Label($"Style: {CrosshairSettings.Style}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Cross")) CrosshairSettings.Style = CrosshairStyle.Cross;
            if (GUILayout.Button("Dot")) CrosshairSettings.Style = CrosshairStyle.Dot;
            if (GUILayout.Button("Circle")) CrosshairSettings.Style = CrosshairStyle.Circle;
            GUILayout.EndHorizontal();

            GUILayout.Label($"Size: {CrosshairSettings.Size:0}");
            CrosshairSettings.Size = GUILayout.HorizontalSlider(CrosshairSettings.Size, 4f, 40f);

            GUILayout.Label($"Color: {CrosshairSettings.ColorNames[CrosshairSettings.ColorIndex]}");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < CrosshairSettings.Colors.Length; i++)
            {
                var prev = GUI.backgroundColor;
                GUI.backgroundColor = CrosshairSettings.Colors[i];
                if (GUILayout.Button(" ")) CrosshairSettings.ColorIndex = i;
                GUI.backgroundColor = prev;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }
    }
}
