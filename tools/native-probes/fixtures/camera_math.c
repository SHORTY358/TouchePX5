/* Copyright (C) 2026 Touche PX5 Project
 * SPDX-License-Identifier: GPL-2.0-or-later */
extern float atan2f(float, float);
extern float asinf(float);
extern float cosf(float);
extern long write(int, const void *, unsigned long);
static int near(float actual, float expected) {
    float delta = actual - expected;
    return delta > -0.00001f && delta < 0.00001f;
}
int main(void) {
    volatile float zero = 0.0f, one = 1.0f, half = 0.5f;
    int failure = 0;
    if (!near(atan2f(-one, zero), -1.5707963267948966f)) failure = 1;
    else if (!near(atan2f(one, -one), 2.356194490192345f)) failure = 2;
    else if (!near(asinf(-half), -0.5235987755982989f)) failure = 3;
    else if (!near(cosf(3.141592653589793f), -one)) failure = 4;
    if (failure) {
        char message[] = "CAMERA_MATH_NATIVE_FAIL case=0\n";
        message[sizeof(message)-3] = (char)('0'+failure);
        write(2, message, sizeof(message)-1);
        return failure;
    }
    static const char pass[] = "CAMERA_MATH_NATIVE_PASS cases=4\n";
    write(2, pass, sizeof(pass)-1);
    return 0;
}
