using ClutchFPS.Weapons;

namespace ClutchFPS.Environment
{
    /// First concrete loot: an ammo crate that refills every carried weapon.
    public class AmmoPickup : LootPickup
    {
        protected override bool TryApplyTo(PlayerWeaponController player)
        {
            return player.ServerRefillAllAmmo();
        }
    }
}
