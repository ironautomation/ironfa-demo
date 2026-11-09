using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IronGEM.Connector;

namespace DemoEtcher
{
    /// <summary>
    /// DemoEtcher와 IronGEM 사이의 연결 — 장비 프로그램에서 IronGEM.Connector를 쓰는 곳은 이 파일 하나입니다.
    ///   ① 연결: 이벤트 등록 → Initialize → Start
    ///   ② 올리기: SV · DVVAL(SetVariable) · 이벤트(SetEvent) · 알람(SetAlarm · ClearAlarm)
    ///   ③ 받기: 호스트의 ECV 변경 요청(GEMReqChangeECV) · 원격 명령 S2F41(GEMUserMsgReceived)
    /// 번호는 IronStudio에서 만든 번호와 같아야 합니다(docs/ep03.md · ep04.md · ep05.md).
    /// </summary>
    public sealed class EquipmentGem
    {
        // ---- IronStudio에서 정한 번호 ------------------------------------------
        public const int SV_ChamberTemp = 1001;      // SV F4 degC
        public const int SV_ChamberPressure = 1002;  // SV F4 mTorr
        public const int SV_ProcessState = 1003;     // SV U1 (0 Idle · 1 Processing)
        public const int SV_ProcessCount = 1004;     // SV U4 — 지금까지 끝낸 공정 수 (EP05)
        public const int DV_ProcessTime = 1101;      // DVVAL F4 sec — 공정이 끝난 순간의 공정 시간
        public const int ECV_TempSetpoint = 2001;    // ECV F4 degC · 20 ~ 200 · 기본 60
        public const int CEID_ProcessStarted = 3001;
        public const int CEID_ProcessCompleted = 3002;
        public const int ALID_OverTemp = 5001;       // ALCD 2(장비 안전) · Chamber over temperature

        // ---- S2F42 HCACK (SEMI E5) ----------------------------------------------
        public const int HCACK_Done = 0;             // 수행했음
        public const int HCACK_InvalidCommand = 1;   // 없는 명령
        public const int HCACK_CannotNow = 2;        // 지금은 할 수 없음
        public const int HCACK_InvalidParameter = 3; // 파라미터 오류
        public const int HCACK_WillComplete = 4;     // 받았음 — 끝나면 이벤트로 알림

        private readonly IronGEMProxy _proxy = new IronGEMProxy();

        // 주기적인 SV 갱신과 이벤트 보고가 섞이지 않게 합니다.
        // (이벤트 직전에 올린 값이 오래된 주기 값으로 덮이면 S6F11에 옛 값이 실립니다.)
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <summary>IronGEM(gRPC)과 통하는지 — 마지막 SetVariable 결과로 판단합니다.</summary>
        public bool Linked { get; private set; }

        /// <summary>호스트가 ECV를 바꾸려 할 때. 반환값이 EAC입니다(0 = 수락).</summary>
        public event Func<int, string, int> EcvChangeRequested;

        /// <summary>
        /// 호스트의 원격 명령(S2F41). 인수: RCMD · 파라미터(CPNAME, CPVAL) 목록. 반환값이 S2F42의 HCACK입니다.
        /// 명령을 '할 수 있는지' 판단만 하고 곧바로 돌려주세요 — 실제 동작은 S2F42를 보낸 뒤에 시작합니다(CommandAccepted).
        /// </summary>
        public event Func<string, IReadOnlyList<KeyValuePair<string, string>>, int> RemoteCommandReceived;

        /// <summary>S2F42를 보낸 뒤 — HCACK이 0 또는 4이면 여기서 실제 동작을 시작합니다.</summary>
        public event Action<string, int> CommandAccepted;

        /// <summary>장비 로그용 한 줄.</summary>
        public event Action<string> Logged;

        /// <summary>호스트와의 통신 · 제어 상태가 바뀌었을 때(화면 표시용).</summary>
        public event Action StateChanged;

        public CommunicationState CommState => (CommunicationState)_proxy.GetCommState();

        public ControlState ControlState => (ControlState)_proxy.GetControlState();

