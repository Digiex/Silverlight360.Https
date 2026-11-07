using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using Https360;
using XboxSdk.Samples;

namespace HttpsSample
{
    /// <summary>
    /// Checks https360 against tests\https_server.py on the PC, whose
    /// certificate is for localhost under Mbed TLS's test CA, then against
    /// public hosts with Mozilla's roots. Under xenia-edge the PC is
    /// 127.0.0.1, and hdd:\Content the --content_root folder, where the
    /// downloads go: tests\verify_downloads.py checks them.
    /// </summary>
    public partial class MainPage : UserControl
    {
        // Where the PC is, as an IPv4 address; "localhost" is sent there.
        private const string Server = "127.0.0.1";
        private const int Port = 7443;
        private const string Local = "https://localhost:7443";
        private const string LocalTls12 = "https://localhost:7444";
        private const string TestCa = @"game:\test-ca2.crt";
        private const string Out = @"hdd:\Content\HttpsTest\";
        private const int DownloadBytes = 8 << 20;
        private const int ResumeCutBytes = 2 << 20;

        private readonly DispatcherTimer timer = new DispatcherTimer();
        private HttpsClient active;
        private int passed;
        private int failed;

        public MainPage()
        {
            InitializeComponent();
            timer.Interval = TimeSpan.FromMilliseconds(500);
            timer.Tick += delegate
            {
                HttpsClient client = active;
                if (client != null)
                {
                    ProgressText.Text = (client.BytesReceived >> 10) + " KB of " +
                        (client.BytesTotal < 0 ? "?" : (client.BytesTotal >> 10).ToString()) + " KB";
                }
            };
            Loaded += delegate
            {
                timer.Start();
                new Thread(RunChecks).Start();
            };
        }

        private void RunChecks()
        {
            try
            {
                HttpsClient.Start(HttpsClient.DefaultHeapBytes);
                Report("Https360: version 0x" + HttpsClient.Version.ToString("X8"));
            }
            catch (Exception e)
            {
                Report("FAIL: start -> " + e.Message);
                return;
            }
            using (HttpsClient local = new HttpsClient(TestCa))
            {
                local.Resolve("localhost:" + Port + ":" + Server);
                local.Resolve("localhost:" + (Port + 1) + ":" + Server);
                local.Resolve("wronghost:" + Port + ":" + Server);
                active = local;
                RunLocal(local);
            }
            using (HttpsClient roots = new HttpsClient())
            {
                roots.Resolve("localhost:" + Port + ":" + Server);
                active = roots;
                Check("localhost against Mozilla's roots is refused", delegate
                {
                    return Refused(roots, Local + "/files/data_4096.bin", HttpsException.BadCertificate);
                });
                RunPublic(roots);
            }
            active = null;
            Report("Https360: checks: " + passed + " passed, " + failed + " failed, heap peak " +
                (HttpsClient.HeapPeak >> 10) + " KB");
        }

