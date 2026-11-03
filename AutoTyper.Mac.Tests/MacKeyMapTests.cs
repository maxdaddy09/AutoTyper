using Avalonia.Input;

namespace AutoTyper.Mac.Tests;

public class MacKeyMapTests
{
    [Fact]
    public void Letters_MapToTheirMacKeys()
    {
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var name = letter.ToString();
            Assert.True(Enum.TryParse<MacKey>(name, out var expected), $"MacKey defines no member {name}");
            Assert.Equal(expected, MacKeyMap.FromAvalonia(Enum.Parse<PhysicalKey>(name)));
        }
    }

    [Theory]
    [InlineData(PhysicalKey.J, MacKey.J)]
    [InlineData(PhysicalKey.K, MacKey.K)]
    [InlineData(PhysicalKey.L, MacKey.L)]
    [InlineData(PhysicalKey.N, MacKey.N)]
    [InlineData(PhysicalKey.M, MacKey.M)]
    [InlineData(PhysicalKey.O, MacKey.O)]
    [InlineData(PhysicalKey.P, MacKey.P)]
    [InlineData(PhysicalKey.I, MacKey.I)]
    [InlineData(PhysicalKey.U, MacKey.U)]
    public void Letters_AfterTheHomeRow_MapToTheirMacKeys(PhysicalKey key, MacKey expected)
    {
        Assert.Equal(expected, MacKeyMap.FromAvalonia(key));
    }

    [Fact]
    public void Digits_MapToDigitKeys()
    {
        for (var digit = 0; digit <= 9; digit++)
        {
            var expected = Enum.Parse<MacKey>($"D{digit}");
            Assert.Equal(expected, MacKeyMap.FromAvalonia(Enum.Parse<PhysicalKey>($"Digit{digit}")));
        }
    }

    [Fact]
    public void FunctionKeys_MapToF1ThroughF12()
    {
        for (var number = 1; number <= 12; number++)
        {
            var expected = Enum.Parse<MacKey>($"F{number}");
            Assert.Equal(expected, MacKeyMap.FromAvalonia(Enum.Parse<PhysicalKey>($"F{number}")));
        }
    }

    [Fact]
    public void Space_MapsToSpace()
    {
        Assert.Equal(MacKey.Space, MacKeyMap.FromAvalonia(PhysicalKey.Space));
    }

    [Theory]
    [InlineData(PhysicalKey.None)]
    [InlineData(PhysicalKey.Enter)]
    [InlineData(PhysicalKey.Escape)]
    [InlineData(PhysicalKey.Tab)]
    [InlineData(PhysicalKey.F13)]
    [InlineData(PhysicalKey.ArrowUp)]
    [InlineData(PhysicalKey.ArrowDown)]
    [InlineData(PhysicalKey.ArrowLeft)]
    [InlineData(PhysicalKey.ArrowRight)]
    public void UnsupportedKeys_ReturnNull(PhysicalKey key)
    {
        Assert.Null(MacKeyMap.FromAvalonia(key));
    }
}
