using System.Reflection;
using System.Runtime.InteropServices;

namespace AutoTyper.Mac.Tests;

// Everything native here is only checked for shape: nothing in these tests calls into macOS on Windows.
// The real check is `AutoTyper --selftest` on a Mac, which the last test runs when it is on one.
public class NativeTests
{
    const string NativeNamespace = "AutoTyper.Mac.Native";

    [Fact]
    public void SplitForEvents_EmptyTextGivesNoGroups()
    {
        Assert.Empty(MacKeySender.SplitForEvents(""));
    }

    [Fact]
    public void SplitForEvents_ShortTextIsOneGroup()
    {
        Assert.Equal(new[] { "hello" }, MacKeySender.SplitForEvents("hello"));
    }

    [Fact]
    public void SplitForEvents_LongTextSplitsAtTwentyUnits()
    {
        var groups = MacKeySender.SplitForEvents(new string('a', 45));

        Assert.Equal(new[] { 20, 20, 5 }, groups.Select(g => g.Length));
    }

    [Fact]
    public void SplitForEvents_NeverSplitsASurrogatePair()
    {
        var text = new string('a', 19) + "\U0001F600";

        var groups = MacKeySender.SplitForEvents(text);

        Assert.Equal(new[] { 19, 2 }, groups.Select(g => g.Length));
        Assert.Equal(text, string.Concat(groups));
    }

    [Fact]
    public void SplitForEvents_RejoinsToTheTextAndKeepsEveryPairWholeAtEveryLimit()
    {
        // Emoji at every offset relative to the group boundary, plus plain letters.
        var text = string.Concat(Enumerable.Range(0, 40).Select(i => i % 3 == 0 ? "\U0001F600" : "ab"));

        for (var limit = 2; limit <= 25; limit++)
        {
            var groups = MacKeySender.SplitForEvents(text, limit);

            Assert.Equal(text, string.Concat(groups));
            foreach (var group in groups)
            {
                Assert.InRange(group.Length, 1, limit);
                Assert.False(char.IsLowSurrogate(group[0]), $"limit {limit}: a group starts with half a pair");
                Assert.False(char.IsHighSurrogate(group[^1]), $"limit {limit}: a group ends with half a pair");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-5)]
    public void SplitForEvents_RejectsALimitThatCannotHoldAPair(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MacKeySender.SplitForEvents("abc", limit));
    }

    [Fact]
    public void EverySymbolImportedIsListedInTheSelfTest()
    {
        var declared = DeclaredImports().Select(i => $"{i.Library} :: {i.EntryPoint}").ToList();
        var listed = NativeSelfTest.Symbols.Select(s => $"{s.Library} :: {s.Symbol}").ToList();

        Assert.NotEmpty(declared);
        Assert.Empty(declared.Except(listed));   // imported but not checked by the self-test
        Assert.Empty(listed.Except(declared));   // checked by the self-test but not declared
        Assert.Equal(declared.Count, declared.Distinct().Count());
        Assert.Equal(listed.Count, listed.Distinct().Count());
    }

    [Fact]
    public void EveryLibraryIsASystemFrameworkByAbsolutePath()
    {
        foreach (var import in DeclaredImports())
        {
            Assert.StartsWith("/System/Library/Frameworks/", import.Library);
            // The folder name and the file name are the same word: Foo.framework/Foo.
            var folder = import.Library.Split('/')[^2];
            var file = import.Library.Split('/')[^1];
            Assert.Equal(file + ".framework", folder);
        }
    }

    [Fact]
    public void BooleansCrossTheNativeBoundaryAsOneByte()
    {
        // A C bool / Boolean is one byte; the default .NET marshalling for bool is four.
        var checkedAny = false;
        foreach (var method in DeclaredImportMethods())
        {
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType == typeof(bool)))
            {
                checkedAny = true;
                Assert.Equal(UnmanagedType.U1, parameter.GetCustomAttribute<MarshalAsAttribute>()?.Value);
            }

            if (method.ReturnType == typeof(bool))
            {
                checkedAny = true;
                Assert.Equal(UnmanagedType.U1, method.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>()?.Value);
            }
        }

        Assert.True(checkedAny);
    }

    [Fact]
    public void TheNativeStructsAreSequentialAndEightBytes()
    {
        var carbon = typeof(MacKeySender).Assembly.GetTypes()
            .Where(t => t.Namespace == NativeNamespace && t.IsValueType)
            .ToDictionary(t => t.Name);

        foreach (var name in new[] { "EventTypeSpec", "EventHotKeyID" })
        {
            Assert.True(carbon.TryGetValue(name, out var type), $"{name} is not declared");
            Assert.True(type!.IsLayoutSequential, $"{name} is not sequential");
            Assert.Equal(8, Marshal.SizeOf(type));
        }
    }

    [Fact]
    public void InstallEventHandlerTakesItsTypeCountAsANativeSizedInteger()
    {
        var install = DeclaredImportMethods().Single(m => m.Name == "InstallEventHandler");

        Assert.True(
            install.GetParameters()[2].ParameterType == typeof(nuint),
            "numTypes must be nuint: the C parameter is an ItemCount, which is 'typedef unsigned long ItemCount;' " +
            "in MacTypes.h, 64 bits on macOS, and the callee reads the whole register; a uint leaves the upper half undefined.");
    }

    [Fact]
    public void SelfTest_ReportsAccordingToThePlatform()
    {
        var output = new StringWriter();

        var exitCode = NativeSelfTest.Run(output);

        var expected = OperatingSystem.IsMacOS() ? "selftest ok" : "selftest: not macOS";
        Assert.Equal(expected, output.ToString().Trim());
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void CreatingAKeySenderTouchesNoNativeCode()
    {
        // On Windows a native call would throw DllNotFoundException, so getting here is the proof.
        IKeySender sender = new MacKeySender();

        Assert.NotNull(sender);
        Assert.NotEmpty(NativeSelfTest.Symbols);
    }

    static IEnumerable<MethodInfo> DeclaredImportMethods() =>
        typeof(MacKeySender).Assembly.GetTypes()
            .Where(t => t.Namespace == NativeNamespace)
            .SelectMany(t => t.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<DllImportAttribute>() != null);

    static IEnumerable<(string Library, string EntryPoint)> DeclaredImports() =>
        DeclaredImportMethods().Select(m =>
        {
            var import = m.GetCustomAttribute<DllImportAttribute>()!;
            return (import.Value, string.IsNullOrEmpty(import.EntryPoint) ? m.Name : import.EntryPoint);
        });
}
