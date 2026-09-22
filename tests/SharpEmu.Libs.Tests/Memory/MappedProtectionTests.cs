using SharpEmu.Core.Memory;
using SharpEmu.HLE.Host;
using Xunit;

namespace SharpEmu.Libs.Tests.Memory;

public sealed class MappedProtectionTests
{
    [Fact]
    public void SplitsProtectionAtHostRegionBoundaries()
    {
        var host = new RecordingHost();
        Assert.True(PhysicalVirtualMemory.ProtectMappedRange(host, 0x14000, 0x28000, HostPageProtection.ReadOnly));
        Assert.Equal(new[] { (0x14000UL, 0xC000UL), (0x20000UL, 0x10000UL), (0x30000UL, 0xC000UL) }, host.Calls);
    }

    [Fact]
    public void UncommittedTailDoesNotChangeEarlierPermissions()
    {
        var host = new RecordingHost { ReservedTail = true };
        Assert.False(PhysicalVirtualMemory.ProtectMappedRange(host, 0x14000, 0x28000, HostPageProtection.ReadOnly));
        Assert.Empty(host.Calls);
    }

    [Fact]
    public void HostProtectionFailureIsReported()
    {
        var host = new RecordingHost { FailProtect = true };
        Assert.False(PhysicalVirtualMemory.ProtectMappedRange(host, 0x14000, 0x28000, HostPageProtection.ReadOnly));
        Assert.Single(host.Calls);
    }

    [Theory]
    [InlineData(0x14000UL, 0UL)]
    [InlineData(ulong.MaxValue, 2UL)]
    [InlineData(0x40000UL, 0x1000UL)]
    public void InvalidRangesAreRejected(ulong address, ulong size)
    {
        var host = new RecordingHost();
        Assert.False(PhysicalVirtualMemory.ProtectMappedRange(host, address, size, HostPageProtection.ReadOnly));
        Assert.Empty(host.Calls);
    }

    private sealed class RecordingHost : IHostMemory
    {
        public readonly List<(ulong, ulong)> Calls = [];
        public bool ReservedTail;
        public bool FailProtect;
        public bool Query(ulong address, out HostRegionInfo info)
        {
            var start = address & ~0xFFFFUL;
            info = new(start, start, 0x10000,
                ReservedTail && start == 0x30000 ? HostRegionState.Reserved : HostRegionState.Committed,
                0, HostPageProtection.ReadWrite, 0, 0);
            return address >= 0x10000 && address < 0x40000;
        }
        public bool Protect(ulong address, ulong size, HostPageProtection protection, out uint old)
        {
            old = 0;
            Calls.Add((address, size));
            return !FailProtect && (address & ~0xFFFFUL) == ((address + size - 1) & ~0xFFFFUL);
        }
        public ulong Allocate(ulong address, ulong size, HostPageProtection protection) => throw new NotSupportedException();
        public ulong Reserve(ulong address, ulong size, HostPageProtection protection) => throw new NotSupportedException();
        public bool Commit(ulong address, ulong size, HostPageProtection protection) => throw new NotSupportedException();
        public bool Free(ulong address) => throw new NotSupportedException();
        public bool ProtectRaw(ulong address, ulong size, uint protection, out uint old) => throw new NotSupportedException();
        public void FlushInstructionCache(ulong address, ulong size) => throw new NotSupportedException();
    }
}
