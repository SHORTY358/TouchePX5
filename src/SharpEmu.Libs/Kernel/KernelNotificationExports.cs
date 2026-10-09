// Copyright (C) 2026 Touché PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Text;
using SharpEmu.HLE;

namespace SharpEmu.Libs.Kernel;

public static class KernelNotificationExports
{
    [SysAbiExport(Nid = "zl7hupSO0C0", ExportName = "sceKernelSendNotificationRequest",
        Target = Generation.Gen5, LibraryName = "libKernel")]
    public static int SendNotificationRequest(CpuContext ctx)
    {
        // Supported homebrew ABI: the 3120-byte text request used by
        // ps5link-sdk. Deliver to the emulator log, not the host OS shell.
        var address = ctx[CpuRegister.Rsi];
        if (ctx[CpuRegister.Rdi] != 0 || ctx[CpuRegister.Rdx] != 3120 ||
            ctx[CpuRegister.Rcx] != 0 || address == 0 || address > ulong.MaxValue - 3120)
            return Return(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        Span<byte> request = stackalloc byte[3120];
        if (!ctx.Memory.TryRead(address, request))
            return Return(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        if (BinaryPrimitives.ReadUInt32LittleEndian(request) != 0)
            return Return(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        var text = request.Slice(45, 3075);
        var end = text.IndexOf((byte)0);
        if (end < 0)
            return Return(ctx, OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        var message = Encoding.UTF8.GetString(text[..end]).Replace('\r', ' ').Replace('\n', ' ');
        Console.Error.WriteLine($"[LOADER][INFO] Guest notification: {message}");
        return Return(ctx, OrbisGen2Result.ORBIS_GEN2_OK);
    }

    private static int Return(CpuContext ctx, OrbisGen2Result result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)(long)(int)result);
        return (int)result;
    }
}
