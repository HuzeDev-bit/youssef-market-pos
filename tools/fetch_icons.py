"""Pull icons from Lucide (MIT licence) and convert them to WPF Geometry data.

Lucide is drawn on a 24x24 grid with round caps and joins, which is exactly the
language the rest of this UI already uses - soft, rounded, even stroke weight.
Hand-drawn paths were never going to match that consistency.
"""
import io, json, math, os, re, urllib.request, xml.etree.ElementTree as ET

UA = {"User-Agent": "MarketPOS/1.0"}
BASE = "https://raw.githubusercontent.com/lucide-icons/lucide/main/icons/"

# WPF resource key -> lucide icon name
WANTED = {
    "Icon.Cart":     "shopping-cart",
    "Icon.Bag":      "shopping-bag",
    "Icon.Receipt":  "receipt",
    "Icon.Power":    "power",
    "Icon.Search":   "search",
    "Icon.Settings": "settings",
    "Icon.Minimize": "minus",
    "Icon.Close":    "x",
    "Icon.Plus":     "plus",
    "Icon.Minus":    "minus",
    "Icon.Back":     "arrow-left",
    "Icon.Pause":    "pause",
    "Icon.User":     "user",
    "Icon.Check":    "check",
    "Icon.Banknote": "banknote",
    "Icon.Wallet":   "wallet",
    "Icon.Coins":    "hand-coins",
    "Icon.Store":    "store",
    "Icon.Box":      "package",
    "Icon.Chart":    "chart-column",
    "Icon.Cash":     "banknote",
    "Icon.Lock":     "lock",

    # -- admin dashboard --
    "Icon.Dashboard":  "layout-dashboard",
    "Icon.Layers":     "layers",
    "Icon.Warehouse":  "warehouse",
    "Icon.Truck":      "truck",
    "Icon.Inbox":      "package-plus",
    "Icon.Users":      "users",
    "Icon.HandCoins":  "hand-coins",
    "Icon.Trending":   "trending-up",
    "Icon.Bell":       "bell",
    "Icon.History":    "clock-fading",
    "Icon.Report":     "file-text",
    "Icon.Alert":      "triangle-alert",
    "Icon.Calendar":   "calendar",
    "Icon.Download":   "download",
    "Icon.Edit":       "pencil",
    "Icon.Trash":      "trash-2",
    "Icon.ChevronDown":"chevron-down",
    "Icon.ChevronRight":"chevron-right",
    "Icon.Filter":     "list-filter",
    "Icon.Broken":     "package-x",
    "Icon.Clock":      "clock",
    "Icon.Refresh":    "refresh-cw",
    "Icon.ArrowUp":    "arrow-up-right",
    "Icon.ArrowDown":  "arrow-down-right",
}


LEADING_M = re.compile(r"^m\s*(-?[\d.]+)[,\s]+(-?[\d.]+)\s*(.*)$", re.S)

def absolutise_start(d):
    """Make a path's opening moveto absolute without changing anything after it.

    SVG treats a leading lowercase m as ABSOLUTE, but only the moveto itself - the
    coordinate pairs that follow are still relative linetos. Concatenating several
    <path> elements into one WPF geometry breaks that rule, so the opening m has to
    become M while the trailing pairs stay relative via an explicit l.
    """
    if not d.startswith("m"):
        return d
    match = LEADING_M.match(d)
    if not match:
        return "M" + d[1:]
    x, y, rest = match.group(1), match.group(2), match.group(3).strip()
    if rest and (rest[0].isdigit() or rest[0] in "-."):
        return f"M{x} {y} l{rest}"      # implicit pairs were relative linetos
    return f"M{x} {y} {rest}".strip()   # an explicit command follows, leave it alone

def num(v):
    return float(v)

def circle_to_path(cx, cy, r):
    # two half arcs: WPF has no circle primitive inside path data
    return (f"M {cx-r} {cy} A {r} {r} 0 1 0 {cx+r} {cy} "
            f"A {r} {r} 0 1 0 {cx-r} {cy} Z")

