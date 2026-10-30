"""Render the repository's simple P mark into a multi-resolution Windows ICO (stdlib only)."""
from pathlib import Path
import math
import struct
import zlib
import json

ASSETS = Path(__file__).resolve().parents[1] / "src" / "Promptlet" / "Assets"
GEOMETRY = json.loads((ASSETS / "logo.json").read_text(encoding="utf-8"))


def color_at(x, y, theme="light"):
    b = GEOMETRY["background"]
    lo, hi, r = b["min"], b["max"], b["radius"]
    dx, dy = max(lo + r - x, 0, x - hi + r), max(lo + r - y, 0, y - hi + r)
    if dx * dx + dy * dy > r * r:
        return (0, 0, 0, 0)
    palette, p = GEOMETRY[theme], GEOMETRY["p"]
    color = palette["background"]
    def rect(v): return v[0] <= x <= v[2] and v[1] <= y <= v[3]
    def ellipse(v): return x >= v[0] and ((x-v[0])/v[2])**2 + ((y-v[1])/v[3])**2 <= 1
    p_outer = rect(p["stem"]) or rect(p["body"]) or ellipse(p["outer"])
    p_inner = rect(p["inner"]) or ellipse(p["hole"])
    if p_outer and not p_inner:
        color = palette["p"]
    star = GEOMETRY["star"]
    if abs((x-star["x"])/star["radius"])**(2/3) + abs((y-star["y"])/star["radius"])**(2/3) <= 1:
        color = palette["star"]
    return color


def chunk(kind, payload):
    return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))


def png(size, theme="light"):
    raw = bytearray()
    samples = 4
    for y in range(size):
        raw.append(0)
        for x in range(size):
            colors = [color_at((x + (sx + .5) / samples) * 128 / size, (y + (sy + .5) / samples) * 128 / size, theme)
                      for sy in range(samples) for sx in range(samples)]
            # Premultiply during downsampling so transparent edges don't acquire a dark fringe.
            alpha = sum(c[3] for c in colors)
            rgb = [round(sum(c[channel] * c[3] for c in colors) / alpha) if alpha else 0 for channel in range(3)]
            raw.extend((*rgb, round(alpha / len(colors))))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


if __name__ == "__main__":
    for theme in ("light", "dark"):
        (ASSETS / f"Logo{theme.title()}.png").write_bytes(png(256, theme))
        palette, b, p, s = GEOMETRY[theme], GEOMETRY["background"], GEOMETRY["p"], GEOMETRY["star"]
        def hexcolor(v): return "#" + "".join(f"{n:02x}" for n in v[:3])
        ox, oy, rx, ry = p["outer"]; ix, iy, irx, iry = p["hole"]
        stem, body, inner = p["stem"], p["body"], p["inner"]
        shape = f'M{stem[0]} {stem[3]}V{body[1]}H{ox}a{rx} {ry} 0 0 1 0 {2*ry}H{stem[2]}V{stem[3]}Z M{inner[0]} {inner[1]}H{ix}a{irx} {iry} 0 0 1 0 {2*iry}H{inner[0]}Z'
        points = " ".join(f'{s["x"]+s["radius"]*math.cos(a*math.pi/36)**3:.3f},{s["y"]+s["radius"]*math.sin(a*math.pi/36)**3:.3f}' for a in range(72))
        svg = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><rect x="{b["min"]}" y="{b["min"]}" width="{b["max"]-b["min"]}" height="{b["max"]-b["min"]}" rx="{b["radius"]}" fill="{hexcolor(palette["background"])}"/><path d="{shape}" fill-rule="evenodd" fill="{hexcolor(palette["p"])}"/><polygon points="{points}" fill="{hexcolor(palette["star"])}"/></svg>'
        (ASSETS / f'Promptlet{theme.title()}.svg').write_text(svg, encoding="utf-8")
        if theme == "light": (ASSETS / 'Promptlet.svg').write_text(svg, encoding="utf-8")
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [png(size) for size in sizes]
    header = struct.pack("<HHH", 0, 1, len(sizes))
    directory = bytearray()
    offset = 6 + 16 * len(sizes)
    for size, image in zip(sizes, images):
        directory.extend(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(image), offset))
        offset += len(image)
    (ASSETS / "AppIcon.ico").write_bytes(header + directory + b"".join(images))
    print("Generated AppIcon.ico from the Promptlet P mark.")
