#!/usr/bin/env python3
"""The PC's side of the sample's local checks: HTTPS with Mbed TLS's test certificate for localhost (server5, under
test-ca2, in mbedtls-xenon's framework/data_files, good until 2033), on --port with TLS 1.2 and 1.3 and on the next port
with TLS 1.2 only. It serves:
- GET and HEAD /files/data_<N>.bin, N bytes, byte i being (i * 31 + 7) & 0xFF, with Range's "bytes=a-" and "bytes=a-b";
- any method on /echo, {"method", "bytes", "sha256", "content_type", "x_test"} of the request, as JSON;
- GET /redirect, a 302 to /files/data_4096.bin;
- GET /status/<N>, an empty response with that status.
Logs each request; runs until stopped, or for --seconds."""

import argparse
import hashlib
import http.server
import json
import pathlib
import re
import ssl
import threading
import time

CERTIFICATES = pathlib.Path(__file__).resolve().parents[3] / "mbedtls-xenon" / "framework" / "data_files"
MAX_FILE_BYTES = 256 * 1024 * 1024
CHUNK_BYTES = 65536
FILE_NAME = re.compile(r"/files/data_([0-9]{1,9})\.bin")
STATUS = re.compile(r"/status/([1-5][0-9][0-9])")
RANGE = re.compile(r"bytes=([0-9]+)-([0-9]*)")
PERIOD = bytes((i * 31 + 7) & 0xFF for i in range(256))


def log(text):
    print(f"{time.strftime('%H:%M:%S')} {text}", flush=True)


def pattern(start, end):
    """Bytes start to end of a data file."""
    offset = start % len(PERIOD)
    return (PERIOD * ((offset + end - start) // len(PERIOD) + 1))[offset:offset + end - start]


class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    timeout = 10

    def log_message(self, format, *args):
        log(f"{self.server.name} {self.client_address[0]}:{self.client_address[1]}: {format % args}")

    def send_empty(self, status, headers=()):
        self.send_response(status)
        for name, value in headers:
            self.send_header(name, value)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self):
        path = self.path.split("?", 1)[0]
        if path == "/echo":
            return self.echo()
        if path == "/redirect":
            return self.send_empty(302, [("Location", "/files/data_4096.bin")])
        status = STATUS.fullmatch(path)
        if status:
            return self.send_empty(int(status.group(1)))
        name = FILE_NAME.fullmatch(path)
        if not name or int(name.group(1)) > MAX_FILE_BYTES:
            return self.send_empty(404)
        size = int(name.group(1))
        start, end = 0, size
        asked = RANGE.fullmatch(self.headers.get("Range", ""))
        if asked:
            start = int(asked.group(1))
            end = min(size, int(asked.group(2)) + 1) if asked.group(2) else size
            if start >= size or start >= end:
                return self.send_empty(416, [("Content-Range", f"bytes */{size}")])
            self.send_response(206)
            self.send_header("Content-Range", f"bytes {start}-{end - 1}/{size}")
        else:
            self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(end - start))
        self.end_headers()
        if self.command == "HEAD":
            return
        for chunk in range(start, end, CHUNK_BYTES):
            self.wfile.write(pattern(chunk, min(end, chunk + CHUNK_BYTES)))

    def echo(self):
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length) if length else b""
        if len(body) != length:
            log(f"{self.server.name}: {self.command} {self.path}: {len(body)} of {length} bytes came")
        answer = json.dumps({"method": self.command, "bytes": len(body), "sha256": hashlib.sha256(body).hexdigest(),
            "content_type": self.headers.get("Content-Type", ""), "x_test": self.headers.get("X-Test", "")}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(answer)))
        self.end_headers()
        self.wfile.write(answer)

    do_HEAD = do_GET
    do_POST = do_PUT = do_DELETE = do_PATCH = echo


def serve(port, maximum, certificates):
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.maximum_version = maximum
    context.load_cert_chain(certificates / "server5.crt", certificates / "server5.key")
    server = http.server.ThreadingHTTPServer(("0.0.0.0", port), Handler)
    server.daemon_threads = True
    server.name = f"https:{port}"
    server.socket = context.wrap_socket(server.socket, server_side=True)
    log(f"{server.name}: up to {maximum.name}")
    threading.Thread(target=server.serve_forever, daemon=True).start()


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=7443, help="TLS 1.2 and 1.3; TLS 1.2 alone on the next port")
    parser.add_argument("--seconds", type=float, help="stop after this long")
    parser.add_argument("--certificates", type=pathlib.Path, default=CERTIFICATES,
        help="the folder with server5.crt and server5.key, mbedtls-xenon's framework/data_files")
    arguments = parser.parse_args()
    serve(arguments.port, ssl.TLSVersion.TLSv1_3, arguments.certificates)
    serve(arguments.port + 1, ssl.TLSVersion.TLSv1_2, arguments.certificates)
    deadline = time.monotonic() + arguments.seconds if arguments.seconds else None
    while deadline is None or time.monotonic() < deadline:
        time.sleep(0.5)


if __name__ == "__main__":
    main()
