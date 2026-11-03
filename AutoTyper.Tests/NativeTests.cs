using System.Runtime.InteropServices;

namespace AutoTyper.Tests;

public class NativeTests
{
    const HotkeyModifiers CtrlAltShift = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    static readonly HotkeyBinding F23 = new(CtrlAltShift, Keys.F23);
    static readonly HotkeyBinding F22 = new(CtrlAltShift, Keys.F22);

    [Fact]
    public void InputStruct_IsFortyBytesOnX64()
    {
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    [Fact]
    public void Registrar_RegistersUnusedCombination()
    {
        using var registrar = new HotkeyRegistrar();

        Assert.True(registrar.TryRegister(F23));
        Assert.Equal(F23, registrar.Current);
    }

    [Fact]
    public void Registrar_SecondInstanceCannotTakeSameCombination_UntilFirstIsDisposed()
    {
        var first = new HotkeyRegistrar();
        using var second = new HotkeyRegistrar();
        Assert.True(first.TryRegister(F23));

        Assert.False(second.TryRegister(F23));

        first.Dispose();
        Assert.True(second.TryRegister(F23));
    }

    [Fact]
    public void Registrar_FailedRegistration_KeepsPreviousBinding()
    {
        using var holder = new HotkeyRegistrar();
        using var registrar = new HotkeyRegistrar();
        Assert.True(holder.TryRegister(F23));
        Assert.True(registrar.TryRegister(F22));

        Assert.False(registrar.TryRegister(F23));

        Assert.Equal(F22, registrar.Current);
    }

    [Fact]
    public void Registrar_ReRegisteringCurrentBinding_ReturnsTrue()
    {
        using var registrar = new HotkeyRegistrar();
        Assert.True(registrar.TryRegister(F23));

        Assert.True(registrar.TryRegister(F23));
        Assert.Equal(F23, registrar.Current);
    }

    [Fact]
    public void Registrar_ChangingBinding_ReleasesTheOldCombination()
    {
        using var registrar = new HotkeyRegistrar();
        using var other = new HotkeyRegistrar();
        Assert.True(registrar.TryRegister(F23));
        Assert.True(registrar.TryRegister(F22));

        Assert.True(other.TryRegister(F23));
    }
}
