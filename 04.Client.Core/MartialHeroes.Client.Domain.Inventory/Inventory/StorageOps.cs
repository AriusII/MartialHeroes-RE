namespace MartialHeroes.Client.Domain.Inventory.Inventory;

public static class StorageOps
{
    public const int WidgetActionIdOffset = 7;

    public static byte OpFromWidgetActionId(int widgetActionId)
    {
        var op = widgetActionId - WidgetActionIdOffset;
        if (op is < 0 or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(widgetActionId), widgetActionId,
                "Storage op must fall within 0..255 after subtracting the widget-action-id offset.");

        return (byte)op;
    }
}
