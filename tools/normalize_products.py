import io, json, os, time, urllib.parse, urllib.request
from PIL import Image

OUT = os.path.join(os.environ["APPDATA"], "MarketPos", "Images")
os.makedirs(OUT, exist_ok=True)
UA = {"User-Agent": "MarketPOS/1.0 (local retail app)"}
CANVAS, FILL = 512, 0.92

PRODUCTS = [
 ("6111234500011","Baguette",          ["baguette isolated","baguette bread white background","baguette bread"]),
 ("6111234500028","Khobz Bread",       ["bread loaf isolated","round bread loaf white background","khobz moroccan bread"]),
 ("6111234500035","Croissant",         ["croissant isolated","croissant white background","croissant"]),
 ("2000000000017","Tomatoes",          ["tomato isolated white background","tomato closeup","tomato"]),
 ("2000000000024","Onions",            ["onion isolated white background","onions closeup","onion bulb"]),
 ("2000000000031","Potatoes",          ["potato isolated white background","potatoes closeup","potato tuber"]),
 ("2000000000048","Bananas",           ["banana isolated white background","bananas bunch closeup","banana"]),
 ("2000000000055","Apples",            ["apple isolated white background","red apple closeup","apple fruit"]),
 ("6111234500042","Milk 1L",           ["milk carton isolated","milk bottle white background","milk carton"]),
 ("6111234500059","Yaourt Pack x8",    ["yogurt pot isolated","yoghurt cup white background","yogurt cup"]),
 ("6111234500066","Jibli Cheese",      ["cheese isolated white background","cheese wedge closeup","cheese wedge"]),
 ("6111234500073","Butter 250g",       ["butter isolated white background","butter block closeup","butter"]),
 ("6111234500080","Olive Oil 1L",      ["olive oil bottle isolated","olive oil bottle white background","olive oil bottle"]),
 ("6111234500097","Couscous 1kg",      ["couscous isolated","couscous closeup","couscous"]),
 ("6111234500103","Sugar 1kg",         ["sugar cubes isolated","white sugar closeup","sugar pile"]),
 ("6111234500110","Tea Gunpowder",     ["green tea leaves closeup","gunpowder tea","tea leaves"]),
 ("6111234500127","Pasta 500g",        ["pasta isolated white background","penne pasta closeup","pasta"]),
 ("6111234500134","Sidi Ali 1.5L",     ["water bottle isolated white background","plastic water bottle isolated","mineral water bottle"]),
 ("6111234500141","Coca-Cola 1.5L",    ["Coca-Cola bottle isolated","coca cola plastic bottle","Coca-Cola bottle"]),
 ("6111234500158","Orange Juice 1L",   ["orange juice carton isolated","orange juice bottle white background","orange juice glass"]),
 ("6111234500165","Dish Soap",         ["dishwashing liquid bottle isolated","detergent bottle white background","dishwashing liquid"]),
 ("6111234500172","Laundry Powder 3kg",["detergent box isolated","washing powder box white background","laundry detergent"]),
 ("6111234500189","Tissue Box",        ["toilet paper roll isolated white background","paper towel roll closeup","toilet paper roll"]),
]

def search(term, retries=4):
    q = urllib.parse.urlencode({"action":"query","format":"json","generator":"search",
        "gsrsearch": "filetype:bitmap " + term, "gsrnamespace":"6","gsrlimit":"8",
        "prop":"imageinfo","iiprop":"url|extmetadata","iiurlwidth":"800"})
    for attempt in range(retries):
        try:
            req = urllib.request.Request("https://commons.wikimedia.org/w/api.php?" + q, headers=UA)
            with urllib.request.urlopen(req, timeout=30) as r:
                data = json.load(r)
            out = []
            for p in ((data.get("query") or {}).get("pages") or {}).values():
                ii = (p.get("imageinfo") or [{}])[0]
                if not ii.get("thumburl"): continue
                m = ii.get("extmetadata") or {}
                out.append({"title": p.get("title",""), "thumb": ii["thumburl"],
                            "page": ii.get("descriptionurl",""),
                            "license": (m.get("LicenseShortName") or {}).get("value","unknown")})
            return out
        except Exception:
            time.sleep(5 * (attempt + 1))
    return []

