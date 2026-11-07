#!/usr/bin/env python3
"""Checks what the sample downloaded into <content root>/HttpsTest: full.bin and resumed.bin, each of https_server.py's
8 MB data file. Prints PASS or FAIL for each; exits 1 if any failed."""

import argparse
import hashlib
import pathlib
import sys

SIZE = 8 << 20
PERIOD = bytes((i * 31 + 7) & 0xFF for i in range(256))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=pathlib.Path, help="<content root>/HttpsTest")
    arguments = parser.parse_args()
    expected = hashlib.sha256(PERIOD * (SIZE // len(PERIOD))).hexdigest()
    failed = 0
    for name in ("full.bin", "resumed.bin"):
        path = arguments.folder / name
        data = path.read_bytes() if path.is_file() else b""
        ok = len(data) == SIZE and hashlib.sha256(data).hexdigest() == expected
        failed += not ok
        print(f"{'PASS' if ok else 'FAIL'}: {name}, {len(data)} bytes")
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
