// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Runtime.CompilerServices;
using System.Text;
using SharpEmu.HLE;

namespace SharpEmu.Libs.Kernel;

public static partial class KernelMemoryCompatExports
{
    [SysAbiExport(Nid = "g7zzzLDYGw0", ExportName = "strdup",
        Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Strdup(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = 0;
        if (!TryReadCString(ctx, ctx[CpuRegister.Rdi], 1_048_576, out var text) || text.Length == 1_048_576)
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        if (!TryAllocateLibcHeap((ulong)text.Length + 1, DefaultLibcHeapAlignment, zeroFill: false, out var address))
            return 0;
        var output = new byte[text.Length + 1];
        text.CopyTo(output.AsSpan());
        if (!TryWriteGuestOrLibcHeap(ctx, address, output))
        {
            FreeLibcHeap(address);
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        }
        ctx[CpuRegister.Rax] = address;
        return 0;
    }

    // strtok's implicit cursor belongs to the guest process, not to the host
    // thread executing this import. Weak ownership avoids retaining runtimes.
    private sealed class TokenCursor { public ulong Next; }
    private static readonly ConditionalWeakTable<ICpuMemory, TokenCursor> _tokenCursors = new();

    [SysAbiExport(Nid = "oVkZ8W8-Q8A", ExportName = "strtok",
        Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Strtok(CpuContext ctx)
    {
        var cursor = _tokenCursors.GetValue(ctx.Memory, _ => new TokenCursor());
        lock (cursor)
        {
            var input = ctx[CpuRegister.Rdi];
            if (input != 0) cursor.Next = input;
            ctx[CpuRegister.Rax] = 0;
            if (cursor.Next == 0) return 0;
            if (!TryReadCString(ctx, ctx[CpuRegister.Rsi], 1_048_576, out var delimiters) ||
                !TryReadCString(ctx, cursor.Next, 1_048_576, out var text))
            {
                cursor.Next = 0;
                return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
            }
            var start = 0;
            while (start < text.Length && delimiters.AsSpan().Contains(text[start])) start++;
            if (start == text.Length) { cursor.Next = 0; return 0; }
            var end = start;
            while (end < text.Length && !delimiters.AsSpan().Contains(text[end])) end++;
            var token = cursor.Next + (ulong)start;
            if (end < text.Length)
            {
                if (!TryWriteGuestOrLibcHeap(ctx, cursor.Next + (ulong)end, stackalloc byte[] { 0 }))
                {
                    cursor.Next = 0;
                    return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
                }
                cursor.Next += (ulong)end + 1;
            }
            else cursor.Next = 0;
            ctx[CpuRegister.Rax] = token;
            return 0;
        }
    }

    [SysAbiExport(Nid = "PqF+kHW-2WQ", ExportName = "tolower",
        Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Tolower(CpuContext ctx)
    {
        // Initial guest locale is the C locale; EOF and non-ASCII bytes survive.
        var value = unchecked((int)ctx[CpuRegister.Rdi]);
        ctx[CpuRegister.Rax] = unchecked((ulong)(value is >= 'A' and <= 'Z' ? value + 32 : value));
        return 0;
    }

    [SysAbiExport(Nid = "TYE4irxSmko", ExportName = "toupper",
        Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Toupper(CpuContext ctx)
    {
        var value = unchecked((int)ctx[CpuRegister.Rdi]);
        ctx[CpuRegister.Rax] = unchecked((ulong)(value is >= 'a' and <= 'z' ? value - 32 : value));
        return 0;
    }

    [SysAbiExport(Nid = "ByfjUZsWiyg", ExportName = "strlcat",
        Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Strlcat(CpuContext ctx)
    {
        var destination = ctx[CpuRegister.Rdi];
        var size = ctx[CpuRegister.Rdx];
        ctx[CpuRegister.Rax] = 0;
        if (!TryReadCString(ctx, ctx[CpuRegister.Rsi], 1_048_576, out var source))
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        if (size == 0) { ctx[CpuRegister.Rax] = (ulong)source.Length; return 0; }
        if (!TryReadCString(ctx, destination, Math.Min(size, 1_048_576), out var previous))
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        if ((ulong)previous.Length == size)
        { ctx[CpuRegister.Rax] = size + (ulong)source.Length; return 0; }
        if ((ulong)previous.Length >= size || previous.Length == 1_048_576)
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT;
        var count = (int)Math.Min((ulong)source.Length, size - (ulong)previous.Length - 1);
        var output = new byte[count + 1];
        source.AsSpan(0, count).CopyTo(output);
        if (!TryWriteGuestOrLibcHeap(ctx, destination + (ulong)previous.Length, output))
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        ctx[CpuRegister.Rax] = (ulong)previous.Length + (ulong)source.Length;
        return 0;
    }

    [SysAbiExport(Nid = "SfQIZcqvvms", ExportName = "strlcpy",
        Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Strlcpy(CpuContext ctx)
    {
        var size = ctx[CpuRegister.Rdx];
        ctx[CpuRegister.Rax] = 0;
        if (!TryReadCString(ctx, ctx[CpuRegister.Rsi], 1_048_576, out var text))
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        if (size != 0)
        {
            var count = (int)Math.Min((ulong)text.Length, size - 1);
            var output = new byte[count + 1];
            text.AsSpan(0, count).CopyTo(output);
            if (!TryWriteGuestOrLibcHeap(ctx, ctx[CpuRegister.Rdi], output))
                return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        }
        ctx[CpuRegister.Rax] = (ulong)text.Length;
        return 0;
    }

    [SysAbiExport(Nid = "mXlxhmLNMPg", ExportName = "strtol",
        Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Strtol(CpuContext ctx)
    {
        var input = ctx[CpuRegister.Rdi];
        var endPointer = ctx[CpuRegister.Rsi];
        var numberBase = unchecked((int)ctx[CpuRegister.Rdx]);
        ctx[CpuRegister.Rax] = 0;
        if (numberBase != 0 && (numberBase < 2 || numberBase > 36))
        { KernelRuntimeCompatExports.TrySetErrno(ctx, 22); return 0; }
        if (!TryReadCString(ctx, input, 1_048_576, out var text))
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        var position = 0;
        while (position < text.Length && text[position] is 32 or 9 or 10 or 11 or 12 or 13) position++;
        var negative = position < text.Length && text[position] == '-';
        if (position < text.Length && text[position] is (byte)'+' or (byte)'-') position++;
        if ((numberBase == 0 || numberBase == 16) && position + 2 < text.Length &&
            text[position] == '0' && text[position + 1] is (byte)'x' or (byte)'X' &&
            TokenDigit(text[position + 2]) is >= 0 and < 16)
        { numberBase = 16; position += 2; }
        if (numberBase == 0) numberBase = position < text.Length && text[position] == '0' ? 8 : 10;
        var digitsStart = position;
        var limit = negative ? 0x8000000000000000UL : 0x7FFFFFFFFFFFFFFFUL;
        ulong value = 0;
        var overflow = false;
        while (position < text.Length)
        {
            var digit = TokenDigit(text[position]);
            if (digit < 0 || digit >= numberBase) break;
            if (value > (limit - (ulong)digit) / (ulong)numberBase) overflow = true;
            else if (!overflow) value = value * (ulong)numberBase + (ulong)digit;
            position++;
        }
        var end = digitsStart == position ? input : input + (ulong)position;
        if (endPointer != 0 && !TryWriteGuestOrLibcHeapUInt64(ctx, endPointer, end))
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        if (overflow) { value = limit; KernelRuntimeCompatExports.TrySetErrno(ctx, 34); }
        ctx[CpuRegister.Rax] = negative ? unchecked(0UL - value) : value;
        return 0;
    }

    private static int TokenDigit(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'a' and <= (byte)'z' => value - 'a' + 10,
        >= (byte)'A' and <= (byte)'Z' => value - 'A' + 10,
        _ => -1,
    };

    [SysAbiExport(Nid = "fPxypibz2MY", ExportName = "atoi",
        Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Atoi(CpuContext ctx)
    {
        var savedRsi = ctx[CpuRegister.Rsi];
        var savedRdx = ctx[CpuRegister.Rdx];
        try
        {
            ctx[CpuRegister.Rsi] = 0;
            ctx[CpuRegister.Rdx] = 10;
            var result = Strtol(ctx);
            if (result == 0) ctx[CpuRegister.Rax] = unchecked((ulong)(int)ctx[CpuRegister.Rax]);
            return result;
        }
        finally { ctx[CpuRegister.Rsi] = savedRsi; ctx[CpuRegister.Rdx] = savedRdx; }
    }

    [SysAbiExport(Nid = "YQ0navp+YIc", ExportName = "puts",
        Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libc")]
    public static int Puts(CpuContext ctx)
    {
        ctx[CpuRegister.Rax] = ulong.MaxValue;
        if (!TryReadCString(ctx, ctx[CpuRegister.Rdi], 1_048_576, out var text))
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        Console.WriteLine(Encoding.UTF8.GetString(text));
        ctx[CpuRegister.Rax] = (ulong)text.Length + 1;
        return 0;
    }
}
