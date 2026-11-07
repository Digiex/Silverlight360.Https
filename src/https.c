// The DLL's HTTP and HTTPS API, on libcurl's easy interface, for csharp/HttpsClient.cs. A client is one curl handle,
// which keeps its connections open between requests, so a second request to a host skips the TLS handshake. A request
// fills a Transfer the caller pinned, big-endian as the console is, which other threads can read while it runs.
#include <errno.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <unistd.h>

#include <curl/curl.h>
#include <mbedtls/ssl.h>

#include "export.h"
#include "runtime.h"
#include "xsdk/log.h"
#include "xsdk/net.h"

enum {
	API_VERSION = 1,
	// What a request's response body may grow to in memory unless the Transfer says otherwise.
	DEFAULT_MAX_RESPONSE_BYTES = 64 << 20,
	HEADERS_MAX_BYTES = 64 << 10,
	// One socket's receive buffer, the most XAM takes; it doubled a download's rate (xsdk docs/GUIDE.md, "Downloading").
	SOCKET_RECEIVE_BYTES = 255 << 10,
	RECEIVE_BUFFER_BYTES = 256 << 10,
	MAX_REDIRECTS = 10,
	ERROR_BYTES = 256,
	TLS_VERSION_BYTES = 32,
	CIPHER_BYTES = 64,
	URL_BYTES = 256,
};

// What XAM's network starts with for this process, if the DLL is first to start it: its buffers raised to the most it
// takes (xsdk include/xsdk/net.h). The Silverlight runtime may have started it already, with its own.
const XsdkNetworkLimits xsdk_network_limits = {
	.stream_sockets = 32,
	.datagram_sockets = 8,
	.receive_kb = 63,
	.send_kb = 63,
};

enum {
	FLAG_FOLLOW_REDIRECTS = 1u << 0,
	// No certificate or name checks: for a test server, never a real one.
	FLAG_INSECURE = 1u << 1,
	// Append to output_path, asking for the rest with Range from its length.
	FLAG_RESUME = 1u << 2,
	// curl's trace of the request, its headers and what it does with the connection, in the log.
	FLAG_VERBOSE = 1u << 3,
};

// A request's settings and results, in memory the caller pinned (csharp/HttpsClient.cs keeps the same offsets).
typedef struct {
	// Set by the caller.
	volatile uint32_t cancel;
	uint32_t flags;
	uint32_t body;
	uint32_t body_bytes;
	uint32_t timeout_seconds;
	uint32_t connect_timeout_seconds;
	uint32_t max_response_bytes;
	// Set by the request.
	int32_t status;
	volatile uint64_t received;
	int64_t total;
	uint32_t response_bytes;
	uint32_t headers_bytes;
	uint32_t redirects;
	uint32_t elapsed_ms;
	char error[ERROR_BYTES];
	char tls_version[TLS_VERSION_BYTES];
	char cipher[CIPHER_BYTES];
	char url[URL_BYTES];
} Transfer;

_Static_assert(sizeof(Transfer) == 672, "csharp/HttpsClient.cs's offsets");

typedef struct {
	char *bytes;
	size_t length;
	size_t capacity;
} Bytes;

typedef struct {
	CURL *easy;
	char *ca_file;
	struct curl_slist *resolve;
	char error[CURL_ERROR_SIZE];
	// The request's; the response's body and headers stay until the next request, for https360_copy.
	Transfer *transfer;
	Bytes body;
	Bytes headers;
	FILE *file;
	int started_body;
	int session_read;
	uint64_t resume_from;
	size_t max_response_bytes;
	char failure[ERROR_BYTES];
} Client;

static int curl_started;

static void copy_text(char *destination, size_t capacity, const char *source)
{
	if (!source)
		source = "";
	size_t length = strlen(source);
	if (length >= capacity)
		length = capacity - 1;
	memcpy(destination, source, length);
	destination[length] = '\0';
}

static int append(Bytes *bytes, const char *data, size_t length, size_t limit)
{
	if (length > limit - bytes->length)
		return 0;
	if (bytes->length + length > bytes->capacity) {
		size_t capacity = bytes->capacity ? bytes->capacity : 16 << 10;
		while (capacity < bytes->length + length)
			capacity = capacity > limit / 2 ? limit : capacity * 2;
		char *grown = realloc(bytes->bytes, capacity);
		if (!grown)
			return 0;
		bytes->bytes = grown;
		bytes->capacity = capacity;
	}
	memcpy(bytes->bytes + bytes->length, data, length);
	bytes->length += length;
	return 1;
}

