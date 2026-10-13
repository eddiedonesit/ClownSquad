"""Upscale a sprite PNG with nearest-neighbour onto a checkerboard, for inspection.

Usage: python tools/preview_sprite.py <in.png> <out.png> [scale]
"""
import zlib, struct, sys

def read_png(p):
    d = open(p, 'rb').read()
    pos, w, h, idat = 8, 0, 0, b''
    while pos < len(d):
        ln = struct.unpack('>I', d[pos:pos+4])[0]; typ = d[pos+4:pos+8]
        body = d[pos+8:pos+8+ln]
        if typ == b'IHDR': w, h = struct.unpack('>II', body[:8])
        elif typ == b'IDAT': idat += body
        pos += 12 + ln
    raw = zlib.decompress(idat)
    stride = w * 4; rows = []; prev = bytearray(stride); i = 0
    for _ in range(h):
        f = raw[i]; i += 1
        line = bytearray(raw[i:i+stride]); i += stride
        if f == 1:
            for x in range(4, stride): line[x] = (line[x] + line[x-4]) & 255
        elif f == 2:
            for x in range(stride): line[x] = (line[x] + prev[x]) & 255
        elif f == 3:
            for x in range(stride):
                a = line[x-4] if x >= 4 else 0
                line[x] = (line[x] + ((a + prev[x]) >> 1)) & 255
        elif f == 4:
            for x in range(stride):
                a = line[x-4] if x >= 4 else 0
                b = prev[x]; c = prev[x-4] if x >= 4 else 0
                pa, pb, pc = abs(b-c), abs(a-c), abs(a+b-2*c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        rows.append(line); prev = line
    return w, h, rows

def write_png(path, w, h, rows):
    raw = b''.join(b'\x00' + bytes(r) for r in rows)
    def ch(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t+d) & 0xffffffff)
    out = (b'\x89PNG\r\n\x1a\n'
           + ch(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
           + ch(b'IDAT', zlib.compress(raw, 9)) + ch(b'IEND', b''))
    open(path, 'wb').write(out)

src = sys.argv[1] if len(sys.argv) > 1 else 'assets/sprites/clown_16bit.png'
dst = sys.argv[2] if len(sys.argv) > 2 else 'debug/sprite_preview.png'
S = int(sys.argv[3]) if len(sys.argv) > 3 else 8
w, h, rows = read_png(src)
out = []
for y in range(h * S):
    r = bytearray()
    for x in range(w * S):
        sp = rows[y//S][(x//S)*4:(x//S)*4+4]
        if sp[3] == 0:
            v = 200 if ((x//16) + (y//16)) % 2 == 0 else 165
            r.extend((v, v, v, 255))
        else:
            r.extend(sp)
    out.append(r)
write_png(dst, w * S, h * S, out)
print(f"preview {w*S}x{h*S} -> {dst}")
