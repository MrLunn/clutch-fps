using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClutchFPS.Weapons
{
    /// Holds the weapons a player currently has equipped and routes input to the
    /// active one. Weapons are child GameObjects (each with a Weapon component);
    /// only one is active/visible at a time.
    public class PlayerWeaponController : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Weapon[] weapons;

        private int _activeIndex;
        private Player.PlayerRespawn _respawn;
        private Player.FirstPersonMovement _movement;
        private Player.PlayerInventory _inventory;

        /// Scales look sensitivity while aiming (read by MouseLook).
        public static float LookSensitivityScale = 1f;

        public bool IsAiming { get; private set; }

        private void Awake()
        {
            _respawn = GetComponent<Player.PlayerRespawn>();
            _movement = GetComponent<Player.FirstPersonMovement>();
        }

        /// Re-applies holster state so only the active slot's model shows.
        public void RefreshVisibleWeapon() => ApplyActiveWeapon(_activeIndex);

        /// Bitmask of loadout slots the player has picked up. Slot 0 (rifle)
        /// is owned from spawn; others come from table pickups.
        private readonly NetworkVariable<int> _ownedSlots = new(1,
            writePerm: NetworkVariableWritePermission.Server);

        public bool OwnsSlot(int slot) => ((_ownedSlots.Value >> slot) & 1) == 1;

        /// Server-side. Returns false if the slot is invalid or already owned.
        public bool ServerGrantSlot(int slot)
        {
            if (!IsServer || slot < 0 || slot >= weapons.Length) return false;
            if (OwnsSlot(slot)) return false;
            _ownedSlots.Value |= 1 << slot;
            return true;
        }

        /// Server-side loadout snapshot for the persistent stash.
        public int OwnedSlotsMask => _ownedSlots.Value;

        public void ServerApplyLoadout(int ownedMask, int[] variants)
        {
            if (!IsServer) return;
            _ownedSlots.Value = ownedMask | 1; // always keep slot 0
            if (variants == null) return;
            for (int i = 0; i < weapons.Length && i < variants.Length; i++)
            {
                if (variants[i] >= 0) weapons[i].ServerSetWeaponData(variants[i]);
            }
        }

        public int[] ServerGetVariants()
        {
            var result = new int[weapons.Length];
            for (int i = 0; i < weapons.Length; i++) result[i] = weapons[i].VariantIndex;
            return result;
        }

        /// Owner-side only: which weapon the local player currently has out.
        public Weapon ActiveWeapon =>
            weapons != null && weapons.Length > 0 ? weapons[_activeIndex] : null;

        public int ActiveIndex => _activeIndex;
        public int SlotCount => weapons != null ? weapons.Length : 0;

        public Weapon WeaponAt(int slot) =>
            weapons != null && slot >= 0 && slot < weapons.Length ? weapons[slot] : null;

        /// Server-side: refill every carried weapon. Returns true if any needed ammo.
        public bool ServerRefillAllAmmo()
        {
            if (!IsServer) return false;
            bool anyRefilled = false;
            foreach (var weapon in weapons)
            {
                anyRefilled |= weapon.ServerRefillAmmo();
            }
            return anyRefilled;
        }

        /// Owner-written so everyone renders the weapon the player actually holds.
        private readonly NetworkVariable<int> _activeSlotSync = new(0,
            writePerm: NetworkVariableWritePermission.Owner);

        public override void OnNetworkSpawn()
        {
            enabled = IsOwner;
            _activeSlotSync.OnValueChanged += (_, slot) =>
            {
                if (!IsOwner) ApplyActiveWeapon(slot);
            };
            ApplyActiveWeapon(_activeSlotSync.Value);
        }

        private void Update()
        {
            if (!IsOwner || weapons.Length == 0) return;
            if (_respawn != null && _respawn.IsDead) return;
            if (Player.PlayerHUD.LocalMenuOpen) return;

            HandleSwitchInput();

            var weapon = weapons[_activeIndex];
            var mouse = Mouse.current;
            if (mouse == null) return;

            // ADS: hold right mouse. Feeds FOV/move-speed to movement and
            // scales look sensitivity proportionally to the zoom.
            IsAiming = mouse.rightButton.isPressed;
            weapon.SetAiming(IsAiming);
            LookSensitivityScale = IsAiming ? weapon.Data.adsFov / 60f : 1f;
            if (_movement != null)
            {
                _movement.SetAimState(IsAiming ? weapon.Data.adsFov : 0f,
                    weapon.Data.adsMoveSpeedMultiplier);
            }

            switch (weapon.CurrentFireMode)
            {
                case FireMode.Automatic:
                    if (mouse.leftButton.isPressed)
                        weapon.TryFire(playerCamera.transform.position, playerCamera.transform.forward);
                    break;
                case FireMode.Burst:
                    if (mouse.leftButton.wasPressedThisFrame)
                        weapon.TryFireBurst(playerCamera.transform);
                    break;
                default:
                    if (mouse.leftButton.wasPressedThisFrame)
                        weapon.TryFire(playerCamera.transform.position, playerCamera.transform.forward);
                    break;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                weapon.TryReload();
            }
            if (keyboard != null && keyboard.bKey.wasPressedThisFrame)
            {
                weapon.CycleFireMode();
            }
            if (keyboard != null && keyboard.digit4Key.wasPressedThisFrame)
            {
                _inventory ??= GetComponent<Player.PlayerInventory>();
                _inventory?.UseMedkit();
            }
            if (keyboard != null && keyboard.gKey.wasPressedThisFrame)
            {
                TryThrowGrenade();
            }
        }

        private float _nextGrenadeTime;

        /// Owner-side: lob a frag if we're carrying one. The server owns the
        /// flight and the damage; clients just get a ball to watch.
        private void TryThrowGrenade()
        {
            if (!IsOwner || Time.time < _nextGrenadeTime || playerCamera == null) return;
            _inventory ??= GetComponent<Player.PlayerInventory>();
            if (_inventory == null || _inventory.CountOf((int)Core.ItemType.Grenade) <= 0) return;

            _nextGrenadeTime = Time.time + 0.9f;
            var cam = playerCamera.transform;
            // Slight upward bias so it arcs instead of bouncing off your boots.
            ThrowGrenadeServerRpc(cam.position + cam.forward * 0.6f,
                (cam.forward + cam.up * 0.18f).normalized);
        }

        [ServerRpc]
        private void ThrowGrenadeServerRpc(Vector3 origin, Vector3 direction, ServerRpcParams _ = default)
        {
            var inventory = GetComponent<Player.PlayerInventory>();
            if (inventory == null || inventory.ServerTakeItem((int)Core.ItemType.Grenade, 1) <= 0) return;

            ThrowGrenadeClientRpc(origin, direction);
            StartCoroutine(ServerGrenadeFlight(origin, direction, OwnerClientId));
        }

        private IEnumerator ServerGrenadeFlight(Vector3 origin, Vector3 direction, ulong thrower)
        {
            Vector3 position = origin;
            Vector3 velocity = direction * Grenade.ThrowSpeed;
            for (float t = 0f; t < Grenade.Fuse; t += Time.deltaTime)
            {
                position = Grenade.Step(position, ref velocity, Time.deltaTime);
                yield return null;
            }
            Grenade.ServerExplode(position, thrower);
            ExplodeClientRpc(position);
        }

        [ClientRpc]
        private void ThrowGrenadeClientRpc(Vector3 origin, Vector3 direction)
        {
            Grenade.SpawnVisual(origin, direction);
        }

        [ClientRpc]
        private void ExplodeClientRpc(Vector3 centre)
        {
            Grenade.SpawnExplosionFx(centre);
        }

        private void HandleSwitchInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame && weapons.Length > 0 && OwnsSlot(0)) SetActiveWeapon(0);
            if (keyboard.digit2Key.wasPressedThisFrame && weapons.Length > 1 && OwnsSlot(1)) SetActiveWeapon(1);
            if (keyboard.digit3Key.wasPressedThisFrame && weapons.Length > 2 && OwnsSlot(2)) SetActiveWeapon(2);
        }

        /// Owner-side: equip an owned slot from the inventory screen.
        public void OwnerEquip(int slot)
        {
            if (IsOwner && OwnsSlot(slot)) SetActiveWeapon(slot);
        }

        /// Owner-side: drop a slot's weapon on the ground. Switches off it first
        /// if it was active, then asks the server to spawn the loot and revoke
        /// ownership. Slot 0 (the starter rifle) can't be dropped.
        public void OwnerDropWeapon(int slot)
        {
            if (!IsOwner || slot <= 0 || !OwnsSlot(slot)) return;
            if (_activeIndex == slot) SetActiveWeapon(0);
            DropWeaponServerRpc(slot);
        }

        [ServerRpc]
        private void DropWeaponServerRpc(int slot)
        {
            if (slot <= 0 || slot >= weapons.Length || !OwnsSlot(slot)) return;
            var weapon = weapons[slot];
            int variant = weapon.VariantIndex;
            if (variant < 0 && WeaponDatabase.Instance != null)
            {
                variant = WeaponDatabase.Instance.IndexOf(weapon.Data);
            }
            Environment.LootSpawner.SpawnWeapon(
                Environment.LootSpawner.DropPoint(transform), slot, variant);

            _ownedSlots.Value &= ~(1 << slot);
            weapon.ServerSetWeaponData(-1); // reset to serialized default for a clean re-grant
        }

        private void SetActiveWeapon(int index)
        {
            if (IsSpawned && IsOwner) _activeSlotSync.Value = index;
            ApplyActiveWeapon(index);
        }

        private void ApplyActiveWeapon(int index)
        {
            _activeIndex = index;
            for (int i = 0; i < weapons.Length; i++)
            {
                weapons[i].SetHolstered(i != index);
            }
        }
    }
}
