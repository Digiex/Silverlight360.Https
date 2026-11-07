using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace Silverlight360.Https
{
    /// <summary>
    /// Sends the platform's own web requests through Silverlight360.Https: once Register
    /// has run, WebRequest.Create and WebClient make an HttpsWebRequest for
    /// https:// (and http:// if asked), so code written for HttpWebRequest
    /// gets TLS 1.3 and Mozilla's roots without changing.
    /// </summary>
    /// <example>
    /// HttpsRequestCreator.Register();          // once, at the app's start
    /// var web = new WebClient();
    /// web.DownloadStringCompleted += (s, e) => Show(e.Result);
    /// web.DownloadStringAsync(new Uri("https://example.com/"));
    /// </example>
    public sealed class HttpsRequestCreator : IWebRequestCreate
    {
        public static readonly HttpsRequestCreator Instance = new HttpsRequestCreator();

        private HttpsRequestCreator()
        {
        }

        /// <summary>Sends https:// requests through Silverlight360.Https; false if the prefix couldn't be registered.</summary>
        public static bool Register()
        {
            return Register(false);
        }

        /// <summary>Sends https://, and with http also http://, requests through Silverlight360.Https.</summary>
        public static bool Register(bool http)
        {
            bool registered = WebRequest.RegisterPrefix("https://", Instance);
            return http ? WebRequest.RegisterPrefix("http://", Instance) && registered : registered;
        }

        public WebRequest Create(Uri uri)
        {
            return new HttpsWebRequest(uri);
        }
    }

    /// <summary>
    /// An HttpWebRequest made by HttpsClient. Its callbacks come on a thread
    /// pool thread, as the platform's client stack's do; WebClient takes its
    /// events to the thread that started it. The whole response is read
    /// before EndGetResponse returns, as with AllowReadStreamBuffering.
    /// </summary>
    /// <remarks>
    /// A response with a status of 400 or more throws a WebException from
    /// EndGetResponse with the response in it, its Status UnknownError, as
    /// the platform's does. No response at all is ConnectFailure, an abort
    /// RequestCanceled, and a body over MaxResponseBytes
    /// MessageLengthLimitExceeded. Credentials aren't sent.
    /// </remarks>
    public class HttpsWebRequest : HttpWebRequest
    {
        /// <summary>Sent when a request gives no User-Agent: some servers, GitHub's API among them, turn away none.</summary>
        public static string DefaultUserAgent = "Silverlight360.Https";

        // Clients kept between requests, so their connections stay open.
        private const int PooledClients = 4;
        private static readonly Stack<HttpsClient> pool = new Stack<HttpsClient>();

        private readonly Uri requestUri;
        private string method = "GET";
        private CookieContainer cookies;
        private bool allowReadStreamBuffering = true;
        private RequestStream body;
        private HttpsClient running;
        private bool aborted;
        private bool started;
        private Result result;

        public HttpsWebRequest(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException("uri");
            }
            requestUri = uri;
        }

        public override Uri RequestUri
        {
            get { return requestUri; }
        }

        public override string Method
        {
            get { return method; }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentException("A method is needed.", "value");
                }
                method = value;
            }
        }

        // Headers, ContentType and Accept are the base's: they share its
        // collection, which Accept and ContentType write through
        // SetSpecialHeader.

        /// <summary>
        /// The User-Agent to send, or null for DefaultUserAgent. The base's
        /// UserAgent can't be used: its GetUserAgent and SetUserAgent are
        /// System.Net's own and throw NotImplementedException for any other
        /// class, so this works where the request is an HttpsWebRequest.
        /// </summary>
        public new string UserAgent { get; set; }

        public override CookieContainer CookieContainer
        {
            get { return cookies; }
            set { cookies = value; }
        }

        public override bool SupportsCookieContainer
        {
            get { return true; }
        }

        /// <summary>Kept for code that sets it; the response is always read whole.</summary>
        public override bool AllowReadStreamBuffering
        {
            get { return allowReadStreamBuffering; }
            set { allowReadStreamBuffering = value; }
        }

        public override bool HaveResponse
        {
            get { return result != null && result.IsCompleted && result.Response != null; }
        }

        /// <summary>The largest body kept, 0 for the DLL's 64 MB.</summary>
        public int MaxResponseBytes { get; set; }

        /// <summary>The longest the request may take, 0 for no limit.</summary>
        public int TimeoutSeconds { get; set; }

        public override IAsyncResult BeginGetRequestStream(AsyncCallback callback, object state)
        {
            if (started)
            {
                throw new InvalidOperationException("The request has been sent.");
            }
            if (body == null)
            {
                body = new RequestStream();
            }
            Result stream = new Result(callback, state);
            stream.Complete(null, null);
            return stream;
        }

        public override Stream EndGetRequestStream(IAsyncResult asyncResult)
        {
            if (aborted)
            {
                throw new WebException("The request was aborted.", null, WebExceptionStatus.RequestCanceled, null);
            }
            return body;
        }

        public override IAsyncResult BeginGetResponse(AsyncCallback callback, object state)
        {
            if (started)
            {
                throw new InvalidOperationException("The request has been sent.");
            }
            started = true;
            result = new Result(callback, state);
            if (aborted)
            {
                result.Complete(null, Cancelled());
                return result;
            }
            ThreadPool.QueueUserWorkItem(delegate { Perform(); });
            return result;
        }

        public override WebResponse EndGetResponse(IAsyncResult asyncResult)
        {
            Result done = asyncResult as Result;
            if (done == null || done != result)
            {
                throw new ArgumentException("That isn't this request's.", "asyncResult");
            }
            done.AsyncWaitHandle.WaitOne();
            if (done.Error != null)
            {
                throw done.Error;
            }
            return done.Response;
        }

        public override void Abort()
        {
            HttpsClient client;
            lock (this)
            {
                aborted = true;
                client = running;
            }
            if (client != null)
            {
                client.Cancel();
            }
            else if (result != null && !result.IsCompleted)
            {
                result.Complete(null, Cancelled());
            }
        }

        private void Perform()
        {
            HttpsWebResponse response = null;
            WebException error = null;
            HttpsClient client = Take();
            try
            {
                lock (this)
                {
                    if (aborted)
                    {
                        throw Cancelled();
                    }
                    running = client;
                }
                client.MaxResponseBytes = MaxResponseBytes;
                client.TimeoutSeconds = TimeoutSeconds;
                byte[] content = body != null ? body.ToArray() : null;
                HttpsResponse answer = client.Send(method, requestUri.AbsoluteUri, RequestHeaders(), content);
                Uri at = new Uri(answer.Url.Length > 0 ? answer.Url : requestUri.AbsoluteUri);
                response = new HttpsWebResponse(method, at, answer);
                if (cookies != null)
                {
                    foreach (string set in response.SetCookies)
                    {
                        try
                        {
                            cookies.SetCookies(at, set);
                        }
                        catch (Exception)
                        {
                            // A cookie the container won't take is left out, as a browser does.
                        }
                    }
                    response.SetCookieCollection(cookies.GetCookies(at));
                }
                if (answer.Status >= 400)
                {
                    error = new WebException(string.Format("The remote server returned an error: ({0}) {1}.", answer.Status,
                        response.StatusDescription), null, WebExceptionStatus.UnknownError, response);
                    response = null;
                }
            }
            catch (HttpsException e)
            {
                // In memory, a write error is a body over MaxResponseBytes
                // (src/https.c's on_write refuses it), or no memory for it.
                error = e.Code == HttpsException.Cancelled ? Cancelled()
                    : new WebException(e.Message, e, e.Code == HttpsException.WriteError
                        ? WebExceptionStatus.MessageLengthLimitExceeded : WebExceptionStatus.ConnectFailure, null);
            }
            catch (WebException e)
            {
                error = e;
            }
            catch (Exception e)
            {
                error = new WebException(e.Message, e, WebExceptionStatus.UnknownError, null);
            }
            finally
            {
                lock (this)
                {
                    running = null;
                }
                Give(client);
            }
            result.Complete(response, error);
        }

        // The headers as "Name: value" lines (Accept and Content-Type among
        // them), with the cookies for the address and a User-Agent.
        private string[] RequestHeaders()
        {
            List<string> lines = new List<string>();
            WebHeaderCollection headers = Headers;
            bool userAgent = false;
            foreach (string name in headers.AllKeys)
            {
                lines.Add(name + ": " + headers[name]);
                userAgent |= string.Compare(name, "User-Agent", StringComparison.OrdinalIgnoreCase) == 0;
            }
            string agent = UserAgent ?? DefaultUserAgent;
            if (!userAgent && !string.IsNullOrEmpty(agent))
            {
                lines.Add("User-Agent: " + agent);
            }
            if (cookies != null && headers["Cookie"] == null)
            {
                string cookie = cookies.GetCookieHeader(requestUri);
                if (!string.IsNullOrEmpty(cookie))
                {
                    lines.Add("Cookie: " + cookie);
                }
            }
            return lines.ToArray();
        }

        private static WebException Cancelled()
        {
            return new WebException("The request was aborted.", null, WebExceptionStatus.RequestCanceled, null);
        }

        private static HttpsClient Take()
        {
            lock (pool)
            {
                if (pool.Count > 0)
                {
                    return pool.Pop();
                }
            }
            return new HttpsClient();
        }

        private static void Give(HttpsClient client)
        {
            lock (pool)
            {
                if (pool.Count < PooledClients)
                {
                    pool.Push(client);
                    return;
                }
            }
            client.Dispose();
        }

        // The request's body, kept when the caller closes it, as it does
        // before asking for the response.
        private sealed class RequestStream : MemoryStream
        {
            private byte[] closed;

            protected override void Dispose(bool disposing)
            {
                if (closed == null)
                {
                    closed = base.ToArray();
                }
                base.Dispose(disposing);
            }

            public override byte[] ToArray()
            {
                return closed ?? base.ToArray();
            }
        }

        private sealed class Result : IAsyncResult
        {
            private readonly AsyncCallback callback;
            private readonly ManualResetEvent done = new ManualResetEvent(false);
            private int completed;

            public Result(AsyncCallback callback, object state)
            {
                this.callback = callback;
                AsyncState = state;
            }

            public object AsyncState { get; private set; }
            public WaitHandle AsyncWaitHandle { get { return done; } }
            public bool CompletedSynchronously { get { return false; } }
            public bool IsCompleted { get { return completed != 0; } }
            public HttpsWebResponse Response { get; private set; }
            public WebException Error { get; private set; }

            // Once: a later call (an abort racing the response) changes nothing.
            public void Complete(HttpsWebResponse response, WebException error)
            {
                if (Interlocked.Exchange(ref completed, 1) != 0)
                {
                    return;
                }
                Response = response;
                Error = error;
                done.Set();
                if (callback != null)
                {
                    callback(this);
                }
            }
        }
    }

    /// <summary>An HttpWebResponse from HttpsWebRequest, its body in memory.</summary>
    public class HttpsWebResponse : HttpWebResponse
    {
        private readonly string method;
        private readonly Uri responseUri;
        private readonly HttpsResponse answer;
        private readonly WebHeaderCollection headers = new WebHeaderCollection();
        private readonly List<string> setCookies = new List<string>();
        private readonly string statusDescription = "";
        private CookieCollection cookies = new CookieCollection();

        internal HttpsWebResponse(string method, Uri responseUri, HttpsResponse answer)
        {
            this.method = method;
            this.responseUri = responseUri;
            this.answer = answer;
            string[] lines = answer.Headers.Replace("\r", "").Split('\n');
            if (lines.Length > 0)
            {
                // "HTTP/1.1 404 Not Found": the reason after the status.
                int first = lines[0].IndexOf(' ');
                int second = first < 0 ? -1 : lines[0].IndexOf(' ', first + 1);
                statusDescription = second < 0 ? "" : lines[0].Substring(second + 1).Trim();
            }
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                string name = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                if (string.Compare(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    setCookies.Add(value);
                }
                // A header given more than once is one, its values joined, as
                // HTTP allows; one the collection won't take is left out.
                try
                {
                    string had = headers[name];
                    headers[name] = had == null ? value : had + ", " + value;
                }
                catch (ArgumentException)
                {
                }
            }
        }

        internal IEnumerable<string> SetCookies
        {
            get { return setCookies; }
        }

        internal void SetCookieCollection(CookieCollection collection)
        {
            cookies = collection;
        }

        /// <summary>The response's TLS version, as "TLSv1.3"; empty over HTTP.</summary>
        public string TlsVersion
        {
            get { return answer.TlsVersion; }
        }

        public override Stream GetResponseStream()
        {
            return new MemoryStream(answer.Body ?? new byte[0], false);
        }

        public override void Close()
        {
        }

        public override long ContentLength
        {
            get { return answer.Body != null ? answer.Body.Length : 0; }
        }

        public override string ContentType
        {
            get { return headers["Content-Type"] ?? ""; }
        }

        public override Uri ResponseUri
        {
            get { return responseUri; }
        }

        public override string ResponseIPAddress
        {
            get { return ""; }
        }

        public override WebHeaderCollection Headers
        {
            get { return headers; }
        }

        public override bool SupportsHeaders
        {
            get { return true; }
        }

        public override string Method
        {
            get { return method; }
        }

        public override HttpStatusCode StatusCode
        {
            get { return (HttpStatusCode)answer.Status; }
        }

        public override string StatusDescription
        {
            get { return statusDescription; }
        }

        public override CookieCollection Cookies
        {
            get { return cookies; }
        }
    }
}
