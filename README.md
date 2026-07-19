# Clutch FPS

A multiplayer FPS built in Unity 6, starting from a simple shooting-range foundation:
movement, rifle/pistol shooting, and networked players. Loot and game modes come later.

## Stack

- **Engine:** Unity 6000 LTS
- **Networking:** Netcode for GameObjects
- **Input:** Unity Input System
- **Render pipeline:** URP

## Status

Foundation stage — shooting range only. No matchmaking, no loot yet.

## Project layout

```
Assets/
  Scripts/
    Player/       movement, camera look
    Weapons/      weapon data + firing logic
    Networking/   NetworkBehaviours, RPCs
    Core/         health/damage, shared interfaces
    Environment/  range targets, pickups (future loot hook)
  Data/
    Weapons/      WeaponData ScriptableObject assets (rifle, pistol)
  Prefabs/
  Scenes/
```

## Getting started

See [SETUP.md](SETUP.md) for the exact steps to open this project in Unity Editor
and build the shooting-range scene.
