using System;
using System.Runtime.InteropServices;
using System.Text;
using XboxSdk.Samples;

namespace Https360
{
    /// <summary>
    /// HTTP and HTTPS requests from the console with https360.xex, libcurl
    /// on Mbed TLS (TLS 1.2 and 1.3), which goes in the package beside
    /// default.xex with cacert.pem, the root certificates servers are checked
    /// against. A client keeps its connections open between requests, so a
    /// second request to a host skips the TLS handshake.
    /// </summary>
    /// <remarks>
    /// Requests block until the response is in, so make them on a thread of
    /// their own. One request at a time per HttpsClient; Cancel and the
    /// progress properties are for other threads, such as the UI's.
    /// </remarks>
    public sealed class HttpsClient : IDisposable
    {
        /// <summary>The heap Start gives the DLL by default.</summary>
        public const uint DefaultHeapBytes = 96u << 20;

        /// <summary>Mozilla's root certificates in the app's own folder.</summary>
        public const string DefaultCaFile = @"game:\cacert.pem";

        private const string Module = "https360.xex";

        // src/https.c's Transfer, big-endian.
        private const int CancelOffset = 0;
        private const int FlagsOffset = 4;
        private const int BodyOffset = 8;
        private const int BodyBytesOffset = 12;
        private const int TimeoutOffset = 16;
        private const int ConnectTimeoutOffset = 20;
        private const int MaxResponseOffset = 24;
        private const int StatusOffset = 28;
        private const int ReceivedOffset = 32;
        private const int TotalOffset = 40;
        private const int ResponseBytesOffset = 48;
        private const int HeadersBytesOffset = 52;
        private const int RedirectsOffset = 56;
        private const int ElapsedOffset = 60;
        private const int ErrorOffset = 64;
        private const int ErrorBytes = 256;
        private const int TlsVersionOffset = ErrorOffset + ErrorBytes;
        private const int TlsVersionBytes = 32;
        private const int CipherOffset = TlsVersionOffset + TlsVersionBytes;
        private const int CipherBytes = 64;
        private const int UrlOffset = CipherOffset + CipherBytes;
        private const int UrlBytes = 256;
        private const int TransferBytes = UrlOffset + UrlBytes;

        private const uint FollowRedirectsFlag = 1;
        private const uint InsecureFlag = 2;
        private const uint ResumeFlag = 4;
        private const uint VerboseFlag = 8;

        private const uint BodyPart = 0;
        private const uint HeadersPart = 1;

        private static readonly object startLock = new object();
        private static bool started;

        private readonly NativeBuffer transfer = new NativeBuffer(TransferBytes);
        private uint handle;

        [DllImport(Module, EntryPoint = "#1")]
        private static extern uint https360_version();

        [DllImport(Module, EntryPoint = "#2")]
        private static extern int https360_init(uint heapBytes);

        [DllImport(Module, EntryPoint = "#3", CharSet = CharSet.Ansi)]
        private static extern uint https360_open(string caFile);

        [DllImport(Module, EntryPoint = "#4", CharSet = CharSet.Ansi)]
        private static extern int https360_resolve(uint client, string entry);

        [DllImport(Module, EntryPoint = "#5", CharSet = CharSet.Ansi)]
        private static extern int https360_perform(uint client, string method, string url, string headers,
            string outputPath, uint transfer);

        [DllImport(Module, EntryPoint = "#6")]
        private static extern uint https360_copy(uint client, uint which, uint destination, uint capacity);

        [DllImport(Module, EntryPoint = "#7")]
        private static extern void https360_close(uint client);

        [DllImport(Module, EntryPoint = "#8")]
        private static extern uint https360_heap_committed();

        [DllImport(Module, EntryPoint = "#9")]
        private static extern uint https360_heap_peak();

        /// <summary>A client checking servers against DefaultCaFile.</summary>
        public HttpsClient() : this(DefaultCaFile)
        {
        }

        /// <summary>A client checking servers against the PEM root certificates in caFile.</summary>
        public HttpsClient(string caFile)
        {
            Start(DefaultHeapBytes);
            FollowRedirects = true;
            ConnectTimeoutSeconds = 30;
            handle = https360_open(caFile);
            if (handle == 0)
            {
                transfer.Dispose();
                throw new InvalidOperationException("https360_open failed");
            }
        }

