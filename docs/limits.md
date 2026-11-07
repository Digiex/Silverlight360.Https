# Limits

- **IPv4, HTTP/1.1.** No HTTP/2 or 3, IPv6, proxies, cookies or compressed transfers. Send `Accept-Encoding` only for something you will decode yourself.
- **ANSI strings.** URLs, headers and paths go to the DLL as single-byte strings, so encode anything outside ASCII in the URL yourself.
- **Every new connection reads `cacert.pem` again,** 121 roots in 189 KB. Connections that are kept open don't.
- **A fatal error in the DLL** (an `abort`) stops the request's thread for good rather than the app, as in UnRar360. The log says why.
- **The log** is the host title's, `default.log` in `game:\`, where `game:` can be written. Under xenia-edge it can't be, so `Verbose` mirrors to DbgPrint.
- **xenia-edge's random numbers repeat:** its `XeCryptRandom` returns the same bytes on every run, so keys made there aren't secret. The console's are random (mbedtls-xenon's notes).
- **The network's buffers.** `xsdk_network_limits` in `src/https.c` raises the socket buffers only if the DLL starts the network first. In an app, the runtime has started it already, so the console's defaults apply; requests worked with them on a console, and no download speed has been measured there yet.
- **One base address.** The DLL is linked at `0x8D000000`, one of the bases free in a Silverlight app's process on the console; another DLL in the same app needs another.
