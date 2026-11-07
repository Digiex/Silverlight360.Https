# Silverlight360.Https

HTTP and HTTPS from a [Silverlight360](https://github.com/Digiex/Silverlight360) app on the Xbox 360, with TLS 1.2 and 1.3. libcurl 8.23 on Mbed TLS 3.6.7, the xsdk ports [curl-xenon](https://github.com/xenosworks/curl-xenon) and [mbedtls-xenon](https://github.com/xenosworks/mbedtls-xenon), built into a DLL of its own, `https360.xex`, which an app calls from C# through `HttpsClient`.

**Documentation: <https://digiex.github.io/Silverlight360.Https/>**

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

The console's own `HttpWebRequest` knows only old certificate authorities and can't reach many sites today; Silverlight360.Https brings Mozilla's current roots and TLS 1.3. It runs on a console, inside [Silverdash360](https://github.com/Digiex/Silverdash360), and in xenia-edge.

- **Drop-in:** `HttpsRequestCreator.Register()` sends the platform's own `WebClient` and `HttpWebRequest` through Silverlight360.Https, so existing code needs no change. See [The drop-in](https://digiex.github.io/Silverlight360.Https/using/#the-drop-in).
- **Using it:** a [release](https://github.com/Digiex/Silverlight360.Https/releases) has `https360.xex`, `cacert.pem`, `Silverlight360.Https.dll` with its XML documentation, and the licenses that go with them. See [Adding it to an app](https://digiex.github.io/Silverlight360.Https/using/) and the [API](https://digiex.github.io/Silverlight360.Https/api/).
- **Building it:** clone with `--recursive`, run `.\tools\Configure.ps1`, which sets up xsdk and its toolchain, then `make` in WSL. See [Building](https://digiex.github.io/Silverlight360.Https/building/).
- **Testing it:** the sample app's 31 checks, in xenia-edge or on a console. See [Testing](https://digiex.github.io/Silverlight360.Https/testing/).

## License

Silverlight360.Https is licensed under the PolyForm Noncommercial License 1.0.0, with an additional condition: projects made with it must reference this repository. See [LICENSE.md](LICENSE.md).

libcurl (curl's license), Mbed TLS (Apache 2.0) and Mozilla's root certificates (MPL 2.0) keep their own licenses, which go with `https360.xex` and `cacert.pem`: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [licenses](licenses).
