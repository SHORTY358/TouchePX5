// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler;

public sealed record Gen5ShaderReplayInstruction(uint Pc, uint[] Words);

/// <summary>
/// Owned diagnostic input snapshot, captured before backend compilation or
/// pooled data disposal. Instruction words describe decoded coverage, not a
/// claim to the full original shader extent. Verify against original bytes
/// before replay; no synthetic register/geometry substitution is implied.
/// </summary>
public sealed record Gen5ShaderReplaySnapshot(int Schema, ulong ShaderAddress,
    Gen5ShaderReplayInstruction[] Instructions, uint[] UserData, Gen5ShaderMetadata? Metadata,
    uint UserDataScalarRegisterBase, Gen5ComputeSystemRegisters? ComputeSystemRegisters,
    uint[] InitialScalarRegisters, uint[] ScalarRegisters, Gen5ImageBinding[] ImageBindings,
    Gen5GlobalMemoryBinding[] GlobalMemoryBindings, Gen5VertexInputBinding[] VertexInputs,
    uint[]? RuntimeScalarRegisters, Gen5ComputeSystemRegisters? EvaluationComputeSystemRegisters)
{
    public static Gen5ShaderReplaySnapshot Create(Gen5ShaderState state, Gen5ShaderEvaluation evaluation)
    {
        static byte[] Copy(byte[] data, int length)
        {
            if (length < 0 || length > data.Length) throw new ArgumentException("Invalid retained shader data extent");
            return data.AsSpan(0, length).ToArray();
        }
        var metadata = state.Metadata is { } m ? m with {
            DirectResources = new Dictionary<uint, uint>(m.DirectResources), Resources = m.Resources.ToArray()
        } : null;
        return new(1, state.Program.Address,
            state.Program.Instructions.Select(i => new Gen5ShaderReplayInstruction(i.Pc, i.Words.ToArray())).ToArray(),
            state.UserData.ToArray(), metadata, state.UserDataScalarRegisterBase,
            state.ComputeSystemRegisters,
            evaluation.InitialScalarRegisters.ToArray(), evaluation.ScalarRegisters.ToArray(),
            evaluation.ImageBindings.Select(i => i with { Control = i.Control with { AddressRegisters = i.Control.AddressRegisters.ToArray() },
                ResourceDescriptor = i.ResourceDescriptor.ToArray(), SamplerDescriptor = i.SamplerDescriptor.ToArray() }).ToArray(),
            evaluation.GlobalMemoryBindings.Select(b => b with { Data = Copy(b.Data, b.DataLength), DataPooled = false,
                InstructionPcs = b.InstructionPcs.ToArray() }).ToArray(),
            (evaluation.VertexInputs ?? []).Select(b => b with { Data = Copy(b.Data, b.DataLength), DataPooled = false,
                AliasPcs = b.AliasPcs?.ToArray() }).ToArray(), evaluation.RuntimeScalarRegisters?.ToArray(), evaluation.ComputeSystemRegisters);
    }
}
