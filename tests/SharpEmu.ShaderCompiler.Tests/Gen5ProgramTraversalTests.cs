// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
using System.Buffers.Binary;
using SharpEmu.HLE;
using Xunit;

namespace SharpEmu.ShaderCompiler.Tests;

public sealed class Gen5ProgramTraversalTests
{
    private const uint End = 0xBF810000;

    [Fact]
    public void ConditionalExitAfterFirstEndIsRetained()
    {
        var (success, program, error) = Decode([0xBF840001, End, 0x7E000280, End]);
        Assert.True(success, error);
        Assert.Equal(new uint[] { 0, 4, 8, 12 }, program.Instructions.Select(i => i.Pc));
        Assert.Equal(2, program.Instructions.Count(i => i.Opcode == "SEndpgm"));
    }

    [Fact]
    public void UnconditionalBranchSkipsUnreachableNonInstructions()
    {
        var (success, program, error) = Decode([0xBF820002, 0xFFFFFFFF, 0xFFFFFFFF, End]);
        Assert.True(success, error);
        Assert.Equal(new uint[] { 0, 12 }, program.Instructions.Select(i => i.Pc));
    }

    [Fact]
    public void BranchIntoLiteralWordIsRejected()
    {
        var (success, program, _) = Decode([0xBF840001, 0x7E0002FF, 0x3F800000, End]);
        Assert.False(success);
        Assert.Empty(program.Instructions);
    }

    [Fact]
    public void BackEdgeAndConditionalExitAreVisitedOnce()
    {
        var (success, program, error) = Decode([0xBF840001, 0xBF82FFFE, End]);
        Assert.True(success, error);
        Assert.Equal(new uint[] { 0, 4, 8 }, program.Instructions.Select(i => i.Pc));
    }

    [Fact]
    public void UnreadableExitCannotHideBehindEarlierEnd()
    {
        var (success, program, _) = Decode([0xBF840002, End]);
        Assert.False(success);
        Assert.Empty(program.Instructions);
    }

    [Fact]
    public void NegativeBranchTargetIsRejected()
    {
        var (success, _, _) = Decode([0xBF82FFFE, End]);
        Assert.False(success);
    }

    [Fact]
    public void TerminalInstructionDoesNotReadUnreferencedPadding()
    {
        var (success, program, error) = Decode([End, 0xFFFFFFFF]);
        Assert.True(success, error);
        Assert.Single(program.Instructions);
    }

    [Fact]
    public void EndlessBranchWithoutTerminalIsRejected()
    {
        var (success, _, error) = Decode([0xBF82FFFF]);
        Assert.False(success);
        Assert.Equal("unterminated", error);
    }

    private static (bool, Gen5ShaderProgram, string) Decode(uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), words[i]);
        var success = Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(new ShaderBytes(bytes), Generation.Gen5), 0x10000, out var program, out var error);
        return (success, program, error);
    }

    private sealed class ShaderBytes(byte[] bytes) : ICpuMemory
    {
        public bool TryRead(ulong address, Span<byte> destination)
        {
            if (address < 0x10000 || address - 0x10000 > (ulong)bytes.Length || (ulong)destination.Length > (ulong)bytes.Length - (address - 0x10000)) return false;
            bytes.AsSpan((int)(address - 0x10000), destination.Length).CopyTo(destination); return true;
        }
        public bool TryWrite(ulong address, ReadOnlySpan<byte> source) => false;
    }
}