static void clear(Bytes *bytes)
{
	free(bytes->bytes);
	bytes->bytes = NULL;
	bytes->length = 0;
	bytes->capacity = 0;
}

// The TLS version and cipher, from Mbed TLS's context while the connection is open: curl's pointer to it is good only
// during the transfer (CURLINFO_TLS_SSL_PTR).
static void read_session(Client *client)
{
	if (client->session_read)
		return;
	client->session_read = 1;
	struct curl_tlssessioninfo *session = NULL;
	if (curl_easy_getinfo(client->easy, CURLINFO_TLS_SSL_PTR, &session) != CURLE_OK || !session ||
		session->backend != CURLSSLBACKEND_MBEDTLS || !session->internals)
		return;
	const mbedtls_ssl_context *ssl = session->internals;
	copy_text(client->transfer->tls_version, TLS_VERSION_BYTES, mbedtls_ssl_get_version(ssl));
	copy_text(client->transfer->cipher, CIPHER_BYTES, mbedtls_ssl_get_ciphersuite(ssl));
}

static size_t on_header(char *data, size_t size, size_t count, void *context)
{
	Client *client = context;
	const size_t length = size * count;
	read_session(client);
	// A new status line starts a response's headers: those of a redirect followed, or of a 100 Continue, are dropped.
	if (length >= 5 && memcmp(data, "HTTP/", 5) == 0)
		client->headers.length = 0;
	if (!append(&client->headers, data, length, HEADERS_MAX_BYTES)) {
		copy_text(client->failure, ERROR_BYTES, "response headers too large");
		return 0;
	}
	return length;
}

static size_t on_body(char *data, size_t size, size_t count, void *context)
{
	Client *client = context;
	const size_t length = size * count;
	read_session(client);
	if (!client->started_body) {
		client->started_body = 1;
		long status = 0;
		(void)curl_easy_getinfo(client->easy, CURLINFO_RESPONSE_CODE, &status);
		// A server that answers a Range with 200 sends the whole file again, so the part starts over.
		if (client->file && client->resume_from && status == 200) {
			if (ftruncate(fileno(client->file), 0) != 0 || fseek(client->file, 0, SEEK_SET) != 0) {
				snprintf(client->failure, ERROR_BYTES, "can't start the file over: %s", strerror(errno));
				return 0;
			}
			client->resume_from = 0;
		}
	}
	if (client->file) {
		if (fwrite(data, 1, length, client->file) != length) {
			snprintf(client->failure, ERROR_BYTES, "can't write the file: %s", strerror(errno));
			return 0;
		}
		return length;
	}
	if (!append(&client->body, data, length, client->max_response_bytes)) {
		snprintf(client->failure, ERROR_BYTES, "response larger than %u bytes, or out of memory",
			(unsigned)client->max_response_bytes);
		return 0;
	}
	return length;
}

static int on_progress(void *context, curl_off_t total, curl_off_t received, curl_off_t upload_total,
	curl_off_t uploaded)
{
	(void)upload_total;
	(void)uploaded;
	Client *client = context;
	Transfer *transfer = client->transfer;
	transfer->received = client->resume_from + (uint64_t)received;
	transfer->total = total > 0 ? (int64_t)(client->resume_from + (uint64_t)total) : -1;
	return transfer->cancel ? 1 : 0;
}

static int on_socket(void *context, curl_socket_t socket, curlsocktype purpose)
{
	(void)context;
	(void)purpose;
	const int bytes = SOCKET_RECEIVE_BYTES;
	// A smaller buffer is slower, not wrong.
	(void)setsockopt(socket, SOL_SOCKET, SO_RCVBUF, &bytes, sizeof(bytes));
	return CURL_SOCKOPT_OK;
}