        /// <summary>
        /// IronGEM에 연결합니다. 이벤트는 Initialize를 부른 스레드(화면 스레드)로 들어오므로
        /// 반드시 이벤트를 먼저 등록하고 Initialize를 부릅니다.
        /// </summary>
        public int Connect(string ip = "127.0.0.1", int port = 5001)
        {
            _proxy.GEMReqChangeECV += OnReqChangeEcv;
            _proxy.GEMUserMsgReceived += OnUserMsgReceived;          // EP05: S2F41
            _proxy.GEMCommStateChanged += _ => StateChanged?.Invoke();
            _proxy.GEMControlStateChanged += _ => StateChanged?.Invoke();
            _proxy.GEMConnectStateChanged += _ => StateChanged?.Invoke();

            var result = _proxy.Initialize(ip, port);
            if (result != IronGEMProxy.SUCCESS)
            {
                return result;
            }

            // IronGEM 서비스가 꺼져 있으면 켭니다(이미 켜져 있으면 아무 일도 하지 않음).
            return _proxy.Start();
        }

        /// <summary>챔버 값을 SV로 올립니다. 화면 타이머에서 0.5초마다 부릅니다.</summary>
        public async Task PublishAsync(Chamber chamber)
        {
            if (!await _gate.WaitAsync(0))
            {
                return; // 앞의 요청이나 이벤트 보고가 진행 중이면 이번 주기는 건너뜀
            }
            try
            {
                var result = await SetChamberValuesAsync(chamber);
                var wasLinked = Linked;
                Linked = result == IronGEMProxy.SUCCESS;
                if (Linked && !wasLinked)
                {
                    await SyncSetpointAsync(chamber.Setpoint);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// 이벤트를 알립니다. 순서가 중요합니다: ① 이벤트에 실릴 값(SV · DVVAL)을 먼저 올리고 ② SetEvent.
        /// IronGEM은 SetEvent를 받은 순간 자기가 들고 있는 값으로 S6F11을 만듭니다.
        /// 호스트가 이 이벤트를 켜 두지 않았으면(S2F37) IronGEM은 S6F11을 보내지 않습니다 — 그래도 반환값은 0입니다.
        /// </summary>
        public async Task<int> ReportEventAsync(Chamber chamber, int ceid, IDictionary<int, string> dataValues = null)
        {
            await _gate.WaitAsync();
            try
            {
                var result = await SetChamberValuesAsync(chamber, dataValues);
                if (result != IronGEMProxy.SUCCESS)
                {
                    return result;
                }
                return await _proxy.SetEventAsync(ceid);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>알람 발생(SetAlarm) / 해제(ClearAlarm). 호스트가 S5F3으로 꺼 두었으면 IronGEM은 S5F1을 보내지 않습니다.</summary>
        public Task<int> SetAlarmAsync(int alid, bool set)
            => set ? _proxy.SetAlarmAsync(alid) : _proxy.ClearAlarmAsync(alid);

        // 장비 값 → SV. 값은 문자열로 넘기고, 형식(F4 · U1 · U4)은 IronStudio에 정한 대로 IronGEM이 맞춥니다.
        // 새 SV를 올리려면 IronStudio에 VID를 만들고 여기에 한 줄을 더합니다.
        private Task<int> SetChamberValuesAsync(Chamber chamber, IDictionary<int, string> extra = null)
        {
            var inv = CultureInfo.InvariantCulture;
            var values = new Dictionary<int, string>
            {
                [SV_ChamberTemp] = chamber.Temperature.ToString("0.0", inv),
                [SV_ChamberPressure] = chamber.Pressure.ToString("0.00", inv),
                [SV_ProcessState] = chamber.ProcessState.ToString(inv),
                [SV_ProcessCount] = chamber.ProcessCount.ToString(inv),   // EP05: 새 SV 1004
            };
            if (extra != null)
            {
                foreach (var kv in extra)
                {
                    values[kv.Key] = kv.Value;
                }
            }
            return _proxy.SetVariableAsync(new List<int>(values.Keys), new List<string>(values.Values));
        }

        /// <summary>
        /// 설정 온도의 주인은 장비입니다. IronGEM과 통하기 시작하면(처음 연결 · IronGEM 재시작 뒤)
        /// 장비의 설정 온도를 ECV 2001에 맞춰 둡니다. IronGEM은 이 값을 Default 열에 저장하므로
        /// 리허설에서 바꿔 둔 값이 남아 있어도 DemoEtcher를 켜면 60으로 돌아옵니다.
        /// </summary>
        public async Task SyncSetpointAsync(double setpoint)
        {
            var value = setpoint.ToString("0.###", CultureInfo.InvariantCulture);
            var result = await _proxy.SetECVChangeAsync(ECV_TempSetpoint, value);
            SetpointSynced?.Invoke(value, result);
        }

        /// <summary>SyncSetpointAsync 결과(값 · 반환 코드) — 장비 로그용.</summary>
        public event Action<string, int> SetpointSynced;

        // 호스트 S2F15 → IronGEM(ECID · 범위 검사 통과) → 여기 → 반환값이 S2F16 EAC
        private int OnReqChangeEcv(List<int> ecidList, List<string> valueList)
        {
            for (var i = 0; i < ecidList.Count; i++)
            {
                var eac = EcvChangeRequested?.Invoke(ecidList[i], valueList[i]) ?? 0;
                if (eac != 0)
                {
                    return eac; // 하나라도 거절하면 전체 거절
                }
            }
            return 0;
        }

        // ---- 원격 명령 S2F41 (EP05) -----------------------------------------------
        // 호스트 S2F41 → IronGEM(형식만 검사하고 장비로 넘김) → GEMUserMsgReceived(2, 41)
        // → 받은 메시지를 GetNextItem으로 차례로 읽음 → 판단 → S2F42를 직접 만들어 SendUserMsg.
        // 장비 프로그램이 켜져 있지 않으면 IronGEM이 대신 HCACK 2(지금은 할 수 없음)로 답합니다.
        private void OnUserMsgReceived(int stream, int function)
        {
            if (stream != 2 || function != 41)
            {
                Logged?.Invoke($"S{stream}F{function} 받음 — 이 장비는 처리하지 않습니다");
                return;
            }

            // S2F41 <L [2] <A RCMD> <L [n] <L [2] <A CPNAME> <A CPVAL>> …>>
            // GetNextItem은 메시지를 앞에서부터 한 항목씩 돌려줍니다. 리스트(L)의 Value는 '항목 수'입니다.
            _proxy.GetNextItem();                          // L [2]
            var rcmd = _proxy.GetNextItem()?.Value ?? "";  // RCMD
            var count = int.Parse(_proxy.GetNextItem()?.Value ?? "0", CultureInfo.InvariantCulture); // L [n]
            var parameters = new List<KeyValuePair<string, string>>();
            for (var i = 0; i < count; i++)
            {
                _proxy.GetNextItem();                      // L [2]
                var name = _proxy.GetNextItem()?.Value ?? "";
                var value = _proxy.GetNextItem()?.Value ?? "";
                parameters.Add(new KeyValuePair<string, string>(name, value));
            }

            var hcack = RemoteCommandReceived?.Invoke(rcmd, parameters) ?? HCACK_InvalidCommand;
            var result = SendS2F42(hcack);
            Logged?.Invoke($"S2F41 RCMD '{rcmd}' → S2F42 HCACK {hcack} ({HcackText(hcack)}) 보냄 (결과 {result})");

            if (result == IronGEMProxy.SUCCESS && (hcack == HCACK_Done || hcack == HCACK_WillComplete))
            {
                CommandAccepted?.Invoke(rcmd, hcack); // 응답을 먼저 보내고, 동작은 그다음에
            }
        }

        // S2F42 <L [2] <B HCACK> <L [0]>> — 파라미터 오류가 없으면 CP 목록은 비웁니다.
        private int SendS2F42(int hcack)
        {
            _proxy.MakeMsg(2, 42);
            using (_proxy.BeginListScope())
            {
                // BIN 값은 16진수 두 자리로 적습니다("04"). "4"처럼 한 자리면 IronGEM 형식 검사에서 떨어집니다.
                _proxy.AddItem("HCACK", hcack.ToString("X2", CultureInfo.InvariantCulture), SecsFormat.BINARY);
                using (_proxy.BeginListScope())
                {
                }
            }
            return _proxy.SendUserMsg(2, 42);
        }

        public static string HcackText(int hcack) => hcack switch
        {
            HCACK_Done => "수행했음",
            HCACK_InvalidCommand => "없는 명령",
            HCACK_CannotNow => "지금은 할 수 없음",
            HCACK_InvalidParameter => "파라미터 오류",
            HCACK_WillComplete => "받았음 · 끝나면 이벤트로 알림",
            _ => "장비 고유 코드",
        };

        // 창을 닫을 때 Stop()을 부르지 않습니다 — Stop()은 IronGEM의 호스트 통신 서비스까지 멈춥니다.
    }
}
