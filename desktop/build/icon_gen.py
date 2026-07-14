"""Generate a 1024x1024 app icon for Lava Playground (a glowing volcano)."""

from PIL import Image, ImageDraw, ImageFilter

S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
draw = ImageDraw.Draw(img)

# Rounded-rect background with a warm vertical gradient (dark plum -> ember).
top = (30, 18, 46)      # #1e122e
bot = (74, 21, 12)      # #4a150c
bg = Image.new("RGB", (S, S))
bgd = ImageDraw.Draw(bg)
for y in range(S):
    t = y / (S - 1)
    bgd.line(
        [(0, y), (S, y)],
        fill=tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)),
    )
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=185, fill=255)
img.paste(bg, (0, 0), mask)
draw = ImageDraw.Draw(img)

# Sky glow behind the crater.
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).ellipse([320, 180, 704, 560], fill=(255, 140, 30, 150))
glow = glow.filter(ImageFilter.GaussianBlur(70))
img.alpha_composite(glow)
draw = ImageDraw.Draw(img)

# Volcano body (dark slate), a trapezoid with a crater notch on top.
body = (38, 34, 52)  # #262234
draw.polygon(
    [(300, 860), (430, 430), (470, 430), (512, 500),
     (554, 430), (594, 430), (724, 860)],
    fill=body,
)

# Lava crater pool + two streams down the flanks.
lava_bright = (255, 210, 60)   # #ffd23c
lava_mid = (255, 110, 20)      # #ff6e14
draw.polygon([(430, 430), (470, 430), (512, 500), (554, 430), (594, 430),
              (560, 470), (512, 540), (464, 470)], fill=lava_mid)
draw.line([(486, 500), (470, 620), (500, 740), (474, 858)], fill=lava_mid, width=26)
draw.line([(540, 505), (566, 640), (548, 760), (572, 858)], fill=lava_bright, width=18)
draw.ellipse([470, 452, 554, 512], fill=lava_bright)

# Ember specks above the crater.
for cx, cy, r in [(512, 300, 15), (455, 350, 10), (575, 345, 11), (500, 235, 8), (548, 250, 7)]:
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 190, 70, 255))

img.save("icon-source.png")
print("wrote icon-source.png")
