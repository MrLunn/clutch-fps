using ClutchFPS.Environment;
using ClutchFPS.Weapons;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClutchFPS.Player
{
    /// Tracks the interactable pickup the player is standing near and forwards
    /// E-presses to the server. The HUD reads Nearby for the pickup prompt.
    public class PlayerInteractor : NetworkBehaviour
    {
        private LootPickup _nearby;

        public LootPickup Nearby =>
            _nearby != null && _nearby.IsAvailable ? _nearby : null;

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent<LootPickup>(out var pickup) && pickup.RequiresInteract)
            {
                _nearby = pickup;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent<LootPickup>(out var pickup) && pickup == _nearby)
            {
                _nearby = null;
            }
        }

        private void Update()
        {
            if (!IsOwner || Nearby == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame)
            {
                InteractServerRpc(_nearby.NetworkObject);
            }
        }

        [ServerRpc]
        private void InteractServerRpc(NetworkObjectReference pickupRef)
        {
            if (!pickupRef.TryGet(out var pickupObject)) return;
            if (!pickupObject.TryGetComponent<LootPickup>(out var pickup)) return;
            if (!TryGetComponent<PlayerWeaponController>(out var weapons)) return;
            pickup.ServerTryPickup(weapons);
        }
    }
}
