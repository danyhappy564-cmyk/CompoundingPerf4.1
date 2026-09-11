### ⚠️ IMPORTANT NOTICE / DISCLAIMER

**Original Author:** EchoStarz
**Original Repository:** CompoundingPerf
**Original Link:** https://forge.sp-tarkov.com/mod/2415/compoundingperf
**License:** MIT
**This Port By:** R_F (danyhappy564-cmyk) — unofficial, AI-assisted port. Not affiliated with or endorsed by the original author.

1. **Reflection & Take-Downs:** I deeply reflect on the ECOT incident. As an AI-assisted "vibe coder," I will immediately delete files if the original authors ask.
2. **No Re-Distribution:** These ported builds are unverified, temporary fixes. Please do NOT re-upload or share them anywhere else.
3. **Do Not Pester Original Authors:** Never report bugs or pester original modders regarding issues from my unofficial ports.
4. **Full Credit & Respect:** I will always credit original creators on GitHub and prioritize their decisions above all else.
5. **Support Original Creators:** Instead of using my ports, please visit the original authors' Forge pages to leave kind words or tips.

---

# CompoundingPerf (SPT 4.1)

> **원작자** — **EchoStarz** (MIT, `LICENSE` 참고)
>
> 이 레포는 위 모드의 **SPT 4.1 포팅**입니다. 4.1에서 이 모드가 쓰던 확장 지점이 통째로
> 사라져서, 단순 리네임이 아니라 **기능별로 재검증하고 전달 방식을 갈아엎은** 작업입니다.
> 기능 11개 중 6개가 빠지고 1개가 새로 들어갔습니다.

현재 기준 **SPT 4.1.5**. **서버 전용** — 게임 프로세스에는 아무것도 안 올라갑니다.

---

## 뭐 하는 모드냐

서버 쪽 작은 최적화들을 하나의 config로 묶어 켜고 끄는 모드입니다. 기능마다 개별 스위치가
있고, 원작이 세운 규칙이 하나 있습니다: **비용이 구조적으로 한정되지 않는 기능은 넣지
않는다.** 모드 잔뜩 깔린 환경에서 터질 여지가 있으면 아예 안 넣습니다.

| 기능 | 기본값 | 하는 일 |
|---|---|---|
| **S15 RaidStartGc** | **켜짐** | **2.0에서 신규.** 레이드 시작 응답 직전에 `GC.Collect(MaxGeneration, Aggressive, blocking, compacting)` — .NET에서 제일 비싼 GC — 가 **요청 경로 안에서** 돕니다. 로딩 화면에서 서버가 힙 전체를 압축하는 걸 매 레이드마다 기다리는 셈. 기본값 `Background` 는 gen-2 수집은 그대로 요청하되 **논블로킹·논컴팩팅** 으로 바꿔서 응답을 안 붙잡습니다 |
| **S8 RagfairCalmUpdates** | 켜짐 | 플리 매물 만료 처리 끝에 강제 blocking·compacting 풀 GC가 붙어 있습니다. 그 **강제 수집 한 번만** 제거하고 만료 시퀀스 자체는 안 건드립니다 |
| **S9 FastCompression** | 켜짐 | 모든 응답을 zlib `SmallestSize`(제일 느림)로 압축합니다. `Fastest` 는 CPU가 몇 배 싸고 대신 몇 % 커지는데, 어차피 localhost/LAN만 건너갑니다. 버퍼 경로와 스트리밍 경로 둘 다 적용 |
| **S12 IsolatedBotRandomisation** | 켜짐 | **성능이 아니라 버그 수정.** 야간 레이드 장비 수정치가 **공유** 봇 config에 그대로 써집니다 → 봇 생성할 때마다 누적되고, 재시작 전까지 낮 레이드에도 남고, 병렬 생성에서 레이스가 납니다. 봇마다 사본을 주면 의도대로 정확히 한 번만 적용됩니다 |
| **S13 CalmNotifier** | 켜짐 | `/notify` 롱폴이 15초 예산 동안 스레드풀 스레드를 **접속자 수만큼** 붙잡고 있습니다. 체크 사이에 스레드를 놓아주도록 바꿉니다. FIKA 호스트에서 제일 값어치 있음 |
| **S11 SaveDirtyTracking** | **꺼짐** | 세션이 확실히 깨끗할 때 주기 저장을 통째로 건너뜁니다 (바닐라는 idle이어도 매 틱 프로필 전체를 직렬화 + MD5). 아래 "왜 기본으로 꺼져 있나" 참고 |

