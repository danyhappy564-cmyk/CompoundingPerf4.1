using System.Reflection;
using System.Reflection.Emit;
using CompoundingPerf.Telemetry;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Services.InRaid;

namespace CompoundingPerf.Features;

/// <summary>
/// S15 (new in 2.0) — the last thing <c>LocationLifecycleService.StartLocalRaidAsync</c>
/// does before returning the raid-start response to the client is:
///
/// <code>GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);</code>
///
/// <para>That is the most expensive collection .NET offers — full generation, aggressive
/// mode (which also decommits memory back to the OS), blocking, and compacting — and it
/// runs <b>inside the request path</b>. The player sits on the loading screen while the
/// server walks and compacts a multi-gigabyte heap, every single raid.</para>
///
/// <para>Note this is <b>not</b> a 4.1 regression: the same call is in 4.0.13. The
/// original mod covered the ragfair collect (S8) and left this one alone, which is the gap
/// this feature closes. It is the bigger of the two — the ragfair collect uses
/// <c>Optimized</c> and fires off a background timer, this one uses <c>Aggressive</c> and
/// fires with the client waiting.</para>
///
/// <para>The default is not to drop the collect but to <b>stop blocking on it</b>: reclaiming
/// before a raid's allocations is a reasonable thing to want, so <see cref="RaidStartGcMode.Background"/>
/// still asks for a gen-2 collection, just a non-blocking, non-compacting one that the
/// runtime finishes on its own while the raid loads. <see cref="RaidStartGcMode.Skip"/>
/// drops it entirely, <see cref="RaidStartGcMode.Vanilla"/> forwards it untouched.</para>
/// </summary>
internal static class CalmRaidStart
{
    private static volatile int _mode = (int)RaidStartGcMode.Background;

    public static RaidStartGcMode Mode
    {
        get => (RaidStartGcMode)_mode;
        set => _mode = (int)value;
    }

    private static int _rewrites;

    public static void Apply(Harmony harmony, ISptLogger<CompoundingPerfMod> logger)
    {
        var stub = AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.StartLocalRaidAsync));
        if (stub is null)
        {
            logger.Warning("[CompoundingPerf/S15] LocationLifecycleService.StartLocalRaidAsync not found — SPT internals moved. Feature inactive.");
            return;
        }

        // The collect lives in the async state machine, not in the stub.
        var moveNext = AccessTools.AsyncMoveNext(stub);
        if (moveNext is null)
        {
            logger.Warning("[CompoundingPerf/S15] StartLocalRaidAsync is no longer an async state machine — cannot reach its IL. Feature inactive.");
            return;
        }

        harmony.Patch(moveNext, transpiler: new HarmonyMethod(AccessTools.Method(typeof(CalmRaidStart), nameof(Transpiler))));

        if (_rewrites == 0)
        {
            logger.Warning("[CompoundingPerf/S15] no GC.Collect call found in StartLocalRaidAsync — SPT may have removed it already. Feature inactive.");
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        GcCallSite.Redirect(instructions, AccessTools.Method(typeof(CalmRaidStart), nameof(MaybeCollect)), () => _rewrites++);

    /// <summary>Same signature as the <c>GC.Collect</c> overload it replaces, so the
    /// arguments vanilla already pushed onto the stack stay valid.</summary>
    public static void MaybeCollect(int generation, GCCollectionMode mode, bool blocking, bool compacting)
    {
        switch (Mode)
        {
            case RaidStartGcMode.Skip:
                TelemetryHub.Increment("s15.raidstart.collects_skipped");
                return;

            case RaidStartGcMode.Background:
                TelemetryHub.Increment("s15.raidstart.collects_backgrounded");
                GC.Collect(generation, GCCollectionMode.Optimized, blocking: false, compacting: false);
                return;

            default:
                GC.Collect(generation, mode, blocking, compacting);
                return;
        }
    }

    public static RaidStartGcMode ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "vanilla" => RaidStartGcMode.Vanilla,
        "skip" => RaidStartGcMode.Skip,
        _ => RaidStartGcMode.Background,
    };

    public static void Configure(RaidStartGcOptions options, ISptLogger<CompoundingPerfMod> logger)
    {
        Mode = options.Enabled ? ParseMode(options.Mode) : RaidStartGcMode.Vanilla;

        switch (Mode)
        {
            case RaidStartGcMode.Background:
                logger.Success("[CompoundingPerf/S15] calm raid start ACTIVE — the raid-start collect is now background and non-compacting instead of blocking the response");
                break;
            case RaidStartGcMode.Skip:
                logger.Success("[CompoundingPerf/S15] calm raid start ACTIVE — the raid-start collect is skipped entirely");
                break;
            default:
                logger.Info("[CompoundingPerf/S15] calm raid start disabled in config — vanilla's aggressive blocking collect runs");
                break;
        }
    }
}

public enum RaidStartGcMode
{
    /// <summary>Forward vanilla's aggressive, blocking, compacting collect untouched.</summary>
    Vanilla,

    /// <summary>Still ask for a gen-2 collection, but non-blocking and non-compacting, so
    /// the raid-start response is not held up by it.</summary>
    Background,

    /// <summary>Do not collect at all; leave it to the runtime's own schedule.</summary>
    Skip,
}
