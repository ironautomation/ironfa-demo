# DemoEtcher

가상 매엽식 식각 장비입니다. (C# WinForms · .NET Framework 4.8 · IronGEM.Connector)

- EP03부터 실행만 하고, **EP05에서 코드를 엽니다.**
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

## 직접 빌드

Visual Studio 2022 또는 .NET SDK. NuGet 패키지 `IronAutomation.IronGEM.Connector`를 씁니다.
