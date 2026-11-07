# Builds https360.xex, libcurl on Mbed TLS in an Xbox 360 DLL, with xsdk's toolchain. In WSL: make
# XSDK is an xsdk checkout with elf2xex --dll and a built libxsdk.a, the dll-modules build XboxNGen uses, and
# TOOLCHAIN its compiler's prefix: tools/Configure.ps1 finds or sets them up and writes them to config.mk.
# MBEDTLS_XENON and CURL_XENON are the two ports, the submodules in deps/ unless given.
-include config.mk
XSDK ?= $(HOME)/xsdk
TOOLCHAIN ?= $(HOME)/xsdk-gcc/bin/powerpc-xsdk-elf-
ELF2XEX ?= $(XSDK)/tools/elf2xex.py
MBEDTLS_XENON ?= $(abspath deps/mbedtls-xenon)
CURL_XENON ?= $(abspath deps/curl-xenon)
CC := $(TOOLCHAIN)gcc
BUILD := build
DEPS := $(BUILD)/deps
BASE := 8D000000

DEP_LIBS := $(DEPS)/curl/lib/libcurl.a $(DEPS)/mbedtls/library/libmbedtls.a $(DEPS)/mbedtls/library/libmbedx509.a \
	$(DEPS)/mbedtls/library/libmbedcrypto.a
OBJECTS := $(BUILD)/runtime.o $(BUILD)/https.o

# curl's headers, and Mbed TLS's with the configuration it was built with (deps/CMakeLists.txt).
CFLAGS := -mcpu=cell -fno-pic -O2 -g -std=gnu17 -Wall -Wextra -Werror -I$(XSDK)/include -I$(CURL_XENON)/include \
	-I$(MBEDTLS_XENON)/include -DCURL_STATICLIB '-DMBEDTLS_USER_CONFIG_FILE="$(abspath config/mbedtls.h)"'
LIBS := -Wl,--start-group $(DEP_LIBS) $(XSDK)/build/libxsdk.a -lc -lm -lgcc -Wl,--end-group

# Beside it in a package: Mozilla's roots, as mbedtls-xenon has them, and Mbed TLS's test CA, which
# tests/https_server.py's certificate is under, for the sample.
all: https360.xex cacert.pem test-ca2.crt

# As LF whatever the checkout's line endings: a Windows checkout of the submodule has CRLF.
cacert.pem: $(MBEDTLS_XENON)/programs/xsdk/cacert.pem
	tr -d '\r' < $< > $@

test-ca2.crt: $(MBEDTLS_XENON)/framework/data_files/test-ca2.crt
	tr -d '\r' < $< > $@

https360.xex: $(BUILD)/https360.elf
	@grep -q -e '--dll' $(ELF2XEX) || { echo "$(ELF2XEX) has no --dll: use xsdk's dll-modules build" \
		"(https://github.com/jessenic/xsdk), which tools/Configure.ps1 finds or clones."; exit 1; }
	python3 $(ELF2XEX) --dll --base $(BASE) $< $@

$(BUILD)/https360.elf: $(OBJECTS) $(DEP_LIBS) https360.ld
	$(CC) -static -nostdlib -T https360.ld -Wl,-Map,$(BUILD)/https360.map -o $@ $(OBJECTS) $(LIBS)

$(BUILD)/https.o: $(DEPS)/build.ninja
$(BUILD)/%.o: src/%.c | $(BUILD)
	$(CC) $(CFLAGS) -c $< -o $@

$(DEPS)/build.ninja: deps/CMakeLists.txt config/mbedtls.h | $(BUILD)
	cmake -S deps -B $(DEPS) -GNinja -DCMAKE_TOOLCHAIN_FILE=$(XSDK)/cmake/xsdk.cmake -DCMAKE_BUILD_TYPE=Release \
		-DXSDK_TOOLCHAIN=$(patsubst %/bin/powerpc-xsdk-elf-,%,$(TOOLCHAIN)) \
		-DMBEDTLS_XENON_DIR=$(MBEDTLS_XENON) -DCURL_XENON_DIR=$(CURL_XENON)

# Ninja rebuilds what changed in the ports; run it again by hand after changing them.
$(DEP_LIBS) &: $(DEPS)/build.ninja
	cmake --build $(DEPS)

$(BUILD):
	mkdir -p $(BUILD)

clean:
	rm -rf $(BUILD) https360.xex cacert.pem test-ca2.crt

.PHONY: all clean
