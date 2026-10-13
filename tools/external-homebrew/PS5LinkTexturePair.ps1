# Copyright (C) 2026 Touche PX5 Project
# SPDX-License-Identifier: GPL-2.0-or-later
# Source-generation helper; never modifies the pinned upstream checkout.
function Add-PS5LinkTexturePair([string]$Source) {
    $marker='/* Where the vertex program wants its two resources. */'
    if(-not $Source.Contains($marker)) { throw 'Texture pair insertion contract changed' }
    return $Source.Replace($marker,@'
/* An independent allocation has a different palette, but identical layout. */
    DirectMem texture_second;
    ALLOC_OR_FAIL(texture_second, 64 * 64 * 4, DIRECT_MEM_MIN_ALIGN, "texture-second");
    unsigned char *second_pixels = texture_second.ptr;
    for (unsigned int i = 0; i < 64 * 64 * 4; i += 4) {
        second_pixels[i] = pixels[i+1];
        second_pixels[i+1] = pixels[i+2];
        second_pixels[i+2] = pixels[i];
        second_pixels[i+3] = 255;
    }
    unsigned long long tb = (unsigned long long)(unsigned long)texture_second.ptr;
    unsigned int second_texture_words[8];
    for (int i = 0; i < 8; i++) second_texture_words[i] = texture_words[i];
    second_texture_words[0] = (unsigned int)(tb >> 8);
    second_texture_words[1] = (unsigned int)(tb >> 40) | (56u << 20) | (3u << 30);

    /* Where the vertex program wants its two resources. */
'@)
}
