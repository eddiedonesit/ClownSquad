"""Minimal dependency-free RGBA PNG writer, shared by the art generators."""
import zlib
import struct


def write_png(path, w, h, rows):
    """Write an RGBA PNG. rows: list of bytearray, each w*4 bytes."""
    filtered = []
    for r in rows:
        filtered.append(bytes([0]) + bytes(r))
    raw = b"".join(filtered)

    def chunk(tag, data):
        crc = zlib.crc32(tag + data) & 0xFFFFFFFF
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

    sig = bytes([137, 80, 78, 71, 13, 10, 26, 10])
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)
    out = sig + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(out)
