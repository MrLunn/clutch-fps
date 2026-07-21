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
        private DroppedLoot _nearbyDrop;

        public LootPickup Nearby =>
            _nearby != null && _nearby.IsAvailable ? _nearby : null;

        /// A dropped weapon within reach, waiting on an E-press. Items aren't
        /// listed here — they auto-collect on walk-over.
        public DroppedLoot NearbyDrop => _nearbyDrop;

        private void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent<LootPickup>(out var pickup) && pickup.RequiresInteract)
            {
                _nearby = pickup;
            }
            else if (other.TryGetComponent<DroppedLoot>(out var drop) && drop.IsWeapon)
            {
                _nearbyDrop = drop;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.TryGetComponent<LootPickup>(out var pickup) && pickup == _nearby)
            {
                _nearby = null;
            }
            else if (other.TryGetComponent<DroppedLoot>(out var drop) && drop == _nearbyDrop)
            {
                _nearbyDrop = null;
            }
        }

        private PlayerRespawn _respawn;

        private void Update()
        {
            if (!IsOwner) return;
            if (PlayerHUD.LocalMenuOpen) return;
            if (_respawn == null) _respawn = GetComponent<PlayerRespawn>();
            if (_respawn != null && _respawn.IsDead) return;

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.eKey.wasPressedThisFrame) return;

            // Scene loot takes priority; otherwise a dropped weapon at your feet.
            if (Nearby != null) InteractServerRpc(_nearby.NetworkObject);
            else if (_nearbyDrop != null) InteractDropServerRpc(_nearbyDrop.NetworkObject);
        }

        [ServerRpc]
        private void InteractServerRpc(NetworkObjectReference pickupRef)
        {
            if (!pickupRef.TryGet(out var pickupObject)) return;
            if (!pickupObject.TryGetComponent<LootPickup>(out var pickup)) return;
            if (!TryGetComponent<PlayerWeaponController>(out var weapons)) return;
            pickup.ServerTryPickup(weapons);
        }

        [ServerRpc]
        private void InteractDropServerRpc(NetworkObjectReference dropRef)
        {
            if (!dropRef.TryGet(out var dropObject)) return;
            if (!dropObject.TryGetComponent<DroppedLoot>(out var drop)) return;
            if (!TryGetComponent<PlayerWeaponController>(out var weapons)) return;
            drop.ServerTryPickup(weapons);
        }
    }
}
