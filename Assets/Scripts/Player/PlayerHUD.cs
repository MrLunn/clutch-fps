using System.Collections.Generic;
using ClutchFPS.Core;
using ClutchFPS.Environment;
using ClutchFPS.Weapons;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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

        // Radar contacts, refreshed a few times a second rather than every
        // OnGUI pass (OnGUI runs twice a frame; FindObjects is not free).
        private readonly List<(Vector3 pos, Color color)> _radarBlips = new();
        private readonly List<Vector3> _extractPositions = new();
        private float _nextRadarScan;
        private Camera _viewCamera;

        private void Update()
        {
            if (!IsOwner) return;

            if (Time.time >= _nextRadarScan)
            {
                _nextRadarScan = Time.time + 0.25f;
                ScanRadar();
            }

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

            // Aiming swaps the full crosshair for a tight reticle rather than
            // removing it — the view models carry no sights of their own, so
            // hiding it outright left you aiming at nothing.
            if (weaponController != null && weaponController.IsAiming) DrawAimReticle();
            else DrawCrosshair();
            DrawHitmarker();
            DrawStatus();
            DrawRadar();
            DrawKillFeed();
            DrawPractice();
            DrawRaidTimer();
            DrawExtractMarkers();
            DrawDamageIndicators();
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

        private void ScanRadar()
        {
            _radarBlips.Clear();

            foreach (var enemy in FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            {
                if (enemy.TryGetComponent<Health>(out var enemyHealth) && enemyHealth.CurrentHealth <= 0f) continue;
                _radarBlips.Add((enemy.transform.position, Core.UITheme.Danger));
            }

            foreach (var other in FindObjectsByType<PlayerRespawn>(FindObjectsSortMode.None))
            {
                if (other == respawn || other.IsDead) continue;
                _radarBlips.Add((other.transform.position, Core.UITheme.Success));
            }

            _extractPositions.Clear();
            foreach (var zone in FindObjectsByType<Environment.ExtractionZone>(FindObjectsSortMode.None))
            {
                _extractPositions.Add(zone.transform.position);
            }
        }

        // Baked once: a translucent disk with an accent rim, a mid ring and a
        // faint cross, so the per-frame draw is one blit plus the blips.
        private static Texture2D _radarDisk;
        private static Texture2D RadarDisk
        {
            get
            {
                if (_radarDisk != null) return _radarDisk;

                const int d = 168;
                float r = d / 2f;
                var tex = new Texture2D(d, d, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                var clear = new Color(0f, 0f, 0f, 0f);
                var fill = new Color(0.04f, 0.06f, 0.07f, 0.80f);
                var grid = new Color(0.35f, 0.55f, 0.62f, 0.16f);
                var accent = Core.UITheme.Accent;
                var rim = new Color(accent.r, accent.g, accent.b, 0.9f);
                var midRing = new Color(accent.r, accent.g, accent.b, 0.3f);

                for (int y = 0; y < d; y++)
                {
                    for (int x = 0; x < d; x++)
                    {
                        float dx = x - r + 0.5f, dy = y - r + 0.5f;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        Color c;
                        if (dist > r) c = clear;
                        else if (dist > r - 2.5f) c = rim;
                        else if (Mathf.Abs(dist - r * 0.5f) < 1f) c = midRing;
                        else if (Mathf.Abs(dx) < 0.9f || Mathf.Abs(dy) < 0.9f)
                            c = new Color(fill.r + grid.r * grid.a, fill.g + grid.g * grid.a, fill.b + grid.b * grid.a, fill.a);
                        else c = fill;
                        tex.SetPixel(x, y, c);
                    }
                }
                tex.Apply();
                _radarDisk = tex;
                return tex;
            }
        }

        private void DrawRadar()
        {
            const float margin = 18f;
            const float radius = 82f;
            const float rangeMetres = 48f;
            Vector2 centre = new(margin + radius, margin + radius);

            // Disk.
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), RadarDisk);

            // Sweep line, rotating for that radar feel.
            Matrix4x4 prevMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot((Time.time * 90f) % 360f, centre);
            var accent = Core.UITheme.Accent;
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.22f);
            GUI.DrawTexture(new Rect(centre.x - 1f, centre.y - radius + 3f, 2f, radius - 3f), Pixel);
            GUI.matrix = prevMatrix;

            // Contacts, rotated so the player's facing is up.
            float scale = radius / rangeMetres;
            float yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
            float sin = Mathf.Sin(yaw), cos = Mathf.Cos(yaw);
            Vector3 self = transform.position;

            foreach (var (pos, color) in _radarBlips)
            {
                float dx = pos.x - self.x, dz = pos.z - self.z;
                if (dx * dx + dz * dz > rangeMetres * rangeMetres) continue;
                float localRight = dx * cos - dz * sin;
                float localFwd = dx * sin + dz * cos;
                float px = centre.x + localRight * scale;
                float py = centre.y - localFwd * scale;
                GUI.color = color;
                GUI.DrawTexture(new Rect(px - 2.5f, py - 2.5f, 5f, 5f), Pixel);
            }

            // Player marker: a small arrow pointing up (forward).
            GUI.color = Core.UITheme.TextBright;
            GUI.DrawTexture(new Rect(centre.x - 1f, centre.y - 6f, 2f, 10f), Pixel);
            GUI.DrawTexture(new Rect(centre.x - 3f, centre.y - 1f, 6f, 2f), Pixel);
            GUI.color = prev;

            // Location label beneath the disk.
            string zone = MapZones.ZoneAt(self) ?? PrettyScene();
            GUI.Label(new Rect(centre.x - radius, centre.y + radius + 5f, radius * 2f, 20f), zone,
                Core.UITheme.Style(14, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.TextBright));
        }

        /// Scene name as a display label: "ShootingRange" -> "SHOOTING RANGE",
        /// "Raid_Complex" -> "RAID COMPLEX". Only used when no zones are
        /// registered (e.g. the range), otherwise the zone name wins.
        private static string PrettyScene()
        {
            string name = SceneManager.GetActiveScene().name;
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if (ch == '_') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(ch) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(ch);
            }
            return sb.ToString().ToUpperInvariant();
        }

        /// Aimed reticle: a centre dot inside a thin ring, with four short
        /// ticks. Deliberately smaller and quieter than the hipfire crosshair
        /// so ADS still reads as the precise state.
        private void DrawAimReticle()
        {
            float cx = Screen.width / 2f;
            float cy = Screen.height / 2f;
            Color previous = GUI.color;
            Color colour = CrosshairSettings.Color;

            GUI.color = colour;
            GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), Pixel);

            // Ring, dimmed so the dot stays the thing your eye lands on.
            GUI.color = new Color(colour.r, colour.g, colour.b, colour.a * 0.55f);
            const float radius = 9f;
            const int steps = 28;
            for (int i = 0; i < steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                GUI.DrawTexture(new Rect(
                    cx + Mathf.Cos(angle) * radius - 0.5f,
                    cy + Mathf.Sin(angle) * radius - 0.5f, 1f, 1f), Pixel);
            }

            // Ticks at the compass points, outside the ring.
            GUI.DrawTexture(new Rect(cx - 0.5f, cy - radius - 6f, 1f, 4f), Pixel);
            GUI.DrawTexture(new Rect(cx - 0.5f, cy + radius + 2f, 1f, 4f), Pixel);
            GUI.DrawTexture(new Rect(cx - radius - 6f, cy - 0.5f, 4f, 1f), Pixel);
            GUI.DrawTexture(new Rect(cx + radius + 2f, cy - 0.5f, 4f, 1f), Pixel);

            GUI.color = previous;
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

            Core.UITheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
            const float pw = 800f, ph = 470f;
            Rect panel = new((Screen.width - pw) / 2f, (Screen.height - ph) / 2f, pw, ph);
            Core.UITheme.Panel3D(panel);

            GUI.Label(new Rect(panel.x + 24f, panel.y + 16f, 400f, 24f), "INVENTORY",
                Core.UITheme.Style(18, FontStyle.Bold, TextAnchor.MiddleLeft, Core.UITheme.TextBright));
            GUI.Label(new Rect(panel.xMax - 84f, panel.y + 16f, 60f, 24f), "TAB",
                Core.UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleRight, Core.UITheme.TextDim));
            Core.UITheme.Fill(new Rect(panel.x + 24f, panel.y + 46f, pw - 48f, 1f), Core.UITheme.Line);

            float top = panel.y + 60f;
            float lx = panel.x + 24f, lw = 210f;
            float mx = lx + lw + 22f, mw = 250f;
            float rx = mx + mw + 22f, rw = panel.xMax - 24f - rx;

            // ---- Left: operator card ----
            Core.UITheme.Header(new Rect(lx, top, lw, 18f), "Operator");
            float ly = top + 28f;
            GUI.Label(new Rect(lx, ly, lw, 26f), respawn != null ? respawn.ResolvedName : "Player",
                Core.UITheme.Style(20, FontStyle.Bold, TextAnchor.MiddleLeft, Core.UITheme.TextBright));
            ly += 36f;
            if (health != null)
            {
                float frac = health.MaxHealth > 0f ? health.CurrentHealth / health.MaxHealth : 0f;
                GUI.Label(new Rect(lx, ly, lw, 16f),
                    $"HEALTH  {Mathf.CeilToInt(health.CurrentHealth)} / {Mathf.CeilToInt(health.MaxHealth)}",
                    Core.UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleLeft, Core.UITheme.TextDim));
                ly += 18f;
                Core.UITheme.Bar(new Rect(lx, ly, lw, 8f), frac,
                    frac > 0.5f ? Core.UITheme.Success : frac > 0.25f ? Core.UITheme.Accent : Core.UITheme.Danger);
                ly += 22f;
            }
            if (respawn != null)
            {
                GUI.Label(new Rect(lx, ly, lw, 18f), $"KILLS  {respawn.Kills.Value}      DEATHS  {respawn.Deaths.Value}",
                    Core.UITheme.Style(12, FontStyle.Normal, TextAnchor.MiddleLeft, Core.UITheme.TextDim));
                ly += 22f;
            }
            if (practice != null)
            {
                GUI.Label(new Rect(lx, ly, lw, 18f), $"BEST RUN  {practice.BestScore}",
                    Core.UITheme.Style(12, FontStyle.Normal, TextAnchor.MiddleLeft, Core.UITheme.TextDim));
            }

            // ---- Middle: loadout with equip / drop ----
            Core.UITheme.Header(new Rect(mx, top, mw, 18f), "Loadout");
            float my = top + 28f;
            if (weaponController != null)
            {
                for (int i = 0; i < weaponController.SlotCount; i++)
                {
                    var sw = weaponController.WeaponAt(i);
                    if (sw == null) continue;

                    bool owned = weaponController.OwnsSlot(i);
                    bool active = owned && i == weaponController.ActiveIndex;
                    Rect row = new(mx, my, mw, 48f);
                    Core.UITheme.Fill(row, active ? Core.UITheme.AccentDim
                        : new Color(0.09f, 0.10f, 0.11f, owned ? 0.9f : 0.4f));
                    GUI.Label(new Rect(row.x + 7f, row.y + 5f, 16f, 14f), $"{i + 1}",
                        Core.UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleLeft,
                            active ? Color.black : Core.UITheme.TextDim));

                    if (!owned)
                    {
                        GUI.Label(new Rect(row.x + 28f, row.y, mw - 34f, 48f), "Empty slot",
                            Core.UITheme.Style(13, FontStyle.Normal, TextAnchor.MiddleLeft, Core.UITheme.TextDim));
                        my += 54f;
                        continue;
                    }

                    var rarity = Core.RarityColors.Get(sw.Data.rarity);
                    Core.IconLibrary.Draw(new Rect(row.x + 26f, row.y + 8f, 32f, 32f),
                        Core.IconLibrary.Weapon(sw.VariantIndex >= 0 ? sw.VariantIndex : i), rarity);
                    GUI.Label(new Rect(row.x + 64f, row.y + 5f, mw - 70f, 20f), sw.Data.weaponName,
                        Core.UITheme.Style(14, FontStyle.Bold, TextAnchor.MiddleLeft, active ? Color.black : rarity));
                    GUI.Label(new Rect(row.x + 64f, row.y + 25f, mw - 70f, 18f),
                        $"{sw.CurrentAmmo}/{sw.Data.magazineSize}   ·   {sw.ReserveAmmo} reserve",
                        Core.UITheme.Style(11, FontStyle.Normal, TextAnchor.MiddleLeft,
                            active ? new Color(0f, 0f, 0f, 0.7f) : Core.UITheme.TextDim));

                    float by = row.yMax + 3f, bw = (mw - 6f) / 2f;
                    if (active)
                    {
                        GUI.Label(new Rect(row.x, by, bw, 22f), "EQUIPPED",
                            Core.UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.Success));
                    }
                    else if (Core.UITheme.Button(new Rect(row.x, by, bw, 22f), "Equip"))
                    {
                        weaponController.OwnerEquip(i);
                    }
                    // Slot 0 is the starter rifle and can't be dropped.
                    if (i > 0 && Core.UITheme.Button(new Rect(row.x + bw + 6f, by, bw, 22f), "Drop"))
                    {
                        weaponController.OwnerDropWeapon(i);
                    }
                    my += 80f;
                }
            }

            // ---- Right: items with use / drop ----
            Core.UITheme.Header(new Rect(rx, top, rw, 18f), "Items");
            float ry = top + 28f;
            if (inventory.SlotCount == 0)
            {
                GUI.Label(new Rect(rx, ry, rw, 40f), "Empty.\nLoot with E, or walk over dropped loot.",
                    Core.UITheme.Style(12, FontStyle.Normal, TextAnchor.UpperLeft, Core.UITheme.TextDim));
            }
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                var slot = inventory.GetSlot(i);
                var info = Core.Items.Get(slot.ItemId);
                Rect row = new(rx, ry, rw, 40f);
                Core.UITheme.Fill(row, new Color(0.09f, 0.10f, 0.11f, 0.9f));
                Core.UITheme.Fill(new Rect(row.x, row.y, 3f, row.height), info.Tint);
                Core.IconLibrary.Draw(new Rect(row.x + 10f, row.y + 6f, 28f, 28f),
                    Core.IconLibrary.Item(slot.ItemId), info.Tint);
                GUI.Label(new Rect(row.x + 44f, row.y + 3f, rw - 150f, 20f), info.Name,
                    Core.UITheme.Style(13, FontStyle.Bold, TextAnchor.MiddleLeft, Core.UITheme.TextBright));
                GUI.Label(new Rect(row.x + 44f, row.y + 21f, rw - 150f, 16f), $"x{slot.Count}",
                    Core.UITheme.Style(11, FontStyle.Normal, TextAnchor.MiddleLeft, Core.UITheme.TextDim));

                const float bw = 46f;
                if (info.Usable && Core.UITheme.Button(new Rect(row.xMax - bw * 2f - 10f, row.y + 8f, bw, 24f), "Use"))
                {
                    inventory.UseItemServerRpc(i);
                }
                if (Core.UITheme.Button(new Rect(row.xMax - bw - 6f, row.y + 8f, bw, 24f), "Drop"))
                {
                    inventory.DropItemServerRpc(i);
                }
                ry += 46f;
            }

            GUI.Label(new Rect(panel.x + 24f, panel.yMax - 26f, pw - 48f, 18f),
                "4  USE MEDKIT      ·      DROPPED LOOT AUTO-COLLECTS ON WALK-OVER      ·      TAB  CLOSE",
                Core.UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.TextDim));
        }

        /// Green markers pointing to the extraction pads: a labelled dot when
        /// on screen, or a dot pinned to the screen edge in the pad's direction
        /// when it's off screen or behind you.
        private void DrawExtractMarkers()
        {
            if (raid == null || !raid.RaidActive || raid.HasExtracted) return;
            if (respawn != null && respawn.IsDead) return;
            if (_viewCamera == null) _viewCamera = GetComponentInChildren<Camera>();
            if (_viewCamera == null || _extractPositions.Count == 0) return;

            Color green = Core.UITheme.Success;
            Vector2 centre = new(Screen.width / 2f, Screen.height / 2f);
            const float margin = 64f;

            foreach (var pos in _extractPositions)
            {
                Vector3 sp = _viewCamera.WorldToScreenPoint(pos + Vector3.up * 1.4f);
                bool behind = sp.z <= 0f;
                Vector2 g = new(sp.x, Screen.height - sp.y);
                Vector2 fromCentre = g - centre;
                if (behind) fromCentre = -fromCentre;

                Rect bounds = new(margin, margin, Screen.width - 2f * margin, Screen.height - 2f * margin);
                bool onScreen = !behind && bounds.Contains(g);
                Vector2 at;
                if (onScreen)
                {
                    at = g;
                }
                else
                {
                    // Push to the border rectangle in the pad's direction.
                    if (fromCentre.sqrMagnitude < 1f) fromCentre = new Vector2(0f, -1f);
                    Vector2 dir = fromCentre.normalized;
                    float scale = Mathf.Min(
                        (bounds.width / 2f) / Mathf.Max(Mathf.Abs(dir.x), 1e-4f),
                        (bounds.height / 2f) / Mathf.Max(Mathf.Abs(dir.y), 1e-4f));
                    at = centre + dir * scale;
                }

                float dist = Vector3.Distance(transform.position, pos);
                GUI.color = green;
                GUI.DrawTexture(new Rect(at.x - 4f, at.y - 4f, 8f, 8f), Pixel);
                GUI.color = Color.white;
                GUI.Label(new Rect(at.x - 60f, at.y + 6f, 120f, 16f),
                    onScreen ? $"EXTRACT  {Mathf.RoundToInt(dist)}m" : $"EXTRACT ▸ {Mathf.RoundToInt(dist)}m",
                    Core.UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleCenter, green));
            }
        }

        /// Red arcs around the crosshair pointing back toward recent hits.
        private void DrawDamageIndicators()
        {
            var hits = PlayerRespawn.LocalDamage;
            if (hits.Count == 0) return;

            Vector2 centre = new(Screen.width / 2f, Screen.height / 2f);
            float yaw = transform.eulerAngles.y;
            var prevColor = GUI.color;
            var prevMatrix = GUI.matrix;

            // Iterate a copy-safe index range (list is pruned by the getter).
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                Vector3 d = hit.Source - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude < 0.01f) continue;

                float worldAngle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float relative = Mathf.DeltaAngle(yaw, worldAngle);
                float alpha = Mathf.Clamp01(1f - (Time.time - hit.Time) / 1.2f);

                GUI.matrix = prevMatrix;
                GUIUtility.RotateAroundPivot(relative, centre);
                GUI.color = new Color(0.95f, 0.12f, 0.12f, 0.6f * alpha);
                GUI.DrawTexture(new Rect(centre.x - 46f, centre.y - 116f, 92f, 9f), Pixel);
            }

            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }

        private void DrawRaidTimer()
        {
            if (raid == null || !raid.RaidActive || raid.HasExtracted) return;
            if (respawn != null && respawn.IsDead) return;

            float remaining = raid.TimeRemaining;
            int secs = Mathf.CeilToInt(remaining);
            Color c = remaining <= 60f ? Core.UITheme.Danger : Core.UITheme.TextBright;
            // Flash amber in the final 30 seconds.
            if (remaining <= 30f && (int)(Time.time * 2f) % 2 == 0) c = Core.UITheme.Accent;

            const float w = 116f, h = 44f;
            Rect r = new((Screen.width - w) / 2f, 14f, w, h);
            Core.UITheme.Fill(r, new Color(0.05f, 0.06f, 0.07f, 0.82f));
            Core.UITheme.Fill(new Rect(r.x, r.yMax - 2f, r.width, 2f), c);
            GUI.Label(new Rect(r.x, r.y + 4f, r.width, 14f), "RAID TIME",
                Core.UITheme.Style(10, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.TextDim));
            GUI.Label(new Rect(r.x, r.y + 16f, r.width, 24f), $"{secs / 60}:{secs % 60:00}",
                Core.UITheme.Style(22, FontStyle.Bold, TextAnchor.MiddleCenter, c));
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

            const float w = 360f, h = 8f;
            float x = Screen.width / 2f - w / 2f;
            float y = Screen.height * 0.64f;

            GUI.Label(new Rect(x, y - 26f, w, 20f), "EXTRACTING",
                Core.UITheme.Style(15, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.Success));
            GUI.Label(new Rect(x, y - 26f, w, 20f), $"{Mathf.RoundToInt(progress * 100f)}%",
                Core.UITheme.Style(13, FontStyle.Bold, TextAnchor.MiddleRight, Core.UITheme.TextDim));
            Core.UITheme.Bar(new Rect(x, y, w, h), progress, Core.UITheme.Success);
            GUI.Label(new Rect(x, y + 12f, w, 18f), "HOLD POSITION",
                Core.UITheme.Style(11, FontStyle.Normal, TextAnchor.MiddleCenter, Core.UITheme.TextDim));
        }

        /// Post-raid results screen: the haul, the stats, and the way out.
        private void DrawRaidSummary()
        {
            var summary = raid.Summary;
            var green = Core.UITheme.Success;

            // Dim the world and frame the screen in green.
            Core.UITheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.05f, 0.03f, 0.9f));
            Core.UITheme.Fill(new Rect(0, 0, Screen.width, 3f), green);
            Core.UITheme.Fill(new Rect(0, Screen.height - 3f, Screen.width, 3f), Core.UITheme.AccentDim);

            float headerY = Screen.height * 0.11f;
            GUI.Label(new Rect(0, headerY, Screen.width, 56f), "EXTRACTION SUCCESSFUL",
                Core.UITheme.Style(46, FontStyle.Bold, TextAnchor.MiddleCenter, green));
            GUI.Label(new Rect(0, headerY + 52f, Screen.width, 20f),
                "G E A R   S E C U R E D",
                Core.UITheme.Style(12, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.TextDim));

            const float panelW = 560f;
            Rect panel = new(Screen.width / 2f - panelW / 2f, Screen.height * 0.24f, panelW, Screen.height * 0.48f);
            Core.UITheme.Panel3D(panel, accentEdge: false);
            Core.UITheme.Fill(new Rect(panel.x, panel.y, panel.width, 3f), green);

            float x = panel.x + 28f;
            float w = panel.width - 56f;
            float y = panel.y + 24f;

            // Stat row: three tiles across the top.
            int minutes = Mathf.FloorToInt(summary.Duration / 60f);
            int seconds = Mathf.FloorToInt(summary.Duration % 60f);
            float tileW = (w - 16f) / 3f;
            DrawStatTile(new Rect(x, y, tileW, 58f), "SURVIVED", $"{minutes}:{seconds:00}", Core.UITheme.TextBright);
            DrawStatTile(new Rect(x + tileW + 8f, y, tileW, 58f), "KILLS", summary.Kills.ToString(), Core.UITheme.Accent);
            DrawStatTile(new Rect(x + (tileW + 8f) * 2f, y, tileW, 58f), "ITEMS OUT",
                (summary.Lines?.Length ?? 0).ToString(), green);
            y += 76f;

            Core.UITheme.Header(new Rect(x, y, w, 18f), "Secured in stash");
            y += 26f;

            // Haul list, two columns.
            if (summary.Lines != null && summary.Lines.Length > 0)
            {
                float colW = w / 2f;
                float rowH = 22f;
                float listBottom = panel.yMax - 52f;
                int perColumn = Mathf.Max(1, Mathf.FloorToInt((listBottom - y) / rowH));
                for (int i = 0; i < summary.Lines.Length && i < perColumn * 2; i++)
                {
                    float cx = x + (i / perColumn) * colW;
                    float cy = y + (i % perColumn) * rowH;
                    Core.UITheme.Fill(new Rect(cx, cy + rowH / 2f - 2f, 4f, 4f), green);
                    GUI.Label(new Rect(cx + 14f, cy, colW - 20f, rowH), summary.Lines[i],
                        Core.UITheme.Style(13, FontStyle.Normal, TextAnchor.MiddleLeft, Core.UITheme.TextBright));
                }
            }
            else
            {
                GUI.Label(new Rect(x, y, w, 22f), "Nothing recovered.",
                    Core.UITheme.Style(13, FontStyle.Normal, TextAnchor.MiddleLeft, Core.UITheme.TextDim));
            }

            GUI.Label(new Rect(x, panel.yMax - 34f, w, 20f),
                "This gear carries into your next raid.",
                Core.UITheme.Style(11, FontStyle.Normal, TextAnchor.MiddleLeft, Core.UITheme.TextDim));

            if (Core.UITheme.Button(new Rect(Screen.width / 2f - 120f, panel.yMax + 26f, 240f, 46f),
                "Leave Raid", primary: true))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
            }
        }

        private static void DrawStatTile(Rect rect, string label, string value, Color valueColor)
        {
            Core.UITheme.Fill(rect, new Color(0.10f, 0.11f, 0.12f, 0.95f));
            Core.UITheme.Fill(new Rect(rect.x, rect.y, rect.width, 1f), Core.UITheme.Line);
            GUI.Label(new Rect(rect.x, rect.y + 8f, rect.width, 28f), value,
                Core.UITheme.Style(26, FontStyle.Bold, TextAnchor.MiddleCenter, valueColor));
            GUI.Label(new Rect(rect.x, rect.y + 36f, rect.width, 16f), label,
                Core.UITheme.Style(10, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.TextDim));
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

            float height = 76f + players.Length * 28f;
            Rect panel = new(Screen.width / 2f - 190f, Screen.height * 0.18f, 380f, height);
            UITheme.Panel3D(panel);

            GUI.Label(new Rect(panel.x + 20f, panel.y + 12f, panel.width - 40f, 22f), "SCOREBOARD",
                UITheme.Style(15, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextBright));

            float rowX = panel.x + 20f;
            float rowW = panel.width - 40f;
            GUI.Label(new Rect(rowX, panel.y + 38f, rowW - 100f, 18f), "OPERATOR",
                UITheme.Style(10, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextDim));
            GUI.Label(new Rect(rowX + rowW - 96f, panel.y + 38f, 40f, 18f), "K",
                UITheme.Style(10, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.TextDim));
            GUI.Label(new Rect(rowX + rowW - 48f, panel.y + 38f, 40f, 18f), "D",
                UITheme.Style(10, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.TextDim));
            UITheme.Fill(new Rect(rowX, panel.y + 56f, rowW, 1f), UITheme.Line);

            float y = panel.y + 62f;
            foreach (var player in players)
            {
                bool isSelf = player.IsOwner;
                if (isSelf) UITheme.Fill(new Rect(rowX - 6f, y, rowW + 12f, 26f), new Color(0.95f, 0.71f, 0.22f, 0.10f));
                var nameColor = isSelf ? UITheme.Accent : UITheme.TextBright;
                GUI.Label(new Rect(rowX, y, rowW - 100f, 26f), player.ResolvedName,
                    UITheme.Style(13, isSelf ? FontStyle.Bold : FontStyle.Normal, TextAnchor.MiddleLeft, nameColor));
                GUI.Label(new Rect(rowX + rowW - 96f, y, 40f, 26f), player.Kills.Value.ToString(),
                    UITheme.Style(13, FontStyle.Bold, TextAnchor.MiddleCenter, UITheme.TextBright));
                GUI.Label(new Rect(rowX + rowW - 48f, y, 40f, 26f), player.Deaths.Value.ToString(),
                    UITheme.Style(13, FontStyle.Normal, TextAnchor.MiddleCenter, UITheme.TextDim));
                y += 28f;
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
            GUILayout.Label("— Source movement —");
            _movement.acceleration = TuningSlider("Ground accel", _movement.acceleration, 4f, 24f);
            _movement.deceleration = TuningSlider("Friction", _movement.deceleration, 2f, 12f);
            _movement.airControl = TuningSlider("Air accel", _movement.airControl, 4f, 24f);

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
            Core.UITheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.08f, 0.01f, 0.01f, 0.72f));
            Core.UITheme.Fill(new Rect(0, 0, Screen.width, 3f), Core.UITheme.Danger);
            Core.UITheme.Fill(new Rect(0, Screen.height - 3f, Screen.width, 3f), Core.UITheme.Danger);

            GUI.Label(new Rect(0, Screen.height / 2f - 66f, Screen.width, 54f), "YOU DIED",
                Core.UITheme.Style(46, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.Danger));

            string killer = respawn != null ? respawn.LastKiller.Value.ToString() : "";
            if (!string.IsNullOrEmpty(killer))
            {
                GUI.Label(new Rect(0, Screen.height / 2f - 14f, Screen.width, 22f), $"Killed by  {killer}",
                    Core.UITheme.Style(16, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.TextBright));
            }
            GUI.Label(new Rect(0, Screen.height / 2f + 12f, Screen.width, 20f),
                "L O O T   L O S T",
                Core.UITheme.Style(12, FontStyle.Bold, TextAnchor.MiddleCenter, Core.UITheme.TextDim));
            GUI.Label(new Rect(0, Screen.height / 2f + 40f, Screen.width, 24f), "Respawning...",
                Core.UITheme.Style(15, FontStyle.Normal, TextAnchor.MiddleCenter, Core.UITheme.TextBright));
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

                // Medkit pip beside the vitals: count carried + the use key.
                if (inventory != null)
                {
                    int medkits = inventory.CountOf((int)Core.ItemType.Medkit);
                    Rect pip = new(block.xMax + 10f, block.y, 92f, 46f);
                    Core.UITheme.Fill(pip, new Color(0.06f, 0.07f, 0.08f, 0.85f));
                    Core.UITheme.Fill(new Rect(pip.x, pip.y, 3f, pip.height),
                        medkits > 0 ? Core.UITheme.Success : Core.UITheme.TextDim);
                    Core.IconLibrary.Draw(new Rect(pip.x + 10f, pip.y + 10f, 26f, 26f),
                        Core.IconLibrary.Item((int)Core.ItemType.Medkit),
                        Core.Items.Get((int)Core.ItemType.Medkit).Tint);
                    GUI.Label(new Rect(pip.x + 42f, pip.y + 6f, 46f, 22f), "x" + medkits,
                        Core.UITheme.Style(20, FontStyle.Bold, TextAnchor.MiddleLeft,
                            medkits > 0 ? Core.UITheme.TextBright : Core.UITheme.TextDim));
                    GUI.Label(new Rect(pip.x + 42f, pip.y + 27f, 46f, 14f), "[4]",
                        Core.UITheme.Style(10, FontStyle.Bold, TextAnchor.MiddleLeft, Core.UITheme.TextDim));
                }
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
            const float panelW = 340f, panelH = 540f;
            Rect panel = new(Screen.width / 2f - panelW / 2f, Screen.height / 2f - panelH / 2f, panelW, panelH);
            UITheme.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));
            UITheme.Panel3D(panel);

            GUI.Label(new Rect(panel.x + 20f, panel.y + 14f, panel.width - 40f, 22f), "SETTINGS",
                UITheme.Style(16, FontStyle.Bold, TextAnchor.MiddleLeft, UITheme.TextBright));
            GUI.Label(new Rect(panel.x + 20f, panel.y + 14f, panel.width - 40f, 22f), "F1",
                UITheme.Style(11, FontStyle.Bold, TextAnchor.MiddleRight, UITheme.TextDim));

            float x = panel.x + 20f;
            float w = panel.width - 40f;
            float y = panel.y + 46f;

            UITheme.Header(new Rect(x, y, w, 18f), "Crosshair");
            y += 26f;
            CrosshairSettings.Style = (CrosshairStyle)UITheme.Segmented(new Rect(x, y, w, 26f),
                new[] { "CROSS", "DOT", "CIRCLE" }, (int)CrosshairSettings.Style);
            y += 36f;
            CrosshairSettings.Size = UITheme.Slider(new Rect(x, y, w, 30f), "Size", CrosshairSettings.Size, 4f, 40f, "0");
            y += 40f;

            GUI.Label(new Rect(x, y, w, 18f), "Colour",
                UITheme.Style(12, FontStyle.Normal, TextAnchor.MiddleLeft, UITheme.TextDim));
            y += 20f;
            float swatch = (w - 5f * 6f) / 6f;
            for (int i = 0; i < CrosshairSettings.Colors.Length; i++)
            {
                Rect cell = new(x + i * (swatch + 6f), y, swatch, 22f);
                UITheme.Fill(cell, CrosshairSettings.Colors[i]);
                if (i == CrosshairSettings.ColorIndex)
                {
                    UITheme.Fill(new Rect(cell.x, cell.yMax + 2f, cell.width, 2f), UITheme.Accent);
                }
                if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) CrosshairSettings.ColorIndex = i;
            }
            y += 40f;

            UITheme.Header(new Rect(x, y, w, 18f), "Mouse");
            y += 26f;
            MouseSettings.Sensitivity = UITheme.Slider(new Rect(x, y, w, 30f),
                "Sensitivity", MouseSettings.Sensitivity, 0.02f, 0.5f);
            y += 38f;
            MouseSettings.InvertY = UITheme.Toggle(new Rect(x, y, w, 20f), "Invert Y axis", MouseSettings.InvertY);
            y += 32f;

            UITheme.Header(new Rect(x, y, w, 18f), "Game");
            y += 26f;
            GameSettings.Fov = UITheme.Slider(new Rect(x, y, w, 30f), "Field of view",
                GameSettings.Fov, GameSettings.MinFov, GameSettings.MaxFov, "0");
            y += 38f;
            GameSettings.MasterVolume = UITheme.Slider(new Rect(x, y, w, 30f), "Master volume",
                GameSettings.MasterVolume, 0f, 1f, "0.00");
            y += 40f;

            UITheme.Header(new Rect(x, y, w, 18f), "Display");
            y += 26f;
            GUILayout.BeginArea(new Rect(x, y, w, panel.yMax - y - 16f));
            DisplaySettings.DrawControls();
            GUILayout.EndArea();
        }
    }
}
