using Xunit;

namespace Scrunch.Tests;

// Tray identity and popup placement in physical desktop pixels, including the
// negative coordinates a secondary display produces.
public sealed class TrayTests
{
    [Fact]
    public void TrayIdentityIsStableAcrossRestartsAndWindowsPathCase()
        => Assert.Equal(TrayIdentity.ForExecutable("Scrunch.exe"), TrayIdentity.ForExecutable("scrunch.exe"));

    [Fact]
    public void MovedUnsignedBinariesReceiveASeparateExplorerRegistration()
        => Assert.NotEqual(TrayIdentity.ForExecutable("Scrunch.exe"), TrayIdentity.ForExecutable("moved/Scrunch.exe"));

    [Fact]
    public void BottomTrayOpensAboveTheIcon()
    {
        var placed = TrayPlacement.Place(new(1800, 1040, 1824, 1080), new(0, 0, 1920, 1040), 400, 500);
        Assert.True(placed.Bottom <= 1040, "Bottom tray opens above icon");
        Assert.Equal(1824, placed.Right);
    }

    [Fact]
    public void TopTrayOpensBelowTheIcon()
    {
        var placed = TrayPlacement.Place(new(1700, 0, 1724, 40), new(0, 40, 1920, 1080), 400, 500);
        Assert.Equal(40, placed.Top);
    }

    [Fact]
    public void LeftTrayOpensRightAndClampsVertically()
    {
        var placed = TrayPlacement.Place(new(0, 900, 40, 924), new(40, 0, 1920, 1080), 400, 500);
        Assert.Equal(40, placed.Left);
        Assert.True(placed.Bottom <= 1080, "Left tray clamps vertically");
    }

    [Fact]
    public void RightTrayOpensLeftAndClampsVertically()
    {
        var placed = TrayPlacement.Place(new(1880, 900, 1920, 924), new(0, 0, 1880, 1080), 400, 500);
        Assert.Equal(1880, placed.Right);
        Assert.True(placed.Bottom <= 1080, "Right tray clamps vertically");
    }

    [Fact]
    public void SecondaryDisplaysSupportNegativeCoordinates()
    {
        var placed = TrayPlacement.Place(new(-140, -40, -116, 0), new(-1920, -1080, 0, -40), 600, 750);
        Assert.True(placed.Left < 0, "Secondary display keeps a negative X");
        Assert.True(placed.Bottom <= -40, "Secondary display keeps a negative Y");
    }

    [Fact]
    public void AnOversizedPopupIsBoundedToASmallWorkArea()
        => Assert.Equal(new DesktopRect(0, 0, 320, 240), TrayPlacement.Clamp(new(-40, -40, 800, 900), new(0, 0, 320, 240)));

    [Fact]
    public void EveryPlacementStaysInsideItsWorkAreaWithAPositiveSize()
    {
        var random = new Random(42);
        for (int i = 0; i < 10000; i++)
        {
            int x = random.Next(-8000, 8000), y = random.Next(-4000, 4000);
            int w = random.Next(320, 8000), h = random.Next(240, 4000);
            var area = new DesktopRect(x, y, x + w, y + h);
            var placed = TrayPlacement.Place(new(x + random.Next(w), y + random.Next(h), x + w, y + h),
                area, random.Next(200, 1400), random.Next(180, 1800));
            if (placed.Left < area.Left || placed.Right > area.Right || placed.Top < area.Top ||
                placed.Bottom > area.Bottom || placed.Width <= 0 || placed.Height <= 0)
                Assert.Fail($"Clamping invariant failed at {i}: {placed} in {area}");
        }
    }
}
