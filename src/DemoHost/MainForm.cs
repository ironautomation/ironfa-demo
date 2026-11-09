using System.Text;
using Microsoft.Extensions.Options;
using Secs4Net;
using Secs4Net.Sml;

namespace DemoHost;

/// <summary>
/// DemoHost 메인 화면: 위쪽 연결 설정 · 가운데 시나리오 버튼 · 아래 메시지 로그.
/// </summary>
public sealed class MainForm : Form
{
    // ---- 화면 요소 ----------------------------------------------------------
    private readonly TextBox _txtIp = new() { Text = "127.0.0.1", Width = 130 };
    private readonly NumericUpDown _numPort = new() { Minimum = 1, Maximum = 65535, Value = 5000, Width = 80 };
    private readonly NumericUpDown _numDeviceId = new() { Minimum = 0, Maximum = 32767, Value = 0, Width = 70 };
    private readonly RadioButton _rbActive = new() { Text = "Active", Checked = true, AutoSize = true };
    private readonly RadioButton _rbPassive = new() { Text = "Passive", AutoSize = true };
    private readonly Button _btnConnect = new() { Text = "연결", Width = 90, Height = 32 };
    private readonly Button _btnDisconnect = new() { Text = "끊기", Width = 90, Height = 32, Enabled = false };
    private readonly Label _lblHsms = new() { AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
    private readonly Label _lblComm = new() { AutoSize = true, Padding = new Padding(8, 6, 8, 6) };

    private readonly Button _btnS1F13 = ScenarioButton("S1F13  통신 확립");
    private readonly Button _btnS1F1 = ScenarioButton("S1F1  Are You There");
    private readonly Button _btnS1F15 = ScenarioButton("S1F15  오프라인 요청");
    private readonly Button _btnS1F17 = ScenarioButton("S1F17  온라인 요청");
    private readonly CheckBox _chkAutoReply = new() { Text = "장비 메시지 자동 응답", Checked = true, AutoSize = true };

    // EP03 · SV · ECV
    private readonly TextBox _txtSvids = new() { Width = 170, PlaceholderText = "비우면 전부" };
    private readonly Button _btnS1F11 = ScenarioButton("S1F11  SV 목록");
    private readonly Button _btnS1F3 = ScenarioButton("S1F3  SV 값");
    private readonly TextBox _txtEcids = new() { Width = 120, PlaceholderText = "비우면 전부" };
    private readonly Button _btnS2F29 = ScenarioButton("S2F29  ECV 목록");
    private readonly Button _btnS2F13 = ScenarioButton("S2F13  ECV 값");
    private readonly TextBox _txtEcv = new() { Width = 80, Text = "150" };
    private readonly ComboBox _cmbEcvFormat = new() { Width = 85, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _btnS2F15 = ScenarioButton("S2F15  ECV 변경");
    private readonly Label _lblIdFormat = new() { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(6, 9, 0, 0) };

    // EP04 · 이벤트 · 리포트 · 알람
    private readonly Button _btnReportClear = ScenarioButton("리포트 전부 지우기");
    private readonly Button _btnS2F33 = ScenarioButton("S2F33  리포트 정의");
    private readonly Button _btnS2F35 = ScenarioButton("S2F35  이벤트 링크");
    private readonly TextBox _txtCeids = new() { Width = 110, Text = "3001 3002", PlaceholderText = "비우면 전부" };
    private readonly Button _btnS2F37On = ScenarioButton("S2F37  이벤트 켜기");
    private readonly Button _btnS2F37Off = ScenarioButton("S2F37  끄기");
    private readonly TextBox _txtAlid = new() { Width = 70, Text = "5001" };
    private readonly Button _btnS5F3On = ScenarioButton("S5F3  알람 켜기");
    private readonly Button _btnS5F3Off = ScenarioButton("S5F3  끄기");
    // EP05 · 원격 명령
    private readonly TextBox _txtRcmd = new() { Width = 110, Text = "START" };
    private readonly TextBox _txtCp = new() { Width = 160, PlaceholderText = "CPNAME=CPVAL (비우면 없음)" };
    private readonly Button _btnS2F41 = ScenarioButton("S2F41  원격 명령");
    private readonly Label _lblEp05 = new() { AutoSize = true, ForeColor = Color.FromArgb(120, 60, 160), Padding = new Padding(6, 6, 0, 0), Text = "HCACK 0 수행함 · 1 없는 명령 · 2 지금은 할 수 없음 · 3 파라미터 오류 · 4 받았음(끝나면 이벤트)" };

    private readonly Label _lblEp04 = new() { AutoSize = true, ForeColor = Color.FromArgb(120, 60, 160), Padding = new Padding(6, 6, 0, 0) };

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    private readonly RichTextBox _log = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = Color.White,
        Font = new Font("Consolas", 11F),
        WordWrap = false,
        HideSelection = false,
    };
    private readonly CheckBox _chkSml = new() { Text = "메시지 본문(SML) 표시", Checked = true, AutoSize = true };
    private readonly CheckBox _chkDetail = new() { Text = "상세 로그(HSMS 타이머 · Linktest)", AutoSize = true };
    private readonly Button _btnClear = new() { Text = "로그 지우기", Width = 110, Height = 30 };
    private readonly Button _btnSave = new() { Text = "로그 저장", Width = 110, Height = 30 };

    // ---- 통신 ---------------------------------------------------------------
    private HsmsConnection? _connection;
    private SecsGem? _secsGem;
    private CancellationTokenSource? _cts;
    private readonly HostLogger _logger;

    public MainForm()
    {
        Text = "DemoHost — IronFA 입문 실습 호스트 (ep05)";
        Font = new Font("Malgun Gothic", 10F);
        ClientSize = new Size(1180, 820);
        StartPosition = FormStartPosition.CenterScreen;
        _logger = new HostLogger(this);

        BuildLayout();

        _btnConnect.Click += async (_, _) => await ConnectAsync();
        _btnDisconnect.Click += async (_, _) => await DisconnectAsync(logIt: true);
        _btnS1F13.Click += async (_, _) => await SendAsync(Messages.S1F13);
        _btnS1F1.Click += async (_, _) => await SendAsync(Messages.S1F1);
        _btnS1F15.Click += async (_, _) => await SendAsync(Messages.S1F15);
        _btnS1F17.Click += async (_, _) => await SendAsync(Messages.S1F17);
        _btnS1F11.Click += async (_, _) => await SendAsync(() => Messages.S1F11(Messages.ParseIds(_txtSvids.Text)));
        _btnS1F3.Click += async (_, _) => await SendAsync(() => Messages.S1F3(Messages.ParseIds(_txtSvids.Text)));
        _btnS2F29.Click += async (_, _) => await SendAsync(() => Messages.S2F29(Messages.ParseIds(_txtEcids.Text)));
        _btnS2F13.Click += async (_, _) => await SendAsync(() => Messages.S2F13(Messages.ParseIds(_txtEcids.Text)));
        _btnS2F15.Click += async (_, _) => await SendEcvChangeAsync();
        _btnReportClear.Click += async (_, _) => await ClearReportsAsync();
        _btnS2F33.Click += async (_, _) => await SendAsync(Messages.S2F33Define);
        _btnS2F35.Click += async (_, _) => await SendAsync(Messages.S2F35Link);
        _btnS2F37On.Click += async (_, _) => await SendCeidEnableAsync(true);
        _btnS2F37Off.Click += async (_, _) => await SendCeidEnableAsync(false);
        _btnS5F3On.Click += async (_, _) => await SendAsync(() => Messages.S5F3(true, _txtAlid.Text));
        _btnS5F3Off.Click += async (_, _) => await SendAsync(() => Messages.S5F3(false, _txtAlid.Text));
        _lblEp04.Text = Messages.Ep04Summary;
        _btnS2F41.Click += async (_, _) => await SendAsync(() => Messages.S2F41(_txtRcmd.Text, _txtCp.Text));
        _txtEcids.TextChanged += (_, _) => SyncEcvFormat();
        _cmbEcvFormat.Items.AddRange(Messages.ValueFormats);
        _cmbEcvFormat.SelectedItem = "F4";
        Messages.FormatsLearned += () => Ui(() => { SyncEcvFormat(); ShowIdFormat(); });
        ShowIdFormat();
        _btnClear.Click += (_, _) => _log.Clear();
        _btnSave.Click += (_, _) => SaveLog();
        _chkSml.CheckedChanged += (_, _) => _showSml = _chkSml.Checked;
        _chkDetail.CheckedChanged += (_, _) => _showDetail = _chkDetail.Checked;
        FormClosing += async (_, _) => await DisconnectAsync(logIt: false);

        SetHsmsState(null);
        SetCommunicating(false);
        SetScenarioEnabled(false);
        AppendLine("DemoHost 준비. 연결 설정을 확인하고 [연결]을 누르세요.", Color.Gray);
    }

    // ---- 레이아웃 -----------------------------------------------------------
    private void BuildLayout()
    {
        // EP04부터 회차별 버튼을 탭으로 나눕니다(창을 화면 절반 폭으로 두어도 로그가 넓게 보이도록).
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 1) 연결(HSMS)
        var conn = Flow();
        conn.Controls.AddRange(new Control[]
        {
            Caption("IP"), _txtIp, Caption("Port"), _numPort, Caption("Device ID"), _numDeviceId,
            Caption("Mode"), _rbActive, _rbPassive, _btnConnect, _btnDisconnect, _lblHsms, _lblComm,
        });
        root.Controls.Add(Group("연결 (HSMS)", conn), 0, 0);

        // 2) 시나리오(EP02)
        var scen = Flow();
        scen.Controls.AddRange(new Control[] { _btnS1F13, _btnS1F1, _btnS1F15, _btnS1F17 });
        _tabs.TabPages.Add(Page("EP02 · 통신 확립 · 온라인", scen));

        // 2-1) 시나리오(EP03)
        var sv = Flow();
        sv.Controls.AddRange(new Control[]
        {
            Caption("SVID"), _txtSvids, _btnS1F11, _btnS1F3,
            Caption("   ECID"), _txtEcids, _btnS2F29, _btnS2F13,
            Caption("값"), _txtEcv, _cmbEcvFormat, _btnS2F15, _lblIdFormat,
        });
        // 창을 화면 절반 폭으로 두어도 S2F15 버튼이 잘리지 않게 SV 줄과 ECV 줄을 나눕니다(EP03 녹화 v05).
        sv.SetFlowBreak(_btnS1F3, true);
        _tabs.TabPages.Add(Page("EP03 · SV · ECV", sv));

        // 2-2) 시나리오(EP04): 호스트가 리포트를 정의하고(S2F33) 이벤트에 걸고(S2F35) 켭니다(S2F37).
        var ev = Flow();
        ev.Controls.AddRange(new Control[]
        {
            _btnReportClear, _btnS2F33, _btnS2F35,
            Caption("CEID"), _txtCeids, _btnS2F37On, _btnS2F37Off,
            Caption("   ALID"), _txtAlid, _btnS5F3On, _btnS5F3Off,
            _lblEp04,
        });
        ev.SetFlowBreak(_btnS2F35, true);
        ev.SetFlowBreak(_btnS5F3Off, true);
        _tabs.TabPages.Add(Page("EP04 · 이벤트 · 알람", ev));

        // 2-3) 시나리오(EP05): 호스트가 장비에 명령합니다(S2F41). 판단과 응답(S2F42 HCACK)은 장비 프로그램이 합니다.
        var rc = Flow();
        rc.Controls.AddRange(new Control[] { Caption("RCMD"), _txtRcmd, Caption("파라미터"), _txtCp, _btnS2F41, _lblEp05 });
        rc.SetFlowBreak(_btnS2F41, true);
        _tabs.TabPages.Add(Page("EP05 · 원격 명령", rc));
        _tabs.SelectedIndex = _tabs.TabPages.Count - 1;
        root.Controls.Add(_tabs, 0, 1);

        // 3) 로그
        var logBox = new GroupBox { Text = "메시지 로그   (H→E 호스트가 보냄 · E→H 장비가 보냄 · W 응답 필요)", Dock = DockStyle.Fill, Padding = new Padding(6) };
        logBox.Controls.Add(_log);
        root.Controls.Add(logBox, 0, 2);

        // 4) 로그 도구
        var tools = Flow();
        tools.Controls.AddRange(new Control[] { _btnClear, _btnSave, _chkSml, _chkDetail, _chkAutoReply });
        root.Controls.Add(tools, 0, 3);

        Controls.Add(root);
    }

