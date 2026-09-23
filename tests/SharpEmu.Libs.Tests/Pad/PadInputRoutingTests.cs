using SharpEmu.HLE;
using SharpEmu.HLE.Host;
using SharpEmu.Libs.Pad;
using Xunit;

namespace SharpEmu.Libs.Tests.Pad;

public sealed class PadInputRoutingTests
{
    [Fact]
    public void LosingFocusReleasesKeyboardAxesAndButtons()
    {
        var host = new Input { Keys = [0x44, 0x57, 0x0D] };
        var held = PadExports.SampleHostInputState(host);
        Assert.Equal((byte)255, held.LeftX);
        Assert.Equal((byte)0, held.LeftY);
        Assert.Equal(OrbisPadButton.Cross, held.Buttons);
        host.Focused = false;
        var released = PadExports.SampleHostInputState(host);
        Assert.Equal((byte)128, released.LeftX);
        Assert.Equal((byte)128, released.LeftY);
        Assert.Equal(0u, released.Buttons);
    }

    [Fact]
    public void OppositeKeysCancelAndReleaseReturnsToCenter()
    {
        var host = new Input { Keys = [0x41, 0x44] };
        Assert.Equal((byte)128, PadExports.SampleHostInputState(host).LeftX);
        host.Keys.Remove(0x44);
        Assert.Equal((byte)0, PadExports.SampleHostInputState(host).LeftX);
        host.Keys.Clear();
        Assert.Equal((byte)128, PadExports.SampleHostInputState(host).LeftX);
    }

    [Fact]
    public void DeflectedControllerWinsButCenteredAxesKeepKeyboardInput()
    {
        var host = new Input {
            Keys = [0x41, 0x57, 0x52, 0x0D],
            Gamepad = new(true, HostGamepadButtons.Circle, 220, 132, 128, 128, 100, 180)
        };
        var state = PadExports.SampleHostInputState(host);
        Assert.Equal((byte)220, state.LeftX);
        Assert.Equal((byte)0, state.LeftY);
        Assert.Equal((byte)255, state.L2);
        Assert.Equal((byte)180, state.R2);
        Assert.Equal(OrbisPadButton.Cross | OrbisPadButton.Circle | OrbisPadButton.L2, state.Buttons);
    }

    [Fact]
    public void KeyboardDeadzoneBoundaryIsInclusive()
    {
        var host = new Input { Keys = [0x44], Gamepad = new(true, 0, 118, 128, 128, 128, 0, 0) };
        Assert.Equal((byte)255, PadExports.SampleHostInputState(host).LeftX);
        host.Gamepad = host.Gamepad.Value with { LeftX = 117 };
        Assert.Equal((byte)117, PadExports.SampleHostInputState(host).LeftX);
    }

    [Fact]
    public void ReadStateWritesExactlyOneGuestRecord()
    {
        var memory = new FakeCpuMemory(0x10000, 0x1000);
        var ctx = new CpuContext(memory, Generation.Gen5);
        var guarded = Enumerable.Repeat((byte)0xA5, 0x78+16).ToArray();
        Assert.True(memory.TryWrite(0x10100, guarded));
        ctx[CpuRegister.Rdi] = 1;
        ctx[CpuRegister.Rsi] = 0x10108;
        Assert.Equal(0, PadExports.PadReadState(ctx));
        Assert.True(memory.TryRead(0x10100, guarded));
        Assert.All(guarded[..8], b => Assert.Equal((byte)0xA5, b));
        Assert.All(guarded[(8+0x78)..], b => Assert.Equal((byte)0xA5, b));
        Assert.Equal((byte)1, guarded[8+0x4C]);
        Assert.Equal((byte)1, guarded[8+0x68]);
        Assert.True(BitConverter.ToUInt64(guarded, 8+0x50) > 0);
    }

    private sealed class Input : IHostInput
    {
        public HashSet<int> Keys = [];
        public bool Focused = true;
        public HostGamepadState? Gamepad;
        public bool IsHostWindowFocused() => Focused;
        public bool IsKeyDown(int key) => Keys.Contains(key);
        public int GetGamepadStates(Span<HostGamepadState> destination) {
            if (Gamepad is not { } pad) return 0;
            destination[0] = pad;
            return 1;
        }
        public void EnsureStarted() { }
        public string? DescribeConnectedGamepad() => null;
        public void SetRumble(byte largeMotor, byte smallMotor) { }
        public void SetTriggerRumble(byte? leftTrigger, byte? rightTrigger) { }
        public void SetLightbar(byte red, byte green, byte blue) { }
        public void ResetLightbar() { }
    }
}
