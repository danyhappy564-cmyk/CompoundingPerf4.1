using CompoundingPerf.Telemetry;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Utils.Cloners;

namespace CompoundingPerf.Features;

/// <summary>
/// S12 — fixes a vanilla bug rather than an inefficiency. <c>GetBotRandomizationDetails</c>
/// hands back the live <c>RandomisationDetails</c> record out of the shared
/// <c>BotConfig</c>, and <c>BotInventoryGenerator.GenerateAndAddEquipmentToBot</c> applies
/// night-raid equipment modifiers by writing straight back into it. Three consequences:
///
/// <list type="number">
///   <item><b>Compounding</b> — the modifier is added once per generated bot
///     (<c>newWeight = modifier + currentValue</c>), so chances drift toward the 0/100
///     clamp bounds as a raid generates more bots.</item>
///   <item><b>Persistence</b> — the mutation is never reverted, so one night raid leaves
///     the modifiers baked into config for every later raid, day or night, until the
///     server restarts.</item>
///   <item><b>Data race</b> — bots are generated in parallel, so those are concurrent
///     read-modify-writes on a plain <c>Dictionary</c>.</item>
/// </list>
///
/// <para>Verified still present in 4.1.5 — <c>BotInventoryGenerator</c> still does
/// <c>botRandomizationDetails.EquipmentMods[key] = Math.Clamp(...)</c> on the returned
/// object.</para>
///
/// <para>The fix hands every caller its own clone, so the nighttime adjustment applies
/// exactly once per bot to that bot's private copy — the evident intent of the code —
/// nothing persists across raids, and there is no shared object to race on.</para>
///
/// <para><b>Behaviour note</b>: one downstream reader (<c>BotEquipmentModGenerator</c>)
/// used to observe the leaked, progressively compounded values; with isolation it reads
/// pristine config. That is deliberate — what it read before was corrupted by accident,
/// not by design.</para>
///
/// <para><b>4.0 → 4.1</b>: was a DI <c>TypeOverride</c> subclass of <c>BotHelper</c>.
/// 4.1 left <c>BotHelper</c> unsealed but made the method non-virtual, so an override is
/// no longer dispatched to. Same one-line behaviour, delivered as a postfix.</para>
/// </summary>
internal static class IsolatedBotRandomisation
{
    /// <summary>Kill-switch. While false the shared reference is returned, as in vanilla.</summary>
    public static volatile bool IsEnabled;

    private static ICloner? _cloner;

    public static void Apply(Harmony harmony, ICloner cloner, ISptLogger<CompoundingPerfMod> logger)
    {
        _cloner = cloner;

        var target = AccessTools.Method(typeof(BotHelper), nameof(BotHelper.GetBotRandomizationDetails));
        if (target is null)
        {
            logger.Warning("[CompoundingPerf/S12] BotHelper.GetBotRandomizationDetails not found — SPT internals moved. Feature inactive.");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(IsolatedBotRandomisation), nameof(ClonePostfix))));
    }

    private static void ClonePostfix(ref RandomisationDetails? __result)
    {
        if (!IsEnabled || __result is null || _cloner is null)
        {
            return;
        }

        TelemetryHub.Increment("s12.randomisation.clones");
        __result = _cloner.Clone(__result);
    }

    public static void Configure(IsolatedBotRandomisationOptions options, ISptLogger<CompoundingPerfMod> logger)
    {
        IsEnabled = options.Enabled;
        if (options.Enabled)
        {
            logger.Success("[CompoundingPerf/S12] isolated bot randomisation ACTIVE — nighttime modifiers no longer compound, persist, or race on shared config");
        }
        else
        {
            logger.Info("[CompoundingPerf/S12] isolated bot randomisation disabled in config");
        }
    }
}
