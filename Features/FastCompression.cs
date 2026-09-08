using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;
using CompoundingPerf.Telemetry;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Servers.Http;

namespace CompoundingPerf.Features;

/// <summary>
/// S9 — vanilla zlib-compresses every JSON response at <see cref="CompressionLevel.SmallestSize"/>,
/// zlib's slowest setting, over what is almost always a localhost or LAN connection.
/// <see cref="CompressionLevel.Fastest"/> costs several times less CPU for a few percent
/// more bytes that never leave the machine.
///
/// <para>Verified still present in 4.1.5: both <c>SptHttpListener.SendZlibJsonAsync</c>
/// and <c>SptHttpListener.SendStreamedJsonAsync</c> construct
/// <c>new ZLibStream(resp.Body, CompressionLevel.SmallestSize)</c>.</para>
///
/// <para><b>4.0 → 4.1</b>: this was a DI <c>TypeOverride</c> subclass of
/// <c>SptHttpListener</c> backed by a Harmony body detour. 4.1 renamed the method
/// (<c>SendZlibJson</c> → <c>SendZlibJsonAsync</c>), added a second streamed path, and
/// left neither virtual. Rather than replace two method bodies, the patch now rewrites
/// only the constant: the <c>CompressionLevel</c> operand pushed immediately before each
/// <c>ZLibStream</c> constructor becomes a call to <see cref="CurrentLevel"/>. Everything
/// else about the response path is untouched, and the level stays runtime-configurable.</para>
///
/// <para>Both methods are <c>async</c>, so the IL lives in the compiler-generated state
/// machine — the patch targets <c>AccessTools.AsyncMoveNext</c>, not the stub.</para>
/// </summary>
internal static class FastCompression
{
    /// <summary>Kill-switch. While false <see cref="CurrentLevel"/> returns vanilla's level.</summary>
    public static volatile bool IsEnabled;

    private static volatile int _level = (int)CompressionLevel.Fastest;

    public static CompressionLevel Level
    {
        get => (CompressionLevel)_level;
        set => _level = (int)value;
    }

    private static readonly string[] TargetMethods = ["SendZlibJsonAsync", "SendStreamedJsonAsync"];

    public static void Apply(Harmony harmony, ISptLogger<CompoundingPerfMod> logger)
    {
        var transpiler = new HarmonyMethod(AccessTools.Method(typeof(FastCompression), nameof(Transpiler)));

        foreach (var name in TargetMethods)
        {
            var stub = AccessTools.Method(typeof(SptHttpListener), name);
            if (stub is null)
            {
                logger.Warning($"[CompoundingPerf/S9] SptHttpListener.{name} not found — SPT internals moved. That response path stays at vanilla compression.");
                continue;
            }

            var moveNext = AccessTools.AsyncMoveNext(stub);
            if (moveNext is null)
            {
                logger.Warning($"[CompoundingPerf/S9] SptHttpListener.{name} is no longer an async state machine — cannot reach its IL. That response path stays at vanilla compression.");
                continue;
            }

            var before = _rewrites;
            harmony.Patch(moveNext, transpiler: transpiler);
            if (_rewrites == before)
            {
                logger.Warning($"[CompoundingPerf/S9] no ZLibStream level operand found in {name} — that response path stays at vanilla compression.");
            }
        }
    }

    private static int _rewrites;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var zlibCtor = AccessTools.Constructor(typeof(ZLibStream), [typeof(Stream), typeof(CompressionLevel)]);
        var currentLevel = AccessTools.Method(typeof(FastCompression), nameof(CurrentLevel));

        // The level is the last thing pushed before the constructor, so hold each
        // instruction back one step and rewrite it once we see what it feeds.
        CodeInstruction? previous = null;

        foreach (var instruction in instructions)
        {
            if (previous is not null)
            {
                var feedsZlibCtor = instruction.opcode == OpCodes.Newobj && ReferenceEquals(instruction.operand, zlibCtor);

                // Only a literal level is safe to swap - anything else is already
                // computed and not ours to second-guess.
                if (feedsZlibCtor && previous.opcode.Name is { } name && name.StartsWith("ldc.i4", StringComparison.Ordinal))
                {
                    _rewrites++;
                    yield return new CodeInstruction(OpCodes.Call, currentLevel) { labels = previous.labels, blocks = previous.blocks };
                }
                else
                {
                    yield return previous;
                }
            }

            previous = instruction;
        }

        if (previous is not null)
        {
            yield return previous;
        }
    }

    /// <summary>Replaces the literal <c>CompressionLevel</c> operand at each patched call site.</summary>
    public static CompressionLevel CurrentLevel()
    {
        if (!IsEnabled)
        {
            return CompressionLevel.SmallestSize; // vanilla
        }

        TelemetryHub.Increment("s9.compression.responses");
        return Level;
    }

    public static CompressionLevel ParseLevel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "optimal" => CompressionLevel.Optimal,
        "smallestsize" => CompressionLevel.SmallestSize,
        "nocompression" => CompressionLevel.NoCompression,
        _ => CompressionLevel.Fastest,
    };

    public static void Configure(FastCompressionOptions options, ISptLogger<CompoundingPerfMod> logger)
    {
        Level = ParseLevel(options.Level);
        IsEnabled = options.Enabled;
        if (options.Enabled)
        {
            logger.Success($"[CompoundingPerf/S9] fast response compression ACTIVE — zlib level {Level} (vanilla: SmallestSize)");
        }
        else
        {
            logger.Info("[CompoundingPerf/S9] fast response compression disabled in config");
        }
    }
}
