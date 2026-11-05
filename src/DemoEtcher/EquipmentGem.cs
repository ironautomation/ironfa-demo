using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using IronGEM.Connector;

namespace DemoEtcher
{
    /// <summary>
    /// DemoEtcher와 IronGEM 사이의 연결. 장비 값(SV · DVVAL)을 올리고, 이벤트 · 알람을 알리고,
    /// 호스트의 ECV 변경 요청에 답합니다.
    /// 번호는 IronStudio에서 만든 번호와 같아야 합니다(docs/ep03.md · docs/ep04.md).
    /// </summary>
    public sealed class EquipmentGem
    {
        // ---- IronStudio에서 정한 번호 ------------------------------------------
        public const int SV_ChamberTemp = 1001;      // SV F4 degC
        public const int SV_ChamberPressure = 1002;  // SV F4 mTorr
        public const int SV_ProcessState = 1003;     // SV U1 (0 Idle · 1 Processing)
        public const int DV_ProcessTime = 1101;      // DVVAL F4 sec — 공정이 끝난 순간의 공정 시간
        public const int ECV_TempSetpoint = 2001;    // ECV F4 degC · 20 ~ 200 · 기본 60
        public const int CEID_ProcessStarted = 3001;
        public const int CEID_ProcessCompleted = 3002;
        public const int ALID_OverTemp = 5001;       // ALCD 2(장비 안전) · Chamber over temperature

        private readonly IronGEMProxy _proxy = new IronGEMProxy();

        // 주기적인 SV 갱신과 이벤트 보고가 섞이지 않게 합니다.
        // (이벤트 직전에 올린 값이 오래된 주기 값으로 덮이면 S6F11에 옛 값이 실립니다.)
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        /// <summary>IronGEM(gRPC)과 통하는지 — 마지막 SetVariable 결과로 판단합니다.</summary>
        public bool Linked { get; private set; }

        /// <summary>호스트가 ECV를 바꾸려 할 때. 반환값이 EAC입니다(0 = 수락).</summary>
        public event Func<int, string, int> EcvChangeRequested;

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

        private Task<int> SetChamberValuesAsync(Chamber chamber, IDictionary<int, string> extra = null)
        {
            var inv = CultureInfo.InvariantCulture;
            var vids = new List<int> { SV_ChamberTemp, SV_ChamberPressure, SV_ProcessState };
            var values = new List<string>
            {
                chamber.Temperature.ToString("0.0", inv),
                chamber.Pressure.ToString("0.00", inv),
                chamber.ProcessState.ToString(inv),
            };
            if (extra != null)
            {
                foreach (var kv in extra)
                {
                    vids.Add(kv.Key);
                    values.Add(kv.Value);
                }
            }
            return _proxy.SetVariableAsync(vids, values);
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

        // 창을 닫을 때 Stop()을 부르지 않습니다 — Stop()은 IronGEM의 호스트 통신 서비스까지 멈춥니다.
    }
}
