# Builds the static libraries https360.xex links: Mbed TLS (mbedtls-xenon) and libcurl (curl-xenon) on it, as curl-xenon's
# tests/xsdk/CMakeLists.txt adds them to a title. The Makefile runs it; the DLL itself is linked there, with its own
# layout in place of a title's.
cmake_minimum_required(VERSION 3.18)
project(https360_deps C)

set(MBEDTLS_XENON_DIR "" CACHE PATH "mbedtls-xenon's checkout")
set(CURL_XENON_DIR "" CACHE PATH "curl-xenon's checkout")

# TLS 1.2 and 1.3: config/mbedtls.h in place of mbedtls-xenon's TLS 1.2-only configs/config-xsdk.h.
set(MBEDTLS_USER_CONFIG_FILE "${CMAKE_CURRENT_SOURCE_DIR}/../config/mbedtls.h" CACHE FILEPATH "" FORCE)
set(ENABLE_PROGRAMS OFF CACHE BOOL "" FORCE)
set(ENABLE_TESTING OFF CACHE BOOL "" FORCE)
add_subdirectory("${MBEDTLS_XENON_DIR}" mbedtls)

# curl takes mbedtls-xenon's own targets, which carry the configuration Mbed TLS is built with.
set(MBEDTLS_INCLUDE_DIR "${MBEDTLS_XENON_DIR}/include" CACHE PATH "")
set(MBEDTLS_LIBRARY mbedtls CACHE STRING "")
set(MBEDX509_LIBRARY mbedx509 CACHE STRING "")
set(MBEDCRYPTO_LIBRARY mbedcrypto CACHE STRING "")
set(CURL_DISABLE_INSTALL ON CACHE BOOL "" FORCE)
add_subdirectory("${CURL_XENON_DIR}" curl)