def strip_background(im):
    im = im.convert("RGBA"); px = im.load(); w, h = im.size
    step_x = max(1, w // 24); step_y = max(1, h // 24)
    border = ([px[x,0] for x in range(0,w,step_x)] + [px[x,h-1] for x in range(0,w,step_x)] +
              [px[0,y] for y in range(0,h,step_y)] + [px[w-1,y] for y in range(0,h,step_y)])
    ar = sum(c[0] for c in border)//len(border)
    ag = sum(c[1] for c in border)//len(border)
    ab = sum(c[2] for c in border)//len(border)
    spread = max(max(abs(c[0]-ar), abs(c[1]-ag), abs(c[2]-ab)) for c in border)
    if spread > 46:
        return im, False
    tol = 60
    seen = set()
    st = ([(x,0) for x in range(0,w,3)] + [(x,h-1) for x in range(0,w,3)] +
          [(0,y) for y in range(0,h,3)] + [(w-1,y) for y in range(0,h,3)])
    while st:
        x, y = st.pop()
        if not (0 <= x < w and 0 <= y < h) or (x,y) in seen: continue
        r, g, b, a = px[x,y]
        if abs(r-ar) > tol or abs(g-ag) > tol or abs(b-ab) > tol: continue
        seen.add((x,y)); px[x,y] = (r,g,b,0)
        st += [(x+1,y),(x-1,y),(x,y+1),(x,y-1)]
    return im, True

def score(im):
    bb = im.getbbox()
    if not bb: return 0.0, None
    crop = im.crop(bb); w, h = crop.size
    if w < 60 or h < 60: return 0.0, None
    if max(w,h) / float(min(w,h)) > 4.2: return 0.0, None
    alpha = crop.getchannel("A")
    opaque = sum(1 for v in alpha.getdata() if v > 25)
    return opaque / float(w*h), crop

def normalize(crop):
    w, h = crop.size
    scale = (CANVAS * FILL) / float(max(w,h))
    cov, _ = score(crop)
    if cov < 0.42:
        scale *= min(1.22, (0.42 / max(cov, 0.18)) ** 0.5)
    nw, nh = max(1,int(w*scale)), max(1,int(h*scale))
    if nw > CANVAS: nh = int(nh * CANVAS / nw); nw = CANVAS
    if nh > CANVAS: nw = int(nw * CANVAS / nh); nh = CANVAS
    crop = crop.resize((nw,nh), Image.LANCZOS)
    canvas = Image.new("RGBA", (CANVAS,CANVAS), (0,0,0,0))
    canvas.paste(crop, ((CANVAS-nw)//2, (CANVAS-nh)//2), crop)
    return canvas

credits = []
for barcode, name, queries in PRODUCTS:
    best = None; best_score = 0.0; best_meta = None
    for term in queries:
        for hit in search(term):
            try:
                req = urllib.request.Request(hit["thumb"], headers=UA)
                with urllib.request.urlopen(req, timeout=40) as r:
                    im = Image.open(io.BytesIO(r.read()))
                im.thumbnail((800,800), Image.LANCZOS)
                im, _ = strip_background(im)
                sc, crop = score(im)
                if crop is not None and sc > best_score:
                    best_score, best, best_meta = sc, crop, hit
            except Exception:
                continue
        if best_score >= 0.55: break
        time.sleep(1.2)
    if best is None:
        print("FAIL " + name, flush=True); continue
    normalize(best).save(os.path.join(OUT, barcode + ".png"), "PNG", optimize=True)
    credits.append(name + " (" + barcode + ") | " + best_meta["title"] + " | " + best_meta["license"] + " | " + best_meta["page"])
    print("OK   %-22s fill=%.2f  %s" % (name, best_score, best_meta["license"]), flush=True)
    time.sleep(1.0)

with io.open(os.path.join(OUT, "CREDITS.txt"), "w", encoding="utf-8") as f:
    f.write("Product photo sources (Wikimedia Commons)\nReview each licence before commercial use.\n\n")
    f.write("\n".join(credits) + "\n")
print("DONE", flush=True)
