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
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private PlayerRespawn respawn;

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

            if (respawn != null && respawn.IsDead)
            {
                DrawDeathScreen();
                return;
            }

            DrawCrosshair();
            DrawHitmarker();
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

        private void DrawDeathScreen()
        {
            EnsureStyles();
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Pixel);
            GUI.color = previous;

            _promptStyle.fontSize = 42;
            _promptStyle.normal.textColor = new Color(0.95f, 0.25f, 0.2f);
            GUI.Label(new Rect(0, Screen.height / 2f - 60, Screen.width, 50), "YOU DIED", _promptStyle);
            _promptStyle.fontSize = 18;
            _promptStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(0, Screen.height / 2f, Screen.width, 30), "Respawning...", _promptStyle);
        }

        private void DrawHitmarker()
        {
            const float showDuration = 0.15f;
            if (Time.time - HitFeedback.LastHitTime > showDuration) return;

            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;
            var matrix = GUI.matrix;
            var previous = GUI.color;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
            GUI.color = new Color(1f, 0.25f, 0.2f);
            const float len = 9f;
            const float gap = 4f;
            GUI.DrawTexture(new Rect(cx - len - gap, cy - 1, len, 2), Pixel);
            GUI.DrawTexture(new Rect(cx + gap, cy - 1, len, 2), Pixel);
            GUI.DrawTexture(new Rect(cx - 1, cy - len - gap, 2, len), Pixel);
            GUI.DrawTexture(new Rect(cx - 1, cy + gap, 2, len), Pixel);
            GUI.color = previous;
            GUI.matrix = matrix;
        }

        private static GUIStyle _nameStyle;
        private static GUIStyle _ammoStyle;
        private static GUIStyle _smallStyle;
        private static GUIStyle _promptStyle;

        private static void EnsureStyles()
        {
            if (_nameStyle != null) return;
            _nameStyle = new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _ammoStyle = new GUIStyle { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _smallStyle = new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleRight };
            _promptStyle = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        private void DrawStatus()
        {
            EnsureStyles();

            if (health != null)
            {
                GUI.Box(new Rect(20, Screen.height - 50, 160, 30),
                    $"HP  {Mathf.CeilToInt(health.CurrentHealth)} / {Mathf.CeilToInt(health.MaxHealth)}");
            }

            // BF-style loadout panel, bottom-right: weapon name, big ammo count,
            // fire mode, and the slot strip showing what you're carrying.
            var weapon = weaponController != null ? weaponController.ActiveWeapon : null;
            if (weapon != null)
            {
                float right = Screen.width - 24;
                float y = Screen.height - 108;

                _nameStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(right - 280, y, 280, 26), weapon.Data.weaponName.ToUpper(), _nameStyle);

                _ammoStyle.normal.textColor = weapon.CurrentAmmo == 0 ? new Color(1f, 0.35f, 0.3f) : Color.white;
                string ammoText = weapon.IsReloading ? "RELOADING" : $"{weapon.CurrentAmmo}  |  {weapon.Data.magazineSize}";
                GUI.Label(new Rect(right - 280, y + 26, 280, 32), ammoText, _ammoStyle);

                _smallStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
                string modeText = weapon.CurrentFireMode.ToString().ToUpper();
                if (Time.time - HitFeedback.MagFullTime < 1f) modeText = "MAG FULL";
                GUI.Label(new Rect(right - 280, y + 58, 280, 18), modeText, _smallStyle);

                // Slot strip: dim unowned, highlight active.
                string strip = "";
                for (int i = 0; i < weaponController.SlotCount; i++)
                {
                    var slotWeapon = weaponController.WeaponAt(i);
                    if (slotWeapon == null) continue;
                    string label = $"{i + 1} {slotWeapon.Data.weaponName.ToUpper()}";
                    if (!weaponController.OwnsSlot(i)) label = $"({label})";
                    else if (i == weaponController.ActiveIndex) label = $"[{label}]";
                    strip += (strip.Length > 0 ? "    " : "") + label;
                }
                _smallStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
                GUI.Label(new Rect(right - 380, y + 78, 380, 18), strip, _smallStyle);
            }

            // Pickup prompt, lower-center, when standing at an interactable.
            var nearby = interactor != null ? interactor.Nearby : null;
            if (nearby != null)
            {
                _promptStyle.normal.textColor = Color.white;
                GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height * 0.62f, 400, 30),
                    $"[E]  Pick up {nearby.DisplayName}", _promptStyle);
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
