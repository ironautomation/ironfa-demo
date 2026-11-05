using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using IronGEM.Connector;

namespace DemoEtcher
{
    /// <summary>
    /// DemoEtcher 화면: 위쪽 IronGEM 연결 상태 · 가운데 챔버(값 + 그래프) · 운전 버튼 · 아래 장비 로그.
    /// </summary>
    public sealed class MainForm : Form
    {
        private readonly Chamber _chamber = new Chamber();
        private readonly EquipmentGem _gem = new EquipmentGem();
        private readonly Timer _timer = new Timer { Interval = 500 };

        // 위쪽: IronGEM
        private readonly Label _lblLink = StateLabel();
        private readonly Label _lblComm = StateLabel();
        private readonly Label _lblControl = StateLabel();

        // 가운데: 챔버
        private readonly Label _lblTemp = new Label { AutoSize = true, Font = new Font("Malgun Gothic", 26F, FontStyle.Bold), ForeColor = Color.FromArgb(210, 40, 40) };
        private readonly Label _lblSetpoint = Value();
        private readonly Label _lblPressure = Value();
        private readonly Label _lblProcess = Value();
        private readonly TrendView _trend = new TrendView { Dock = DockStyle.Fill, MinimumSize = new Size(300, 150) };

        // 운전(EP04): 공정 실행 · 과온 시뮬레이션 · 알람 표시
        public const double ProcessSeconds = 10.0;
        private readonly Button _btnProcess = new Button { Text = "공정 실행 (10초)", AutoSize = true, Height = 32, Padding = new Padding(6, 0, 6, 0) };
        private readonly CheckBox _chkFault = new CheckBox { Text = "과온 시뮬레이션", Appearance = Appearance.Button, AutoSize = true, Height = 32, Padding = new Padding(6, 0, 6, 0), TextAlign = ContentAlignment.MiddleCenter };
        private readonly Label _lblAlarm = StateLabel();
        private bool _ending;

