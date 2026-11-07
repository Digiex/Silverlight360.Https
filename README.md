# Https360

HTTP and HTTPS from a [Silverlight360](https://github.com/Digiex/Silverlight360) app on the Xbox 360, with TLS 1.2 and 1.3. libcurl 8.23 on Mbed TLS 3.6.7, the xsdk ports [curl-xenon](https://github.com/xenosworks/curl-xenon) and [mbedtls-xenon](https://github.com/xenosworks/mbedtls-xenon), built into a DLL of its own, `https360.xex`, which an app calls from C# through `HttpsClient`.

**Documentation: <https://digiex.github.io/Https360/>**

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

The console's own `HttpWebRequest` knows only old certificate authorities and can't reach many sites today; Https360 brings Mozilla's current roots and TLS 1.3. It runs on a console, inside [Silverdash360](https://github.com/Digiex/Silverdash360), and in xenia-edge.

- **Using it:** a [release](https://github.com/Digiex/Https360/releases) has `https360.xex`, `cacert.pem`, `HttpsClient.cs` and the licenses that go with them. See [Adding it to an app](https://digiex.github.io/Https360/using/) and the [API](https://digiex.github.io/Https360/api/).
- **Building it:** clone with `--recursive`, run `.\tools\Configure.ps1`, which sets up xsdk and its toolchain, then `make` in WSL. See [Building](https://digiex.github.io/Https360/building/).
- **Testing it:** the sample app's 31 checks, in xenia-edge or on a console. See [Testing](https://digiex.github.io/Https360/testing/).

## License

Https360 is licensed under the PolyForm Noncommercial License 1.0.0, with an additional condition: projects made with it must reference this repository. See [LICENSE.md](LICENSE.md).

libcurl (curl's license), Mbed TLS (Apache 2.0) and Mozilla's root certificates (MPL 2.0) keep their own licenses, which go with `https360.xex` and `cacert.pem`: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [licenses](licenses).
