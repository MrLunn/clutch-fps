# Setup: opening Clutch FPS and building the shooting range

The code and project scaffolding is already in this repo. The parts that only exist
inside the Unity Editor (scenes, prefabs, GameObject wiring) need to be built by hand
once, following the steps below. Budget ~30-45 minutes the first time.

## 1. Install Unity

1. Install **Unity Hub** if you don't have it.
2. In Unity Hub > Installs, install **Unity 6000.0 LTS** (any 6000.0.x patch is fine).
   Make sure the **Windows Build Support** module is checked.

## 2. Open the project

1. In Unity Hub > Projects, click **Add** > **Add project from disk**.
2. Select this folder (`ClutchFPS`).
3. Open it. Unity will import packages from `Packages/manifest.json`
   (Netcode for GameObjects, Input System, URP, etc.) — this takes a few minutes
   the first time. If Package Manager complains about an exact version, let it
   resolve to the closest compatible version; that's fine.
4. If prompted to enable the new Input System backend, choose **Input System
   Package (New)** and let the editor restart.

## 3. Create the scene

1. `File > New Scene` > Basic (URP) > save as `Assets/Scenes/ShootingRange.unity`.
2. Delete the default `Main Camera` (the player prefab will bring its own).
3. Build a simple range: a large `Plane` or `Cube` as the floor, a few `Cube`
   walls around the edges, and 3-5 simple `Capsule` or `Cube` objects as targets.

## 4. Set up Netcode

1. `GameObject > Create Empty`, name it `NetworkManager`.
2. Add component **Network Manager** (from Netcode for GameObjects).
3. Add component `ClutchFPS.Networking.NetworkBootstrap` (same object).
4. Add component `ClutchFPS.Networking.PlayerSpawnPoints` (same object) — leave
   Spawn Points empty for now, you'll fill it in step 6.

## 5. Build the Player prefab

1. `GameObject > Create Empty`, name it `Player`.
2. Add component **Character Controller** (set Height ~1.8, Center Y ~0.9).
3. Add component **Network Object** (Netcode).
4. Add component **Network Transform** (Netcode) — syncs position/rotation
   automatically, no custom code needed.
5. Add these scripts (all under `Assets/Scripts/`):
   - `Player/FirstPersonMovement.cs`
   - `Player/MouseLook.cs`
   - `Weapons/PlayerWeaponController.cs`
   - `Core/Health.cs`
6. Create a child empty `CameraPivot` at roughly eye height (Y ~0.7 relative to
   player origin). Add a `Camera` component to it (or a child `Camera` GameObject).
   Assign this camera to `MouseLook > Camera Pivot` and to
   `PlayerWeaponController > Player Camera`.
7. Create two child GameObjects under Player: `Rifle` and `Pistol`. Each needs:
   - A **Network Object** is *not* needed (they're not independently networked;
     they ride along with the parent Player's NetworkObject).
   - Component `Weapons/Weapon.cs`.
   - A `WeaponData` asset assigned (see step 6 below).
8. On `PlayerWeaponController`, assign the `Rifle` and `Pistol` GameObjects into
   the `Weapons` array (index 0 = Rifle, index 1 = Pistol).
9. Drag the finished `Player` GameObject into `Assets/Prefabs/` to make it a
   prefab, then delete the instance from the scene.
10. Select the `NetworkManager` object > Network Manager component > assign the
    `Player` prefab as the **Player Prefab**.
11. Add a few empty GameObjects around the range as spawn points, assign them to
    `PlayerSpawnPoints > Spawn Points` on the `NetworkManager`.

## 6. Create weapon data assets

In the Project window, right-click `Assets/Data/Weapons` > **Create > Clutch FPS >
Weapon Data**, once for each weapon. Recommended starting values:

**Rifle** (`Assets/Data/Weapons/Rifle.asset`)
| Field | Value |
|---|---|
| Fire Mode | Automatic |
| Damage | 20 |
| Range | 150 |
| Fire Rate | 8 |
| Magazine Size | 30 |
| Reload Time | 1.8 |
| Spread Degrees | 1.5 |

**Pistol** (`Assets/Data/Weapons/Pistol.asset`)
| Field | Value |
|---|---|
| Fire Mode | Single |
| Damage | 25 |
| Range | 60 |
| Fire Rate | 4 |
| Magazine Size | 12 |
| Reload Time | 1.2 |
| Spread Degrees | 0.8 |

Assign `Rifle.asset` to the Rifle GameObject's `Weapon > Data` field, and
`Pistol.asset` to the Pistol GameObject's `Weapon > Data` field.

## 7. Add targets

1. Give each target Cube/Capsule a **Network Object**, a `Core/Health.cs`, and an
   `Environment/ShootingTarget.cs`.
2. On `ShootingTarget`, assign the target's own mesh Transform to the `Visual`
   field (this is what tips over on hit).
3. Drag targets into `Assets/Prefabs/` and place a few instances in the scene.

## 8. Test it

1. Press Play, click **Host** (the on-screen button from `NetworkBootstrap`).
2. Build the game (`File > Build Settings > Build`) and run the build alongside
   the editor, clicking **Client** in the build so it connects to your hosted
   session (`127.0.0.1` by default via Unity Transport).
3. WASD to move, mouse to look, left-click to fire, `1`/`2` to switch Rifle/Pistol,
   `R` to reload, Space to jump.

## What's intentionally not here yet

- No menus, no matchmaking/relay (local network testing only for now).
- No loot, inventory, or pickups — `Environment/` is where that will grow later.
- No animations/VFX/sound — `Weapon.HitEffectClientRpc` is the hook point.
