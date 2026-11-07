# Silverlight360.Https

HTTP and HTTPS from a [Silverlight360](https://github.com/Digiex/Silverlight360) app on the Xbox 360, with TLS 1.2 and 1.3. It's libcurl 8.23 on Mbed TLS 3.6.7, the xsdk ports [curl-xenon](https://github.com/xenosworks/curl-xenon) and [mbedtls-xenon](https://github.com/xenosworks/mbedtls-xenon), built into a DLL of its own, `https360.xex`, which an app calls from C# through `HttpsClient`.

```csharp
using (var client = new HttpsClient())   // checks servers against game:\cacert.pem
{
    // On a thread of its own: requests block until the response is in.
    HttpsResponse page = client.Get("https://example.com/");
    HttpsResponse posted = client.Post("https://api.example.com/items", "application/json",
        Encoding.UTF8.GetBytes("{\"name\": \"xbox\"}"));
    client.Download("https://example.com/big.zip", @"hdd:\Downloads\big.zip", true);
}
```

The platform's own `HttpWebRequest` does HTTPS too, but only with the certificate authorities the console knows, which are years old: it can't reach `example.com` or `github.com`, for instance. Silverlight360.Https brings Mozilla's current roots and TLS 1.3, and its requests run on any thread, not the UI's. Its drop-in, `HttpsRequestCreator.Register()`, sends an app's existing `WebClient` and `HttpWebRequest` calls through it (see [Adding it to an app](using.md#the-drop-in)).

## Getting it

A [release](https://github.com/Digiex/Silverlight360.Https/releases) has what an app needs, built:

| | |
|---|---|
| `https360.xex` | The DLL, at base `0x8D000000` |
| `cacert.pem` | Mozilla's root certificates, which servers are checked against |
| `Silverlight360.Https.dll`, `Silverlight360.Https.xml` | The C# side, with its documentation for IntelliSense: `HttpsClient`, and the drop-in for the platform's `WebClient` and `HttpWebRequest`. It needs Silverlight360.Common, which an app built with Silverlight360 has |
| `licenses/`, `THIRD-PARTY-NOTICES.md` | curl's, Mbed TLS's and Mozilla's licenses, which go with the DLL |
| `LICENSE.md`, `README.md`, `BUILD.txt` | Silverlight360.Https's license, and what the release was built from |

Then see [Adding it to an app](using.md). To build it yourself, see [Building](building.md).

## Why curl on Mbed TLS

- **Already ported for the console.** xsdk's own ports, built on its sockets, `getaddrinfo`, `getentropy` and clocks, and checked on a real console: Mbed TLS's self-tests, TLS 1.2 to public hosts, and 9 MB/s downloads over ChaCha20-Poly1305.
- **Modern TLS.** Mbed TLS 3.6 is an LTS release with TLS 1.3. mbedtls-xenon builds TLS 1.2 only by default; `config/mbedtls.h` keeps 1.3 in.
- **A real HTTP client.** curl brings redirects, chunked transfers, keep-alive, timeouts, `Range` resumes and certificate name checks, none of which an app has to write itself.
- **Fast on the console's CPU.** Mbed TLS offers ChaCha20-Poly1305 first, which ran about four times faster than AES-GCM on the console, and servers that weigh the client's order take it.
- **Licenses an app can ship.** curl's license is MIT-style and Mbed TLS's is Apache 2.0. BearSSL has no TLS 1.3, wolfSSL is GPL unless bought, and OpenSSL is several times larger, with no xsdk port.

## Where it runs

On a console (retail kernel 2.0.17559, running unsigned code) inside [Silverdash360](https://github.com/Digiex/Silverdash360), and in [xenia-edge](https://github.com/jessenic/xenia-edge). See [Testing](testing.md) for what was checked where.
