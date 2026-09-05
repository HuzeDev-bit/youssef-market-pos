import io, json, os, time, urllib.parse, urllib.request
from PIL import Image

OUT = os.path.join(os.environ["APPDATA"], "MarketPos", "Images")
os.makedirs(OUT, exist_ok=True)
UA = {"User-Agent": "MarketPOS/1.0 (local retail app)"}
CANVAS, FILL = 512, 0.94

PRODUCTS = [
 # barcode, name, search terms, REQUIRED title keywords (relevance gate)
 ("6111234500011","Baguette",          ["baguette bread"],            ["baguette"]),
 ("6111234500028","Khobz Bread",       ["bread loaf","bread"],        ["bread","pain","loaf"]),
 ("6111234500035","Croissant",         ["croissant"],                 ["croissant"]),
 ("2000000000017","Tomatoes",          ["tomato"],                    ["tomato","tomate"]),
 ("2000000000024","Onions",            ["onion bulb","onion"],        ["onion","oignon","allium"]),
 ("2000000000031","Potatoes",          ["potato raw","potato"],       ["potato","pomme de terre","kartoffel"]),
 ("2000000000048","Bananas",           ["banana"],                    ["banana","banane"]),
 ("2000000000055","Apples",            ["apple fruit","apple"],       ["apple","pomme","apfel"]),
 ("6111234500042","Milk 1L",           ["milk carton","milk bottle"], ["milk","lait"]),
 ("6111234500059","Yaourt Pack x8",    ["yogurt","yoghurt"],          ["yogurt","yoghurt","yaourt","joghurt"]),
 ("6111234500066","Jibli Cheese",      ["cheese"],                    ["cheese","fromage","kase"]),
 ("6111234500073","Butter 250g",       ["butter"],                    ["butter","beurre"]),
 ("6111234500080","Olive Oil 1L",      ["olive oil bottle","olive oil"], ["olive"]),
 ("6111234500097","Couscous 1kg",      ["couscous"],                  ["couscous","semolina"]),
 ("6111234500103","Sugar 1kg",         ["sugar cube","sugar"],        ["sugar","sucre","zucker","sokeri"]),
 ("6111234500110","Tea Gunpowder",     ["tea leaves","green tea"],    ["tea","the","thee"]),
 ("6111234500127","Pasta 500g",        ["pasta penne","pasta"],       ["pasta","penne","macaroni","spaghetti"]),
 ("6111234500134","Sidi Ali 1.5L",     ["water bottle plastic"],      ["water bottle","bottle of water","plastic bottle","mineral water"]),
 ("6111234500141","Coca-Cola 1.5L",    ["Coca-Cola bottle"],          ["coca-cola bottle","coke bottle","cola bottle"]),
 ("6111234500158","Orange Juice 1L",   ["orange juice"],              ["orange juice","jus d orange"]),
 ("6111234500165","Dish Soap",         ["dishwashing liquid","detergent bottle"], ["dish","detergent","washing-up","liquide vaisselle"]),
 ("6111234500172","Laundry Powder 3kg",["detergent box","washing powder"], ["detergent","washing powder","soap powder","lessive"]),
 ("6111234500189","Tissue Box",        ["toilet paper roll","paper towel"], ["toilet paper","paper towel","tissue"]),
]

def search(raw, limit=10):
    q = urllib.parse.urlencode({"action":"query","format":"json","generator":"search",
        "gsrsearch": raw, "gsrnamespace":"6","gsrlimit":str(limit),
        "prop":"imageinfo","iiprop":"url|extmetadata","iiurlwidth":"800"})
    for attempt in range(4):
        try:
            req = urllib.request.Request("https://commons.wikimedia.org/w/api.php?"+q, headers=UA)
            with urllib.request.urlopen(req, timeout=30) as r:
                data = json.load(r)
            out=[]
            for p in ((data.get("query") or {}).get("pages") or {}).values():
                ii=(p.get("imageinfo") or [{}])[0]
                if not ii.get("thumburl"): continue
                m=ii.get("extmetadata") or {}
                out.append({"title":p.get("title",""),"thumb":ii["thumburl"],
                            "page":ii.get("descriptionurl",""),
                            "license":(m.get("LicenseShortName") or {}).get("value","unknown")})
            return out
        except Exception:
            time.sleep(4*(attempt+1))
    return []

def has_alpha(im):
    if im.mode != "RGBA": return False
    lo, hi = im.getchannel("A").getextrema()
    return lo < 240

