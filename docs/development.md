# Development

## What's here

| | |
|---|---|
| `src/https.c` | The DLL's API on curl's easy interface: clients, requests, responses copied out |
| `src/runtime.c` | The runtime xsdk's title start would set up, set up by `https360_init` instead, with a bounded heap (from UnRar360) |
| `src/export.h`, `src/runtime.h` | `HTTPS360_EXPORT`, which gives a function an ordinal for elf2xex, and the runtime's interface |
| `config/mbedtls.h` | Mbed TLS's configuration: the default, TLS 1.3 kept |
| `deps/curl-xenon`, `deps/mbedtls-xenon` | The two ports, as submodules |
| `deps/CMakeLists.txt` | Builds them as static libraries |
| `https360.ld`, `Makefile` | The DLL's layout, at base `0x8D000000`, and its build |
| `csharp/HttpsClient.cs` | The C# side: `HttpsClient`, `HttpsResponse`, `HttpsException` |
| `sample/` | A Silverlight360 app that runs 31 checks against a local server and public hosts |
| `tests/` | The local HTTPS server, and a check of what the sample downloaded |
| `tools/Configure.ps1`, `tools/Config.ps1` | Sets up the build ([Building](building.md)), and reads what it saved |
| `tools/Release.ps1` | Builds a release and publishes it on GitHub |
| `docs/`, `mkdocs.yml` | This site |

The C side and the C# side share the layout of a transfer (`Transfer` in `src/https.c`, 672 bytes, which a static assert holds to, and the offsets at the top of `HttpsClient.cs`), big-endian; a change to one is a change to both. The exports are by ordinal, `#1` to `#9`: the `HTTPS360_EXPORT` lines in `src/https.c`, and the `DllImport`s.

## Releasing

```powershell
.\tools\Release.ps1 -DryRun     # builds and zips, shows the notes, publishes nothing
.\tools\Release.ps1             # 0.1.0 first, then the last release's patch raised
.\tools\Release.ps1 -Bump Minor
```

As Silverdash360's and XboxNGen's release scripts do:

1. The release is made from the commit as it is: nothing may be uncommitted, and the commit must be on GitHub already. Nothing is pushed.
2. The version is `-Version` if given, else the last release's (the highest `v<version>` tag) with its patch, or `-Bump`'s part, raised. It's tagged `v<version>` on the commit, by `gh`.
3. The DLL is built from scratch in WSL (`make clean`, then `make`), with what `tools\Configure.ps1` set up, and the submodules at the commits this one records.
4. `Https360-<version>.zip`, in `build\release`, has in an `Https360` folder `https360.xex`, `cacert.pem`, `csharp\HttpsClient.cs`, `README.md`, `LICENSE.md`, `THIRD-PARTY-NOTICES.md`, `licenses\` and `BUILD.txt`, which records the commits of Https360, the two ports and xsdk it was built from, and the toolchain.
5. The release's notes are the commits since the last release, and the zip is its file.

It needs the GitHub CLI (`gh`), signed in, unless `-DryRun`. Test a build before releasing it ([Testing](testing.md)): the script builds it, it doesn't run it.

## The docs

This site is the `docs` folder, built with [MkDocs](https://www.mkdocs.org/) (`mkdocs.yml`). A push to `master` that changes it publishes it to GitHub Pages (`.github/workflows/docs.yml`). To preview it:

```bash
pip install mkdocs==1.6.1
python -m mkdocs serve
```
