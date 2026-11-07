# Building

The DLL builds in WSL (or on Linux) with xsdk's toolchain. Clone with the submodules, then set up the rest, in PowerShell:

```powershell
git clone --recursive https://github.com/Digiex/Silverlight360.Https.git
cd Silverlight360.Https
.\tools\Configure.ps1
```

`Configure.ps1` gets what the build needs, each the way that suits it:

| | | |
|---|---|---|
| **curl-xenon**, **mbedtls-xenon** | Submodules in `deps/`, at the commits Silverlight360.Https is built from (Mbed TLS's `framework` with them) | Checked out if a clone wasn't `--recursive` |
| **xsdk** | [jessenic/xsdk](https://github.com/jessenic/xsdk), branch `dll-modules`, the build XboxNGen uses: its `elf2xex.py` has `--dll`, and its `build/libxsdk.a` is what the DLL links | Found from `-Xsdk`, `XSDK`, XboxNGen's configuration beside this repository, or a checkout two folders up (`C:\Proj\xsdk`) or beside it; otherwise cloned two folders up, if you say so. `libxsdk.a` is built if it isn't |
| **xsdk's toolchain** | GCC 15.3, binutils and newlib for `powerpc-xsdk-elf`, in WSL | Found in `~/xsdk-gcc` or at `-Toolchain`; otherwise built there with xsdk's `tools/toolchain/build.sh`, if you say so: about 10 minutes, and a 136 MB download of their sources |
| **Silverlight360** | The SDK, for `Silverlight360.Https.dll` and the sample | Found from `-Silverlight360`, its environment variable, or a checkout beside this repository; otherwise cloned there, if you say so |

It also checks WSL has `make`, `cmake`, `ninja`, `python3` and `git` (`sudo apt install build-essential cmake ninja-build python3 git` in Debian; xsdk's own `docs/GETTING_STARTED.md` lists what its toolchain needs besides). `-NonInteractive` asks nothing: `-CloneXsdk`, `-BuildToolchain` and `-CloneSilverlight360` say what it may fetch or build. It writes `config.mk` (`XSDK` and `TOOLCHAIN` as WSL sees them, for the Makefile), `config.props` (for the sample's project) and `config.json` (for `tools\Release.ps1`); git ignores them, and `-Show` prints them.

Then, in WSL, in the repository's folder. The first build compiles both libraries, about 300 files:

```bash
make -j$(nproc)
```

That writes `https360.xex` (1.7 MB). It also copies in `cacert.pem`, Mozilla's roots as mbedtls-xenon has them, and `test-ca2.crt`, Mbed TLS's test CA, for the sample. After changing a port, run `cmake --build build/deps` and then `make`; `make clean` starts over. The Makefile's `XSDK`, `TOOLCHAIN`, `ELF2XEX`, `MBEDTLS_XENON` and `CURL_XENON` can be given on its command line to build with something else.

To move a port to a newer commit: check it out in `deps/<port>`, build and test, and commit the submodule.

## Silverlight360.Https.dll

The C# side builds as a Silverlight360 class library, in PowerShell:

```powershell
..\XboxSilverlightSdk\tools\Build-XboxApp.ps1 -Project .\csharp\Silverlight360.Https.csproj
```

That writes `csharp\bin\Release\Silverlight360.Https.dll` and `Silverlight360.Https.xml`, its documentation for IntelliSense, with `Silverlight360.Common.dll`, which it references, beside them.

## The sample

`sample/` is a Silverlight360 app that runs 31 checks against a local server and public hosts. In PowerShell, with Silverlight360 where `.\tools\Configure.ps1 -Show` says (here beside this repository, as `XboxSilverlightSdk`):

```powershell
..\XboxSilverlightSdk\tools\Build-XboxApp.ps1 -Project .\sample\HttpsSample.csproj
```

See [Testing](testing.md) for running it.
