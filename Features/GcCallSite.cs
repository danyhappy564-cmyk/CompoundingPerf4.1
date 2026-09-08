using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CompoundingPerf.Features;

/// <summary>
/// Shared plumbing for the two features that neutralise a forced <c>GC.Collect</c>
/// (<see cref="CalmRagfair"/> and <see cref="CalmRaidStart"/>).
///
/// <para>Both work the same way and deliberately do as little as possible: rewrite the
/// single <c>GC.Collect</c> call instruction into a call to a replacement with the
/// identical signature, so the arguments vanilla already pushed stay valid and every other
/// instruction in the method is left exactly as the compiler emitted it. Behaviour is
/// vanilla by construction rather than by re-implementation, and because the decision moved
/// into a method rather than into the patch, the config still takes effect at runtime
/// instead of needing a server restart.</para>
/// </summary>
internal static class GcCallSite
{
    /// <summary>The overload both call sites use.</summary>
    public static readonly MethodInfo VanillaCollect =
        AccessTools.Method(typeof(GC), nameof(GC.Collect), [typeof(int), typeof(GCCollectionMode), typeof(bool), typeof(bool)]);

    /// <summary>Replaces every call to that overload with <paramref name="replacement"/>,
    /// counting how many it found. A count of zero means SPT moved the call and the feature
    /// is inert — the callers report that rather than pretending to be active.</summary>
    public static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions, MethodInfo replacement, Action onRewrite)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && ReferenceEquals(instruction.operand, VanillaCollect))
            {
                onRewrite();
                yield return new CodeInstruction(OpCodes.Call, replacement) { labels = instruction.labels, blocks = instruction.blocks };
                continue;
            }

            yield return instruction;
        }
    }
}
