# API

`Silverlight360.Https.dll` (`csharp/HttpsClient.cs`), namespace `Silverlight360.Https`.

## HttpsClient

| | |
|---|---|
| `new HttpsClient()` | A client checking servers against `game:\cacert.pem` |
| `new HttpsClient(string caFile)` | A client checking servers against the PEM root certificates in `caFile` |
| `HttpsResponse Get(string url)` | A GET, its whole body in memory |
| `HttpsResponse Post(string url, string contentType, byte[] body)` | A POST |
| `HttpsResponse Send(string method, string url, string[] headers, byte[] body)` | Any method; `headers` are `"Name: value"` lines, `body` may be null |
| `HttpsResponse Download(string url, string path, bool resume)` | A GET written straight to a file on the console's drives, such as `hdd:\Downloads\a.zip`, with no size limit; its `Body` is null |
| `void Resolve(string entry)` | Sends a host and port to another address for every request after, as `"localhost:7381:192.168.0.125"`; the certificate is still checked against the name |
| `void Cancel()` | From another thread: stops the request at its next block of data, which then throws `HttpsException` with `Cancelled` |
| `long BytesReceived`, `long BytesTotal` | From another thread: the body's bytes so far (a resumed download's first part among them), and its whole size, -1 until the server says |
| `void Dispose()` | Closes the client's connections |

Settings, for the requests after they're set:

| | |
|---|---|
| `FollowRedirects` | Follows 3xx responses' `Location`, up to 10 times; on unless set |
| `TimeoutSeconds` | The longest a request may take; 0, no limit, unless set |
| `ConnectTimeoutSeconds` | The longest a connection may take to open, name lookup and handshake included; 30 unless set |
| `MaxResponseBytes` | The largest body kept in memory; 0 for the DLL's 64 MB. `Download` has no limit |
| `Insecure` | Skips certificate and name checks: for a test server only |
| `Verbose` | Writes curl's trace of each request to the log, `default.log` in `game:\` where it can be written, and to DbgPrint |

The DLL, for all clients:

| | |
|---|---|
| `static void Start(uint heapBytes)` | Starts the DLL with a heap of up to `heapBytes`, reserved now and committed as it's used; once, and the first client does it with `DefaultHeapBytes` if nothing has |
| `static uint Version` | The wrapper's API version, then curl's, 16 bits each: `0x00010817` for curl 8.23 |
| `static uint HeapCommitted`, `static uint HeapPeak` | The DLL's heap in use now, and the most it has held at once: what to size `Start` by |

With `Download` and `resume`, a file already there is taken as the first part and the rest asked for with `Range`; a server that sends the whole file again (200) starts it over.

## HttpsResponse

| | |
|---|---|
| `int Status` | The HTTP status, as 200 or 404 |
| `string Headers` | The final response's header lines as they came, its status line first |
| `string Header(string name)` | The first value of a header, its name in any case; null if there's none |
| `byte[] Body`, `string Text` | The body, and the body as UTF-8; null for a `Download` |
| `string Url` | The URL the response came from, after redirects |
| `string TlsVersion` | `"TLSv1.3"` or `"TLSv1.2"`, empty over HTTP |
| `string Cipher` | Mbed TLS's name for the cipher suite, as `"TLS1-3-CHACHA20-POLY1305-SHA256"` |
| `int Redirects`, `int ElapsedMilliseconds` | The redirects followed, and the whole request's time |

## HttpsException

No response came. `Code` is curl's `CURLcode`; the message is curl's.

| Constant | Code | |
|---|---|---|
| `CouldNotResolve` | 6 | The name didn't resolve |
| `CouldNotConnect` | 7 | Nothing answered at the address |
| `WriteError` | 23 | A `Download`'s file couldn't be written |
| `TimedOut` | 28 | `TimeoutSeconds` or `ConnectTimeoutSeconds` ran out |
| `TlsFailed` | 35 | The TLS handshake failed |
| `Cancelled` | 42 | `Cancel()` |
| `TooManyRedirects` | 47 | More than 10 redirects |
| `BadCertificate` | 60 | The server's certificate didn't check out |

## The drop-in: HttpsWebRequest

`Silverlight360.Https.dll` (`csharp/HttpsWebRequest.cs`), namespace `Silverlight360.Https`. It makes the platform's own `WebRequest.Create`, `HttpWebRequest` and `WebClient` go through Silverlight360.Https, so existing code needs no change:

```csharp
HttpsRequestCreator.Register();         // https://; Register(true) takes http:// too
```

| | |
|---|---|
| `HttpsRequestCreator.Register()`, `Register(bool http)` | `WebRequest.RegisterPrefix` for `https://`, and `http://` with `true`; false if a prefix couldn't be registered. Once, at the app's start: it holds until the app ends |
| `HttpsWebRequest : HttpWebRequest` | `Method`, `Headers`, `ContentType`, `Accept`, `CookieContainer`, `BeginGetRequestStream`/`EndGetRequestStream`, `BeginGetResponse`/`EndGetResponse` and `Abort`, as the platform's; and `MaxResponseBytes` and `TimeoutSeconds` of its own |
| `HttpsWebRequest.UserAgent` | The `User-Agent` to send, where the request is typed `HttpsWebRequest`. The platform's `HttpWebRequest.UserAgent` can't be used on any class but its own: it throws `NotImplementedException` (its `GetUserAgent` and `SetUserAgent` are internal to `System.Net`), and `Headers` won't take a `User-Agent` |
| `HttpsWebRequest.DefaultUserAgent` | Sent when a request gives none: `"Silverlight360.Https"` unless set. Some servers, GitHub's API among them, turn away a request without one |
| `HttpsWebResponse : HttpWebResponse` | `StatusCode`, `StatusDescription`, `Headers` (a `WebHeaderCollection`), `ContentType`, `ContentLength`, `ResponseUri` (after redirects), `Cookies`, `GetResponseStream()`, and `TlsVersion` |

As the platform's client stack does:

- callbacks come on a thread pool thread, and `WebClient` takes its events to the thread that started it;
- a status of 400 or more throws a `WebException` from `EndGetResponse` with the response in it, its `Status` `UnknownError` (this runtime's `WebExceptionStatus` has no `ProtocolError`), so `WebClient`'s `e.Error` has it;
- no response is `ConnectFailure`, `Abort()` is `RequestCanceled`, and a body over `MaxResponseBytes` is `MessageLengthLimitExceeded`.

Requests share a pool of four clients, so a host's connection stays open from one request to the next. The response is read whole before `EndGetResponse` returns, whatever `AllowReadStreamBuffering` says. Credentials (`ICredentials`) aren't sent.
