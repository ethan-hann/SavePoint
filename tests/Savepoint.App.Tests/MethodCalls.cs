using System.Reflection;
using System.Reflection.Emit;

namespace Savepoint.App.Tests;

// Reads a compiled method body, for tests that check where a call is made.
internal static class MethodCalls
{
    private static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    // The methods and constructors a method body calls or creates, in the order its IL does.
    public static IEnumerable<MethodBase> In(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var typeArguments = method.DeclaringType is { IsGenericType: true } type ? type.GetGenericArguments() : null;
        var methodArguments = method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null;
        for (var i = 0; i < il.Length;)
        {
            var opCode = il[i] == 0xFE ? _opCodes[(short)(0xFE00 | il[i + 1])] : _opCodes[il[i]];
            i += opCode.Size;
            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)
            {
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(il, i), typeArguments, methodArguments)!;
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