        private void RunLocal(HttpsClient client)
        {
            Check("GET 1 MB over TLS 1.3", delegate
            {
                HttpsResponse response = client.Get(Local + "/files/data_1048576.bin");
                return Expect(response.Status == 200 && response.TlsVersion == "TLSv1.3" &&
                    Sha256(response.Body) == PatternSha256(1048576), response);
            });
            Check("GET again on the same connection", delegate
            {
                HttpsResponse response = client.Get(Local + "/files/data_65536.bin");
                return Expect(response.Status == 200 && Sha256(response.Body) == PatternSha256(65536), response);
            });
            Check("GET 64 KB over TLS 1.2", delegate
            {
                HttpsResponse response = client.Get(LocalTls12 + "/files/data_65536.bin");
                return Expect(response.Status == 200 && response.TlsVersion == "TLSv1.2" &&
                    Sha256(response.Body) == PatternSha256(65536), response);
            });
            Post(client, Local, 1000);
            Post(client, Local, 16384);
            Post(client, Local, 65536);
            Post(client, Local, 100000);
            Post(client, LocalTls12, 100000);
            Post(client, Local, 1 << 20);
            Check("PUT JSON", delegate
            {
                HttpsResponse response = client.Send("PUT", Local + "/echo",
                    new[] { "Content-Type: application/json" }, Encoding.UTF8.GetBytes("{\"a\": 1}"));
                string text = response.Text;
                return Expect(response.Status == 200 && text.Contains("\"method\": \"PUT\"") &&
                    text.Contains("\"bytes\": 8") && text.Contains("\"content_type\": \"application/json\""),
                    response, text);
            });
            Check("DELETE with no body", delegate
            {
                HttpsResponse response = client.Send("DELETE", Local + "/echo", null, null);
                return Expect(response.Status == 200 && response.Text.Contains("\"bytes\": 0"), response,
                    response.Text);
            });
            Check("HEAD", delegate
            {
                HttpsResponse response = client.Send("HEAD", Local + "/files/data_4096.bin", null, null);
                return Expect(response.Status == 200 && response.Body.Length == 0 &&
                    response.Header("Content-Length") == "4096", response);
            });
            Check("a redirect followed", delegate
            {
                HttpsResponse response = client.Get(Local + "/redirect");
                return Expect(response.Status == 200 && response.Redirects == 1 &&
                    response.Url.EndsWith("/files/data_4096.bin") && response.Body.Length == 4096, response);
            });
            Check("a redirect not followed", delegate
            {
                client.FollowRedirects = false;
                try
                {
                    HttpsResponse response = client.Get(Local + "/redirect");
                    return Expect(response.Status == 302 && response.Header("location") == "/files/data_4096.bin",
                        response);
                }
                finally
                {
                    client.FollowRedirects = true;
                }
            });
            Check("404 is a response", delegate
            {
                HttpsResponse response = client.Get(Local + "/status/404");
                return Expect(response.Status == 404, response);
            });
            Check("a response over MaxResponseBytes is refused", delegate
            {
                client.MaxResponseBytes = 65536;
                try
                {
                    return Refused(client, Local + "/files/data_1048576.bin", HttpsException.WriteError);
                }
                finally
                {
                    client.MaxResponseBytes = 0;
                }
            });
            Check("Download 8 MB to hdd:", delegate
            {
                HttpsResponse response = client.Download(Local + "/files/data_" + DownloadBytes + ".bin",
                    Out + "full.bin", false);
                return Expect(response.Status == 200 && client.BytesReceived == DownloadBytes, response);
            });
            Check("Download cancelled after 2 MB", delegate
            {
                // Watches from another thread, as an app's Cancel button would.
                using (new Timer(delegate
                {
                    if (client.BytesReceived >= ResumeCutBytes)
                    {
                        client.Cancel();
                    }
                }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(2)))
                {
                    return Refused(delegate
                    {
                        return client.Download(Local + "/files/data_" + DownloadBytes + ".bin", Out + "resumed.bin",
                            false);
                    }, HttpsException.Cancelled);
                }
            });
            Check("Download resumed", delegate
            {
                HttpsResponse response = client.Download(Local + "/files/data_" + DownloadBytes + ".bin",
                    Out + "resumed.bin", true);
                return Expect(response.Status == 206 && client.BytesReceived == DownloadBytes &&
                    client.BytesTotal == DownloadBytes, response);
            });
            Check("the wrong name is refused", delegate
            {
                return Refused(client, "https://wronghost:" + Port + "/files/data_4096.bin",
                    HttpsException.BadCertificate);
            });
            Check("the wrong name taken with Insecure", delegate
            {
                client.Insecure = true;
                try
                {
                    HttpsResponse response = client.Get("https://wronghost:" + Port + "/files/data_4096.bin");
                    return Expect(response.Status == 200 && response.Body.Length == 4096, response);
                }
                finally
                {
                    client.Insecure = false;
                }
            });
            Check("a closed port is refused", delegate
            {
                client.Resolve("localhost:7499:" + Server);
                return Refused(client, "https://localhost:7499/", HttpsException.CouldNotConnect);
            });
        }

        private void Post(HttpsClient client, string server, int bytes)
        {
            Check("POST " + bytes + " bytes to " + server.Substring(server.LastIndexOf(':') + 1), delegate
            {
                byte[] body = new byte[bytes];
                for (int i = 0; i < 256 && i < bytes; i++)
                {
                    body[i] = (byte)i;
                }
                // Filled by doubling, through the framework's copy, as app code is interpreted.
                for (int filled = 256; filled < bytes; filled *= 2)
                {
                    Buffer.BlockCopy(body, 0, body, filled, Math.Min(filled, bytes - filled));
                }
                HttpsResponse response = client.Send("POST", server + "/echo",
                    new[] { "Content-Type: application/octet-stream", "X-Test: xbox360" }, body);
                string text = response.Text;
                return Expect(response.Status == 200 && text.Contains("\"method\": \"POST\"") &&
                    text.Contains("\"bytes\": " + bytes + ",") && text.Contains("\"sha256\": \"" + Sha256(body) + "\"") &&
                    text.Contains("\"x_test\": \"xbox360\""), response, text);
            });
        }

