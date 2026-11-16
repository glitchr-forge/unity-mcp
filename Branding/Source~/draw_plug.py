"""Forge MCP's mark: a pixel-art plug in the Forge anvil's steel palette, a spark of the forge's fire between its
prongs (the editor live to an AI assistant). Writes a PNG of the given path: python3 draw_plug.py plug.png"""
import struct, sys, zlib
W, H = 24, 30
Y0 = 6   # rows free above the prongs for the arc
def body(x, y):
    y -= Y0
    if 1 <= y <= 6 and (x in (7, 8) or x in (15, 16)): return True        # the prongs
    if 7 <= y <= 15:                                                        # the head, its corners cut
        left, right = (5, 18) if 8 <= y <= 14 else (6, 17)
        return left <= x <= right
    if 16 <= y <= 17 and 8 <= x <= 15: return True                          # the neck
    if 18 <= y <= 19 and 10 <= x <= 13: return True
    if 20 <= y <= 22 and 11 <= x <= 12: return True                         # the cable
    return False
inside = lambda x, y: 0 <= x < W and 0 <= y < H and body(x, y)
OUT, SHINE, FACE, LIGHT, MID, DARK, DEEP = (0x17, 0x14, 0x23), (0xF8, 0xFA, 0xFF), (0xD3, 0xDC, 0xE6), (0x9C, 0xA8, 0xB9), (0x6F, 0x7B, 0x8F), (0x4C, 0x55, 0x68), (0x30, 0x34, 0x44)
px = [[(0, 0, 0, 0)] * W for _ in range(H)]
for y in range(H):
    for x in range(W):
        if inside(x, y):
            yy = y - Y0
            if 1 <= yy <= 6: c = FACE if x in (7, 15) else LIGHT            # prongs lit on their left
            elif yy == 7: c = FACE                                          # the head's lit top
            elif not inside(x - 1, y): c = LIGHT
            elif not inside(x + 1, y) or not inside(x, y + 1): c = DEEP
            elif yy == 13: c = DARK                                         # the grip's groove
            elif x >= 15 or yy >= 16: c = DARK
            else: c = MID
            px[y][x] = c + (255,)
        elif any(inside(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
            px[y][x] = OUT + (255,)
for x, y in ((6, 9), (7, 9), (6, 10), (8, 2)):                               # glints
    if inside(x, y): px[y][x] = SHINE + (255,)
# An electric arc from one prong to the other, in the Forge's fire: a hot core arching over the gap, a yellow glow
# above it, orange where it leaves the prongs, two sparks thrown off.
HEART, GLOW, EMBER, RED = (0xFF, 0xF4, 0xB0), (0xFF, 0xD2, 0x3F), (0xFF, 0x7A, 0x1A), (0xE0, 0x30, 0x1E)
arc = {}
# From the tip of one prong, over the gap, to the tip of the other.
for x, y in ((8, -1), (9, -2), (10, -3), (11, -4), (12, -4), (13, -3), (14, -2), (15, -1)): arc[(x, y)] = HEART
for x, y in ((8, -2), (9, -3), (10, -4), (11, -5), (12, -5), (13, -4), (14, -3), (15, -2)): arc[(x, y)] = GLOW
arc.update({(6, -3): EMBER, (17, -4): RED, (7, -1): EMBER, (16, -1): EMBER})                                       # sparks
for (x, y), c in arc.items():
    px[y + Y0][x] = c + (255,)
raw = b"".join(b"\0" + bytes(v for p in row for v in p) for row in px)
chunk = lambda t, d: struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d))
open(sys.argv[1], "wb").write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0))
     + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
