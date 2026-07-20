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
        }

        private void HandleSwitchInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame && weapons.Length > 0 && OwnsSlot(0)) SetActiveWeapon(0);
            if (keyboard.digit2Key.wasPressedThisFrame && weapons.Length > 1 && OwnsSlot(1)) SetActiveWeapon(1);
            if (keyboard.digit3Key.wasPressedThisFrame && weapons.Length > 2 && OwnsSlot(2)) SetActiveWeapon(2);
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
