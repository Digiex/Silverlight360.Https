# Adding it to an app

An app needs four files:

- `csharp/HttpsClient.cs`, compiled into the app;
- Silverlight360's `samples/Common/NativeBuffer.cs`, which `HttpsClient` uses for memory the DLL reads and writes (or an app's own class like it, with the same members);
- `https360.xex` and `cacert.pem`, as `Content` beside `default.xex`.

In the app's project, as `sample/HttpsSample.csproj` has them:

```xml
<ItemGroup>
  <Content Include="..\Https360\https360.xex"><Link>https360.xex</Link></Content>
  <Content Include="..\Https360\cacert.pem"><Link>cacert.pem</Link></Content>
</ItemGroup>
```

Ship the release's `licenses` folder and `THIRD-PARTY-NOTICES.md` with them too: curl's and Mbed TLS's licenses ask for their notices to go with the binary. Https360's own license asks for a visible reference to <https://github.com/Digiex/Https360> in the app's documentation, credits or "about" information (see [Licenses](licenses.md)).

## Making requests

Requests block until the response is in, so make them on a thread of their own, never the UI's:

```csharp
ThreadPool.QueueUserWorkItem(delegate
{
    try
    {
        using (var client = new HttpsClient { TimeoutSeconds = 30 })
        {
            HttpsResponse response = client.Get("https://api.github.com/");
            string text = response.Status == 200 ? response.Text : null;
            Deployment.Current.Dispatcher.BeginInvoke(() => Show(text));
        }
    }
    catch (HttpsException e)
    {
        // No response came: e.Code is curl's, such as HttpsException.BadCertificate.
    }
});
```

- **One request at a time per client.** A client keeps its connections open, so the next request to the same host skips the handshake: 7 ms against 260 ms for the first in xenia-edge. Use one client per thread for requests at the same time.
- **Statuses aren't errors.** Any HTTP response comes back as an `HttpsResponse`, 404 included. `HttpsException` means no response came.
- **The first client starts the DLL** with a heap of `DefaultHeapBytes` (96 MB, reserved and committed as it's used). Call `HttpsClient.Start(bytes)` before the first client for another size; `HeapPeak` says what the requests needed. Silverdash360 gives it 16 MB, and its requests have peaked under 2 MB.
- **The DLL loads at `0x8D000000`.** In a Silverlight app's process on the console, `0x8A000000`, `0x8C000000` and `0x8E000000` are taken, so another DLL of the app's can't be at this base. A DllImport of a module that can't load turns into a call through null, which can power the console off, so check a new build loads first (see [Testing](testing.md)).

## Certificates

Servers are checked against `game:\cacert.pem`, and against the console's clock, which has to be right. Pass another PEM file to `new HttpsClient(path)` to trust other roots. `Insecure` turns the checks off, for a test server only.

See [API](api.md) for everything a client has.
