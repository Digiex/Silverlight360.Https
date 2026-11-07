# Licenses

Silverlight360.Https itself, its C code, `HttpsClient.cs`, its build and its documentation, is licensed under the [PolyForm Noncommercial License 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0) with an additional condition: any project made with it must include a visible reference to <https://github.com/Digiex/Silverlight360.Https> in its documentation, credits or "about" information. A project made with it includes an app shipped with `https360.xex` or `HttpsClient.cs`. See [LICENSE.md](https://github.com/Digiex/Silverlight360.Https/blob/master/LICENSE.md).

`https360.xex` is built from libraries that keep their own licenses, and `cacert.pem` is Mozilla's data. An app that ships them ships their notices with them; a release has them in its `licenses` folder, with [THIRD-PARTY-NOTICES.md](https://github.com/Digiex/Silverlight360.Https/blob/master/THIRD-PARTY-NOTICES.md):

| | License | |
|---|---|---|
| **libcurl** 8.23 ([curl-xenon](https://github.com/xenosworks/curl-xenon)) | curl's license, MIT/X-style | `licenses/curl.txt` |
| **Mbed TLS** 3.6.7 ([mbedtls-xenon](https://github.com/xenosworks/mbedtls-xenon)) | Apache 2.0 (or GPL 2.0 or later; Silverlight360.Https uses Apache 2.0) | `licenses/mbedtls.txt` |
| **`cacert.pem`**, Mozilla's root certificates as curl publishes them | Mozilla Public License 2.0 | <https://mozilla.org/MPL/2.0/> |
