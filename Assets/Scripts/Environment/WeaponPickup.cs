using ClutchFPS.Weapons;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Table/loot weapon: grants the matching loadout slot, optionally with a
    /// specific database variant (rare versions etc.). Picking up a variant
    /// for a slot you already own swaps that slot's weapon.
    public class WeaponPickup : LootPickup
    {
        [SerializeField] private int slotIndex = 1;

        protected override string LootModelKey => $"Weapon_{Mathf.Clamp(slotIndex, 0, 2)}";

        [Tooltip("WeaponDatabase index to equip in the slot; -1 keeps the slot's default.")]
        [SerializeField] private int weaponDataIndex = -1;

        public override bool RequiresInteract => true;

        protected override bool TryApplyTo(PlayerWeaponController player)
        {
            bool granted = player.ServerGrantSlot(slotIndex);
            if (weaponDataIndex >= 0)
            {
                var weapon = player.WeaponAt(slotIndex);
                if (weapon != null)
                {
                    weapon.ServerSetWeaponData(weaponDataIndex);
                    return true;
                }
            }
            return granted;
        }
    }
}