def rect_to_path(x, y, w, h, rx):
    if rx <= 0:
        return f"M {x} {y} H {x+w} V {y+h} H {x} Z"
    return (f"M {x+rx} {y} H {x+w-rx} A {rx} {rx} 0 0 1 {x+w} {y+rx} "
            f"V {y+h-rx} A {rx} {rx} 0 0 1 {x+w-rx} {y+h} "
            f"H {x+rx} A {rx} {rx} 0 0 1 {x} {y+h-rx} "
            f"V {y+rx} A {rx} {rx} 0 0 1 {x+rx} {y} Z")

def points_to_path(points, close=False):
    nums = [p for p in re.split(r"[ ,\s]+", points.strip()) if p]
    pairs = [(nums[i], nums[i+1]) for i in range(0, len(nums) - 1, 2)]
    d = "M " + " L ".join(f"{x} {y}" for x, y in pairs)
    return d + (" Z" if close else "")

def svg_to_geometry(svg):
    root = ET.fromstring(svg)
    parts = []
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        a = el.attrib
        if tag == "path" and "d" in a:
            parts.append(absolutise_start(a["d"].strip()))
        elif tag == "circle":
            parts.append(circle_to_path(num(a["cx"]), num(a["cy"]), num(a["r"])))
        elif tag == "line":
            parts.append(f"M {a['x1']} {a['y1']} L {a['x2']} {a['y2']}")
        elif tag == "rect":
            parts.append(rect_to_path(num(a["x"]), num(a["y"]), num(a["width"]),
                                      num(a["height"]), num(a.get("rx", 0))))
        elif tag == "polyline":
            parts.append(points_to_path(a["points"]))
        elif tag == "polygon":
            parts.append(points_to_path(a["points"], close=True))
    return " ".join(parts)

geoms = {}
cache = {}
for key, name in WANTED.items():
    if name not in cache:
        with urllib.request.urlopen(urllib.request.Request(BASE + name + ".svg", headers=UA), timeout=30) as r:
            cache[name] = r.read().decode("utf-8")
        print(f"fetched {name}", flush=True)
    geoms[key] = (name, svg_to_geometry(cache[name]))

out = io.StringIO()
out.write('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n')
out.write('                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n\n')
out.write("    <!-- ===================== Icon geometry =====================\n")
out.write("         Icons from Lucide (https://lucide.dev), MIT licence - see Assets/ICONS-LICENSE.txt.\n")
out.write("         Drawn on a 24x24 grid with round caps and joins, which matches the rounded,\n")
out.write("         even-weight language used everywhere else in this UI. Circles, rects and\n")
out.write("         polylines were converted to path data because WPF Geometry has no such\n")
out.write("         primitives; the coordinates themselves are untouched. -->\n\n")
for key, (name, d) in geoms.items():
    out.write(f'    <!-- lucide: {name} -->\n')
    out.write(f'    <Geometry x:Key="{key}">{d}</Geometry>\n\n')

out.write('''    <!-- Stroke follows the parent control's Foreground, so an icon tints with hover
         and active state without any extra binding at the call site. -->
    <Style x:Key="Icon" TargetType="Path">
        <Setter Property="Stroke" Value="{Binding (TextElement.Foreground), RelativeSource={RelativeSource Self}}"/>
        <Setter Property="StrokeThickness" Value="1.9"/>
        <Setter Property="StrokeStartLineCap" Value="Round"/>
        <Setter Property="StrokeEndLineCap" Value="Round"/>
        <Setter Property="StrokeLineJoin" Value="Round"/>
        <Setter Property="Fill" Value="{x:Null}"/>
        <Setter Property="Stretch" Value="Uniform"/>
        <Setter Property="Width" Value="21"/>
        <Setter Property="Height" Value="21"/>
        <Setter Property="HorizontalAlignment" Value="Center"/>
        <Setter Property="VerticalAlignment" Value="Center"/>
    </Style>

    <Style x:Key="Icon.Small" TargetType="Path" BasedOn="{StaticResource Icon}">
        <Setter Property="Width" Value="15"/>
        <Setter Property="Height" Value="15"/>
        <Setter Property="StrokeThickness" Value="2.1"/>
    </Style>

</ResourceDictionary>
''')
io.open("Theme/Icons.xaml", "w", encoding="utf-8").write(out.getvalue())
print("\nwrote Theme/Icons.xaml with", len(geoms), "icons")
