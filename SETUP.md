# Setup: opening Clutch FPS

The scene, prefabs, and weapon data are already built and committed — you don't need
to assemble anything by hand in the Editor. This doc just covers opening the project
and testing it.

## 1. Install Unity

Install **Unity 6000.3 LTS** via Unity Hub (any 6000.3.x patch is fine — that's what
this project is pinned to). Make sure **Windows Build Support** is checked.

## 2. Open the project

1. In Unity Hub > Projects, **Add** > **Add project from disk**, select this folder.
2. Open it. Unity resolves packages (Netcode for GameObjects, Input System, URP)
   on first open — this takes a few minutes.
3. You may see a one-time Console notice about the legacy Input Manager being
   deprecated. Safe to ignore — the project's Active Input Handling is set to
   "Both", so the new Input System (which our scripts use) works fine alongside it.

## 3. Open and test the scene

1. In the Project window, open `Assets/Scenes/ShootingRange.unity`.
2. Press **Play**, then click the **Host** button that appears on screen.
3. Controls: WASD to move, mouse to look, left-click to fire, `1`/`2` to switch
   Rifle/Pistol, `R` to reload, Space to jump.
4. Shoot the targets lined up ahead — they tip over on a kill and reset after
   ~3 seconds.

## Testing with a second player

1. `File > Build Settings > Build` to make a standalone build.
2. Run the build alongside the Editor. In the Editor, click **Host**; in the
   build, click **Client** (connects to `127.0.0.1` via Unity Transport by default).

## What's in the scene

- `Ground` + four `Wall*` objects forming a boxed arena (no roof/lighting fixture —
  add a Directional Light via `GameObject > Light > Directional Light` if it looks
  too flat/dark).
- `NetworkManager` — has `NetworkManager`, `UnityTransport`, `NetworkBootstrap`
  (the on-screen Host/Client/Server buttons), and `PlayerSpawnPoints` components.
- `SpawnPoint1` / `SpawnPoint2` — where connecting players appear.
- Four `ShootingTarget` prefab instances.

## What's intentionally not here yet

- No menus, no matchmaking/relay (local network testing only for now).
- No loot, inventory, or pickups — `Assets/Scripts/Environment/` is where that'll grow later.
- No animations/VFX/sound — `Weapon.HitEffectClientRpc` is the hook point for that.
- No Directional Light in the scene (add one if the flat ambient lighting bugs you).
