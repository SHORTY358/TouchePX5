/* Copyright (C) 2026 Touche PX5 Project
 * SPDX-License-Identifier: GPL-2.0-or-later */
extern void sincosf(float, float *, float *);
extern long write(int, const void *, unsigned long);
static int near(float actual, float expected) {
    float delta = actual - expected;
    return delta > -0.00001f && delta < 0.00001f;
}
int main(void) {
    const float angles[] = {0.0f, 1.5707963267948966f, -1.5707963267948966f};
    const float sins[] = {0.0f, 1.0f, -1.0f};
    const float coss[] = {1.0f, 0.0f, 0.0f};
    for (int i = 0; i < 3; ++i) {
        volatile struct { unsigned before; float sine; unsigned between;
            float cosine; unsigned after; } result = {0x12345678, 123.0f, 0x87654321, 456.0f, 0xABCD1234};
        sincosf(angles[i], (float *)&result.sine, (float *)&result.cosine);
        if (!near(result.sine, sins[i]) || !near(result.cosine, coss[i]) ||
            result.before != 0x12345678 || result.between != 0x87654321 || result.after != 0xABCD1234) {
            static const char fail[] = "SINCOSF_NATIVE_FAIL\n";
            write(2, fail, sizeof(fail)-1); return 1;
        }
    }
    static const char pass[] = "SINCOSF_NATIVE_PASS cases=3 guards=intact\n";
    write(2, pass, sizeof(pass)-1); return 0;
}
