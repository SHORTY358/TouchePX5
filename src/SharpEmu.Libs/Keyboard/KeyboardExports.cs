// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using SharpEmu.HLE;
using SharpEmu.HLE.Host;
using SharpEmu.Libs.Kernel;

namespace SharpEmu.Libs.Keyboard;

// Open SDL PS5 keyboard-state ABI: available@16, modifiers@28,
// sixteen USB/SDL scancodes@32, total size 96. No IME/text-input claims.
public static class KeyboardExports
{
    private sealed class KeyboardState
    {
        public bool Initialized;
        public KeyboardReplayInput? Replay;
        public int NextHandle;
        public readonly Dictionary<int, ushort[]> Handles = [];
    }
    private static readonly ConditionalWeakTable<ICpuMemory, KeyboardState> States = new();
    private static readonly (int VirtualKey, ushort Scan)[] Keys = CreateKeyMap();

    [SysAbiExport(Nid = "wadT3QBCGY0", ExportName = "sceKeyboardInit", Target = Generation.Gen5, LibraryName = "libSceKeyboard")]
    public static int Init(CpuContext ctx)
    {
        var state = States.GetValue(ctx.Memory, _ => new KeyboardState());
        lock (state)
        {
            if (!state.Initialized && Environment.GetEnvironmentVariable("SHARPEMU_TEST_KEYBOARD_REPLAY") is { Length: > 0 } script)
            {
                try { state.Replay = new KeyboardReplayInput(script); }
                catch (ArgumentException error)
                {
                    Console.Error.WriteLine($"[LOADER][ERROR] {error.Message}");
                    return Return(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
                }
                Console.Error.WriteLine($"[LOADER][INFO] Test keyboard replay enabled: {script}");
            }
            state.Initialized = true;
        }
        return Return(ctx, 0);
    }

    [SysAbiExport(Nid = "HJ+KnEHcaxI", ExportName = "sceKeyboardOpen", Target = Generation.Gen5, LibraryName = "libSceKeyboard")]
    public static int Open(CpuContext ctx)
    {
        var state = States.GetValue(ctx.Memory, _ => new KeyboardState());
        lock (state)
        {
            if (!state.Initialized || ctx[CpuRegister.Rdi] != 0x10000000 || ctx[CpuRegister.Rsi] != 0 || ctx[CpuRegister.Rdx] != 0)
                return Return(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
            var handle = ++state.NextHandle;
            state.Handles.Add(handle, new ushort[16]);
            return Return(ctx, handle);
        }
    }

    [SysAbiExport(Nid = "0LWei+c7RNc", ExportName = "sceKeyboardClose", Target = Generation.Gen5, LibraryName = "libSceKeyboard")]
    public static int Close(CpuContext ctx)
    {
        var state = States.GetValue(ctx.Memory, _ => new KeyboardState());
        lock (state)
            return Return(ctx, state.Handles.Remove(unchecked((int)ctx[CpuRegister.Rdi])) ? 0 : (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
    }

    [SysAbiExport(Nid = "6HpE68bzX6M", ExportName = "sceKeyboardReadState", Target = Generation.Gen5, LibraryName = "libSceKeyboard")]
    public static int ReadState(CpuContext ctx) => ReadState(ctx, HostPlatform.Current.Input);

    internal static int ReadState(CpuContext ctx, IHostInput input)
    {
        var state = States.GetValue(ctx.Memory, _ => new KeyboardState());
        lock (state)
        {
            if (!state.Handles.TryGetValue(unchecked((int)ctx[CpuRegister.Rdi]), out var previous))
                return Return(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
            input = state.Replay ?? input;
            Span<byte> output = stackalloc byte[96];
            output.Clear();
            output[16] = 1;
            var focused = input.IsHostWindowFocused();
            var down = Keys.Where(key => focused && input.IsKeyDown(key.VirtualKey)).Select(key => key.Scan).ToHashSet();
            var next = new ushort[16];
            // Keep held keys in their slots. Leave released slots empty for one
            // read so SDL sees release before another key reuses the slot.
            for (var i = 0; i < 16; i++)
                if (down.Remove(previous[i])) next[i] = previous[i];
            foreach (var scan in down.Order())
            {
                var slot = Array.FindIndex(next, value => value == 0);
                while (slot >= 0 && previous[slot] != 0)
                    slot = Array.FindIndex(next, slot + 1, value => value == 0);
                if (slot < 0) break;
                next[slot] = scan;
            }
            uint modifiers = 0;
            int[] modifierKeys = [0xA2, 0xA0, 0xA4, 0x5B, 0xA3, 0xA1, 0xA5, 0x5C];
            for (var i = 0; i < modifierKeys.Length; i++)
                if (focused && input.IsKeyDown(modifierKeys[i])) modifiers |= 1u << i;
            BinaryPrimitives.WriteUInt32LittleEndian(output[28..], modifiers);
            for (var i = 0; i < 16; i++) BinaryPrimitives.WriteUInt16LittleEndian(output[(32 + i * 2)..], next[i]);
            if (!KernelMemoryCompatExports.TryWriteGuestOrLibcHeap(ctx, ctx[CpuRegister.Rsi], output))
                return Return(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            next.CopyTo(previous, 0);
            return Return(ctx, 0);
        }
    }

    private static (int, ushort)[] CreateKeyMap()
    {
        var keys = new List<(int, ushort)>();
        for (var i = 0; i < 26; i++) keys.Add((0x41 + i, (ushort)(4 + i)));
        for (var i = 0; i < 9; i++) keys.Add((0x31 + i, (ushort)(30 + i)));
        keys.Add((0x30, 39));
        for (var i = 0; i < 12; i++) keys.Add((0x70 + i, (ushort)(58 + i)));
        keys.AddRange([(0x0D, 40), (0x1B, 41), (0x08, 42), (0x09, 43), (0x20, 44),
            (0x27, 79), (0x25, 80), (0x28, 81), (0x26, 82), (0x2D, 73),
            (0x24, 74), (0x21, 75), (0x2E, 76), (0x23, 77), (0x22, 78)]);
        return keys.ToArray();
    }
    private static int Return(CpuContext ctx, int value)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)(long)value);
        return value;
    }
}