        // 아래: 로그
        private readonly ListBox _log = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F), IntegralHeight = false };

        public MainForm()
        {
            Text = "DemoEtcher — IronFA 입문 실습 장비 (ep04)";
            Font = new Font("Malgun Gothic", 10F);
            ClientSize = new Size(640, 520);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(1260, 510);

            BuildLayout();

            _gem.EcvChangeRequested += OnEcvChangeRequested;
            _gem.StateChanged += ShowGemState;
            _gem.SetpointSynced += (value, result) => Log(result == IronGEMProxy.SUCCESS
                ? $"IronGEM 연결됨 · ECV 2001 TempSetpoint를 장비 설정 온도 {value} ℃로 맞춤"
                : $"ECV 2001 맞추기 실패 (코드 {result}) — IronStudio에 ECV 2001이 있는지 확인하세요");
            _btnProcess.Click += async (_, __) => await StartProcessAsync();
            _chkFault.CheckedChanged += (_, __) =>
            {
                _chamber.HeaterFault = _chkFault.Checked;
                Log(_chkFault.Checked ? "과온 시뮬레이션 켬 — 히터 이상, 온도가 설정 온도 + 40℃까지 오릅니다" : "과온 시뮬레이션 끔 — 온도가 설정 온도로 돌아갑니다");
            };

            _timer.Tick += async (_, __) =>
            {
                var overTempChanged = _chamber.Step(_timer.Interval / 1000.0);
                ShowChamber();
                if (overTempChanged)
                {
                    await ReportOverTempAsync(_chamber.OverTemp);
                }
                if (_chamber.ProcessState == Chamber.Processing && _chamber.ProcessElapsed >= ProcessSeconds && !_ending)
                {
                    await EndProcessAsync();
                }
                await _gem.PublishAsync(_chamber);
                ShowGemState();
            };

            Load += (_, __) =>
            {
                ShowChamber();
                ShowGemState();
                var result = _gem.Connect("127.0.0.1", 5001);
                Log(result == IronGEMProxy.SUCCESS
                    ? "IronGEM 127.0.0.1:5001 에 연결을 시작했습니다"
                    : $"IronGEM 연결 시작 실패 (코드 {result}) — IronGEM이 켜져 있는지 확인하세요");
                _timer.Start();
            };
        }

        // ---- 공정 (EP04) -----------------------------------------------------------
        // 장비는 일이 일어날 때마다 이벤트를 올립니다. 호스트에게 보낼지는 IronGEM이 호스트 설정(S2F33 · S2F35 · S2F37)대로 정합니다.
        private async Task StartProcessAsync()
        {
            if (!_chamber.StartProcess())
            {
                Log("이미 공정 중입니다");
                return;
            }
            ShowChamber();
            var result = await _gem.ReportEventAsync(_chamber, EquipmentGem.CEID_ProcessStarted);
            Log($"공정 시작 · ProcessState 1 → 이벤트 CEID {EquipmentGem.CEID_ProcessStarted} ProcessStarted ({ResultText(result)})");
        }

        private async Task EndProcessAsync()
        {
            _ending = true;
            try
            {
                var seconds = _chamber.EndProcess();
                ShowChamber();
                var dv = new Dictionary<int, string>
                {
                    [EquipmentGem.DV_ProcessTime] = seconds.ToString("0.0", CultureInfo.InvariantCulture),
                };
                var result = await _gem.ReportEventAsync(_chamber, EquipmentGem.CEID_ProcessCompleted, dv);
                Log($"공정 끝 · ProcessTime {seconds:0.0} s (DVVAL 1101) → 이벤트 CEID {EquipmentGem.CEID_ProcessCompleted} ProcessCompleted ({ResultText(result)})");
            }
            finally
            {
                _ending = false;
            }
        }

        // ---- 알람 (EP04) -----------------------------------------------------------
        private async Task ReportOverTempAsync(bool set)
        {
            ShowAlarm();
            var result = await _gem.SetAlarmAsync(EquipmentGem.ALID_OverTemp, set);
            Log(set
                ? $"과온! 온도 {_chamber.Temperature:0.0} ℃ > 설정 {_chamber.Setpoint:0.#} + {Chamber.OverTempOn:0} → 알람 {EquipmentGem.ALID_OverTemp} 발생 SetAlarm ({ResultText(result)})"
                : $"온도 정상 {_chamber.Temperature:0.0} ℃ → 알람 {EquipmentGem.ALID_OverTemp} 해제 ClearAlarm ({ResultText(result)})");
        }

        private static string ResultText(int result) => result == IronGEMProxy.SUCCESS ? "결과 0 = 성공" : $"결과 {result} = 실패";

        // ---- 호스트가 ECV를 바꾸려 할 때 ------------------------------------------
        // 범위(20 ~ 200)는 IronGEM이 먼저 검사합니다. 여기서는 '장비가 지금 받아들일 수 있는가'만 판단합니다.
        private int OnEcvChangeRequested(int ecid, string value)
        {
            if (ecid != EquipmentGem.ECV_TempSetpoint)
            {
                Log($"ECV 변경 요청 {ecid} = {value} → 수락 (EAC 0)");
                return 0; // 이 장비가 쓰지 않는 ECV는 그대로 받아들임
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var setpoint))
            {
                Log($"ECV 변경 요청 {ecid} = '{value}' → 숫자가 아님, 거절 (EAC 3)");
                return 3;
            }

            var old = _chamber.Setpoint;
            _chamber.Setpoint = setpoint;
            Log($"ECV 변경 요청 {ecid} TempSetpoint = {setpoint:0.#} → 수락 (EAC 0) · 설정 온도 {old:0.#} → {setpoint:0.#} ℃");
            ShowChamber();
            return 0;
        }

        // ---- 화면 ----------------------------------------------------------------
        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(6) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));

            var gem = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            gem.Controls.AddRange(new Control[] { _lblLink, _lblComm, _lblControl });
            root.Controls.Add(Group("IronGEM", gem), 0, 0);

            var values = new TableLayoutPanel { Dock = DockStyle.Left, ColumnCount = 2, AutoSize = true, Padding = new Padding(0, 4, 12, 0) };
            values.Controls.Add(Caption("챔버 온도 (1001)"), 0, 0);
            values.SetColumnSpan(values.GetControlFromPosition(0, 0), 2);
            values.Controls.Add(_lblTemp, 0, 1);
            values.SetColumnSpan(_lblTemp, 2);
            values.Controls.Add(Caption("설정 온도 (2001)"), 0, 2);
            values.Controls.Add(_lblSetpoint, 1, 2);
            values.Controls.Add(Caption("압력 (1002)"), 0, 3);
            values.Controls.Add(_lblPressure, 1, 3);
            values.Controls.Add(Caption("공정 상태 (1003)"), 0, 4);
            values.Controls.Add(_lblProcess, 1, 4);

            var chamber = new Panel { Dock = DockStyle.Fill };
            chamber.Controls.Add(_trend);
            chamber.Controls.Add(values);
            root.Controls.Add(Group("챔버", chamber), 0, 1);

            var run = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            run.Controls.AddRange(new Control[] { _btnProcess, _chkFault, _lblAlarm });
            root.Controls.Add(Group("운전", run), 0, 2);

            root.Controls.Add(Group("장비 로그", _log), 0, 3);
            Controls.Add(root);
        }

        private void ShowChamber()
        {
            _lblTemp.Text = $"{_chamber.Temperature:0.0} ℃";
            _lblSetpoint.Text = $"{_chamber.Setpoint:0.0} ℃";
            _lblPressure.Text = $"{_chamber.Pressure:0.00} mTorr";
            _lblProcess.Text = _chamber.ProcessState == Chamber.Processing
                ? $"1  Processing  {_chamber.ProcessElapsed:0}/{ProcessSeconds:0} s"
                : "0  Idle";
            _btnProcess.Enabled = _chamber.ProcessState == Chamber.Idle;
            ShowAlarm();
            _trend.Add(_chamber.Temperature, _chamber.Setpoint);
        }

        private void ShowAlarm()
        {
            SetState(_lblAlarm,
                _chamber.OverTemp ? $"알람 {EquipmentGem.ALID_OverTemp} OverTemp: 발생" : $"알람 {EquipmentGem.ALID_OverTemp} OverTemp: 정상",
                _chamber.OverTemp ? Color.LightSalmon : Color.Gainsboro);
        }

        private void ShowGemState()
        {
            SetState(_lblLink, _gem.Linked ? "IronGEM: 연결됨" : "IronGEM: 연결 안 됨", _gem.Linked ? Color.PaleGreen : Color.LightSalmon);

            if (!_gem.Linked)
            {
                SetState(_lblComm, "호스트 통신: -", Color.Gainsboro);
                SetState(_lblControl, "제어 상태: -", Color.Gainsboro);
                return;
            }

            var comm = _gem.CommState;
            SetState(_lblComm, $"호스트 통신: {comm}", comm == CommunicationState.COMMUNICATING ? Color.PaleGreen : Color.Gainsboro);

            var control = _gem.ControlState;
            var text = control switch
            {
                ControlState.REMOTE => "ONLINE REMOTE",
                ControlState.LOCAL => "ONLINE LOCAL",
                ControlState.HOST_OFFLINE => "HOST OFFLINE",
                ControlState.EQUIP_OFFLINE => "EQUIPMENT OFFLINE",
                _ => control.ToString(),
            };
            SetState(_lblControl, $"제어 상태: {text}", control == ControlState.REMOTE || control == ControlState.LOCAL ? Color.PaleGreen : Color.Gainsboro);
        }

        private void Log(string text)
        {
            _log.Items.Add($"{DateTime.Now:HH:mm:ss}  {text}");
            _log.TopIndex = Math.Max(0, _log.Items.Count - 1);
        }

        private static void SetState(Label label, string text, Color back)
        {
            label.Text = text;
            label.BackColor = back;
        }

        private static Label StateLabel() => new Label { AutoSize = true, Padding = new Padding(8, 5, 8, 5), Margin = new Padding(3, 3, 6, 3) };

        private static Label Value() => new Label { AutoSize = true, Font = new Font("Malgun Gothic", 11F, FontStyle.Bold), Padding = new Padding(0, 4, 0, 4) };

        private static Label Caption(string text) => new Label { Text = text, AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 6, 8, 4) };

        private static GroupBox Group(string text, Control content)
        {
            var g = new GroupBox { Text = text, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6) };
            g.Controls.Add(content);
            return g;
        }
    }
}