        private void RunPublic(HttpsClient client)
        {
            client.TimeoutSeconds = 30;
            Check("public: example.com", delegate
            {
                HttpsResponse response = client.Get("https://example.com/");
                return Expect(response.Status == 200 && response.Text.Contains("Example Domain"), response);
            });
            Check("public: www.google.com", delegate
            {
                HttpsResponse response = client.Get("https://www.google.com/");
                return Expect(response.Status == 200, response);
            });
            Check("public: github.com's API", delegate
            {
                HttpsResponse response = client.Send("GET", "https://api.github.com/zen",
                    new[] { "User-Agent: Https360" }, null);
                return Expect(response.Status == 200 && response.Body.Length > 0, response, response.Text);
            });
            Check("public: TLS 1.2 only (tls-v1-2.badssl.com)", delegate
            {
                HttpsResponse response = client.Get("https://tls-v1-2.badssl.com:1012/");
                return Expect(response.Status == 200 && response.TlsVersion == "TLSv1.2", response);
            });
            Check("public: expired.badssl.com is refused", delegate
            {
                return Refused(client, "https://expired.badssl.com/", HttpsException.BadCertificate);
            });
            Check("public: wrong.host.badssl.com is refused", delegate
            {
                return Refused(client, "https://wrong.host.badssl.com/", HttpsException.BadCertificate);
            });
            Check("public: self-signed.badssl.com is refused", delegate
            {
                return Refused(client, "https://self-signed.badssl.com/", HttpsException.BadCertificate);
            });
            Check("public: untrusted-root.badssl.com is refused", delegate
            {
                return Refused(client, "https://untrusted-root.badssl.com/", HttpsException.BadCertificate);
            });
        }

        private delegate string Run();

        private delegate HttpsResponse Request();

        // A check's outcome: null when it passed, else what went wrong.
        private void Check(string name, Run run)
        {
            DebugLog.WriteLine("Https360: " + name + "...");
            int start = Environment.TickCount;
            string outcome;
            try
            {
                outcome = run();
            }
            catch (HttpsException e)
            {
                outcome = "! " + e.Message;
            }
            int milliseconds = Environment.TickCount - start;
            bool ok = outcome != null && !outcome.StartsWith("! ");
            string line = name + " -> " + (outcome ?? "") + " (" + milliseconds + " ms)";
            if (ok)
            {
                passed++;
                Report("PASS: " + line);
            }
            else
            {
                failed++;
                Report("FAIL: " + line);
            }
        }

        private static string Expect(bool ok, HttpsResponse response)
        {
            return Expect(ok, response, null);
        }

        private static string Expect(bool ok, HttpsResponse response, string detail)
        {
            string summary = response.Status + ", " + (response.Body == null ? "to a file" :
                response.Body.Length + " bytes") + (response.TlsVersion.Length > 0 ? ", " + response.TlsVersion + " " +
                response.Cipher : "") + ", " + response.ElapsedMilliseconds + " ms in curl";
            if (!ok && detail != null)
            {
                summary += ": " + (detail.Length > 200 ? detail.Substring(0, 200) : detail);
            }
            return (ok ? "" : "! ") + summary;
        }

        private static string Refused(HttpsClient client, string url, int code)
        {
            return Refused(delegate { return client.Get(url); }, code);
        }

        private static string Refused(Request request, int code)
        {
            try
            {
                HttpsResponse response = request();
                return "! answered " + response.Status;
            }
            catch (HttpsException e)
            {
                return (e.Code == code ? "" : "! ") + e.Message;
            }
        }

        private static string Sha256(byte[] bytes)
        {
            byte[] hash = new SHA256Managed().ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        // tests\https_server.py's data files: byte i is (i * 31 + 7) & 0xFF.
        private static string PatternSha256(int size)
        {
            byte[] bytes = new byte[size];
            for (int i = 0; i < 256 && i < size; i++)
            {
                bytes[i] = (byte)(i * 31 + 7);
            }
            for (int filled = 256; filled < size; filled *= 2)
            {
                Buffer.BlockCopy(bytes, 0, bytes, filled, Math.Min(filled, size - filled));
            }
            return Sha256(bytes);
        }

        private void Report(string line)
        {
            DebugLog.WriteLine(line);
            Dispatcher.BeginInvoke(delegate { ResultsText.Text += line + "\n"; });
        }

        public void ShowError(Exception error)
        {
            Report("Unhandled: " + error.GetType().Name + ": " + error.Message);
        }
    }
}