### 왜 S11만 기본으로 꺼져 있나

이 모드에서 **유일하게 바닐라 호출을 막는** 기능이기 때문입니다. 나머지는 전부 상수를
바꾸거나, 호출 하나를 동등한 것으로 돌리거나, 사본을 얹는 식이라 **잘못돼도 최악이
"최적화가 아무 일도 안 함"** 입니다. S11은 최악이 **"프로필 변경이 안 써짐"** 입니다.

게다가 이득이 제일 작습니다. 메뉴에서 가만히 있을 때만 효과가 있고, 레이드 중엔 요청이
오가니 어차피 dirty로 표시돼서 안 걸립니다. **위험은 제일 크고 이득은 제일 작아서**
opt-in으로 뺐습니다. 쓰려면 `config.json` 에서 `SaveDirtyTracking.Enabled: true`.

---

## 4.1이 가져간 것 — 기능 5개 폐기

4.0에서 돌던 11개 중 6개가 없어졌고, 그중 5개는 **SPT 4.1이 같은 일을 직접 하기 때문**
입니다. 추측이 아니라 4.1.5 서버 어셈블리를 직접 읽고 확인했습니다.

| 폐기 | 4.1이 하는 일 |
|---|---|
| **S1 ProfileSaveDebouncer** | `SaveProfileAsync` 에 프로필별 `SemaphoreSlim` 이 생겨서 같은 프로필 저장이 겹치지 않습니다. 코얼레서가 합칠 게 없어졌습니다 |
| **S2 ResponseCache** | items / globals / handbook / customization / hideout 같은 무거운 엔드포인트가 `StreamedJsonBody` 를 반환해 응답 스트림으로 직행합니다. **캐싱할 문자열이 애초에 안 만들어집니다** — 우리가 하던 것보다 나은 해법 |
| **S6 ThreadSafeRandom** | `RandomUtil` 에 공유 `System.Random` 자체가 없습니다. `RandomNumberGenerator` 를 쓰는데 이건 원래 스레드 세이프 |
| **S7 ResponseSanitizer** | `ClearString` 이 이미 `SearchValues` 단일 스캔 + 풀 버퍼입니다. 우리가 넣으려던 그 최적화가 이미 들어가 있음 |
| **S10 ThreadSafeCaches** | `ItemBaseClassService` 에 `Lock` 이 생겼고, `HandbookHelper` 의 지연 초기화는 무해한 참조 대입이고, **SPT 내부에 `ItemFilterService` 블랙리스트 변경 호출자가 하나도 없습니다.** 남은 레이스는 "모드가 레이드 중에 다른 스레드에서 블랙리스트를 쓰는" 경우뿐인데, 그걸 막으려면 루팅이 계속 때리는 `IsItemBlacklisted` 를 패치해야 합니다. 비용이 이득에 비해 안 맞아서 뺐습니다 |

여섯 번째는 **S13의 웹소켓 절반**입니다. 4.1은 메시지를 `byte[]` 로 **한 번만** 직렬화해서
`SendRawToSocketsAsync` 에 넘기고, 거기서 `_sendGates` 의 **소켓별 `SemaphoreSlim`** 을 잡으며
전역 락은 목록 스냅샷 뜨는 동안만 잡습니다. 정확히 우리가 넣으려던 것입니다. 롱폴 절반만
살아남았습니다.

그 이전에 정직한 테스트를 통과 못 해서 빠진 것들: 배경 루트 사전생성, 셰이더 프리웜,
레이드 후 GC — 그리고 1.3에서 로그 필터링(S3/C4)과 라우트 디스패치 메모이제이션(S14).
S14는 FIKA에서 런처 응답이 바뀌는데 원인을 끝까지 설명하지 못해서 뺐습니다. **동작이
같다는 걸 증명 못 하는 성능 기능은 출시 안 합니다.**

### 넣을까 하다가 안 넣은 것

`RagfairOfferGenerator.GenerateDynamicOffers` 가 assort 하나당 `Task.Factory.StartNew` 를
**수천 개** 만들고 `Task.WaitAll` 로 블록합니다. 파티셔닝된 `Parallel.ForEach` 면 할당도
로드밸런싱도 훨씬 낫습니다. 안 넣은 이유: **메서드 본문을 통째로 Harmony prefix로
갈아끼워야 하는데**, 그건 나머지 기능들이 일부러 피하는 형태고, 여기서 이득을 측정할 방법이
없었습니다. 위 규칙을 제 코드에도 똑같이 적용했습니다.

---

