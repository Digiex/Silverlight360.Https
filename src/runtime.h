// The DLL's runtime, set up by https360_init in place of a title's start (src/runtime.c).
#ifndef HTTPS360_RUNTIME_H
#define HTTPS360_RUNTIME_H

#include <stdint.h>

// Reserves a heap of up to heap_bytes and starts xsdk's runtime; 0, or the NTSTATUS the reserve failed with. Once.
int32_t https360_runtime_start(uint32_t heap_bytes);
uint32_t https360_heap_committed(void);
uint32_t https360_heap_peak(void);

#endif
