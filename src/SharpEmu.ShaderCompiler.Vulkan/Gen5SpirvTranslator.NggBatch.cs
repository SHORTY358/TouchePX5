// Copyright (C) 2026 Touche PX5 Project
// SPDX-License-Identifier: GPL-2.0-or-later
namespace SharpEmu.ShaderCompiler.Vulkan;

public static partial class Gen5SpirvTranslator
{
    private sealed partial class CompilationContext
    {
        private uint _nggBatchVertexCount;
        private void EmitNggBatchInputs(Gen5ComputeNggBatchInput batch, uint localId, uint groupId)
        {
            _nggBatchVertexCount = _module.AddGlobalVariable(_privateUintPointer,
                SpirvStorageClass.Private, UInt(0));
            _interfaces.Add(_nggBatchVertexCount);
            var priorActive = Load(_boolType, _programActive);
            Store(_programActive, _module.ConstantBool(false));
            Store(_programCounter, UInt(uint.MaxValue));
            var group = _module.AddInstruction(SpirvOp.CompositeExtract, _uintType, groupId, 0);
            var active = _module.AddInstruction(SpirvOp.ULessThan, _boolType, group, UInt(batch.GroupCount));
            for (uint axis = 1; axis < 3; axis++)
                active = And(active, Equal(_module.AddInstruction(SpirvOp.CompositeExtract, _uintType, groupId, axis), UInt(0)));
            var descriptor = IAdd(UInt(batch.DescriptorFirstWord), Mul(group, 4));
            active = And(active, IsBufferWordInRange(batch.DescriptorBinding, IAdd(descriptor, UInt(3))));
            EmitConditional(active, () =>
            {
                var info = LoadBufferWord(batch.DescriptorBinding, descriptor);
                var sourceOffset = LoadBufferWord(batch.DescriptorBinding, IAdd(descriptor, UInt(1)));
                var vertexBase = LoadBufferWord(batch.DescriptorBinding, IAdd(descriptor, UInt(2)));
                var primitiveBase = LoadBufferWord(batch.DescriptorBinding, IAdd(descriptor, UInt(3)));
                var vertices = BitwiseAnd(ShiftRightLogical(info, UInt(12)), UInt(511));
                var primitives = BitwiseAnd(ShiftRightLogical(info, UInt(22)), UInt(511));
                var firstSlot = Mul(group, batch.ThreadCount);
                var lastSlot = IAdd(firstSlot, UInt(batch.ThreadCount - 1));
                var valid = Equal(BitwiseAnd(info, UInt(0x80200000)), UInt(0));
                // A padded subgroup must participate as a whole. Reject a
                // dispatch limit cutting through it before any guest barrier.
                for (uint axis = 0; axis < 3; axis++)
                {
                    var limitPointer = _module.AddInstruction(SpirvOp.AccessChain,
                        _pushConstantUintPointer, _computeDispatchLimit, UInt(0), UInt(axis));
                    valid = And(valid, Less(axis == 0 ? lastSlot : UInt(0), Load(_uintType, limitPointer)));
                }
                valid = And(valid, Less(vertices, UInt(batch.ThreadCount + 1)));
                valid = And(valid, Less(primitives, UInt(batch.ThreadCount + 1)));
                // Canonical reserved ranges prevent a descriptor from borrowing
                // another group's sources/outputs. More flexible allocation is
                // a distinct contract, not silently accepted here.
                valid = And(valid, Equal(sourceOffset, Mul(firstSlot, 9)));
                valid = And(valid, Equal(vertexBase, firstSlot));
                valid = And(valid, Equal(primitiveBase, firstSlot));
                var sourceFirst = IAdd(UInt(batch.SourceFirstWord), sourceOffset);
                var sourceLast = IAdd(UInt(batch.SourceFirstWord), IAdd(Mul(lastSlot, 9), UInt(8)));
                valid = And(valid, IsBufferWordInRange(batch.SourceBinding, sourceLast));
                if (_exportOutput is { } position)
                    valid = And(valid, IsBufferWordInRange(position.BufferBinding,
                        IAdd(Mul(lastSlot, (position.ParameterCount + 1) * 4), UInt((position.ParameterCount + 1) * 4 - 1))));
                if (_triangleOutput is { } triangle)
                    valid = And(valid, IsBufferWordInRange(triangle.BufferBinding,
                        IAdd(UInt(triangle.FirstWord), IAdd(Mul(lastSlot, 5), UInt(4)))));
                EmitConditional(valid, () =>
                {
                    Store(_programActive, priorActive);
                    Store(_programCounter, UInt(0));
                    StoreS(2, info);
                    Store(_nggBatchVertexCount, vertices);
                    var local = _module.AddInstruction(SpirvOp.CompositeExtract, _uintType, localId, 0);
                    var wave = _module.AddInstruction(SpirvOp.UDiv, _uintType, local, UInt(batch.WaveSize));
                    var first = Mul(wave, batch.WaveSize);
                    uint Tail(uint count)
                    {
                        var remaining = _module.AddInstruction(SpirvOp.ISub, _uintType, count, first);
                        var bounded = _module.AddInstruction(SpirvOp.Select, _uintType,
                            Less(remaining, UInt(batch.WaveSize)), remaining, UInt(batch.WaveSize));
                        return _module.AddInstruction(SpirvOp.Select, _uintType, Less(first, count), bounded, UInt(0));
                    }
                    StoreS(3, BitwiseOr(ShiftLeftLogical(wave, UInt(24)),
                        BitwiseOr(ShiftLeftLogical(Tail(primitives), UInt(8)), Tail(vertices))));
                    var sourceRecord = IAdd(sourceFirst, Mul(local, 9));
                    for (uint register = 0; register < 9; register++)
                        StoreV(register, LoadBufferWord(batch.SourceBinding, IAdd(sourceRecord, UInt(register))), guardWithExec: false);
                });
            });

            uint Mul(uint value, uint constant) => _module.AddInstruction(SpirvOp.IMul, _uintType, value, UInt(constant));
            uint And(uint a, uint b) => _module.AddInstruction(SpirvOp.LogicalAnd, _boolType, a, b);
            uint Equal(uint a, uint b) => _module.AddInstruction(SpirvOp.IEqual, _boolType, a, b);
            uint Less(uint a, uint b) => _module.AddInstruction(SpirvOp.ULessThan, _boolType, a, b);
        }
    }
}