## 어떻게 동작하나 — 4.1에서 통째로 바뀐 부분

4.0까지는 모든 기능이 **DI 서브클래싱**이었습니다. `Injectable.TypeOverride` 로
`CoalescingSaveServer : SaveServer`, `CachingHttpRouter : HttpRouter` 같은 걸 등록해서 컨테이너
해석 시점에 내장 클래스를 대체했습니다. 평범한 C# 가상 디스패치라 IL 수술이 없고, **다른
모드가 그 클래스에 건 Harmony 패치도 그대로 살아 있었습니다** — 우리 서브클래스가 곧 그들이
패치한 그 객체였으니까요.

**SPT 4.1은 그 선택지를 없앴습니다.**

- `Injectable` 어트리뷰트에서 `TypeOverride` 속성이 **삭제**됨
- `SaveServer` / `RagfairServer` / `RandomUtil` / `SptWebSocketConnectionHandler` 가 **sealed**
- 이 모드가 오버라이드하던 **메서드 11개 중 virtual인 것이 0개**
- 그중 3개는 **이름조차 없어짐** — HTTP 응답, 웹소켓 송신, 라우터 디스패치 경로가 재작성됨

그래서 살아남은 기능들은 전부 Harmony 패치입니다. **이건 이 모드가 자랑하던 호환성 특성을
잃는다는 뜻이고, 숨길 일이 아니니 여기 적어둡니다.** 대신 가능한 한 좁게 만들었고, 둘은
원작보다 **더** 좁습니다.

- **S8 / S15** — 예전엔 메서드를 재구현했습니다. 지금은 `GC.Collect` **호출 명령 하나**를
  동일 시그니처 메서드 호출로 바꿉니다. 나머지 명령은 컴파일러가 뽑은 그대로 남습니다.
  동작 동일성이 "구현을 잘해서"가 아니라 **구조적으로** 보장되고, 판단이 패치가 아니라
  메서드 안으로 들어가서 **설정이 재시작 없이 런타임에 먹힙니다**
- **S9** — 예전엔 응답 전송 메서드를 통째로 대체했습니다. 지금은 `ZLibStream` 생성자에
  들어가는 `CompressionLevel` **상수 하나만** 바꿉니다
- **S11** — `SaveProfileAsync` 에 스킵 prefix, 라우터에 **요청 경로만 읽는** prefix.
  라우터 쪽을 못 찾으면 **저장 스킵도 설치하지 않습니다** — 표시하는 절반 없이 스킵만
  살면 세션이 영원히 깨끗해 보여서 진짜 저장을 날립니다
- **S12** — 사본을 반환하는 postfix
- **S13** — `Thread.Sleep` 을 `await Task.Delay` 로 바꾸는 prefix. 300ms 간격, 15초 예산,
  기본 알림 폴백까지 전부 동일

모든 패치는 대상을 못 찾으면 **조용히 아무 일도 안 하는 대신 로드 시점에 경고**를 찍습니다.

---

## 호환성

- 모드 없는 **SPT 4.1.5** 기준
- **FIKA는 4.1에서 미검증입니다.** FIKA를 특별히 겨냥한 코드는 없고 4.0 계열은 FIKA 2.3.x
  에서 테스트됐지만, 이 버전은 안 해봤습니다
- 다른 모드와 공존하도록 설계돼 있지만 **4.1에서는 그 보장이 4.0 때보다 약합니다.**
  5개 중 4개가 Harmony 패치이고, 그중 바닐라 호출을 막을 수 있는 건 S11의 저장 스킵
  하나뿐입니다. 같은 메서드를 패치하는 모드가 있으면 DI 서브클래스 시절과 달리 **순서가
  영향을 줍니다**

## 설치

- `CompoundingPerf.dll` + `config.json` → `SPT/user/mods/CompoundingPerf/`

BepInEx 플러그인은 없습니다. 기능마다 `Enabled` 플래그가 있어서 하나씩 끄고 켤 수 있고,
`MasterEnabled: false` 면 패치를 아예 설치하지 않습니다 (A/B 비교용, 재시작 필요).

## 빌드

평소엔 이거면 됩니다 — 실제로 배포되는 것만 빌드합니다:

```
dotnet build CompoundingPerf.csproj -c Release
```