static int on_trace(CURL *easy, curl_infotype type, char *data, size_t size, void *context)
{
	(void)easy;
	(void)context;
	static const char *const kinds[] = {
		[CURLINFO_TEXT] = "*",
		[CURLINFO_HEADER_IN] = "<",
		[CURLINFO_HEADER_OUT] = ">",
	};
	if (type == CURLINFO_DATA_IN || type == CURLINFO_DATA_OUT) {
		xsdk_log_write("https360: %s %u bytes", type == CURLINFO_DATA_IN ? "<" : ">", (unsigned)size);
		return 0;
	}
	if (type > CURLINFO_HEADER_OUT)
		return 0;
	// A line to each log line, without its line end.
	while (size) {
		size_t length = 0;
		while (length < size && data[length] != '\n')
			length++;
		const size_t next = length < size ? length + 1 : length;
		if (length && data[length - 1] == '\r')
			length--;
		if (length)
			xsdk_log_write("https360: %s %.*s", kinds[type], (int)length, data);
		data += next;
		size -= next;
	}
	return 0;
}

// "Name: value" lines split at '\n', a '\r' before it dropped.
static struct curl_slist *header_list(const char *headers, int *failed)
{
	struct curl_slist *list = NULL;
	*failed = 0;
	if (!headers)
		return NULL;
	while (*headers) {
		const char *end = strchr(headers, '\n');
		size_t length = end ? (size_t)(end - headers) : strlen(headers);
		size_t kept = length;
		if (kept && headers[kept - 1] == '\r')
			kept--;
		if (kept) {
			char *line = malloc(kept + 1);
			struct curl_slist *grown = NULL;
			if (line) {
				memcpy(line, headers, kept);
				line[kept] = '\0';
				grown = curl_slist_append(list, line);
				free(line);
			}
			if (!grown) {
				*failed = 1;
				return list;
			}
			list = grown;
		}
		headers += length + (end ? 1 : 0);
	}
	return list;
}

// The API version, then curl's, a 16-bit half each: 0x0001 then 0x0817 for 8.23.
uint32_t https360_version(void)
{
	return (uint32_t)API_VERSION << 16 | (curl_version_info(CURLVERSION_NOW)->version_num >> 8 & 0xFFFF);
}

// Starts the DLL with a heap of up to heap_bytes, then curl; 0, an NTSTATUS, or -(CURLcode). Once.
int32_t https360_init(uint32_t heap_bytes)
{
	const int32_t status = https360_runtime_start(heap_bytes);
	if (status)
		return status;
	if (curl_started)
		return 0;
	const CURLcode code = curl_global_init(CURL_GLOBAL_DEFAULT);
	if (code != CURLE_OK) {
		xsdk_log_write("https360: curl_global_init failed: %d", (int)code);
		return -(int32_t)code;
	}
	curl_started = 1;
	// What curl's connection layers do, TLS among them, in a verbose request's trace; nothing otherwise.
	(void)curl_global_trace("all");
	xsdk_log_write("https360: %s", curl_version());
	return 0;
}

// A client that checks servers' certificates against the roots in ca_file, such as "game:/cacert.pem"; its handle, or 0.
uint32_t https360_open(const char *ca_file)
{
	if (!curl_started)
		return 0;
	Client *client = calloc(1, sizeof(*client));
	if (!client)
		return 0;
	client->easy = curl_easy_init();
	client->ca_file = ca_file ? strdup(ca_file) : NULL;
	if (!client->easy || (ca_file && !client->ca_file)) {
		curl_easy_cleanup(client->easy);
		free(client->ca_file);
		free(client);
		return 0;
	}
	return (uint32_t)(uintptr_t)client;
}

// Sends a host:port to an address of its own, as "localhost:7381:192.168.0.125", for every request after; 0 or a
// CURLcode. The name is still what the certificate is checked against.
int32_t https360_resolve(uint32_t handle, const char *entry)
{
	Client *client = (Client *)(uintptr_t)handle;
	struct curl_slist *grown = curl_slist_append(client->resolve, entry);
	if (!grown)
		return CURLE_OUT_OF_MEMORY;
	client->resolve = grown;
	return CURLE_OK;
}