        /// <summary>
        /// Sets the DLL's runtime up with a heap of up to heapBytes, reserved
        /// now and committed as it's used. Once; the first client does it with
        /// DefaultHeapBytes if nothing has.
        /// </summary>
        public static void Start(uint heapBytes)
        {
            lock (startLock)
            {
                if (started)
                {
                    return;
                }
                int status = https360_init(heapBytes);
                if (status != 0)
                {
                    throw new InvalidOperationException("https360_init failed: 0x" + status.ToString("X8"));
                }
                started = true;
            }
        }

        /// <summary>The wrapper's API version, then curl's, a 16-bit half each: 0x00010817 for curl 8.23.</summary>
        public static uint Version
        {
            get { return https360_version(); }
        }

        /// <summary>The DLL's heap in use, in bytes.</summary>
        public static uint HeapCommitted
        {
            get { return https360_heap_committed(); }
        }

        /// <summary>The most the DLL's heap has held at once, in bytes: what to size Start's heap by.</summary>
        public static uint HeapPeak
        {
            get { return https360_heap_peak(); }
        }

        /// <summary>Whether to follow 3xx responses' Location, up to 10 times; true unless set.</summary>
        public bool FollowRedirects { get; set; }

        /// <summary>The longest a request may take, 0 for no limit.</summary>
        public int TimeoutSeconds { get; set; }

        /// <summary>The longest a connection may take to open, name lookup and TLS handshake included; 30 unless set.</summary>
        public int ConnectTimeoutSeconds { get; set; }

        /// <summary>The largest response body kept in memory, 0 for the DLL's 64 MB; Download has no limit.</summary>
        public int MaxResponseBytes { get; set; }

        /// <summary>Skips certificate and name checks: for a test server only.</summary>
        public bool Insecure { get; set; }

        /// <summary>Writes curl's trace of each request to the app's log, default.log in game:\.</summary>
        public bool Verbose { get; set; }

        /// <summary>
        /// Sends a host and port to another address for every request after,
        /// as "localhost:7381:192.168.0.125". The certificate is still checked
        /// against the name.
        /// </summary>
        public void Resolve(string entry)
        {
            int code = https360_resolve(handle, entry);
            if (code != 0)
            {
                throw new HttpsException(code, "can't add " + entry);
            }
        }

        public HttpsResponse Get(string url)
        {
            return Send("GET", url, null, null);
        }

        public HttpsResponse Post(string url, string contentType, byte[] body)
        {
            return Send("POST", url, new[] { "Content-Type: " + contentType }, body);
        }

        /// <summary>
        /// Makes a request and waits for the whole response, its body in
        /// memory. headers are "Name: value" lines; body may be null. Throws
        /// HttpsException if no response came; any status is a response.
        /// </summary>
        public HttpsResponse Send(string method, string url, string[] headers, byte[] body)
        {
            return Perform(method, url, headers, body, null, 0);
        }

        /// <summary>
        /// Writes a GET's response body to path, a file on the console's
        /// drives such as "hdd:\Downloads\a.zip", without passing it through
        /// managed memory. With resume, a file already there is taken as the
        /// first part and the rest asked for with Range; a server that sends
        /// the whole file again starts it over. The response's Body is null.
        /// </summary>
        public HttpsResponse Download(string url, string path, bool resume)
        {
            return Perform("GET", url, null, null, path, resume ? ResumeFlag : 0);
        }

        /// <summary>Asks the request to stop at its next block of data; it throws HttpsException with Cancelled.</summary>
        public void Cancel()
        {
            transfer.SetU32(CancelOffset, 1);
        }

        /// <summary>The response body's bytes in so far, a resumed download's first part among them.</summary>
        public long BytesReceived
        {
            get { return (long)transfer.U64(ReceivedOffset); }
        }

        /// <summary>The response body's whole size, -1 until the server says.</summary>
        public long BytesTotal
        {
            get { return (long)transfer.U64(TotalOffset); }
        }

