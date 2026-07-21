# Clutch FPS — Multiplayer & Accounts (Unity Gaming Services)

Goal: play over the internet with **no port-forwarding** (Relay), with **accounts**
(Authentication) so each player's **stash, inventory, and stats** persist in the
cloud (Cloud Save). All three are one integrated stack with a free tier.

This is staged. Nothing here compiles until **your setup tasks** below are done,
because the code references packages that aren't installed yet.

---

## Your setup tasks (blocking — do these first)

1. **Link the project to Unity Cloud.**
   - Edit → Project Settings → **Services** → sign in with your Unity ID →
     create a new project (or link an existing one). This writes a project ID
     into the project.
   - Or do it at https://cloud.unity.com → create project → then link in-editor.

2. **Enable the three services** (in the Unity Cloud dashboard for this project):
   - **Authentication**
   - **Cloud Save**
   - **Relay**
   (Free tier is fine to start. Note the current free limits on the dashboard.)

3. **Install packages** (Window → Package Manager → + → Add by name):
   - `com.unity.services.core`
   - `com.unity.services.authentication`
   - `com.unity.services.cloudsave`
   - `com.unity.services.relay`
   `com.unity.netcode.gameobjects` and its UnityTransport are already present.

4. **Tell me when done** and confirm the project is linked. Then I implement the
   stages below.

---

## Implementation stages (my work, after setup)

### Stage 1 — Services init + Authentication
- Initialize `UnityServices` on boot.
- A **login screen** before the main menu: start with anonymous sign-in
  (one tap, gives a stable account id), then add username/password so the
  account is portable across devices.
- The account id replaces `PlayerIdentity.LocalName` as the identity that keys
  everything.

### Stage 2 — Cloud Save for stash / inventory / stats
- On login, load this account's data from Cloud Save into memory (one async
  read). Gameplay keeps mutating the in-memory copy synchronously as it does
  now; we flush to Cloud Save at checkpoints (after a purchase, on extract, on
  logout).
- Move `StashService` persistence from the local JSON file to Cloud Save,
  keyed by account id instead of player name. The in-memory mutation logic
  (credits, equip/unequip, deposit) stays the same.
- Add a **stats** blob: kills, deaths, raids run, raids survived, credits
  earned, playtime. Persisted alongside the stash.

### Stage 3 — Relay connectivity (the "free server")
- **Host**: request a Relay allocation → get a short **join code** → feed the
  allocation into UnityTransport → StartHost. The menu shows the join code.
- **Client**: enter the host's **join code** (replaces the IP field) → Relay
  resolves it → StartClient. No router config, works over the internet.
- Keep direct-IP as a fallback for LAN.

### Stage 4 — Wire raid results to accounts
- Persistence becomes **client-owned**: each player saves their *own* account
  data. The host runs the raid authoritatively and reports each player's
  outcome (haul, credits, kills) to that player, who writes it to their own
  Cloud Save. (See the ownership note below.)

---

## Key design decisions

- **Persistence ownership: client-owned.** UGS Cloud Save is per-user — the
  host can't write to a friend's account. So each client persists its own
  stash/stats; the host just reports raid outcomes. This is trivially
  cheatable, which is **fine for playing with friends**. If this ever becomes a
  public game, the fix is server-authoritative writes via UGS Cloud Code or a
  dedicated server (Stage 5, later).

- **Connect by join code, not IP.** Relay hands out a short code per host
  session. That's what a friend types in to join.

- **Login is required to play.** Anonymous sign-in keeps it one-tap; the
  username/password upgrade lets the account move between machines.

- **Migration.** Existing on-disk stashes (`stashes.json`) can be imported into
  the signed-in account the first time, so current progress isn't lost.

---

## What changes for the player

- A quick login on first launch (then it's remembered).
- Host clicks **Host**, shares a **join code** (not an IP); friend pastes it and
  clicks **Join**. No port-forwarding, no VPN.
- Stash, loadout, credits, and stats follow the account on any machine.
