using System.Text.Json;
using Xunit;

namespace CompoundingPerf.Tests;

public class ConfigTests
{
    [Fact]
    public void Defaults_match_expected_safe_values()
    {
        var c = new CompoundingPerfConfig();

        Assert.True(c.Server.RagfairCalmUpdates.Enabled);
        Assert.True(c.Server.FastCompression.Enabled);
        Assert.Equal("Fastest", c.Server.FastCompression.Level);
        Assert.False(c.Server.SaveDirtyTracking.Enabled); // opt-in since 2.0 — worst risk, least reward
        Assert.True(c.Server.IsolatedBotRandomisation.Enabled);
        Assert.True(c.Server.CalmNotifier.Enabled);
        Assert.True(c.Server.RaidStartGc.Enabled);
        Assert.Equal("Background", c.Server.RaidStartGc.Mode);

        // Must stay above SPT's 60s save tick or the dirty-skip never actually fires.
        Assert.True(c.Server.SaveDirtyTracking.ForceSaveIntervalSeconds > 60);

        // Telemetry off by default — opt-in is intentional.
        Assert.False(c.Telemetry.Enabled);
        Assert.False(c.Telemetry.TimingEnabled);
    }

    [Fact]
    public void Json_roundtrip_preserves_values()
    {
        var original = new CompoundingPerfConfig
        {
            Server = new ServerToggles
            {
                FastCompression = new FastCompressionOptions { Enabled = false, Level = "Optimal" },
                SaveDirtyTracking = new SaveDirtyTrackingOptions { Enabled = false, ForceSaveIntervalSeconds = 900 },
            },
            Client = new ClientToggles
            {
                FrameStats = new FrameStatsOptions { Enabled = false, WarmupSkipSeconds = 35 },
            },
        };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<CompoundingPerfConfig>(json)!;

        Assert.False(roundTripped.Server.FastCompression.Enabled);
        Assert.Equal("Optimal", roundTripped.Server.FastCompression.Level);
        Assert.False(roundTripped.Server.SaveDirtyTracking.Enabled);
        Assert.Equal(900, roundTripped.Server.SaveDirtyTracking.ForceSaveIntervalSeconds);
        Assert.False(roundTripped.Client.FrameStats.Enabled);
        Assert.Equal(35, roundTripped.Client.FrameStats.WarmupSkipSeconds);
    }

    [Fact]
    public void Retired_feature_keys_are_ignored_rather_than_fatal()
    {
        // Someone upgrading from 1.x keeps their old config.json. SPT's loader is strict
        // about syntax but not about unknown members, and neither is this — the five
        // retired feature blocks must simply be skipped, not throw at boot.
        const string legacy = """
            {
              "MasterEnabled": true,
              "Server": {
                "ProfileSaveDebouncer": { "Enabled": true },
                "ResponseCache": { "Enabled": true, "AdditionalPaths": ["/custom/path"] },
                "ThreadSafeRandom": { "Enabled": true },
                "ResponseSanitizer": { "Enabled": true },
                "ThreadSafeCaches": { "Enabled": true },
                "FastCompression": { "Enabled": true, "Level": "Fastest" }
              }
            }
            """;

        var parsed = JsonSerializer.Deserialize<CompoundingPerfConfig>(legacy);

        Assert.NotNull(parsed);
        Assert.True(parsed!.Server.FastCompression.Enabled);
    }

    [Fact]
    public void ShippedConfig_json_parses_cleanly()
    {
        // The shipping config.json next to the csproj is the source of truth users see —
        // make sure it still deserializes after any schema changes.
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config.json");
        path = Path.GetFullPath(path);
        Assert.True(File.Exists(path), $"config.json not found at {path}");

        var raw = File.ReadAllText(path);
        // STRICT options on purpose: SPT's ModHelper.GetJsonDataFromFile does not allow
        // trailing commas — a lenient test here once passed a config the live loader
        // rejected, killing every feature at boot (2026-06-13).
        var parsed = JsonSerializer.Deserialize<CompoundingPerfConfig>(raw);
        Assert.NotNull(parsed);
        Assert.True(parsed!.Server.RagfairCalmUpdates.Enabled);
        Assert.False(parsed.Server.SaveDirtyTracking.Enabled);
        Assert.True(parsed.Server.RaidStartGc.Enabled);
        Assert.True(parsed.Server.CalmNotifier.Enabled);
        Assert.True(parsed.Client.FrameStats.Enabled);
    }
}