        private HttpsResponse Perform(string method, string url, string[] headers, byte[] body, string path,
            uint flags)
        {
            Array.Clear(transfer.Bytes, 0, TransferBytes);
            flags |= (FollowRedirects ? FollowRedirectsFlag : 0) | (Insecure ? InsecureFlag : 0) |
                (Verbose ? VerboseFlag : 0);
            transfer.SetU32(FlagsOffset, flags);
            transfer.SetU32(TimeoutOffset, (uint)Math.Max(0, TimeoutSeconds));
            transfer.SetU32(ConnectTimeoutOffset, (uint)Math.Max(0, ConnectTimeoutSeconds));
            transfer.SetU32(MaxResponseOffset, (uint)Math.Max(0, MaxResponseBytes));
            NativeBuffer content = body != null && body.Length > 0 ? new NativeBuffer(body) : null;
            int code;
            try
            {
                if (content != null)
                {
                    transfer.SetU32(BodyOffset, content.Address);
                    transfer.SetU32(BodyBytesOffset, (uint)body.Length);
                }
                code = https360_perform(handle, method, url, headers != null ? string.Join("\n", headers) : null,
                    path, transfer.Address);
            }
            finally
            {
                if (content != null)
                {
                    content.Dispose();
                }
            }
            if (code != 0)
            {
                throw new HttpsException(code, transfer.Ansi(ErrorOffset, ErrorBytes));
            }
            var response = new HttpsResponse();
            response.Status = (int)transfer.U32(StatusOffset);
            response.Url = transfer.Ansi(UrlOffset, UrlBytes);
            response.TlsVersion = transfer.Ansi(TlsVersionOffset, TlsVersionBytes);
            response.Cipher = transfer.Ansi(CipherOffset, CipherBytes);
            response.Redirects = (int)transfer.U32(RedirectsOffset);
            response.ElapsedMilliseconds = (int)transfer.U32(ElapsedOffset);
            response.Headers = Encoding.UTF8.GetString(
                Copy(HeadersPart, (int)transfer.U32(HeadersBytesOffset)), 0, (int)transfer.U32(HeadersBytesOffset));
            response.Body = path == null ? Copy(BodyPart, (int)transfer.U32(ResponseBytesOffset)) : null;
            return response;
        }

        private byte[] Copy(uint which, int length)
        {
            if (length == 0)
            {
                return new byte[0];
            }
            using (var buffer = new NativeBuffer(length))
            {
                https360_copy(handle, which, buffer.Address, (uint)length);
                return buffer.Length == length ? buffer.Bytes : buffer.Slice(0, length);
            }
        }

        public void Dispose()
        {
            if (handle != 0)
            {
                https360_close(handle);
                handle = 0;
            }
            transfer.Dispose();
        }
    }

    public sealed class HttpsResponse
    {
        /// <summary>The HTTP status, as 200 or 404.</summary>
        public int Status { get; internal set; }

        /// <summary>The final response's header lines as they came, its status line first.</summary>
        public string Headers { get; internal set; }

        /// <summary>The body; null for a Download, which wrote it to its file.</summary>
        public byte[] Body { get; internal set; }

        /// <summary>The URL the response came from, after redirects.</summary>
        public string Url { get; internal set; }

        /// <summary>"TLSv1.3" or "TLSv1.2", empty over HTTP.</summary>
        public string TlsVersion { get; internal set; }

        /// <summary>Mbed TLS's name for the cipher suite, as "TLS1-3-CHACHA20-POLY1305-SHA256".</summary>
        public string Cipher { get; internal set; }

        public int Redirects { get; internal set; }

        public int ElapsedMilliseconds { get; internal set; }

        /// <summary>The body as UTF-8.</summary>
        public string Text
        {
            get { return Body == null ? null : Encoding.UTF8.GetString(Body, 0, Body.Length); }
        }

        /// <summary>The first value of a header, its name in any case; null if there's none.</summary>
        public string Header(string name)
        {
            foreach (string line in Headers.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && string.Compare(line.Substring(0, colon).Trim(), name,
                    StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return line.Substring(colon + 1).Trim();
                }
            }
            return null;
        }
    }

    /// <summary>A request that got no response; Code is curl's CURLcode.</summary>
    public sealed class HttpsException : Exception
    {
        public const int CouldNotResolve = 6;
        public const int CouldNotConnect = 7;
        public const int WriteError = 23;
        public const int TimedOut = 28;
        public const int Cancelled = 42;
        public const int TooManyRedirects = 47;
        public const int TlsFailed = 35;
        public const int BadCertificate = 60;

        public HttpsException(int code, string message) : base(message + " (curl " + code + ")")
        {
            Code = code;
        }

        public int Code { get; private set; }
    }
}
