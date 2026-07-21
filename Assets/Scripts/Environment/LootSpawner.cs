using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Environment
{
    /// Server-side helper for throwing loot on the ground. Loads the shared
    /// DroppedLoot prefab from Resources (registered as a network prefab by
    /// NetworkBootstrap) and spawns configured instances.
    public static class LootSpawner
    {
        private static GameObject _prefab;
        public static GameObject Prefab =>
            _prefab != null ? _prefab : _prefab = Resources.Load<GameObject>("DroppedLoot");

        /// A little in front of and above the dropper, so it lands clear of them.
        public static Vector3 DropPoint(Transform dropper) =>
            dropper.position + dropper.forward * 1.2f + Vector3.up * 0.6f;

        public static void SpawnItem(Vector3 position, int itemId, int amount)
        {
            var loot = Spawn(position);
            if (loot != null) loot.ServerSetItem(itemId, amount);
        }

        public static void SpawnWeapon(Vector3 position, int slot, int variantIndex)
        {
            var loot = Spawn(position);
            if (loot != null) loot.ServerSetWeapon(slot, variantIndex);
        }

        private static DroppedLoot Spawn(Vector3 position)
        {
            if (Prefab == null)
            {
                Debug.LogWarning("LootSpawner: DroppedLoot prefab not found in Resources.");
                return null;
            }
            var instance = Object.Instantiate(Prefab, position, Quaternion.identity);
            var netObject = instance.GetComponent<NetworkObject>();
            netObject.Spawn();
            return instance.GetComponent<DroppedLoot>();
        }
    }
}
