# DemoHost

실습용 호스트(MES 역할)입니다. C# WinForms · .NET 8 · [secs4net](https://github.com/mkjeff/secs4net)(MIT).
IronGEM에 HSMS로 접속해 SECS 메시지를 주고받고, 오간 메시지를 로그로 보여 줍니다.

## 실행

**바로 실행(권장)** — 레포 **Releases**의 `DemoHost-ep04-win-x64.zip`을 받아 `C:\Demo\ironfa-demo`에 풀고 `DemoHost.exe`를 실행합니다. (.NET 설치 불필요)

**직접 빌드** — .NET 8 SDK 또는 Visual Studio 2022(17.8 이상, .NET 데스크톱 개발)

```
cd C:\Demo\ironfa-demo
dotnet run --project src\DemoHost
```

## 화면

| 영역 | 내용 |
|---|---|
| 연결 (HSMS) | IP · Port · Device ID · Mode(Active/Passive) · [연결] [끊기] · HSMS 상태(Connecting → Selected) · GEM 통신 상태 |
| 탭 EP02 · 통신 확립 · 온라인 | S1F13 통신 확립 · S1F1 Are You There · S1F15 오프라인 요청 · S1F17 온라인 요청 |
| 탭 EP03 · SV · ECV | SVID 칸 + S1F11 SV 목록 · S1F3 SV 값 / ECID 칸 + S2F29 ECV 목록 · S2F13 ECV 값 / 값 · 형식 + S2F15 ECV 변경. ID 칸을 비우면 전부(빈 리스트) · ID 형식은 장비 응답(S1F12 · S2F30)에서 자동으로 맞춤 |
| 탭 EP04 · 이벤트 · 알람 | [리포트 전부 지우기](S2F37 전부 끄기 + S2F33 빈 목록) · S2F33 리포트 정의 · S2F35 이벤트 링크 / CEID 칸 + S2F37 이벤트 켜기 · 끄기 / ALID 칸 + S5F3 알람 켜기 · 끄기. 받은 S6F11 · S5F1은 로그에서 이름을 붙여 풀어 줌 |
| 메시지 로그 | 시각 · 방향(H→E 호스트가 보냄 / E→H 장비가 보냄) · SxFy · W(응답 필요) · 이름 · SML 본문 · 응답 코드 설명(→) |

기본값은 IronGEM 기본 설정과 짝입니다: **127.0.0.1 : 5000 · Device ID 0 · Active**(IronGEM이 Passive).

## 자동 응답

장비가 먼저 보내는 메시지에 호스트가 답합니다(체크 해제 시 응답하지 않음).

| 장비 → 호스트 | 호스트 응답 |
|---|---|
| S1F13 통신 확립 요청 | S1F14 COMMACK 0 |
| S1F1 Are You There | S1F2 (빈 리스트) |
| S2F17 시각 요청 | S2F18 현재 시각(yyyyMMddHHmmssff) |
| S5F1 알람 | S5F2 ACKC5 0 |
| S6F11 이벤트 | S6F12 ACKC6 0 |
| S10F1 터미널 메시지 | S10F2 ACKC10 0 |
| 그 밖의 W 메시지 | SxF0 (Abort) |

회차마다 시나리오 탭이 늘어납니다(EP04부터 탭). '장비 메시지 자동 응답'은 아래 로그 도구 줄로 옮겼습니다.

## EP04 리포트 정의 (DemoHost가 보내는 값)

| 메시지 | 본문 |
|---|---|
| S2F33 리포트 정의 | RPTID 4001 = VID 1001 · 1002 · 1003 / RPTID 4002 = VID 1101 |
| S2F35 이벤트 링크 | CEID 3001 → 4001 / CEID 3002 → 4001 · 4002 |
| S2F37 이벤트 켜기 | CEED TRUE · CEID 3001 3002 |

형식: DATAID · RPTID · VID · ALID = U2, CEID = U4, CEED = BOOLEAN, ALED = B(128 켜기 · 0 끄기) — IronGEM 기본 Data Item Dictionary와 같아야 합니다(다르면 S9F7).
