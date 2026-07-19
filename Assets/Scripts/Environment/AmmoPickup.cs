using ClutchFPS.Core;
using ClutchFPS.Player;
using ClutchFPS.Weapons;

namespace ClutchFPS.Environment
{
    /// Walk-over ammo crate: grants a bundle of both calibers as inventory
    /// items (ammo is a finite resource; reloading consumes it).
    public class AmmoPickup : LootPickup
    {
        protected override bool TryApplyTo(PlayerWeaponController player)
        {
            if (!player.TryGetComponent<PlayerInventory>(out var inventory)) return false;
            bool addedRifle = inventory.ServerAddItem((int)ItemType.Ammo556, 30);
            bool addedPistol = inventory.ServerAddItem((int)ItemType.Ammo9mm, 15);
            return addedRifle || addedPistol;
        }
    }
}
