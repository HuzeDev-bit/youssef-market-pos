"""Normalise whatever images are in the folder so every one fits its card identically.
Runs offline on existing files - re-run it any time new photos are dropped in."""
import os
from PIL import Image

OUT = os.path.join(os.environ["APPDATA"], "MarketPos", "Images")
CANVAS, FILL = 512, 0.94

def strip_uniform_border(im):
    px = im.load(); w,h = im.size
    sx, sy = max(1,w//24), max(1,h//24)
    border = ([px[x,0] for x in range(0,w,sx)] + [px[x,h-1] for x in range(0,w,sx)] +
              [px[0,y] for y in range(0,h,sy)] + [px[w-1,y] for y in range(0,h,sy)])
    ar = sum(c[0] for c in border)//len(border)
    ag = sum(c[1] for c in border)//len(border)
    ab = sum(c[2] for c in border)//len(border)
    if max(max(abs(c[0]-ar),abs(c[1]-ag),abs(c[2]-ab)) for c in border) > 42: return im
    if ar < 145 or ag < 145 or ab < 145: return im
    tol, seen = 64, set()
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

done = 0
for f in sorted(os.listdir(OUT)):
    if not f.lower().endswith((".png",".jpg",".jpeg",".webp")) or f.startswith("_"): continue
    path = os.path.join(OUT, f)
    im = Image.open(path).convert("RGBA")
    lo, _ = im.getchannel("A").getextrema()
    if lo >= 240:                                  # fully opaque: try lifting a studio backdrop
        im = strip_uniform_border(im)
    bb = im.getbbox()
    if bb: im = im.crop(bb)                        # trim padding so nothing renders small
    w,h = im.size
    scale = (CANVAS*FILL)/float(max(w,h))          # longest edge hits the same size every time
    nw,nh = max(1,int(w*scale)), max(1,int(h*scale))
    im = im.resize((nw,nh), Image.LANCZOS)
    canvas = Image.new("RGBA",(CANVAS,CANVAS),(0,0,0,0))
    canvas.paste(im, ((CANVAS-nw)//2,(CANVAS-nh)//2), im)   # perfectly centred
    canvas.save(os.path.join(OUT, os.path.splitext(f)[0] + ".png"), "PNG", optimize=True)
    print("%-18s %4dx%-4d -> 512x512 (content %d%%)" % (f, w, h, int(max(nw,nh)/CANVAS*100)), flush=True)
    done += 1
print("normalised %d images" % done)
