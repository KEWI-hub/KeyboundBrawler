# Draws the typing bird (Bongo Cat style) in three frames: idle, left wing down, right wing down.
# Usage: python -I tools/draw_bird.py images [preview.png]
import math
import sys
from PIL import Image, ImageDraw

S = 4                      # supersampling
W, H = 300, 290
INK = (70, 46, 36, 255)
WHITE = (255, 255, 255, 255)
CHEEK = (248, 196, 200, 255)
BEAK = (243, 170, 96, 255)
LW = 4                     # outline width before supersampling

# The desk edge slants down to the right, like the Bongo Cat gif.
DESK_L, DESK_R = 150, 205
TILT = math.degrees(math.atan2(DESK_R - DESK_L, W))


def s(v): return [round(c * S) for c in v]


def desk_y(x): return DESK_L + (DESK_R - DESK_L) * x / W


def layer(w, h):
    img = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def mitt(w, h, feathers):
    # A round wing tip with little feather scallops along its front edge.
    img, d = layer(w + 4, h + 4)
    d.ellipse(s((2, 2, w, h)), fill=WHITE, outline=INK, width=LW * S)
    step = w / (feathers + 1)
    for i in range(1, feathers + 1):
        x = 2 + step * i
        d.arc(s((x - 6, h - 16, x + 6, h - 4)), 200, 340, fill=INK, width=3 * S)
    return img


def keyboard():
    kw, kh = 210, 92
    img, d = layer(kw + 4, kh + 4)
    d.rounded_rectangle(s((2, 2, kw, kh)), radius=8 * S, fill=WHITE, outline=INK, width=LW * S)
    # Plain line-art keys like the gif: four rows, a wide key or two, and the space bar.
    for row in range(4):
        y = 12 + row * 16
        x = 12 + (row % 2) * 5
        while x < kw - 20:
            w = 20 if (row == 0 and x < 20) or (row == 2 and x > kw - 50) else 11
            d.rounded_rectangle(s((x, y, x + w, y + 10)), radius=2 * S, outline=INK, width=2 * S)
            x += w + 4
    d.rounded_rectangle(s((62, 76, 150, 85)), radius=2 * S, outline=INK, width=2 * S)
    return img.rotate(-TILT, expand=True, resample=Image.BICUBIC)


def frame(down):
    img, d = layer(W, H)
    # A wide white blob peeking over the desk; the desk below hides its lower half.
    d.ellipse(s((38, 78, 292, 330)), fill=WHITE, outline=INK, width=LW * S)
    # Head tuft: three little curls on top.
    for x, h in ((150, 14), (164, 22), (178, 12)):
        d.arc(s((x - 8, 79 - h, x + 8, 79 + h)), 200, 340, fill=INK, width=LW * S)
    # Round glasses with dot eyes, pink cheeks, a small beak.
    for cx in (128, 196):
        d.ellipse(s((cx - 22, 112, cx + 22, 156)), outline=INK, width=LW * S)
        d.ellipse(s((cx - 6, 128, cx + 6, 140)), fill=INK)
        d.ellipse(s((cx - 3, 129, cx + 1, 133)), fill=WHITE)
    d.arc(s((150, 124, 174, 140)), 200, 340, fill=INK, width=LW * S)
    for cx in (106, 218):
        d.ellipse(s((cx - 13, 150, cx + 13, 163)), fill=CHEEK)
    d.polygon(s((153, 152, 171, 152, 162, 165)), fill=BEAK, outline=INK, width=3 * S)

    # Cut the body off below the desk edge; the desk itself stays see-through so it sits on the window colour.
    cut = Image.new("L", img.size, 0)
    ImageDraw.Draw(cut).polygon(s((0, DESK_L, W, DESK_R, W, H, 0, H)), fill=255)
    img.paste((0, 0, 0, 0), mask=cut)
    d.line(s((0, DESK_L, W, DESK_R)), fill=INK, width=LW * S)

    kb = keyboard()
    img.alpha_composite(kb, (22 * S, 172 * S))
    d.line(s((212, 268, 226, 276, 232, 290)), fill=INK, width=3 * S)

    # Resting wings sit up on the desk edge; the typing one slaps down onto the keys.
    for side, x in (("left", 66), ("right", 222)):
        if side == down:
            w = mitt(70, 44, 3).rotate(-28 if side == "left" else 28, expand=True, resample=Image.BICUBIC)
            if side == "left":
                pos = (34, 168)
            else:
                pos = (176, 184)
        else:
            w = mitt(48, 38, 2)
            pos = (x - 24, desk_y(x) - 30)
        img.alpha_composite(w, (round(pos[0] * S), round(pos[1] * S)))
    return img.resize((W, H), Image.LANCZOS)


out = sys.argv[1]
frames = {"idle": frame(None), "left": frame("left"), "right": frame("right")}
for name, f in frames.items():
    f.save(f"{out}/bird_{name}.png")
if len(sys.argv) > 2:
    preview = Image.new("RGBA", (W * 3 + 40, H + 20), (253, 247, 234, 255))
    for i, f in enumerate(frames.values()):
        preview.alpha_composite(f, (10 + i * (W + 10), 10))
    preview.save(sys.argv[2])
