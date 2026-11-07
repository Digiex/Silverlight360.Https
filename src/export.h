// An export record for xsdk's elf2xex --dll: the ordinal, then the address, in a section that isn't loaded, as xsdk's
// import records are (xsdk tools/gen_imports.py). The function must have C linkage.
#ifndef HTTPS360_EXPORT_H
#define HTTPS360_EXPORT_H

#define HTTPS360_EXPORT(ordinal, function) \
	__asm__("\t.section .xsdk.exports, \"\"\n\t.long " #ordinal "\n\t.long " #function "\n\t.previous")

#endif
