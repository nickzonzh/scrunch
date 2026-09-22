using Scrunch;

int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++; Console.WriteLine("PASS: " + label);
}
Check(TrayIdentity.ForExecutable("Scrunch.exe") == TrayIdentity.ForExecutable("scrunch.exe"), "Tray identity is stable across restarts and Windows path case");
Check(TrayIdentity.ForExecutable("Scrunch.exe") != TrayIdentity.ForExecutable("moved/Scrunch.exe"), "Moved unsigned binaries receive a separate Explorer registration");
var work = new DesktopRect(0, 0, 1920, 1040);
var bottom = TrayPlacement.Place(new(1800,1040,1824,1080), work, 400, 500);
Check(bottom.Bottom <= 1040 && bottom.Right == 1824, "Bottom tray opens above icon");
var top = TrayPlacement.Place(new(1700,0,1724,40), new(0,40,1920,1080),400,500);
Check(top.Top == 40, "Top tray opens below icon");
var left = TrayPlacement.Place(new(0,900,40,924),new(40,0,1920,1080),400,500);
Check(left.Left == 40 && left.Bottom <= 1080, "Left tray opens right and clamps vertically");
var right = TrayPlacement.Place(new(1880,900,1920,924),new(0,0,1880,1080),400,500);
Check(right.Right == 1880 && right.Bottom <= 1080, "Right tray opens left and clamps vertically");
var negative = TrayPlacement.Place(new(-140, -40, -116, 0), new(-1920,-1080,0,-40),600,750);
Check(negative.Left < 0 && negative.Bottom <= -40, "Secondary display supports negative X and Y");
var oversized = TrayPlacement.Clamp(new(-40,-40,800,900), new(0,0,320,240));
Check(oversized == new DesktopRect(0,0,320,240), "Oversized popup is bounded to a small work area");
var random = new Random(42);
for (int i=0;i<10000;i++)
{
    int x=random.Next(-8000,8000), y=random.Next(-4000,4000), w=random.Next(320,8000),h=random.Next(240,4000);
    var area=new DesktopRect(x,y,x+w,y+h);
    var placed=TrayPlacement.Place(new(x+random.Next(w),y+random.Next(h),x+w,y+h),area,random.Next(200,1400),random.Next(180,1800));
    if(placed.Left < area.Left || placed.Right > area.Right || placed.Top < area.Top || placed.Bottom > area.Bottom || placed.Width <= 0 || placed.Height <= 0)
        throw new Exception("Clamping invariant failed at " + i);
}
Check(true,"10,000 deterministic placements stay inside varied work areas and scales");
Console.WriteLine($"{checks} tray positioning checks passed.");
