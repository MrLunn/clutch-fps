using ClutchFPS.Core;
using ClutchFPS.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace ClutchFPS.Player
{
    /// Server-authoritative item inventory synced to clients. Items stack up
    /// to their ItemInfo.MaxStack across a fixed number of slots; using an
    /// item applies its effect server-side and consumes one.
    public class PlayerInventory : NetworkBehaviour
    {
        public const int MaxSlots = 8;

        public struct Slot : INetworkSerializable, System.IEquatable<Slot>
        {
            public int ItemId;
            public int Count;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref ItemId);
                serializer.SerializeValue(ref Count);
            }

            public bool Equals(Slot other) => ItemId == other.ItemId && Count == other.Count;
        }

        private NetworkList<Slot> _slots;

        private void Awake()
        {
            _slots = new NetworkList<Slot>();
        }

        public int SlotCount => _slots.Count;
        public Slot GetSlot(int index) => _slots[index];

        /// Total amount of one item across all slots (e.g. reserve ammo).
        public int CountOf(int itemId)
        {
            int total = 0;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].ItemId == itemId) total += _slots[i].Count;
            }
            return total;
        }

        /// Server-side: consume up to `amount` of an item; returns how many
        /// were actually taken. Used by reloading.
        public int ServerTakeItem(int itemId, int amount)
        {
            if (!IsServer || amount <= 0) return 0;
            int taken = 0;
            for (int i = _slots.Count - 1; i >= 0 && taken < amount; i--)
            {
                if (_slots[i].ItemId != itemId) continue;
                var slot = _slots[i];
                int take = Mathf.Min(slot.Count, amount - taken);
                slot.Count -= take;
                taken += take;
                if (slot.Count <= 0) _slots.RemoveAt(i);
                else _slots[i] = slot;
            }
            return taken;
        }

        public override void OnNetworkSpawn()
        {
            // Starting reserve: enough to fight, scarce enough that looting matters.
            if (IsServer)
            {
                ServerAddItem((int)ItemType.Ammo556, 60);
                ServerAddItem((int)ItemType.Ammo9mm, 30);
            }
        }

        /// Server-side. Returns false if nothing could be added (inventory full).
        public bool ServerAddItem(int itemId, int amount)
        {
            if (!IsServer || amount <= 0) return false;
            var info = Items.Get(itemId);
            int remaining = amount;

            for (int i = 0; i < _slots.Count && remaining > 0; i++)
            {
                if (_slots[i].ItemId != itemId || _slots[i].Count >= info.MaxStack) continue;
                var slot = _slots[i];
                int add = Mathf.Min(info.MaxStack - slot.Count, remaining);
                slot.Count += add;
                _slots[i] = slot;
                remaining -= add;
            }
            while (remaining > 0 && _slots.Count < MaxSlots)
            {
                int add = Mathf.Min(info.MaxStack, remaining);
                _slots.Add(new Slot { ItemId = itemId, Count = add });
                remaining -= add;
            }
            return remaining < amount;
        }

        [ServerRpc]
        public void UseItemServerRpc(int index)
        {
            if (index < 0 || index >= _slots.Count) return;
            var slot = _slots[index];
            bool consumed = false;

            switch ((ItemType)slot.ItemId)
            {
                case ItemType.Medkit:
                    if (TryGetComponent<Health>(out var health) && health.CurrentHealth < health.MaxHealth)
                    {
                        health.ServerHeal(50f);
                        consumed = true;
                    }
                    break;
                // Ammo items are not "used" — reloading consumes them.
            }

            if (!consumed) return;
            slot.Count--;
            if (slot.Count <= 0) _slots.RemoveAt(index);
            else _slots[index] = slot;
        }
    }
}
