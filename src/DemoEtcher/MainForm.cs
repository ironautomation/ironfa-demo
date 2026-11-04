using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using IronGEM.Connector;

namespace DemoEtcher
{
    /// <summary>
    /// DemoEtcher 화면: 위쪽 IronGEM 연결 상태 · 가운데 챔버(값 + 그래프) · 아래 장비 로그.
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

        // 아래: 로그
        private readonly ListBox _log = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5F), IntegralHeight = false };

        public MainForm()
        {
            Text = "DemoEtcher — IronFA 입문 실습 장비 (ep03)";
            Font = new Font("Malgun Gothic", 10F);
            ClientSize = new Size(640, 470);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(1260, 560);

            BuildLayout();

            _gem.EcvChangeRequested += OnEcvChangeRequested;
            _gem.StateChanged += ShowGemState;
            _timer.Tick += async (_, __) =>
            {
                _chamber.Step(_timer.Interval / 1000.0);
                ShowChamber();
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
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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

            root.Controls.Add(Group("장비 로그", _log), 0, 2);
            Controls.Add(root);
        }

        private void ShowChamber()
        {
            _lblTemp.Text = $"{_chamber.Temperature:0.0} ℃";
            _lblSetpoint.Text = $"{_chamber.Setpoint:0.0} ℃";
            _lblPressure.Text = $"{_chamber.Pressure:0.00} mTorr";
            _lblProcess.Text = _chamber.ProcessState == 0 ? "0  Idle" : _chamber.ProcessState.ToString();
            _trend.Add(_chamber.Temperature, _chamber.Setpoint);
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
