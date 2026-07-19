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

            bool wantsToFire = weapon.Data.fireMode == FireMode.Automatic
                ? mouse.leftButton.isPressed
                : mouse.leftButton.wasPressedThisFrame;

            if (wantsToFire)
            {
                weapon.TryFire(playerCamera.transform.position, playerCamera.transform.forward);
            }

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                weapon.TryReload();
            }
        }

        private void HandleSwitchInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame && weapons.Length > 0) SetActiveWeapon(0);
            if (keyboard.digit2Key.wasPressedThisFrame && weapons.Length > 1) SetActiveWeapon(1);
            if (keyboard.digit3Key.wasPressedThisFrame && weapons.Length > 2) SetActiveWeapon(2);
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
