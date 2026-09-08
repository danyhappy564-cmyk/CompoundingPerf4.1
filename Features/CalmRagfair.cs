using System.Reflection;
using CompoundingPerf.Telemetry;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Servers;

namespace CompoundingPerf.Features;

/// <summary>
/// S8 — vanilla's flea-offer expiry pass ends with a forced, blocking, compacting full
/// GC: a recurring multi-hundred-millisecond stall on a large heap, for memory the
/// runtime's server GC would have reclaimed on its own schedule anyway.
///
/// <para>Verified still present in 4.1.5 —
/// <c>RagfairServer.ProcessExpiredFleaOffers</c>:</para>
/// <code>
/// ragfairOfferService.RemoveExpiredOffers();
/// GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: true, compacting: true);
/// </code>
///
/// <para><b>4.0 → 4.1</b>: this used to be a DI <c>TypeOverride</c> subclass of
/// <c>RagfairServer</c> that reproduced <c>Update()</c> minus the collect. 4.1 sealed
/// <c>RagfairServer</c>, so that is no longer possible. The replacement is narrower and
/// strictly safer: a transpiler that rewrites the single <c>GC.Collect</c> call site into
/// a call to <see cref="MaybeCollect"/> with the identical argument list. Nothing else in
/// the method is touched, so behaviour is vanilla by construction rather than by careful
/// re-implementation — and because the decision moved into a method rather than into the
/// patch, the config flag still works at runtime instead of needing a server restart.</para>
/// </summary>
internal static class CalmRagfair
{
    /// <summary>Kill-switch. While false the forced collect happens exactly as in vanilla.</summary>
    public static volatile bool IsEnabled;

    private static MethodBase Target =>
        AccessTools.Method(typeof(RagfairServer), "ProcessExpiredFleaOffers")
        ?? throw new MissingMethodException("RagfairServer.ProcessExpiredFleaOffers not found — SPT internals moved");

    public static void Apply(Harmony harmony, ISptLogger<CompoundingPerfMod> logger)
    {
        var target = Target;
        var patched = harmony.Patch(target, transpiler: new HarmonyMethod(AccessTools.Method(typeof(CalmRagfair), nameof(Transpiler))));

        // A transpiler that matched nothing leaves the method byte-identical and would
        // silently do nothing at runtime, so say whether the rewrite actually landed.
        if (_rewrites == 0)
        {
            logger.Warning("[CompoundingPerf/S8] no GC.Collect call found in RagfairServer.ProcessExpiredFleaOffers — SPT may have removed it already. Feature inactive.");
        }
        else if (patched is null)
        {
            logger.Warning("[CompoundingPerf/S8] Harmony returned no patched method for ProcessExpiredFleaOffers. Feature inactive.");
        }
    }

    private static int _rewrites;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
        GcCallSite.Redirect(instructions, AccessTools.Method(typeof(CalmRagfair), nameof(MaybeCollect)), () => _rewrites++);

    /// <summary>Same signature as the <c>GC.Collect</c> overload it replaces, so the
    /// arguments vanilla already pushed onto the stack stay valid.</summary>
    public static void MaybeCollect(int generation, GCCollectionMode mode, bool blocking, bool compacting)
    {
        if (IsEnabled)
        {
            TelemetryHub.Increment("s8.ragfair.collects_skipped");
            return;
        }

        GC.Collect(generation, mode, blocking, compacting);
    }

    public static void Configure(RagfairCalmUpdatesOptions options, ISptLogger<CompoundingPerfMod> logger)
    {
        IsEnabled = options.Enabled;
        if (options.Enabled)
        {
            logger.Success("[CompoundingPerf/S8] calm ragfair updates ACTIVE — offer expiry runs without vanilla's forced blocking GC");
        }
        else
        {
            logger.Info("[CompoundingPerf/S8] calm ragfair updates disabled in config");
        }
    }
}
