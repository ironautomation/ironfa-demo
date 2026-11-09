# HelloGem — EP05 앞부분 최소 예제

빈 **Windows Forms 앱(.NET Framework 4.8)**에 IronGEM.Connector를 붙여 SV 하나를 호스트로 올립니다. 코드는 `Form1.cs` 세 군데뿐입니다.

1. NuGet 패키지 `IronAutomation.IronGEM.Connector` 설치 — `grpc_csharp_ext.x86.dll` · `x64.dll`이 출력 폴더에 자동으로 복사됩니다
2. `Form1_Load`: ① 이벤트 등록 → ② `Initialize("127.0.0.1", 5001)`
3. 버튼: ③ `SetVariable(1001, "123.4")` → DemoHost EP03 탭 `S1F3`으로 1001을 물으면 123.4

준비: IronGEM 서비스 실행 중 · IronStudio에 SV 1001(EP03) · **DemoEtcher는 꺼 둡니다**(DemoEtcher도 0.5초마다 1001을 올립니다).
