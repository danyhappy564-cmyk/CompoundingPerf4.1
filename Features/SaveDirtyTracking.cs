using CompoundingPerf.Telemetry;
using HarmonyLib;
using Microsoft.AspNetCore.Http;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;

namespace CompoundingPerf.Features;

/// <summary>
/// S11 — vanilla's <c>SaveServer.SaveProfileAsync</c> serializes the full profile to JSON
/// and MD5-hashes it on every periodic save tick, and only then compares hashes to decide
/// whether to touch the disk. An idle session pays the serialize and the hash forever, to
/// discover nothing changed. This skips the call outright when the session is provably
/// clean; <see cref="ProfileDirtyTracker"/> holds the policy.
///
/// <para>Verified still present in 4.1.5. 4.1 did add a per-profile
/// <c>SemaphoreSlim</c> around the save, so concurrent saves for one profile no longer
/// overlap — that is what retired S1, the old coalescer — but the serialize and hash on
/// every tick are unchanged.</para>
///
/// <para><b>4.0 → 4.1</b>: both halves used to live in DI <c>TypeOverride</c> subclasses
/// (<c>CoalescingSaveServer</c> skipped, <c>CachingHttpRouter</c> marked). 4.1 sealed
/// <c>SaveServer</c> and left <c>HttpRouter.GetResponseObjectAsync</c> non-virtual, so
/// both are Harmony now: a skipping prefix on the save, and a read-only prefix on the
/// router that only observes the request path.</para>
/// </summary>
internal static class SaveDirtyTracking
{
    public static void Apply(Harmony harmony, ISptLogger<CompoundingPerfMod> logger)
    {
        var save = AccessTools.Method(typeof(SaveServer), nameof(SaveServer.SaveProfileAsync));
        if (save is null)
        {
            logger.Warning("[CompoundingPerf/S11] SaveServer.SaveProfileAsync not found — SPT internals moved. Dirty-tracking inactive.");
            return;
        }

        var route = AccessTools.Method(typeof(HttpRouter), nameof(HttpRouter.GetResponseObjectAsync));
        if (route is null)
        {
            // Without the marking half every session would look clean forever, which
            // would drop real saves. Refuse to install the skip on its own.
            logger.Warning("[CompoundingPerf/S11] HttpRouter.GetResponseObjectAsync not found — cannot observe requests, so the save skip would be unsafe. Dirty-tracking inactive.");
            return;
        }

        harmony.Patch(route, prefix: new HarmonyMethod(AccessTools.Method(typeof(SaveDirtyTracking), nameof(MarkRequestPrefix))));
        harmony.Patch(save, prefix: new HarmonyMethod(AccessTools.Method(typeof(SaveDirtyTracking), nameof(SkipCleanSavePrefix))));
    }

    /// <summary>Observes every routed request and marks the session dirty unless the path
    /// is on the known-pure list. Never alters the request or the response.</summary>
    private static void MarkRequestPrefix(HttpRequest req, MongoId sessionID)
    {
        try
        {
            ProfileDirtyTracker.MarkRequest(sessionID, req?.Path.Value);
        }
        catch
        {
            // A failure to mark must never take down request routing. Worst case the
            // session stays dirty and gets saved - the safe direction.
        }
    }

    /// <summary>Skips the save when the session is clean and its last real save is still
    /// inside the force interval. Returning a completed <c>Task&lt;long&gt;</c> of 0 matches
    /// what vanilla returns for a save it decided not to perform.</summary>
    private static bool SkipCleanSavePrefix(MongoId sessionID, ref Task<long> __result)
    {
        if (!ProfileDirtyTracker.MaySkipSave(sessionID))
        {
            ProfileDirtyTracker.OnRealSaveStarting(sessionID);
            return true;
        }

        TelemetryHub.Increment("s11.saves.skipped");
        __result = Task.FromResult(0L);
        return false;
    }

    public static void Configure(SaveDirtyTrackingOptions options, ISptLogger<CompoundingPerfMod> logger)
    {
        if (options.Enabled)
        {
            ProfileDirtyTracker.ForceSaveIntervalSeconds = Math.Max(10, options.ForceSaveIntervalSeconds);
            ProfileDirtyTracker.IsEnabled = true;
            logger.Success($"[CompoundingPerf/S11] save dirty-tracking ACTIVE — clean sessions skip serialization (force-save every {ProfileDirtyTracker.ForceSaveIntervalSeconds}s)");
        }
        else
        {
            ProfileDirtyTracker.IsEnabled = false;
            logger.Info("[CompoundingPerf/S11] save dirty-tracking disabled in config");
        }
    }
}
