using CompoundingPerf.Telemetry;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Ws;
using SPTarkov.Server.Core.Services.Server;

namespace CompoundingPerf.Features;

/// <summary>
/// S13 — the <c>/client/notifier/channel/create</c> long-poll. Vanilla runs
/// <c>Task.Factory.StartNew</c> and then <c>Thread.Sleep(300)</c> in a loop for up to
/// fifteen seconds, which pins one thread-pool thread per connected client for as long as
/// that client is connected — and the EFT client re-polls immediately after every
/// response, so the pinning is effectively permanent. With N players on a FIKA host that
/// is N thread-pool threads doing nothing but sleeping.
///
/// <para>Verified still present in 4.1.5 — <c>NotifierController.NotifyAsync</c> is
/// unchanged in shape.</para>
///
/// <para>The replacement keeps the observable behaviour exactly: same 300 ms poll
/// interval, same 15 s budget, same default-notification fallback. It just awaits
/// <c>Task.Delay</c> instead of sleeping, which returns the thread to the pool between
/// checks.</para>
///
/// <para><b>4.0 → 4.1</b>: S13 used to have a second half that reworked
/// <c>SptWebSocketConnectionHandler</c> — serialize once per message instead of once per
/// socket, and don't hold the global socket lock across network writes. <b>4.1 does both
/// of those itself</b>: the handler now serializes to a <c>byte[]</c> once and hands it to
/// <c>SendRawToSocketsAsync</c>, which takes a per-socket <c>SemaphoreSlim</c> out of
/// <c>_sendGates</c> and only holds <c>_socketsLock</c> long enough to snapshot the socket
/// list. That half is therefore gone, and only the long-poll remains.</para>
/// </summary>
internal static class CalmNotifier
{
    /// <summary>Kill-switch. While false the vanilla thread-pinning loop runs.</summary>
    public static volatile bool IsEnabled;

    private const int PollIntervalMs = 300;
    private const int TimeoutMs = 15000;

    private static NotifierHelper? _notifierHelper;
    private static NotificationService? _notificationService;

    public static void Apply(Harmony harmony, NotifierHelper notifierHelper, NotificationService notificationService, ISptLogger<CompoundingPerfMod> logger)
    {
        _notifierHelper = notifierHelper;
        _notificationService = notificationService;

        var target = AccessTools.Method(typeof(NotifierController), nameof(NotifierController.NotifyAsync));
        if (target is null)
        {
            logger.Warning("[CompoundingPerf/S13] NotifierController.NotifyAsync not found — SPT internals moved. Feature inactive.");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(CalmNotifier), nameof(NotifyPrefix))));
    }

    private static bool NotifyPrefix(MongoId sessionId, CancellationToken cancellationToken, ref Task<List<WsNotificationEvent>> __result)
    {
        if (!IsEnabled || _notifierHelper is null || _notificationService is null)
        {
            return true; // vanilla
        }

        TelemetryHub.Increment("s13.notifier.polls");
        __result = PollAsync(sessionId, cancellationToken);
        return false;
    }

    private static async Task<List<WsNotificationEvent>> PollAsync(MongoId sessionId, CancellationToken cancellationToken)
    {
        var notificationService = _notificationService!;
        var notifierHelper = _notifierHelper!;

        for (var waited = 0; waited < TimeoutMs; waited += PollIntervalMs)
        {
            if (notificationService.Has(sessionId))
            {
                var messages = notificationService.Get(sessionId);
                notificationService.UpdateMessageOnQueue(sessionId, []);
                return messages;
            }

            // The one real difference from vanilla: this releases the thread.
            await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        return [notifierHelper.GetDefaultNotification()];
    }

    public static void Configure(CalmNotifierOptions options, ISptLogger<CompoundingPerfMod> logger)
    {
        IsEnabled = options.Enabled;
        if (options.Enabled)
        {
            logger.Success("[CompoundingPerf/S13] calm notifier ACTIVE — the /notify long-poll releases its thread between checks");
        }
        else
        {
            logger.Info("[CompoundingPerf/S13] calm notifier disabled in config");
        }
    }
}
