using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using IronGEM.Connector;

namespace DemoEtcher
{
    /// <summary>
    /// DemoEtcher와 IronGEM 사이의 연결. 장비 값(SV)을 올리고, 호스트의 ECV 변경 요청에 답합니다.
    /// VID 번호는 IronStudio에서 만든 번호와 같아야 합니다(docs/ep03.md).
    /// </summary>
    public sealed class EquipmentGem
    {
        // ---- IronStudio에서 정한 번호 ------------------------------------------
        public const int SV_ChamberTemp = 1001;      // F4 degC
        public const int SV_ChamberPressure = 1002;  // F4 mTorr
        public const int SV_ProcessState = 1003;     // U1
        public const int ECV_TempSetpoint = 2001;    // F4 degC · 20 ~ 200 · 기본 60

        private readonly IronGEMProxy _proxy = new IronGEMProxy();
        private bool _sending;

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
            if (_sending)
            {
                return; // 앞의 요청이 아직 끝나지 않았으면 이번 주기는 건너뜀
            }
            _sending = true;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                var vids = new List<int> { SV_ChamberTemp, SV_ChamberPressure, SV_ProcessState };
                var values = new List<string>
                {
                    chamber.Temperature.ToString("0.0", inv),
                    chamber.Pressure.ToString("0.00", inv),
                    chamber.ProcessState.ToString(inv),
                };
                var result = await _proxy.SetVariableAsync(vids, values);
                Linked = result == IronGEMProxy.SUCCESS;
            }
            finally
            {
                _sending = false;
            }
        }

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
