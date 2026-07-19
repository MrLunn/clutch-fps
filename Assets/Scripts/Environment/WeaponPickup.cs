using ClutchFPS.Weapons;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Table weapon: grants the matching loadout slot when walked over.
    /// Not consumed if the player already owns that slot.
    public class WeaponPickup : LootPickup
    {
        [SerializeField] private int slotIndex = 1;

        protected override bool TryApplyTo(PlayerWeaponController player)
        {
            return player.ServerGrantSlot(slotIndex);
        }
    }
}
