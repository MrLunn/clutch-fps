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
        [SerializeField] private PracticeMode practice;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private RaidController raid;

        private bool _settingsOpen;
        private bool _tuningOpen;
        private bool _inventoryOpen;
        private int _tuningSlot;
        private static Texture2D _pixel;

        /// True while the local player has any menu open — gameplay input
        /// (look, fire, interact) checks this and stands down.
        public static bool LocalMenuOpen { get; private set; }

        private bool _wasExtracted;

        private void Update()
        {
            if (!IsOwner) return;

            // Extraction ends the raid for this player: free the cursor and
            // stop gameplay input (LocalMenuOpen gates look/fire/interact).
            if (raid != null && raid.HasExtracted != _wasExtracted)
            {
                _wasExtracted = raid.HasExtracted;
                ApplyCursorState();
            }
            if (raid != null && raid.HasExtracted) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f1Key.wasPressedThisFrame)
            {
                _settingsOpen = !_settingsOpen;
                _tuningOpen = false;
                _inventoryOpen = false;
                ApplyCursorState();
            }
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                _inventoryOpen = !_inventoryOpen;
                _settingsOpen = false;
                _tuningOpen = false;
                ApplyCursorState();
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Tuning is a dev tool only: stripped from release builds so
            // players can't touch weapon balance.
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                _tuningOpen = !_tuningOpen;
                _settingsOpen = false;
                ApplyCursorState();
            }
