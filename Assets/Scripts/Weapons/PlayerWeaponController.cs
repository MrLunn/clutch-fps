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

        public override void OnNetworkSpawn()
        {
            enabled = IsOwner;
            if (IsOwner)
            {
                SetActiveWeapon(0);
            }
        }

        private void Update()
        {
            if (!IsOwner || weapons.Length == 0) return;

            HandleSwitchInput();

            var weapon = weapons[_activeIndex];
            var mouse = Mouse.current;
            if (mouse == null) return;

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
            _activeIndex = index;
            for (int i = 0; i < weapons.Length; i++)
            {
                weapons[i].gameObject.SetActive(i == index);
            }
        }
    }
}
