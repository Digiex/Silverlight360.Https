# Testing

## In xenia-edge

```powershell
New-Item -ItemType Directory -Force $env:TEMP\https-content\HttpsTest
Start-Process python -ArgumentList '-I', 'tests\https_server.py', '--seconds', '600'
..\XboxSilverlightSdk\tools\Test-XboxApp.ps1 -Xex .\sample\bin\Release\xbox\default.xex -Output $env:TEMP\https-run `
    -ExtraArgs "--content_root=$env:TEMP\https-content" -Seconds 150
python tests\verify_downloads.py $env:TEMP\https-content\HttpsTest
```

`tests/https_server.py` serves HTTPS on 7443, with TLS 1.2 and 1.3, and on 7444, with TLS 1.2 only. It uses Mbed TLS's test certificate for `localhost`, from mbedtls-xenon's `framework/data_files`. The sample sends `localhost` to 127.0.0.1 with `Resolve`. On a console, set `Server` in `sample/MainPage.xaml.cs` to the PC's address.

What passed in xenia-edge, 2026-11-02: all 31 checks; the heap peaked at 1.9 MB.

**Local:**

- GETs over TLS 1.3 and TLS 1.2, their SHA-256 checked;
- POSTs from 1 KB to 1 MB, echoed back with their hashes, plus PUT, DELETE and HEAD;
- a redirect followed and one not followed, a 404, and a response over `MaxResponseBytes` refused;
- an 8 MB download to `hdd:`, one cancelled at 2 MB and then resumed with a 206, both byte for byte;
- a wrong name refused, then accepted with `Insecure`, and a closed port refused.

**Public:**

- example.com, www.google.com and api.github.com, over TLS 1.3 with ChaCha20-Poly1305;
- tls-v1-2.badssl.com over TLS 1.2;
- expired, wrong-host, self-signed and untrusted-root certificates from badssl.com, each refused for its own reason.

**Uploads needed a fix in xenia-edge.** xenia opens a guest TCP socket as a host acceptor and swaps in a new host socket at `connect`. That lost the guest's FIONBIO and its options, so a non-blocking socket's `recv` blocked on the host. A POST larger than one TLS record stalled: curl reads between records, and the read never came back. The fix is in xenia-edge's `src/xenia/kernel/xsocket.cc` and `xsocket.h`. It is not a console problem.

## On a console

Before an app calls a new build, check that the console loads it: a DllImport of a module that can't load turns into a call through null, which can power the console off. The CLR loads a DllImport module as `GAME:\<name>` with `XexLoadImage` flags 9; Silverdash360's debug server does the same with `xex.probe GAME:\https360.xex 9`, then unloads it.

What ran on a console, 2026-11-07, inside Silverdash360 (retail kernel 2.0.17559, running unsigned code):

- `XexLoadImage` loaded the DLL at `0x8D000000` (status 0), after the runtime had started the network;
- GETs to xenia-manager.github.io, raw.githubusercontent.com and api.github.com, over TLS 1.3 with ChaCha20-Poly1305: 170 to 370 ms for the first request to a host, less on a kept connection;
- a 404 from GitHub Pages, as a response;
- GitHub's release download, following its redirect to GitHub's file storage, 1.2 MB checked against its SHA-256;
- the heap peaked at 1.7 MB.

Again the same day, the DLL built against xsdk's `dll-modules` `libxsdk.a` (`tools\Configure.ps1`'s xsdk, the one XboxNGen uses), with the drop-in:

- Silverdash360 loaded it at its start and fetched its art with it;
- before `HttpsRequestCreator.Register()`, the platform's `HttpWebRequest` failed for example.com and github.com; after it, `WebRequest.Create` gave `HttpsWebRequest`s, which got 200 from example.com, github.com (524 KB) and api.github.com;
- `WebClient.DownloadStringAsync` got example.com and x360db's `info.json`, the content type in `ResponseHeaders`, and a 404 came as `e.Error`, a `WebException` with the response, its status 404;
- `WebClient.UploadStringAsync` POSTed its body, with its `Content-Type`, and `Accept` set on the request arrived, with the default `User-Agent`.
