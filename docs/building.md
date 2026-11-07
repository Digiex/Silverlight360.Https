# Building

The DLL builds in WSL (or on Linux) with xsdk's toolchain. You need:

- [xsdk](https://github.com/xenosworks/xsdk) built, with its toolchain in `~/xsdk-gcc` (the Makefile's `XSDK` and `TOOLCHAIN` say where otherwise);
- xsdk's `elf2xex.py` with `--dll`, which is on its `dll-modules` branch (`ELF2XEX`);
- [mbedtls-xenon](https://github.com/xenosworks/mbedtls-xenon) and [curl-xenon](https://github.com/xenosworks/curl-xenon) checked out two folders up, beside the folder that holds this one, as they sit in `C:\Proj` (otherwise `MBEDTLS_XENON` and `CURL_XENON`);
- `make`, `cmake`, `ninja` and `python3`.

The first build compiles both libraries, about 300 files:

```bash
cd /mnt/c/Proj/SilverlightX360/Https360 && make -j$(nproc) ELF2XEX=/mnt/c/Proj/xsdk/tools/elf2xex.py
```

That writes `https360.xex` (1.7 MB). It also copies in `cacert.pem`, Mozilla's roots as mbedtls-xenon has them, and `test-ca2.crt`, Mbed TLS's test CA, for the sample. After changing either port, run `cmake --build build/deps` and then `make`; `make clean` starts over.

## The sample

`sample/` is a Silverlight360 app that runs 31 checks against a local server and public hosts. With [Silverlight360](https://github.com/Digiex/Silverlight360) beside this folder as `XboxSilverlightSdk`, in PowerShell:

```powershell
..\XboxSilverlightSdk\tools\Build-XboxApp.ps1 -Project .\sample\HttpsSample.csproj
```

See [Testing](testing.md) for running it.
