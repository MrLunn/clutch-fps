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

        protected override bool TryApplyTo(PlayerWeaponController player)
        {
            return player.TryGetComponent<PlayerInventory>(out var inventory)
                && inventory.ServerAddItem(itemId, amount);
        }
    }
}