`$(SptRoot)\SPT\user\mods\CompoundingPerf\` 로 dll + config.json 을 바로 복사합니다.
기본 `SptRoot` 는 `E:\SPT 4.1`, `-p:SptRoot=...` 로 덮어쓰기, `-p:SkipDeploy=true` 로 복사 생략
(서버가 켜져 있어서 파일이 잠겨 있을 때).

솔루션은 3개 프로젝트를 묶어둔 편의용입니다:

```
dotnet build CompoundingPerf.sln -c Release
dotnet test  CompoundingPerf.sln -c Release
```

> ⚠️ 솔루션 빌드는 **클라이언트 프로젝트까지 빌드**하므로 `SptRoot` 에 EFT 어셈블리
> (`Assembly-CSharp.dll`, `BepInEx.dll`)가 실제로 있어야 합니다. 없으면 클라 프로젝트에서
> 에러가 납니다 — 서버 dll 자체는 그래도 정상적으로 나오지만, 그럴 바엔 csproj로 빌드하는
> 게 깔끔합니다.

### `client/` 폴더는 기능이 아닙니다

벤치마크 도구입니다. 안의 실제 코드가 전부 `#if BENCH` 라서 `-p:Bench=true` 없이 빌드하면
**패치가 하나도 없는 껍데기**가 나옵니다 (빌드된 dll에 `WorldTickPatch`,
`FrameStatsRecorder` 가 아예 없고 Harmony 참조도 안 들어갑니다). 그래서 배포 단계도
`Bench=true` 일 때만 돕니다 — 평범한 솔루션 빌드가 아무것도 안 하는 dll을
`BepInEx\plugins\` 에 떨구지 않도록.

서버 프로젝트는 `net10.0` + `SPTushonka.Server.Core` 4.1.5 (4.1에서 패키지 ID가
`SPTarkov.*` → `SPTushonka.*` 로 바뀌었고, 안의 네임스페이스는 그대로입니다).
클라이언트는 `netstandard2.1`.

`Lib.Harmony` 는 **2.4.2** 로 고정돼 있습니다. 2.3.3이 아닙니다 — 4.1 서버가 .NET 10에서
도는데 거기서 `System.Reflection.Emit.LocalBuilder` 가 abstract가 됐고, 2.3.3은 패치를 만들다
지역 변수를 선언하는 순간 `MemberAccessException` 을 던집니다.

---

## 확인한 것 / 확인 못 한 것

Harmony 패치는 대상이 옮겨져도 **컴파일이 깨지지 않습니다.** 로드 시점에 실패하거나, 더
나쁘게는 조용히 아무 일도 안 합니다. 그래서 실제 4.1.5 어셈블리와 이 모드를 한 프로세스에
올려서 6개 기능을 각자의 `Apply` 로 **실제로 설치**하고 Harmony가 바인딩했는지 확인했습니다:

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
ok    ... 타겟 8개 전부 우리 패치를 달고 있음
ALL PATCHES BIND
```

| | 상태 |
|---|---|
| 패치 대상 8개가 4.1.5에 존재 | **확인** |
| 시그니처 · 주입 파라미터 이름 · `__result` 타입 | **확인** — Harmony가 패치 시점에 셋 다 검증합니다 |
| 트랜스파일러가 실제로 재작성했는지 | **확인** — 예상 개수와 정확히 일치 (1 / 2 / 1). IL을 그냥 흘려보낸 게 아님 |
| 폐기한 5개가 정말 4.1에 들어갔는지 | **확인** — 위 표의 근거는 전부 4.1.5 어셈블리에서 읽은 것 |
| 유닛 테스트 | **41개 통과** |
| 실서버 구동 | **안 함** |
| 인게임 성능 측정 | **안 함** — 위 성능 서술은 4.1.5 소스에서 읽은 것이고, CHANGELOG의 수치는 **4.0 기준**입니다 |

## 안 되면 여기부터 보세요

- **기능이 안 먹는다**: 서버 로그에 `[CompoundingPerf/Sxx] ... not found` 경고가 있는지
  보세요. 4.1.5보다 새 빌드면 대상 메서드가 또 움직였을 수 있습니다
- **`MemberAccessException` 이 뜬다**: 서버가 들고 있는 `0Harmony.dll` 이 구버전입니다
  (위 .NET 10 이슈)
- **프로필이 저장이 안 되는 것 같다**: `SaveDirtyTracking` 을 켜뒀는지 확인하세요.
  기본값은 꺼짐이고, 이게 유일하게 저장을 건너뛸 수 있는 기능입니다
- **레이드 로딩은 빨라졌는데 메모리가 계속 는다**: `RaidStartGc.Mode` 를 `Background`
  (기본) 대신 `Vanilla` 로 되돌려 보세요

## License

MIT — 원작자 EchoStarz, `LICENSE` 참고.
