/*
 * What https360 changes from Mbed TLS's default configuration, appended to it as MBEDTLS_USER_CONFIG_FILE
 * (deps/CMakeLists.txt). It takes the place of mbedtls-xenon's configs/config-xsdk.h, which takes TLS 1.3 out: here
 * TLS 1.3 stays in beside 1.2, as Mbed TLS 3.6 has it by default, so nothing is changed.
 */
