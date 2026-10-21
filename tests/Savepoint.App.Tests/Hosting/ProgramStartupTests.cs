using System.Reflection;
using System.Reflection.Emit;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Hosting;

public class ProgramStartupTests
{
    private static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    [Fact]
    public void Main_runs_the_installer_hooks_before_anything_else()
    {
        // The installer and updater start the executable with hook arguments; Main must handle them (they may end the process)
        // before the single-instance guard or a window exists, and Velopack's packager looks for the call in Main itself.
        var main = typeof(Program).GetMethod("Main", BindingFlags.Public | BindingFlags.Static)!;

        var firstCalls = CallsIn(main).Take(2).Select(target => $"{target.DeclaringType!.FullName}.{target.Name}").ToArray();

        Assert.Equal(["Velopack.VelopackApp.Build", "Velopack.VelopackApp.Run"], firstCalls);
    }

    [Fact]
    public void No_source_file_asks_for_updates()
    {
        // A release feed does not exist, and the app makes no request nobody asked for.
        var root = TestEnvironment.RepoRoot ?? throw new InvalidOperationException("The tests did not run inside a checkout.");
        var offenders = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("UpdateManager", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(offenders);
    }

    // The methods and constructors a method body calls or creates, in the order its IL does.
    private static IEnumerable<MethodBase> CallsIn(MethodInfo method)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (var i = 0; i < il.Length;)
        {
            var opCode = il[i] == 0xFE ? _opCodes[(short)(0xFE00 | il[i + 1])] : _opCodes[il[i]];
            i += opCode.Size;
            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)
            {
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, i))!;
            }

            i += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                _ => 4,
            };
        }
    }
}
