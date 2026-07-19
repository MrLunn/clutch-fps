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
                case ItemType.AmmoPack:
                    if (TryGetComponent<PlayerWeaponController>(out var weapons))
                    {
                        consumed = weapons.ServerRefillAllAmmo();
                    }
                    break;
            }

            if (!consumed) return;
            slot.Count--;
            if (slot.Count <= 0) _slots.RemoveAt(index);
            else _slots[index] = slot;
        }
    }
}