static CURLcode set_up(Client *client, const char *method, const char *url, struct curl_slist *headers,
	Transfer *transfer)
{
	CURL *easy = client->easy;
	CURLcode code = CURLE_OK;
#define SET(option, value)                                         \
	do {                                                           \
		if ((code = curl_easy_setopt(easy, option, value)) != CURLE_OK) \
			return code;                                           \
	} while (0)
	SET(CURLOPT_URL, url);
	SET(CURLOPT_ERRORBUFFER, client->error);
	SET(CURLOPT_NOSIGNAL, 1L);
	SET(CURLOPT_PROTOCOLS_STR, "http,https");
	SET(CURLOPT_REDIR_PROTOCOLS_STR, "http,https");
	SET(CURLOPT_HEADERFUNCTION, on_header);
	SET(CURLOPT_HEADERDATA, client);
	SET(CURLOPT_WRITEFUNCTION, on_body);
	SET(CURLOPT_WRITEDATA, client);
	SET(CURLOPT_XFERINFOFUNCTION, on_progress);
	SET(CURLOPT_XFERINFODATA, client);
	SET(CURLOPT_NOPROGRESS, 0L);
	SET(CURLOPT_SOCKOPTFUNCTION, on_socket);
	SET(CURLOPT_BUFFERSIZE, (long)RECEIVE_BUFFER_BYTES);
	if (client->ca_file)
		SET(CURLOPT_CAINFO, client->ca_file);
	if (client->resolve)
		SET(CURLOPT_RESOLVE, client->resolve);
	if (headers)
		SET(CURLOPT_HTTPHEADER, headers);
	if (transfer->flags & FLAG_FOLLOW_REDIRECTS) {
		SET(CURLOPT_FOLLOWLOCATION, 1L);
		SET(CURLOPT_MAXREDIRS, (long)MAX_REDIRECTS);
	}
	if (transfer->flags & FLAG_INSECURE) {
		SET(CURLOPT_SSL_VERIFYPEER, 0L);
		SET(CURLOPT_SSL_VERIFYHOST, 0L);
	}
	if (transfer->flags & FLAG_VERBOSE) {
		SET(CURLOPT_DEBUGFUNCTION, on_trace);
		SET(CURLOPT_VERBOSE, 1L);
	}
	if (transfer->timeout_seconds)
		SET(CURLOPT_TIMEOUT, (long)transfer->timeout_seconds);
	if (transfer->connect_timeout_seconds)
		SET(CURLOPT_CONNECTTIMEOUT, (long)transfer->connect_timeout_seconds);
	if (client->resume_from)
		SET(CURLOPT_RESUME_FROM_LARGE, (curl_off_t)client->resume_from);

	const int has_body = transfer->body_bytes != 0;
	if (strcmp(method, "HEAD") == 0)
		SET(CURLOPT_NOBODY, 1L);
	else if (strcmp(method, "GET") != 0 || has_body) {
		// POSTFIELDS sends the body with any method, as CUSTOMREQUEST names it.
		SET(CURLOPT_POSTFIELDSIZE_LARGE, (curl_off_t)transfer->body_bytes);
		SET(CURLOPT_POSTFIELDS, has_body ? (const char *)(uintptr_t)transfer->body : "");
		if (strcmp(method, "POST") != 0)
			SET(CURLOPT_CUSTOMREQUEST, method);
	}
#undef SET
	return CURLE_OK;
}

