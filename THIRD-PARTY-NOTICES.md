# Third-party notices

`https360.xex` is built from the two libraries below, and `cacert.pem` is Mozilla's root certificates. They keep their own licenses, not Silverlight360.Https's (`LICENSE.md`). An app that ships `https360.xex` or `cacert.pem` ships these notices with them: a release has them in its `licenses` folder.

## libcurl

curl 8.23, from [curl-xenon](https://github.com/xenosworks/curl-xenon), xsdk's port of [curl](https://curl.se/).

Licensed under curl's license, a permissive MIT/X-style license: `licenses/curl.txt`.

Copyright (c) 1996 - 2026, Daniel Stenberg, <daniel@haxx.se>, and many contributors, see the THANKS file.

## Mbed TLS

Mbed TLS 3.6.7, from [mbedtls-xenon](https://github.com/xenosworks/mbedtls-xenon), xsdk's port of [Mbed TLS](https://www.trustedfirmware.org/projects/mbed-tls/), built with `config/mbedtls.h`.

Mbed TLS is offered under the Apache License 2.0 or the GNU GPL 2.0 or later; Silverlight360.Https uses it under the Apache License 2.0. Both are in `licenses/mbedtls.txt`.

Copyright The Mbed TLS Contributors.

## Mozilla's root certificates (`cacert.pem`)

The CA bundle curl publishes at <https://curl.se/docs/caextract.html>, extracted from Mozilla's `certdata.txt` (certificate data as of 2026-09-25), as mbedtls-xenon carries it.

This data is subject to the Mozilla Public License, version 2.0. You can get a copy of the license at <https://mozilla.org/MPL/2.0/>. The file is distributed unmodified, and its source is `certdata.txt` in Mozilla's source tree.
