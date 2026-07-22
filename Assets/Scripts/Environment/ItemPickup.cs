using ClutchFPS.Player;
using ClutchFPS.Weapons;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// World loot that goes into the inventory on E-pickup (medkits, ammo packs).
    public class ItemPickup : LootPickup
    {
        [SerializeField] private int itemId;
        [SerializeField] private int amount = 1;

        public override bool RequiresInteract => true;

        protected override string LootModelKey => Core.Items.IsGear(itemId) ? null : (Core.ItemType)itemId switch
        {
            Core.ItemType.Ammo556 => "Ammo556",
            Core.ItemType.Ammo9mm => "Ammo9mm",
            _ => "Medkit",
        };

        protected override bool TryApplyTo(PlayerWeaponController player)
        {
            // Gear equips on the spot; everything else stacks in the inventory.
            if (Core.Items.IsGear(itemId))
            {
                if (!player.TryGetComponent<Core.Health>(out var health)) return false;
                health.ServerEquipGear((Core.ItemType)itemId);
                return true;
            }
            return player.TryGetComponent<PlayerInventory>(out var inventory)
                && inventory.ServerAddItem(itemId, amount);
        }
    }
}
