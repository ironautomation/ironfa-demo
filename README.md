# ironfa-demo

**IronFA 입문** 유튜브 강좌의 실습 레포입니다.
가상 식각 장비 **DemoEtcher**와 실습 호스트 **DemoHost**로 SECS/GEM 통신을 처음부터 끝까지 따라 합니다.

| 폴더 | 내용 |
|---|---|
| `src/DemoEtcher` | 가상 매엽식 식각 장비 — 로드포트 · 챔버 · 공정 시퀀스 · 알람 (C#, IronGEM.Connector) |
| `src/DemoHost` | 실습용 호스트(MES 역할) — 메시지 로그 + 시나리오 버튼 (C#, [secs4net](https://github.com/mkjeff/secs4net)) |
| `config` | 회차별 IronFA 설정 예시 (SV · ECV · CEID · ALID · RCMD) |
| `docs` | 회차별 시나리오와 SECS 로그 예시 |

## 준비물

- Windows 10/11 64비트
- **IronTools v2** (IronFA 포함 설치) — 90일 트라이얼: https://trial.ironautomation.com
- DemoHost — EP02부터. Releases의 `DemoHost-ep02-win-x64.zip`(설치 불필요) 또는 .NET 8 SDK로 직접 빌드 (`src/DemoHost/README.md`)
- Visual Studio 2022 — EP05(장비 프로그램 코드)부터 필요

## 받는 방법

```
cd C:\Demo
git clone https://github.com/ironautomation/ironfa-demo.git
```

git이 없으면 **Code › Download ZIP**으로 받아 `C:\Demo`에 풀고, 폴더 이름 `ironfa-demo-main`을 `ironfa-demo`로 바꿉니다.
특정 회차 상태로 받으려면 해당 태그(`ep01` …)를 고릅니다.

## 회차와 태그

| 회차 | 제목 | 태그 |
|---|---|---|
| EP01 | SECS/GEM과 IronFA — 장비와 호스트가 대화하는 법 | `ep01` |
| EP02 | 호스트와 첫 연결 — 통신 확립과 온라인 | `ep02` |
| EP03 | 상태 변수와 장비 상수 — SV · ECV | 예정 |
| EP04 | 이벤트 · 리포트 · 알람 — 장비가 먼저 말한다 | 예정 |
| EP05 | 장비 프로그램 속으로 — IronGEM.Connector (C#) | 예정 |
| EP06 | 300mm ① 캐리어 — 로드포트에 FOUP이 올라오면 (E87) | 예정 |
| EP07 | 300mm ② 잡 — Process Job과 Control Job (E40 · E94) | 예정 |
| EP08 | PLC 값을 호스트로 — IronServer 연동 | 예정 |

## 라이선스

- 이 레포: MIT License — `LICENSE` 참고
- DemoHost는 [secs4net](https://github.com/mkjeff/secs4net)(MIT License)을 사용합니다.
- IronTools v2 · IronFA · IronGEM은 (주)아이언오토메이션의 상용 제품이며 이 레포에 포함되지 않습니다.
- SEMI 표준(E5 · E30 · E37 · E40 · E87 · E94) 문서는 SEMI에서 구입할 수 있습니다.

---
제작: (주)아이언오토메이션 · 질문은 Issues에 남겨 주세요.