#endif
        }

        private void ApplyCursorState()
        {
            bool anyMenu = _settingsOpen || _tuningOpen || _inventoryOpen
                || (raid != null && raid.HasExtracted);
            LocalMenuOpen = anyMenu;
            Cursor.lockState = anyMenu ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = anyMenu;
        }

        private void OnGUI()
        {
            if (!IsOwner) return;

            if (respawn != null && respawn.IsDead)
            {
                DrawDeathScreen();
                return;
            }

            // Crosshair hides while aiming down sights; hitmarkers stay.
            if (weaponController == null || !weaponController.IsAiming) DrawCrosshair();
            DrawHitmarker();
            DrawStatus();
            DrawKillFeed();
            DrawPractice();
            DrawExtraction();
            if (Keyboard.current != null && Keyboard.current.vKey.isPressed) DrawScoreboard();
            if (_settingsOpen) DrawSettings();
            if (_tuningOpen) DrawWeaponTuning();
            if (_inventoryOpen) DrawInventory();
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
                    // The gap breathes with bloom: tight when accurate,
                    // spread wide during sustained fire.
                    float bloom = weaponController != null && weaponController.ActiveWeapon != null
                        ? weaponController.ActiveWeapon.CurrentBloom : 0f;
                    float gap = 4f + bloom * 14f;
                    GUI.DrawTexture(new Rect(cx - size - bloom * 10f, cy - 1, size - 4f, 2), Pixel);
                    GUI.DrawTexture(new Rect(cx + gap, cy - 1, size - 4f, 2), Pixel);
                    GUI.DrawTexture(new Rect(cx - 1, cy - size - bloom * 10f, 2, size - 4f), Pixel);
                    GUI.DrawTexture(new Rect(cx - 1, cy + gap, 2, size - 4f), Pixel);
                    break;
            }
            GUI.color = previous;
        }

        private void DrawInventory()
        {
            if (inventory == null) return;
            EnsureStyles();

            Rect panel = new(Screen.width / 2f - 340, Screen.height / 2f - 160, 680, 320);
            GUI.Box(panel, "CHARACTER  (Tab to close)");

            // Left column: the character.
            GUILayout.BeginArea(new Rect(panel.x + 15, panel.y + 32, 190, panel.height - 45));
            _smallStyle.alignment = TextAnchor.MiddleLeft;
            GUILayout.Label(respawn != null ? respawn.ResolvedName : "Player",
                new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white } });
            GUILayout.Space(8);
            if (health != null)
            {
                GUILayout.Label($"Health  {Mathf.CeilToInt(health.CurrentHealth)} / {Mathf.CeilToInt(health.MaxHealth)}");
                Rect barBack = GUILayoutUtility.GetRect(170, 12);
                var prevColor = GUI.color;
                GUI.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
                GUI.DrawTexture(barBack, Pixel);
                GUI.color = new Color(0.85f, 0.25f, 0.2f);
                GUI.DrawTexture(new Rect(barBack.x, barBack.y,
                    barBack.width * (health.CurrentHealth / health.MaxHealth), barBack.height), Pixel);
                GUI.color = prevColor;
            }
            GUILayout.Space(8);
            if (respawn != null)
            {
                GUILayout.Label($"Kills  {respawn.Kills.Value}    Deaths  {respawn.Deaths.Value}");
            }
            if (practice != null)
            {
                GUILayout.Label($"Best practice run  {practice.BestScore}");
            }
            GUILayout.EndArea();

            // Middle column: loadout with reserve ammo per weapon.
            GUILayout.BeginArea(new Rect(panel.x + 220, panel.y + 32, 210, panel.height - 45));
            GUILayout.Label("— Loadout —");
            if (weaponController != null)
            {
                for (int i = 0; i < weaponController.SlotCount; i++)
                {
                    var slotWeapon = weaponController.WeaponAt(i);
                    if (slotWeapon == null) continue;
                    if (!weaponController.OwnsSlot(i))
                    {
                        GUILayout.Label($"[{i + 1}] —");
                        continue;
                    }
                    var ammoInfo = Core.Items.Get(slotWeapon.Data.ammoItemId);
                    GUILayout.BeginHorizontal();
                    Rect wIcon = GUILayoutUtility.GetRect(40, 30, GUILayout.Width(40));
                    Core.IconLibrary.Draw(wIcon, Core.IconLibrary.Weapon(slotWeapon.VariantIndex >= 0
                        ? slotWeapon.VariantIndex : i), Core.RarityColors.Get(slotWeapon.Data.rarity));
                    GUILayout.Label(
                        $" [{i + 1}] {slotWeapon.Data.weaponName}\n {slotWeapon.CurrentAmmo}/{slotWeapon.Data.magazineSize}  ·  {slotWeapon.ReserveAmmo} {ammoInfo.Name}",
                        _smallStyle, GUILayout.Height(30));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndArea();

            // Right column: item slots.
            GUILayout.BeginArea(new Rect(panel.x + 450, panel.y + 32, 215, panel.height - 45));
            GUILayout.Label("— Items —");
            if (inventory.SlotCount == 0)
            {
                GUILayout.Label("Empty. Loot the range with E.");
            }
            for (int i = 0; i < inventory.SlotCount; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, inventory.SlotCount); j++)
                {
                    var slot = inventory.GetSlot(j);
                    var info = Core.Items.Get(slot.ItemId);
                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = info.Tint;

                    Rect cell = GUILayoutUtility.GetRect(98, 44, GUILayout.Width(98), GUILayout.Height(44));
                    bool clicked = false;
                    if (info.Usable)
                    {
                        clicked = GUI.Button(cell, GUIContent.none);
                    }
                    else
                    {
                        GUI.Box(cell, GUIContent.none);
                    }

                    // Icon on the left of the cell, name/count on the right.
                    Core.IconLibrary.Draw(new Rect(cell.x + 4, cell.y + 6, 32, 32),
                        Core.IconLibrary.Item(slot.ItemId), info.Tint);
                    GUI.Label(new Rect(cell.x + 40, cell.y + 4, 56, 36), $"{info.Name}\nx{slot.Count}", _smallStyle);

                    if (clicked) inventory.UseItemServerRpc(j);
                    GUI.backgroundColor = prev;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Click usable items (medkits) to use.\nAmmo is spent by reloading.", _smallStyle);
            _smallStyle.alignment = TextAnchor.MiddleRight;
            GUILayout.EndArea();
        }

        private void DrawPractice()
        {
            if (practice == null) return;
            EnsureStyles();

            if (practice.IsActive)
            {
                int seconds = Mathf.CeilToInt(practice.TimeRemaining);
                _promptStyle.fontSize = 30;
                _promptStyle.normal.textColor = seconds <= 10 ? new Color(1f, 0.4f, 0.3f) : Color.white;
                GUI.Label(new Rect(0, 24, Screen.width, 36), $"{seconds / 60}:{seconds % 60:00}", _promptStyle);
                _promptStyle.fontSize = 20;
                _promptStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
                GUI.Label(new Rect(0, 60, Screen.width, 26), $"SCORE  {practice.Score}", _promptStyle);
                _promptStyle.fontSize = 18;
            }
            else if (Time.time - practice.RunEndedAt < 5f)
            {
                _promptStyle.fontSize = 24;
                _promptStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
                GUI.Label(new Rect(0, 30, Screen.width, 30),
                    $"RUN COMPLETE  —  {practice.LastScore} pts   (Best {practice.BestScore})", _promptStyle);
                _promptStyle.fontSize = 18;
            }
            else
            {
                _smallStyle.alignment = TextAnchor.MiddleLeft;
                _smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
                GUI.Label(new Rect(24, Screen.height - 76, 220, 18),
                    $"[P] Practice run   Best: {practice.BestScore}", _smallStyle);
                _smallStyle.alignment = TextAnchor.MiddleRight;
            }
        }

        private void DrawExtraction()
        {
            EnsureStyles();

            if (raid != null && raid.HasExtracted)
            {
                DrawRaidSummary();
                return;
            }

            if (!Environment.ExtractionZone.LocalInZone) return;
            float progress = Environment.ExtractionZone.LocalProgress;
            float w = 320, h = 26;
            float x = Screen.width / 2f - w / 2f, y = Screen.height * 0.66f;
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y, w, h), Pixel);
            GUI.color = new Color(0.35f, 0.85f, 0.4f);
            GUI.DrawTexture(new Rect(x, y, w * progress, h), Pixel);
            GUI.color = prev;
            _promptStyle.fontSize = 16;
            _promptStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(x, y, w, h), "EXTRACTING...", _promptStyle);
            _promptStyle.fontSize = 18;
        }

        /// Post-raid results screen: the haul, the stats, and the way out.
        private void DrawRaidSummary()
        {
            EnsureStyles();
            var summary = raid.Summary;

            var previous = GUI.color;
            GUI.color = new Color(0.02f, 0.06f, 0.03f, 0.88f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Pixel);
            GUI.color = previous;

            _promptStyle.fontSize = 44;
            _promptStyle.normal.textColor = new Color(0.45f, 1f, 0.55f);
            GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, 56), "EXTRACTION SUCCESSFUL", _promptStyle);
            _promptStyle.fontSize = 18;

            Rect panel = new(Screen.width / 2f - 260, Screen.height * 0.24f, 520, Screen.height * 0.5f);
            GUI.Box(panel, "RAID REPORT");
            GUILayout.BeginArea(new Rect(panel.x + 24, panel.y + 36, panel.width - 48, panel.height - 56));

            _smallStyle.alignment = TextAnchor.MiddleLeft;
            _smallStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            int minutes = Mathf.FloorToInt(summary.Duration / 60f);
            int seconds = Mathf.FloorToInt(summary.Duration % 60f);
            GUILayout.Label($"Survived:   {minutes}:{seconds:00}", _smallStyle);
            GUILayout.Label($"Kills this raid:   {summary.Kills}", _smallStyle);
            if (practice != null && practice.BestScore > 0)
            {
                GUILayout.Label($"Best practice run:   {practice.BestScore}", _smallStyle);
            }

            GUILayout.Space(14);
            _smallStyle.normal.textColor = new Color(0.45f, 1f, 0.55f);
            GUILayout.Label("SECURED IN STASH", _smallStyle);
            _smallStyle.normal.textColor = Color.white;

            if (summary.Lines != null)
            {
                foreach (var line in summary.Lines)
                {
                    GUILayout.Label($"   • {line}", _smallStyle);
                }
            }

            GUILayout.FlexibleSpace();
            _smallStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            GUILayout.Label("This gear is kept for your next raid.", _smallStyle);
            GUILayout.EndArea();
            _smallStyle.alignment = TextAnchor.MiddleRight;

            // Exit back to the connect menu.
            Rect button = new(Screen.width / 2f - 110, panel.yMax + 24, 220, 44);
            if (GUI.Button(button, "LEAVE RAID"))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            }
        }

        private void DrawKillFeed()
        {
            EnsureStyles();
            var entries = KillFeed.Recent();
            _smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
            float y = 16;
            for (int i = entries.Count - 1; i >= 0 && i > entries.Count - 6; i--)
            {
                GUI.Label(new Rect(Screen.width - 320, y, 300, 20), entries[i], _smallStyle);
                y += 20;
            }
        }

        private void DrawScoreboard()
        {
            EnsureStyles();
            _smallStyle.alignment = TextAnchor.MiddleLeft;
            var players = Object.FindObjectsByType<PlayerRespawn>(FindObjectsSortMode.None);
            System.Array.Sort(players, (a, b) => b.Kills.Value.CompareTo(a.Kills.Value));

            float height = 60 + players.Length * 24;
            Rect panel = new(Screen.width / 2f - 180, Screen.height * 0.2f, 360, height);
            GUI.Box(panel, "SCOREBOARD");

            _smallStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(panel.x + 20, panel.y + 28, 200, 20), "PLAYER", _smallStyle);
            GUI.Label(new Rect(panel.x + 220, panel.y + 28, 50, 20), "K", _smallStyle);
            GUI.Label(new Rect(panel.x + 280, panel.y + 28, 50, 20), "D", _smallStyle);

            float rowY = panel.y + 52;
            foreach (var player in players)
            {
                bool isSelf = player.IsOwner;
                _smallStyle.normal.textColor = isSelf ? new Color(0.5f, 0.85f, 1f) : Color.white;
                GUI.Label(new Rect(panel.x + 20, rowY, 200, 20),
                    $"{player.ResolvedName}{(isSelf ? " (you)" : "")}", _smallStyle);
                GUI.Label(new Rect(panel.x + 220, rowY, 50, 20), player.Kills.Value.ToString(), _smallStyle);
                GUI.Label(new Rect(panel.x + 280, rowY, 50, 20), player.Deaths.Value.ToString(), _smallStyle);
                rowY += 24;
            }
            _smallStyle.alignment = TextAnchor.MiddleRight;
        }

        private float TuningSlider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.00}", GUILayout.Width(180));
            float result = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(160));
            GUILayout.EndHorizontal();
            return result;
        }

        /// Esc menu: live per-weapon recoil/spread tuning. Edits the WeaponData
        /// ScriptableObject instances directly — in the editor the values stick
        /// (save the asset to keep them); in builds they last for the session.
        private FirstPersonMovement _movement;

        private void DrawWeaponTuning()
        {
            if (weaponController == null) return;
            if (_movement == null) _movement = weaponController.GetComponent<FirstPersonMovement>();

            Rect panel = new(Screen.width / 2f - 200, Screen.height / 2f - 190, 400, 380);
            GUI.Box(panel, "TUNING  (Esc to close)");
            GUILayout.BeginArea(new Rect(panel.x + 15, panel.y + 30, panel.width - 30, panel.height - 45));

            // Tabs: one per weapon, plus Movement.
            int movementTab = weaponController.SlotCount;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < weaponController.SlotCount; i++)
            {
                var slotWeapon = weaponController.WeaponAt(i);
                if (slotWeapon == null) continue;
                bool selected = i == _tuningSlot;
                if (GUILayout.Toggle(selected, slotWeapon.Data.weaponName, GUI.skin.button) && !selected)
                {
                    _tuningSlot = i;
                }
            }
            if (GUILayout.Toggle(_tuningSlot == movementTab, "Movement", GUI.skin.button)
                && _tuningSlot != movementTab)
            {
                _tuningSlot = movementTab;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            if (_tuningSlot == movementTab)
            {
                DrawMovementTuning();
                GUILayout.EndArea();
                return;
            }

            var weapon = weaponController.WeaponAt(_tuningSlot);
            if (weapon == null) { GUILayout.EndArea(); return; }
            var data = weapon.Data;

            GUILayout.Label("— Spread —");
            data.spreadDegrees = TuningSlider("Max spread (deg)", data.spreadDegrees, 0f, 6f);
            data.bloomPerShot = TuningSlider("Bloom per shot", data.bloomPerShot, 0f, 1f);
            data.bloomRecoverPerSecond = TuningSlider("Bloom recovery /s", data.bloomRecoverPerSecond, 0.5f, 8f);
            data.crouchSpreadMultiplier = TuningSlider("Crouch spread mult", data.crouchSpreadMultiplier, 0.1f, 1f);

            GUILayout.Space(6);
            GUILayout.Label("— Recoil —");
            data.recoilPitchKick = TuningSlider("Pitch kick (deg)", data.recoilPitchKick, 0f, 3f);
            data.recoilYawKick = TuningSlider("Yaw kick (deg)", data.recoilYawKick, 0f, 1.5f);
            data.recoilMinScale = TuningSlider("First-shot scale", data.recoilMinScale, 0f, 1f);
            data.recoilMaxScale = TuningSlider("Full-bloom scale", data.recoilMaxScale, 0.5f, 2.5f);
            data.crouchRecoilMultiplier = TuningSlider("Crouch recoil mult", data.crouchRecoilMultiplier, 0.1f, 1f);

            GUILayout.Space(6);
            GUILayout.Label("— Feel —");
            data.kickbackDistance = TuningSlider("Kickback (m)", data.kickbackDistance, 0f, 0.2f);
            data.fireRate = TuningSlider("Fire rate /s", data.fireRate, 1f, 20f);

            GUILayout.EndArea();
        }

        /// Movement tab: edits persist via PlayerPrefs (survive restarts and builds).
        private void DrawMovementTuning()
        {
            if (_movement == null) return;

            GUI.changed = false;
            GUILayout.Label("— Speed —");
            _movement.walkSpeed = TuningSlider("Walk speed", _movement.walkSpeed, 2f, 10f);
            _movement.sprintSpeed = TuningSlider("Sprint speed", _movement.sprintSpeed, 4f, 14f);
            _movement.jumpHeight = TuningSlider("Jump height", _movement.jumpHeight, 0.4f, 3f);

            GUILayout.Space(6);
            GUILayout.Label("— Weight —");
            _movement.acceleration = TuningSlider("Acceleration", _movement.acceleration, 5f, 100f);
            _movement.deceleration = TuningSlider("Deceleration", _movement.deceleration, 5f, 100f);
            _movement.airControl = TuningSlider("Air control", _movement.airControl, 0f, 1f);

            GUILayout.Space(6);
            GUILayout.Label("— Camera feel —");
            _movement.bobFrequency = TuningSlider("Bob frequency", _movement.bobFrequency, 0.5f, 4f);
            _movement.bobAmplitude = TuningSlider("Bob amplitude", _movement.bobAmplitude, 0f, 0.12f);
            _movement.landDipScale = TuningSlider("Land dip scale", _movement.landDipScale, 0f, 0.05f);
            _movement.sprintFov = TuningSlider("Sprint FOV", _movement.sprintFov, 60f, 80f);

            if (GUI.changed) _movement.SaveTuning();

            GUILayout.Space(8);
            if (GUILayout.Button("Reset movement to defaults"))
            {
                _movement.ResetTuning();
            }
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
            // Kills flash longer and larger than plain hits; headshots go gold.
            bool kill = Time.time - HitFeedback.LastKillTime < 0.35f;
            const float hitDuration = 0.15f;
            if (!kill && Time.time - HitFeedback.LastHitTime > hitDuration) return;

            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;
            var matrix = GUI.matrix;
            var previous = GUI.color;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));

            if (kill) GUI.color = new Color(1f, 0.1f, 0.1f);
            else if (HitFeedback.LastHitWasHeadshot) GUI.color = new Color(1f, 0.8f, 0.15f);
            else GUI.color = new Color(1f, 0.25f, 0.2f);

            float len = kill ? 14f : 9f;
            float gap = kill ? 6f : 4f;
            float thick = kill ? 3f : 2f;
            GUI.DrawTexture(new Rect(cx - len - gap, cy - thick / 2f, len, thick), Pixel);
            GUI.DrawTexture(new Rect(cx + gap, cy - thick / 2f, len, thick), Pixel);
            GUI.DrawTexture(new Rect(cx - thick / 2f, cy - len - gap, thick, len), Pixel);
            GUI.DrawTexture(new Rect(cx - thick / 2f, cy + gap, thick, len), Pixel);
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

            // Vitals block, bottom-left: big number plus a health bar that
            // shifts amber then red as it drains.
            if (health != null)
            {
                float fraction = health.MaxHealth > 0f ? health.CurrentHealth / health.MaxHealth : 0f;
                Color healthColor = fraction > 0.6f ? Core.UITheme.Success
                    : fraction > 0.3f ? Core.UITheme.Accent
                    : Core.UITheme.Danger;

                Rect block = new(28f, Screen.height - 86f, 220f, 58f);
                Core.UITheme.Fill(new Rect(block.x, block.y, 3f, block.height), healthColor);
                GUI.Label(new Rect(block.x + 12f, block.y - 2f, 120f, 34f),
                    Mathf.CeilToInt(health.CurrentHealth).ToString(),
                    Core.UITheme.Style(32, FontStyle.Bold, TextAnchor.MiddleLeft, Core.UITheme.TextBright));
                GUI.Label(new Rect(block.x + 12f + 56f, block.y + 8f, 80f, 20f), "HP",
                    Core.UITheme.Style(12, FontStyle.Bold, TextAnchor.MiddleLeft, Core.UITheme.TextDim));
                Core.UITheme.Bar(new Rect(block.x + 12f, block.y + 36f, 190f, 6f), fraction, healthColor);
            }

            // BF-style loadout panel, bottom-right: weapon name, big ammo count,
            // fire mode, and the slot strip showing what you're carrying.
            // Weapon block, bottom-right: rarity-tinted name, big mag count,
            // dim reserve, fire mode, and a slot strip.
            var weapon = weaponController != null ? weaponController.ActiveWeapon : null;
            if (weapon != null)
            {
                float right = Screen.width - 28f;
                float y = Screen.height - 96f;
                var rarityColor = Core.RarityColors.Get(weapon.Data.rarity);

                GUI.Label(new Rect(right - 300f, y, 300f, 20f), weapon.Data.weaponName.ToUpper(),
                    Core.UITheme.Style(15, FontStyle.Bold, TextAnchor.MiddleRight, rarityColor));

                if (weapon.IsReloading)
                {
                    GUI.Label(new Rect(right - 300f, y + 22f, 300f, 34f), "RELOADING",
                        Core.UITheme.Style(26, FontStyle.Bold, TextAnchor.MiddleRight, Core.UITheme.Accent));
                }
                else
                {
                    var magColor = weapon.CurrentAmmo == 0 ? Core.UITheme.Danger : Core.UITheme.TextBright;
                    string mag = weapon.CurrentAmmo.ToString();
                    var magStyle = Core.UITheme.Style(34, FontStyle.Bold, TextAnchor.MiddleRight, magColor);
                    string reserve = $" / {weapon.ReserveAmmo}";
                    var reserveStyle = Core.UITheme.Style(16, FontStyle.Normal, TextAnchor.MiddleRight, Core.UITheme.TextDim);
                    float reserveWidth = reserveStyle.CalcSize(new GUIContent(reserve)).x;
                    GUI.Label(new Rect(right - 300f, y + 24f, 300f - reserveWidth, 36f), mag, magStyle);
                    GUI.Label(new Rect(right - 300f, y + 32f, 300f, 24f), reserve, reserveStyle);
                }

                // Status line: fire mode, or a transient warning.
                string modeText = weapon.CurrentFireMode.ToString().ToUpper();
                Color modeColor = Core.UITheme.TextDim;
                if (Time.time - HitFeedback.MagFullTime < 1f) { modeText = "MAG FULL"; modeColor = Core.UITheme.Accent; }
                if (Time.time - HitFeedback.NoAmmoTime < 1f)
                {
                    modeText = $"NO {Core.Items.Get(weapon.Data.ammoItemId).Name.ToUpper()}";
                    modeColor = Core.UITheme.Danger;
                }
                GUI.Label(new Rect(right - 300f, y + 62f, 300f, 18f), modeText,
                    Core.UITheme.Style(12, FontStyle.Bold, TextAnchor.MiddleRight, modeColor));

                // Slot chips.
                float chipW = 62f, chipH = 20f, gap = 6f;
                int slots = weaponController.SlotCount;
                float stripX = right - (chipW + gap) * slots + gap;
                for (int i = 0; i < slots; i++)
                {
                    var slotWeapon = weaponController.WeaponAt(i);
                    if (slotWeapon == null) continue;
                    Rect chip = new(stripX + i * (chipW + gap), y + 82f, chipW, chipH);
                    bool owned = weaponController.OwnsSlot(i);
                    bool active = owned && i == weaponController.ActiveIndex;
                    Core.UITheme.Fill(chip, active
                        ? Core.UITheme.AccentDim
                        : new Color(0.09f, 0.10f, 0.11f, owned ? 0.9f : 0.5f));
                    GUI.Label(chip, $"{i + 1}",
                        Core.UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleCenter,
                            active ? Color.black : (owned ? Core.UITheme.TextBright : Core.UITheme.TextDim)));
                }
            }

            // Pickup prompt, lower-center, when standing at an interactable.
            var nearby = interactor != null ? interactor.Nearby : null;
            if (nearby != null)
            {
                _promptStyle.normal.textColor = Core.RarityColors.Get(nearby.Rarity);
                GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height * 0.62f, 400, 30),
                    $"[E]  Pick up {nearby.DisplayName}", _promptStyle);
            }
        }

        private void DrawSettings()
        {
            Rect panel = new(Screen.width / 2f - 150, Screen.height / 2f - 240, 300, 480);
            GUI.Box(panel, "Settings  (F1 to close)");

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

            GUILayout.Space(10);
            GUILayout.Label($"Mouse sensitivity: {MouseSettings.Sensitivity:0.00}");
            MouseSettings.Sensitivity = GUILayout.HorizontalSlider(MouseSettings.Sensitivity, 0.02f, 0.5f);
            MouseSettings.InvertY = GUILayout.Toggle(MouseSettings.InvertY, " Invert Y axis");

            GUILayout.Space(10);
            DisplaySettings.DrawControls();

            GUILayout.EndArea();
        }
    }
}