    private static FlowLayoutPanel Flow() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        WrapContents = true,
        FlowDirection = FlowDirection.LeftToRight,
    };

    private static GroupBox Group(string text, Control content)
    {
        var g = new GroupBox { Text = text, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6) };
        g.Controls.Add(content);
        return g;
    }

    private static TabPage Page(string text, Control content)
    {
        var page = new TabPage(text) { Padding = new Padding(4), UseVisualStyleBackColor = true };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(6, 7, 0, 0) };

    private static Button ScenarioButton(string text) => new() { Text = text, AutoSize = true, Height = 34, Padding = new Padding(6, 0, 6, 0) };

    // ---- 연결 · 끊기 --------------------------------------------------------
    private async Task ConnectAsync()
    {
        await DisconnectAsync(logIt: false);

        var options = Options.Create(new SecsGemOptions
        {
            IsActive = _rbActive.Checked,
            IpAddress = _txtIp.Text.Trim(),
            Port = (int)_numPort.Value,
            DeviceId = (ushort)_numDeviceId.Value,
        });

        var cts = new CancellationTokenSource();
        var connection = new HsmsConnection(options, _logger);
        var secsGem = new SecsGem(options, connection, _logger);
        connection.ConnectionChanged += (_, state) => Ui(() => OnConnectionChanged(state));

        _cts = cts;
        _connection = connection;
        _secsGem = secsGem;
        SetEditable(false);

        var mode = options.Value.IsActive ? "Active(접속하러 감)" : "Passive(접속을 기다림)";
        AppendLine($"{Now()}  ---  {mode} · {options.Value.IpAddress}:{options.Value.Port} · Device ID {options.Value.DeviceId}", Color.Gray);

        connection.Start(cts.Token);

        try
        {
            await foreach (var wrapper in secsGem.GetPrimaryMessageAsync(cts.Token))
            {
                await HandlePrimaryAsync(wrapper, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // 끊기
        }
    }

    private async Task DisconnectAsync(bool logIt)
    {
        var cts = _cts;
        var connection = _connection;
        var secsGem = _secsGem;
        _cts = null;
        _connection = null;
        _secsGem = null;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
        secsGem?.Dispose();
        cts.Dispose();

        if (!IsDisposed)
        {
            SetHsmsState(null);
            SetCommunicating(false);
            SetScenarioEnabled(false);
            SetEditable(true);
            if (logIt)
            {
                AppendLine($"{Now()}  ---  연결을 끊었습니다", Color.Gray);
            }
        }
    }

    private void OnConnectionChanged(ConnectionState state)
    {
        SetHsmsState(state);
        SetScenarioEnabled(state == ConnectionState.Selected);
        if (state != ConnectionState.Selected)
        {
            SetCommunicating(false);
        }
        AppendLine($"{Now()}  ---  HSMS {state}{StateHint(state)}", Color.Gray);
    }

    private static string StateHint(ConnectionState state) => state switch
    {
        ConnectionState.Connecting => "  (TCP 접속 시도 중)",
        ConnectionState.Connected => "  (TCP 연결됨 · Select 대기)",
        ConnectionState.Selected => "  (HSMS 연결 완료 · 이제 SECS 메시지를 주고받을 수 있음)",
        ConnectionState.Retry => "  (연결 실패 · 다시 시도)",
        _ => string.Empty,
    };

    // ---- 보내기 · 받기 ------------------------------------------------------
    private async Task SendAsync(Func<SecsMessage> create)
    {
        var secsGem = _secsGem;
        var cts = _cts;
        if (secsGem is null || cts is null || _connection?.State != ConnectionState.Selected)
        {
            AppendLine("HSMS가 Selected 상태가 아닙니다. 먼저 [연결]하세요.", Color.DarkOrange);
            return;
        }

        using var message = create();
        try
        {
            using var reply = await secsGem.SendAsync(message, cts.Token);
            if (reply.S == 1 && reply.F == 14 && reply.SecsItem?[0].FirstValueOrDefault<byte>(255) == 0)
            {
                SetCommunicating(true);
            }
        }
        catch (SecsException ex)
        {
            AppendLine($"           ✗ 응답 오류: {ex.Message}", Color.Red);
        }
        catch (OperationCanceledException)
        {
            // 끊기
        }
    }

    private async Task SendEcvChangeAsync()
    {
        var ids = Messages.ParseIds(_txtEcids.Text);
        if (ids.Count != 1)
        {
            AppendLine("S2F15는 ECID 칸에 하나만 적어 주세요(예: 2001).", Color.DarkOrange);
            return;
        }
        var format = _cmbEcvFormat.SelectedItem as string ?? "F4";
        Secs4Net.Item value;
        try
        {
            value = Messages.Value(format, _txtEcv.Text);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            AppendLine($"값 '{_txtEcv.Text}'을 {format} 형식으로 바꿀 수 없습니다.", Color.DarkOrange);
            return;
        }
        await SendAsync(() => Messages.S2F15(ids[0], value));
    }

    /// <summary>리포트 전부 지우기: 이벤트를 모두 끄고(S2F37 FALSE · 빈 목록) 리포트를 모두 지웁니다(S2F33 빈 목록).
    /// 호스트는 보통 이렇게 깨끗이 지운 뒤 자기 리포트를 정의합니다 — 이미 있는 RPTID를 다시 정의하면 DRACK 3.</summary>
    private async Task ClearReportsAsync()
    {
        await SendAsync(() => Messages.S2F37(false, Array.Empty<string>()));
        await SendAsync(Messages.S2F33DeleteAll);
    }

    private async Task SendCeidEnableAsync(bool enable)
    {
        IReadOnlyList<string> ceids = Messages.ParseIds(_txtCeids.Text);
        if (ceids.Any(c => !uint.TryParse(c, out _)))
        {
            AppendLine("CEID 칸에는 숫자만 띄어 써 주세요(예: 3001 3002).", Color.DarkOrange);
            return;
        }
        await SendAsync(() => Messages.S2F37(enable, ceids));
    }

    /// <summary>ECID 칸이 하나이고 장비가 알려 준 형식이 있으면 값 형식을 그에 맞춥니다.</summary>
    private void SyncEcvFormat()
    {
        var ids = Messages.ParseIds(_txtEcids.Text);
        if (ids.Count == 1 && Messages.EcvFormats.TryGetValue(ids[0], out var f))
        {
            var key = f == "ASCII" ? "A" : f;
            if (_cmbEcvFormat.Items.Contains(key))
            {
                _cmbEcvFormat.SelectedItem = key;
            }
        }
    }

    private void ShowIdFormat() => _lblIdFormat.Text = $"ID 형식 {Messages.IdFormat}";

    private async Task HandlePrimaryAsync(PrimaryMessageWrapper wrapper, CancellationToken cancellation)
    {
        var primary = wrapper.PrimaryMessage;
        try
        {
            if (!primary.ReplyExpected)
            {
                return;
            }
            if (!_chkAutoReply.Checked)
            {
                AppendLine($"           자동 응답 꺼짐 — S{primary.S}F{primary.F}에 응답하지 않습니다", Color.DarkOrange);
                return;
            }

            using var reply = Messages.AutoReply(primary);
            await wrapper.TryReplyAsync(reply, cancellation);
            if (primary.S == 1 && primary.F == 13)
            {
                SetCommunicating(true);
            }
        }
        catch (OperationCanceledException)
        {
            // 끊기
        }
        catch (SecsException ex)
        {
            AppendLine($"           ✗ 응답 실패: {ex.Message}", Color.Red);
        }
        finally
        {
            primary.Dispose();
        }
    }

    // ---- 상태 표시 ----------------------------------------------------------
    private void SetHsmsState(ConnectionState? state)
    {
        (_lblHsms.Text, _lblHsms.BackColor) = state switch
        {
            null => ("HSMS: 끊김", Color.Gainsboro),
            ConnectionState.Connecting => ("HSMS: Connecting", Color.Khaki),
            ConnectionState.Connected => ("HSMS: Connected", Color.Khaki),
            ConnectionState.Selected => ("HSMS: Selected", Color.PaleGreen),
            ConnectionState.Retry => ("HSMS: Retry", Color.LightSalmon),
            _ => ($"HSMS: {state}", Color.Gainsboro),
        };
    }

    private void SetCommunicating(bool on)
    {
        _lblComm.Text = on ? "GEM 통신: COMMUNICATING" : "GEM 통신: NOT COMMUNICATING";
        _lblComm.BackColor = on ? Color.PaleGreen : Color.Gainsboro;
    }

    private void SetScenarioEnabled(bool on)
    {
        foreach (var b in new[]
        {
            _btnS1F13, _btnS1F1, _btnS1F15, _btnS1F17, _btnS1F11, _btnS1F3, _btnS2F29, _btnS2F13, _btnS2F15,
            _btnReportClear, _btnS2F33, _btnS2F35, _btnS2F37On, _btnS2F37Off, _btnS5F3On, _btnS5F3Off,
            _btnS2F41,
        })
        {
            b.Enabled = on;
        }
    }

    private void SetEditable(bool on)
    {
        _txtIp.Enabled = _numPort.Enabled = _numDeviceId.Enabled = _rbActive.Enabled = _rbPassive.Enabled = on;
        _btnConnect.Enabled = on;
        _btnDisconnect.Enabled = !on;
    }

    // ---- 로그 ---------------------------------------------------------------
    internal static string Now() => DateTime.Now.ToString("HH:mm:ss.fff");

    // secs4net 스레드에서 읽으므로 체크 상태를 필드로 들고 있습니다.
    private volatile bool _showSml = true;
    private volatile bool _showDetail;

    internal bool ShowSml => _showSml;

    internal bool ShowDetail => _showDetail;

    internal void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    internal void AppendLine(string text, Color color, bool bold = false)
    {
        if (IsDisposed)
        {
            return;
        }
        _log.SelectionStart = _log.TextLength;
        _log.SelectionLength = 0;
        _log.SelectionColor = color;
        _log.SelectionFont = bold ? new Font(_log.Font, FontStyle.Bold) : _log.Font;
        _log.SelectedText = text + Environment.NewLine;
        _log.ScrollToCaret();
    }

    private void SaveLog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "텍스트 (*.txt)|*.txt",
            FileName = $"demohost-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, _log.Text, Encoding.UTF8);
        }
    }

    /// <summary>secs4net 로그를 화면 로그로 옮깁니다. secs4net은 다른 스레드에서 부르므로
    /// 문자열을 이 자리에서 만들고(메시지는 곧 해제될 수 있음) 화면 갱신만 UI 스레드로 넘깁니다.</summary>
    private sealed class HostLogger : ISecsGemLogger
    {
        private static readonly Color Sent = Color.FromArgb(0, 70, 170);
        private static readonly Color Received = Color.FromArgb(0, 120, 60);
        private static readonly Color Note = Color.FromArgb(120, 60, 160);
        private readonly MainForm _form;

        public HostLogger(MainForm form) => _form = form;

        public void MessageOut(SecsMessage msg, int id) => Write(msg, "H→E", Sent);

        public void MessageIn(SecsMessage msg, int id) => Write(msg, "E→H", Received);

        private void Write(SecsMessage msg, string direction, Color color)
        {
            var head = $"{Now()}  {direction}  S{msg.S}F{msg.F}{(msg.ReplyExpected ? " W" : "  ")}  {Messages.NameOf(msg.S, msg.F)}";
            var body = _form.ShowSml ? Sml(msg) : null;
            var note = Messages.Explain(msg);
            _form.Ui(() =>
            {
                _form.AppendLine(head, color, bold: true);
                if (!string.IsNullOrEmpty(body))
                {
                    _form.AppendLine(body, color);
                }
                if (note is not null)
                {
                    _form.AppendLine($"           → {note}", Note);
                }
            });
        }

        private static string? Sml(SecsMessage msg)
        {
            if (msg.SecsItem is null)
            {
                return null;
            }
            using var sw = new StringWriter();
            SmlWriter.Write(sw, msg.SecsItem, 11);
            return sw.ToString().TrimEnd();
        }

        public void Info(string msg)
        {
            // HSMS 제어 메시지(Select 등)는 항상, 나머지(타이머)는 상세 로그일 때만
            const string sent = "Sent Control Message: ";
            const string recv = "Receive Control message: ";
            if (msg.StartsWith(sent, StringComparison.Ordinal) || msg.StartsWith(recv, StringComparison.Ordinal))
            {
                var outgoing = msg.StartsWith(sent, StringComparison.Ordinal);
                var type = msg[(outgoing ? sent.Length : recv.Length)..];
                if (type.StartsWith("LinkTest", StringComparison.Ordinal) && !_form.ShowDetail)
                {
                    return;
                }
                var line = $"{Now()}  {(outgoing ? "H→E" : "E→H")}  [HSMS] {type}";
                _form.Ui(() => _form.AppendLine(line, Color.DimGray));
                return;
            }
            if (_form.ShowDetail)
            {
                var line = $"{Now()}  ...  {msg}";
                _form.Ui(() => _form.AppendLine(line, Color.Silver));
            }
        }

        public void Debug(string msg)
        {
            if (_form.ShowDetail)
            {
                var line = $"{Now()}  ...  {msg}";
                _form.Ui(() => _form.AppendLine(line, Color.Silver));
            }
        }

        public void Warning(string msg)
        {
            var line = $"{Now()}  !    {msg}";
            _form.Ui(() => _form.AppendLine(line, Color.DarkOrange));
        }

        public void Error(string msg, SecsMessage? message, Exception? ex)
        {
            var line = $"{Now()}  ✗    {msg}{(ex is null ? string.Empty : " — " + ex.Message)}";
            _form.Ui(() => _form.AppendLine(line, Color.Red));
        }
    }
}
