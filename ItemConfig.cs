namespace SandboxServer;

/// <summary>
/// Temporary item/inventory/world-drop knobs. Not final game balance.
/// </summary>
internal static class ItemConfig
{
    public const int FistSlot = 0;
    public const int WrenchSlot = 1;
    public const int ToolSlotCount = 2;
    public const int CollectibleSlotCount = 30;
    public const int FirstCollectibleSlot = 2;
    public const int InventoryCapacity = ToolSlotCount + CollectibleSlotCount;
    public const int DefaultMaxStack = 200;
    public const float WorldItemMergeRadius = 0.5f;
    public const float PickupInflate = 0.15f;
    public const float DropInsetMin = 0.35f;
    public const float DropInsetMax = 0.65f;
}
