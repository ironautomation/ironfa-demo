# DemoEtcher

가상 매엽식 식각 장비입니다. (C# WinForms · .NET Framework 4.8 · IronGEM.Connector)

- EP03 · EP04는 실행만 하고, **EP05에서 코드를 엽니다**(`docs/ep05.md`).
- EP06 · EP07에서 로드포트(E87)와 Process Job · Control Job(E40 · E94) 처리가 추가됩니다.

## 실행 (EP03)

Releases `ep03`의 `DemoEtcher-ep03.zip`을 `C:\Demo\ironfa-demo\DemoEtcher`에 풀고 `DemoEtcher.exe`를 실행합니다.
IronGEM(127.0.0.1:5001)이 먼저 떠 있어야 합니다. IronStudio에 EP03 설정(SV 1001~1003 · ECV 2001, `docs/ep03.md`)이 있어야 값이 호스트로 나갑니다.

| 영역 | 내용 |
|---|---|
| IronGEM | IronGEM 연결 · 호스트 통신 상태 · 제어 상태 |
| 챔버 | 온도(SV 1001) · 설정 온도(ECV 2001) · 압력(SV 1002) · 공정 상태(SV 1003) · 최근 2분 그래프 |
| 장비 로그 | 호스트의 ECV 변경 요청과 응답(EAC) |

- 0.5초마다 SV 1001~1003을 IronGEM에 올립니다(`SetVariableAsync`).
- 호스트가 S2F15로 ECV 2001을 바꾸면 IronGEM이 범위(20 ~ 200)를 먼저 검사하고, 통과하면 DemoEtcher에 묻습니다(`GEMReqChangeECV`). DemoEtcher가 0을 돌려주면 설정 온도가 바뀝니다.
- Connector에는 ECV 현재 값을 읽는 API가 없어 시작할 때 설정 온도는 60(ECV 기본값)입니다.

## EP04 — 공정 · 이벤트 · 알람

Releases `ep04`의 `DemoEtcher-ep04.zip`으로 바꿉니다. IronStudio에 EP04 설정(DVVAL 1101 · CEID 3001 · 3002 · ALID 5001, `docs/ep04.md`)이 있어야 합니다.

| 운전 | 동작 |
|---|---|
| 공정 실행 (10초) | ProcessState 1 → **CEID 3001 ProcessStarted** → 10초 → ProcessState 0 · DVVAL 1101 ProcessTime → **CEID 3002 ProcessCompleted** |
| 과온 시뮬레이션 | 히터 이상: 온도가 설정 온도 + 40℃까지 오름. 설정 온도 + 15℃를 넘으면 **알람 5001 발생**(SetAlarm), + 5℃ 아래로 내려오면 **해제**(ClearAlarm) |

- 이벤트에 실릴 값(SV · DVVAL)을 먼저 올리고(`SetVariableAsync`) 이벤트를 알립니다(`SetEventAsync`). IronGEM은 그 순간 자기가 가진 값으로 S6F11을 만듭니다.
- 장비는 이벤트 · 알람을 **늘** 알립니다. 호스트에 보낼지는 IronGEM이 정합니다 — 호스트가 S2F37로 끈 이벤트, S5F3으로 끈 알람은 보내지 않습니다(이때도 반환값은 0).

## EP05 — 원격 명령 S2F41 · 공정 수 SV 1004

IronStudio에 SV 1004 ProcessCount(UINT4)를 추가합니다. 호스트가 `S2F41 START`를 보내면:

| 상황 | S2F42 HCACK |
|---|---|
| REMOTE · 공정 중 아님 · 파라미터 없음 | **4** 받았음 → 공정 시작, 끝나면 CEID 3002로 알림 |
| 공정 중 | 2 지금은 할 수 없음 |
| REMOTE가 아님(LOCAL) | 2 |
| START가 아닌 명령 | 1 없는 명령 |
| START에 파라미터 | 3 파라미터 오류 |
| DemoEtcher가 꺼져 있음 | 2 — IronGEM이 대신 답함 |

- IronGEM은 S2F41을 형식만 검사하고 넘깁니다(`GEMUserMsgReceived(2, 41)`). 장비는 `GetNextItem`으로 읽고, `MakeMsg(2, 42)` · `AddItem` · `SendUserMsg(2, 42)`로 답합니다.
- **응답을 먼저 보내고 동작을 시작합니다** — 그래야 호스트가 S2F42(HCACK 4)를 S6F11(3001)보다 먼저 받습니다.

## 직접 빌드

Visual Studio 2022 또는 .NET SDK. NuGet 패키지 `IronAutomation.IronGEM.Connector`를 씁니다.