// Makes one request and waits for its response; 0 or a CURLcode, the response's status, size and the rest in
// transfer. The body goes to output_path when there is one, else into memory for https360_copy; method is GET, HEAD,
// POST, PUT, DELETE or any other, request_headers "Name: value" lines split at '\n'.
int32_t https360_perform(uint32_t handle, const char *method, const char *url, const char *request_headers,
	const char *output_path, Transfer *transfer)
{
	Client *client = (Client *)(uintptr_t)handle;
	curl_easy_reset(client->easy);
	clear(&client->body);
	clear(&client->headers);
	client->transfer = transfer;
	client->error[0] = '\0';
	client->failure[0] = '\0';
	client->started_body = 0;
	client->session_read = 0;
	client->resume_from = 0;
	client->max_response_bytes = transfer->max_response_bytes ? transfer->max_response_bytes
		: DEFAULT_MAX_RESPONSE_BYTES;
	transfer->status = 0;
	transfer->received = 0;
	transfer->total = -1;
	transfer->response_bytes = 0;
	transfer->headers_bytes = 0;
	transfer->redirects = 0;
	transfer->elapsed_ms = 0;
	transfer->error[0] = '\0';
	transfer->tls_version[0] = '\0';
	transfer->cipher[0] = '\0';
	transfer->url[0] = '\0';

	if (output_path) {
		struct stat part;
		if ((transfer->flags & FLAG_RESUME) && stat(output_path, &part) == 0 && part.st_size > 0)
			client->resume_from = (uint64_t)part.st_size;
		client->file = fopen(output_path, client->resume_from ? "ab" : "wb");
		if (!client->file) {
			snprintf(transfer->error, ERROR_BYTES, "can't open %s: %s", output_path, strerror(errno));
			return CURLE_WRITE_ERROR;
		}
	}

	int failed = 0;
	struct curl_slist *headers = header_list(request_headers, &failed);
	CURLcode code = failed ? CURLE_OUT_OF_MEMORY : set_up(client, method ? method : "GET", url, headers, transfer);
	if (code == CURLE_OK) {
		// A trace goes to DbgPrint too, for a debugger or xenia.log: the log is in game:\, which may be read-only.
		const bool dbgprint = (transfer->flags & FLAG_VERBOSE) && !xsdk_log_set_dbgprint(true);
		code = curl_easy_perform(client->easy);
		if (dbgprint)
			(void)xsdk_log_set_dbgprint(false);
	}
	curl_slist_free_all(headers);

	long status = 0;
	long redirects = 0;
	curl_off_t elapsed_us = 0;
	char *final_url = NULL;
	(void)curl_easy_getinfo(client->easy, CURLINFO_RESPONSE_CODE, &status);
	(void)curl_easy_getinfo(client->easy, CURLINFO_REDIRECT_COUNT, &redirects);
	(void)curl_easy_getinfo(client->easy, CURLINFO_TOTAL_TIME_T, &elapsed_us);
	(void)curl_easy_getinfo(client->easy, CURLINFO_EFFECTIVE_URL, &final_url);
	transfer->status = (int32_t)status;
	transfer->redirects = (uint32_t)redirects;
	transfer->elapsed_ms = (uint32_t)(elapsed_us / 1000);
	copy_text(transfer->url, URL_BYTES, final_url);
	transfer->response_bytes = (uint32_t)client->body.length;
	transfer->headers_bytes = (uint32_t)client->headers.length;

	if (client->file) {
		if ((fflush(client->file) != 0 || fsync(fileno(client->file)) != 0) && code == CURLE_OK) {
			snprintf(client->failure, ERROR_BYTES, "can't write the file: %s", strerror(errno));
			code = CURLE_WRITE_ERROR;
		}
		(void)fclose(client->file);
		client->file = NULL;
	}
	if (code != CURLE_OK) {
		// What the callbacks said, curl's own message, or its name for the code.
		const char *message = client->failure[0] ? client->failure
			: client->error[0]                   ? client->error
		                                         : curl_easy_strerror(code);
		copy_text(transfer->error, ERROR_BYTES, message);
		xsdk_log_write("https360: %s %s: %d, %s", method ? method : "GET", url, (int)code, transfer->error);
	}
	client->transfer = NULL;
	return code;
}

// Copies up to capacity bytes of the last response's body (which 0) or headers (1) to destination; the bytes copied.
uint32_t https360_copy(uint32_t handle, uint32_t which, void *destination, uint32_t capacity)
{
	Client *client = (Client *)(uintptr_t)handle;
	const Bytes *bytes = which ? &client->headers : &client->body;
	const uint32_t length = bytes->length < capacity ? (uint32_t)bytes->length : capacity;
	if (length)
		memcpy(destination, bytes->bytes, length);
	return length;
}

// Closes the client's connections and frees it.
void https360_close(uint32_t handle)
{
	Client *client = (Client *)(uintptr_t)handle;
	if (!client)
		return;
	curl_easy_cleanup(client->easy);
	curl_slist_free_all(client->resolve);
	clear(&client->body);
	clear(&client->headers);
	free(client->ca_file);
	free(client);
}

HTTPS360_EXPORT(1, https360_version);
HTTPS360_EXPORT(2, https360_init);
HTTPS360_EXPORT(3, https360_open);
HTTPS360_EXPORT(4, https360_resolve);
HTTPS360_EXPORT(5, https360_perform);
HTTPS360_EXPORT(6, https360_copy);
HTTPS360_EXPORT(7, https360_close);
HTTPS360_EXPORT(8, https360_heap_committed);
HTTPS360_EXPORT(9, https360_heap_peak);
