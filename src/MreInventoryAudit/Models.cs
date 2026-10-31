using System;
using System.Collections.Generic;

namespace MreInventoryAudit
{
    public sealed class StoredItem
    {
        public ushort Id;
        public byte Amount, Quality, Page, X, Y, Rotation;
        public string WornSlot;
        public byte[] State;
        public StoredItem Copy() { return new StoredItem { Id=Id, Amount=Amount, Quality=Quality, Page=Page, X=X, Y=Y, Rotation=Rotation, WornSlot=WornSlot, State=(byte[])State.Clone() }; }
    }
    public sealed class DeathSnapshot
    {
        public string Id, Status;
        public ulong PlayerId, RestoredBy;
        public DateTime AtUtc;
        public List<StoredItem> Items = new List<StoredItem>();
        public DeathSnapshot Copy() { var copy=new DeathSnapshot { Id=Id,Status=Status,PlayerId=PlayerId,RestoredBy=RestoredBy,AtUtc=AtUtc }; foreach(var item in Items)copy.Items.Add(item.Copy());return copy; }
    }
    public sealed class InventoryItemView
    {
        public ushort Id { get; internal set; }
        public string Name { get; internal set; }
        public byte Amount { get; internal set; }
        public byte Quality { get; internal set; }
        public byte Page { get; internal set; }
        public byte X { get; internal set; }
        public byte Y { get; internal set; }
        public byte Rotation { get; internal set; }
        public string WornSlot { get; internal set; }
    }
    public sealed class InventoryView
    {
        public ulong PlayerId { get; internal set; }
        public string SnapshotId { get; internal set; }
        public DateTime AtUtc { get; internal set; }
        public string RestoreStatus { get; internal set; }
        public InventoryItemView[] Items { get; internal set; }
    }
    public sealed class RestorePreview
    {
        public string Token { get; internal set; }
        public DateTime ExpiresUtc { get; internal set; }
        public InventoryView Inventory { get; internal set; }
    }
}
