# CompoundingPerf

A performance mod for [SPT](https://www.sp-tarkov.com/) **4.1** that stacks several small, independent server-side optimizations under one config. Each feature is individually toggleable, designed to coexist with other mods, and held to one rule: **a feature ships only if its cost is bounded by construction** — nothing that could blow up on a heavily-modded install.

The mod is **server-only**: it ships no BepInEx plugin and never touches your game client.

## What it does

| Feature | Default | What you get |
|---|---|---|
| **RagfairCalmUpdates** (S8) | on | Vanilla forces a blocking, compacting full GC every time enough flea offers expire — a recurring stall. Only that forced collect is removed; the expiry sequence itself is untouched. |
| **FastCompression** (S9) | on | Vanilla zlib-compresses every response at `SmallestSize` (slowest). `Fastest` is several times cheaper in CPU for a few percent larger payloads that only cross localhost/LAN. Covers both the buffered and the streamed response paths. |
| **RaidStartGc** (S15) | on | **New in 2.0.** `StartLocalRaidAsync` ends with `GC.Collect(MaxGeneration, Aggressive, blocking, compacting)` — the most expensive collection .NET offers — *inside the request path*, so you sit on the loading screen while the server compacts its whole heap, every raid. Default `Background` still asks for a gen-2 collection, just a non-blocking, non-compacting one the runtime finishes while the raid loads. |
| **SaveDirtyTracking** (S11) | **off** | Skips the periodic profile save entirely when the session is provably clean — vanilla serializes and MD5-hashes the full profile every tick even when idle. Any non-pure request marks the session dirty, so player-driven changes can never be skipped. Off by default: see below. |
| **IsolatedBotRandomisation** (S12) | on | Fixes a vanilla bug: night-raid equipment modifiers are written into shared bot config — they compound per generated bot, persist across raids until restart, and race across parallel bot generation. Every bot now gets a private copy. |
| **CalmNotifier** (S13) | on | The `/notify` long-poll releases its thread between checks instead of pinning one thread-pool thread per connected client for its full 15-second budget. Most valuable for FIKA hosts. |

### Why S11 ships off

It is the only feature here that **suppresses a vanilla call** rather than changing a value it passes. Every other one either swaps a constant, redirects a single call to an equivalent, or adds a clone — if any of them misbehaves the worst case is "the optimization did nothing". S11's worst case is "a profile change was not written".

Its payoff is also the smallest of the set: it only helps a session sitting idle in the menu, because anything happening in a raid marks the session dirty anyway. Worst risk, least reward — so it is opt-in. Set `SaveDirtyTracking.Enabled: true` if you want it.

### Considered and not shipped

`RagfairOfferGenerator.GenerateDynamicOffers` spawns one `Task.Factory.StartNew` per assort item — thousands of them — and then blocks on `Task.WaitAll`. A partitioned `Parallel.ForEach` would allocate far fewer tasks and balance better. It is not here because delivering it means replacing a whole method body through a Harmony prefix, which is exactly the shape of change the other four avoid, and because the win could not be measured from here. A perf feature that can't prove it's behaviour-neutral doesn't ship — including when it's ours.

## What 4.1 took over — five features retired

Six of the eleven features that shipped on 4.0 are **gone in 2.0**, five of them because SPT 4.1 now does the job itself. That was checked against the 4.1.5 server assembly, not assumed:

| Retired | Why |
|---|---|
| **ProfileSaveDebouncer** (S1) | `SaveServer.SaveProfileAsync` now takes a per-profile `SemaphoreSlim`, so saves for one profile no longer overlap. The coalescer had nothing left to merge. |
| **ResponseCache** (S2) | The heavy endpoints — items, globals, handbook, customization, hideout areas and recipes — now return `StreamedJsonBody` and serialize straight to the response stream. There is no longer a big string to cache; SPT's fix is better than ours was. |
| **ThreadSafeRandom** (S6) | `RandomUtil` no longer holds a shared `System.Random` at all. It uses `RandomNumberGenerator`, which is thread-safe by construction. |
| **ResponseSanitizer** (S7) | `ClearString` is already a single `SearchValues` scan over a pooled buffer — the same optimization we were adding, done upstream. |
| **ThreadSafeCaches** (S10) | `ItemBaseClassService` now guards its cache with a `Lock`, `HandbookHelper`'s lazy init is a benign reference assignment, and **nothing inside SPT calls `ItemFilterService`'s blacklist mutators at all** — so the remaining race needs a mod to write the blacklist mid-raid from another thread. Guarding it would mean patching `IsItemBlacklisted`, which loot generation calls constantly. Cost not bounded by benefit; dropped. |

The sixth, the websocket half of **CalmNotifier** (S13), is also gone: 4.1 serializes each message to a `byte[]` once and hands it to `SendRawToSocketsAsync`, which takes a per-socket `SemaphoreSlim` out of `_sendGates` and only holds the global socket lock long enough to snapshot the list. Exactly what we were adding. The long-poll half survives.

Earlier features that didn't survive honest testing were removed rather than shipped disabled: background loot pre-generation, shader pre-warming, post-raid GC — and, in 1.3, the log-filtering pair (S3/C4) plus a route-dispatch memoization (S14) that changed a launcher response under FIKA in a way we couldn't fully explain. A perf feature that can't prove it's behavior-neutral doesn't ship.

## How it works

**This changed completely in 2.0, and not by choice.** Through 4.0 every feature was a DI subclass registered with `Injectable.TypeOverride` — `CoalescingSaveServer : SaveServer`, `CachingHttpRouter : HttpRouter` — replacing the built-in at container resolution. Normal C# virtual dispatch, no IL surgery, and other mods' Harmony patches on those classes kept working because our subclass *was* the object they patched.

SPT 4.1 removed that option outright:

- the `TypeOverride` property no longer exists on the `Injectable` attribute
- `SaveServer`, `RagfairServer`, `RandomUtil` and `SptWebSocketConnectionHandler` are **sealed**
- **not one** of the eleven methods this mod used to override is virtual any more
- three of them don't exist under any name — the HTTP response, websocket send and router dispatch paths were rewritten

So the five surviving features are Harmony patches. They are kept as small as the job allows, and two of them are deliberately *smaller* than what they replaced:

- **S8** used to reimplement `RagfairServer.Update()` minus the forced collect. It now transpiles the single `GC.Collect` call site into a call with the identical argument list that decides whether to forward. Nothing else in the method is touched, so it is behaviour-neutral by construction rather than by careful re-implementation — and the config flag still works at runtime instead of needing a restart. **S15** is the same technique on the raid-start collect.
- **S9** used to replace the whole response-send method. It now rewrites only the `CompressionLevel` constant pushed into each `ZLibStream` constructor.
- **S11** is a skipping prefix on `SaveProfileAsync` plus a read-only prefix on the router that does nothing but observe the request path. If the router method can't be found the save skip is **not** installed either — a skip without the marking half would look clean forever and drop real saves.
- **S12** is a postfix returning a clone.
- **S13** is a prefix that swaps `Thread.Sleep` for `await Task.Delay`, keeping the same 300 ms interval, 15 s budget and fallback.

Every patch reports at load if its target could not be found, instead of silently doing nothing.

## Compatibility

- Works on unmodded SPT 4.1.5.
- FIKA compatibility on 4.1 is **untested** — nothing here targets FIKA specifically, but the 4.0 line was tested against FIKA 2.3.x and this one has not been.
- Designed to coexist with other mods. On 4.1 that guarantee is weaker than it was: four of the five features are Harmony patches, and only one of them (S11's save skip) can suppress a vanilla call. If another mod patches the same methods, ordering matters in a way it didn't when these were DI subclasses.

## Install

Drop the release archive onto your SPT folder, or manually:

- `CompoundingPerf.dll` + `config.json` → `SPT/user/mods/CompoundingPerf/`

Every feature has an `Enabled` flag — flip any of them without touching the rest. `MasterEnabled: false` disables every optimization at once for A/B comparisons.

## Building from source

Server targets `net10.0` against the `SPTushonka.Server.Core` 4.1.5 NuGet packages (4.1 renamed the packages `SPTarkov.*` → `SPTushonka.*`; the namespaces inside them are unchanged). The client project exists only for dev benchmark builds (`-p:Bench=true` compiles an in-raid frame-stats recorder; release builds contain nothing and are not shipped) and targets `netstandard2.1`. Set `-p:SptRoot=<path>` if SPT isn't at `E:\SPT 4.1`; pass `-p:SkipDeploy=true` to build without deploying to a live install.

`Lib.Harmony` is pinned to **2.4.2**, not 2.3.3. The 4.1 server runs on .NET 10, where `System.Reflection.Emit.LocalBuilder` became abstract, and 2.3.3 throws `MemberAccessException` the moment it declares a local while building a patch.

```
dotnet build CompoundingPerf.csproj -c Release
dotnet test tests/CompoundingPerf.Tests.csproj -c Release
```

The unit-test suite covers the dirty-tracking save-skip rules, the telemetry hub, the bench-sample and frame-stats maths, and the config schema — including that a 1.x `config.json` with the five retired feature blocks still loads rather than throwing at boot.

### Verifying the patches against a real 4.1 server assembly

A Harmony patch whose target moved doesn't fail to compile — it fails at load, or worse, silently does nothing. Every patch in 2.0 was checked by loading `SPTarkov.Server.Core` 4.1.5 and this mod into one process, installing all six through their own `Apply` methods, and asserting that Harmony bound them:

```
ok    S8  RagfairServer.ProcessExpiredFleaOffers resolved
ok    S9  AsyncMoveNext(SendZlibJsonAsync) resolved
ok    S9  AsyncMoveNext(SendStreamedJsonAsync) resolved
ok    S11 SaveServer.SaveProfileAsync resolved
ok    S11 HttpRouter.GetResponseObjectAsync resolved
ok    S12 BotHelper.GetBotRandomizationDetails resolved
ok    S13 NotifierController.NotifyAsync resolved
ok    S15 AsyncMoveNext(StartLocalRaidAsync) resolved
ok    S8  transpiler rewrote the GC.Collect call (count=1)
ok    S9  transpiler rewrote both ZLibStream levels (count=2)
ok    S15 transpiler rewrote the raid-start GC.Collect (count=1)
ok    ... all eight targets carry our patch
ALL PATCHES BIND
```

That covers signatures, injected parameter names and `__result` types — Harmony validates all three at patch time — and, for the two transpilers, that the rewrite actually matched the expected number of call sites rather than passing the IL through untouched.

**What is not verified**: none of this has been run on a live 4.1 server or measured in game. The behavioural claims above are read off the 4.1.5 source; the performance numbers in the changelog are from 4.0.

## License

MIT — see [LICENSE](LICENSE).
