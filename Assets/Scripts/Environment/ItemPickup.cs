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

        protected override string LootModelKey => (Core.ItemType)itemId switch
        {
            Core.ItemType.Ammo556 => "Ammo556",
            Core.ItemType.Ammo9mm => "Ammo9mm",
            _ => "Medkit",
        };

        protected override bool TryApplyTo(PlayerWeaponController player)
        {
            return player.TryGetComponent<PlayerInventory>(out var inventory)
                && inventory.ServerAddItem(itemId, amount);
        }
    }
}
