namespace Scrunch;

// Physical desktop pixels throughout, including negative monitor coordinates.
public readonly record struct DesktopRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public static class TrayPlacement
{
    public static DesktopRect Clamp(DesktopRect popup, DesktopRect work)
    {
        int width = Math.Min(popup.Width, work.Width), height = Math.Min(popup.Height, work.Height);
        int x = Math.Clamp(popup.Left, work.Left, work.Right - width);
        int y = Math.Clamp(popup.Top, work.Top, work.Bottom - height);
        return new(x, y, x + width, y + height);
    }

    // Fallback also handles top/left/right taskbars and overflow-area icons.
    public static DesktopRect Place(DesktopRect icon, DesktopRect work, int width, int height)
    {
        int cx = icon.Left + icon.Width / 2, cy = icon.Top + icon.Height / 2;
        int[] distances = [Math.Abs(cy - work.Bottom), Math.Abs(cy - work.Top),
            Math.Abs(cx - work.Left), Math.Abs(cx - work.Right)];
        int edge = Array.IndexOf(distances, distances.Min());
        var rect = edge switch
        {
            0 => new DesktopRect(icon.Right - width, icon.Top - height, icon.Right, icon.Top),
            1 => new DesktopRect(icon.Right - width, icon.Bottom, icon.Right, icon.Bottom + height),
            2 => new DesktopRect(icon.Right, cy - height / 2, icon.Right + width, cy + height / 2 + height % 2),
            _ => new DesktopRect(icon.Left - width, cy - height / 2, icon.Left, cy + height / 2 + height % 2)
        };
        return Clamp(rect, work);
    }
}