def strip_background(im):
    im = im.convert("RGBA"); px = im.load(); w,h = im.size
    sx, sy = max(1,w//24), max(1,h//24)
    border = ([px[x,0] for x in range(0,w,sx)] + [px[x,h-1] for x in range(0,w,sx)] +
              [px[0,y] for y in range(0,h,sy)] + [px[w-1,y] for y in range(0,h,sy)])
    ar = sum(c[0] for c in border)//len(border)
    ag = sum(c[1] for c in border)//len(border)
    ab = sum(c[2] for c in border)//len(border)
    if max(max(abs(c[0]-ar),abs(c[1]-ag),abs(c[2]-ab)) for c in border) > 40:
        return None
    if ar < 150 or ag < 150 or ab < 150:
        return None
    tol = 62
    seen=set()
    st = ([(x,0) for x in range(0,w,2)] + [(x,h-1) for x in range(0,w,2)] +
          [(0,y) for y in range(0,h,2)] + [(w-1,y) for y in range(0,h,2)])
    while st:
        x,y = st.pop()
        if not(0<=x<w and 0<=y<h) or (x,y) in seen: continue
        r,g,b,a = px[x,y]
        if abs(r-ar)>tol or abs(g-ag)>tol or abs(b-ab)>tol: continue
        seen.add((x,y)); px[x,y]=(r,g,b,0)
        st += [(x+1,y),(x-1,y),(x,y+1),(x,y-1)]
    return im

def coverage(crop):
    w,h = crop.size
    a = crop.getchannel("A")
    return sum(1 for v in a.getdata() if v > 25) / float(w*h)

def evaluate(im):
    src = im.convert("RGBA")
    cut = src if has_alpha(src) else strip_background(src)
    if cut is None: return None
    bb = cut.getbbox()
    if not bb: return None
    crop = cut.crop(bb); w,h = crop.size
    if w < 70 or h < 70: return None
    if max(w,h)/float(min(w,h)) > 3.6: return None
    cov = coverage(crop)
    if cov > 0.97 or cov < 0.16: return None
    return crop, cov

def normalize(crop):
    w,h = crop.size
    scale = (CANVAS*FILL)/float(max(w,h))
    nw,nh = max(1,int(w*scale)), max(1,int(h*scale))
    crop = crop.resize((nw,nh), Image.LANCZOS)
    canvas = Image.new("RGBA",(CANVAS,CANVAS),(0,0,0,0))
    canvas.paste(crop, ((CANVAS-nw)//2,(CANVAS-nh)//2), crop)
    return canvas

credits=[]; results=[]
for barcode,name,terms,must in PRODUCTS:
    best=None; best_cov=0; best_meta=None
    plans=[]
    for t in terms:
        plans.append("filemime:image/png " + t)
        plans.append("filetype:bitmap " + t + " white background")
        plans.append("filetype:bitmap " + t)
    for raw in plans:
        for hit in search(raw):
            title = hit["title"].lower().replace("_"," ")
            if not any(k in title for k in must):
                continue                      # image must actually depict the product
            try:
                req = urllib.request.Request(hit["thumb"], headers=UA)
                with urllib.request.urlopen(req, timeout=40) as r:
                    im = Image.open(io.BytesIO(r.read()))
                im.thumbnail((900,900), Image.LANCZOS)
                res = evaluate(im)
                if res and res[1] > best_cov:
                    best, best_cov, best_meta = res[0], res[1], hit
            except Exception:
                continue
        if best_cov >= 0.45: break
        time.sleep(0.8)
    if best is None:
        results.append((barcode,name,0.0,"NONE")); print("FAIL " + name, flush=True); continue
    normalize(best).save(os.path.join(OUT,barcode+".png"),"PNG",optimize=True)
    credits.append(name+" ("+barcode+") | "+best_meta["title"]+" | "+best_meta["license"]+" | "+best_meta["page"])
    results.append((barcode,name,best_cov,best_meta["title"]))
    print("OK   %-22s cov=%.2f  %s" % (name,best_cov,best_meta["title"][:52]), flush=True)
    time.sleep(0.6)

with io.open(os.path.join(OUT,"CREDITS.txt"),"w",encoding="utf-8") as f:
    f.write("Product photo sources (Wikimedia Commons)\n\n")
    f.write("\n".join(credits)+"\n")

cols, cell = 6, 190
rows = (len(PRODUCTS)+cols-1)//cols
sheet = Image.new("RGB",(cols*cell, rows*cell),(241,246,243))
for i,(barcode,name,cov,_) in enumerate(results):
    p = os.path.join(OUT,barcode+".png")
    if not os.path.exists(p): continue
    im = Image.open(p).convert("RGBA"); im.thumbnail((int(cell*0.86),int(cell*0.86)), Image.LANCZOS)
    x = (i%cols)*cell + (cell-im.size[0])//2
    y = (i//cols)*cell + (cell-im.size[1])//2
    sheet.paste(im,(x,y),im)
sheet.save(os.path.join(OUT,"_contact_sheet.png"))
print("DONE", flush=True)
