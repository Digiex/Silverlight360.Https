# Adding it to an app

An app needs the C# side and two files beside `default.xex`:

- **`Https360.dll`**, the C# side, `HttpsClient` and the drop-in, with `Https360.xml` beside it for IntelliSense: a reference to the release's DLL, or a `ProjectReference` to `csharp/Https360.csproj`. Or compile `csharp/HttpsClient.cs` and `csharp/HttpsWebRequest.cs` into the app itself.
- **`https360.xex`** and **`cacert.pem`**, as `Content`.

The C# side uses `NativeBuffer` from Silverlight360's [common library](https://digiex.github.io/Silverlight360/common-library/), Silverlight360.Common, for the memory the DLL reads and writes. An app built with Silverlight360 references that library already, and its build puts `Silverlight360.Common.dll` in the package.

In the app's project, as `sample/HttpsSample.csproj` has them:

```xml
<ItemGroup>
  <ProjectReference Include="..\Https360\csharp\Https360.csproj" />
  <!-- or, from a release: <Reference Include="Https360"><HintPath>..\Https360\Https360.dll</HintPath></Reference> -->
</ItemGroup>
<ItemGroup>
  <Content Include="..\Https360\https360.xex"><Link>https360.xex</Link></Content>
  <Content Include="..\Https360\cacert.pem"><Link>cacert.pem</Link></Content>
</ItemGroup>
```

Ship the release's `licenses` folder and `THIRD-PARTY-NOTICES.md` with them too: curl's and Mbed TLS's licenses ask for their notices to go with the binary. Https360's own license asks for a visible reference to <https://github.com/Digiex/Https360> in the app's documentation, credits or "about" information (see [Licenses](licenses.md)).

## The drop-in

To leave an app's `WebClient` and `HttpWebRequest` code as it is, register the drop-in (in `Https360.dll`, or `csharp/HttpsWebRequest.cs`) once, at the start:

```csharp
HttpsRequestCreator.Register();
var web = new WebClient();
web.DownloadStringCompleted += (s, e) => Show(e.Error == null ? e.Result : e.Error.Message);
web.DownloadStringAsync(new Uri("https://example.com/"));
```

From then on every `https://` request the app makes through the platform goes through Https360, with TLS 1.3 and Mozilla's roots; `Register(true)` takes `http://` too. On a console the platform's own stack fails for `example.com` and `github.com`, and Https360's doesn't. See [API](api.md#the-drop-in-httpswebrequest).

## What each one has

| | `HttpsClient` | `HttpsWebRequest` | The platform's `HttpWebRequest` |
|---|---|---|---|
| TLS 1.3, Mozilla's current roots | yes | yes | no: the console's own TLS and roots, which fail for `example.com` and `github.com` |
| Works off the UI thread | blocks, on any thread | `Begin`/`End`, on the thread pool | yes |
| `WebClient` | no | yes | yes |
| Request body | `byte[]` | a stream | a stream |
| Response body | in memory, or a file of any size | in memory | in memory, or read as it comes |
| Response headers | lines as they came, `Header(name)` | `WebHeaderCollection` | `WebHeaderCollection` |
| Cookies | no | `CookieContainer` | `CookieContainer` |
| Progress, cancelling | `BytesReceived`, `Cancel()` | `Abort()` | `WebClient`'s progress events, `Abort()` |
| Downloads to a file, resumed | yes | no | no |
| Credentials (`ICredentials`) | no | no | yes |
| Compressed responses | no | no | not checked |

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
