using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Secs4Net;
using Secs4Net.Sml;
using static Secs4Net.Item;

namespace DemoHost;

/// <summary>
/// 호스트가 보내는 메시지, 장비 메시지에 대한 자동 응답, 로그에 붙일 설명.
/// 회차마다 여기에 메시지가 늘어납니다.
/// </summary>
internal static class Messages
{
    private static readonly Dictionary<(byte S, byte F), string> Names = new()
    {
        [(1, 1)] = "Are You There",
        [(1, 2)] = "On Line Data",
        [(1, 3)] = "Selected Equipment Status Request",
        [(1, 4)] = "Selected Equipment Status Data",
        [(1, 11)] = "Status Variable Namelist Request",
        [(1, 12)] = "Status Variable Namelist Reply",
        [(1, 13)] = "Establish Communications Request",
        [(1, 14)] = "Establish Communications Acknowledge",
        [(1, 15)] = "Request OFF-LINE",
        [(1, 16)] = "OFF-LINE Acknowledge",
        [(1, 17)] = "Request ON-LINE",
        [(1, 18)] = "ON-LINE Acknowledge",
        [(2, 13)] = "Equipment Constant Request",
        [(2, 14)] = "Equipment Constant Data",
        [(2, 15)] = "New Equipment Constant Send",
        [(2, 16)] = "New Equipment Constant Acknowledge",
        [(2, 17)] = "Date and Time Request",
        [(2, 18)] = "Date and Time Data",
        [(2, 29)] = "Equipment Constant Namelist Request",
        [(2, 30)] = "Equipment Constant Namelist",
        [(2, 33)] = "Define Report",
        [(2, 34)] = "Define Report Acknowledge",
        [(2, 35)] = "Link Event Report",
        [(2, 36)] = "Link Event Report Acknowledge",
        [(2, 37)] = "Enable/Disable Event Report",
        [(2, 38)] = "Enable/Disable Event Report Acknowledge",
        [(5, 1)] = "Alarm Report Send",
        [(5, 2)] = "Alarm Report Acknowledge",
        [(5, 3)] = "Enable/Disable Alarm Send",
        [(5, 4)] = "Enable/Disable Alarm Acknowledge",
        [(6, 11)] = "Event Report Send",
        [(6, 12)] = "Event Report Acknowledge",
        [(9, 1)] = "Unrecognized Device ID",
        [(9, 3)] = "Unrecognized Stream Type",
        [(9, 5)] = "Unrecognized Function Type",
        [(9, 7)] = "Illegal Data",
        [(9, 9)] = "Transaction Timer Timeout",
        [(9, 11)] = "Data Too Long",
        [(10, 1)] = "Terminal Request",
        [(10, 2)] = "Terminal Request Acknowledge",
    };

    public static string NameOf(byte s, byte f)
        => Names.TryGetValue((s, f), out var name) ? name : (f == 0 ? "Abort Transaction" : string.Empty);

    // ---- 호스트 → 장비 (EP02) ----------------------------------------------

    /// <summary>S1F13 W — 통신 확립 요청. 호스트는 빈 리스트를 보냅니다.</summary>
    public static SecsMessage S1F13() => new(1, 13) { Name = NameOf(1, 13), SecsItem = L() };

    /// <summary>S1F1 W — 장비 확인. 본문 없음(헤더만).</summary>
    public static SecsMessage S1F1() => new(1, 1) { Name = NameOf(1, 1) };

    /// <summary>S1F15 W — 오프라인 요청. 본문 없음.</summary>
    public static SecsMessage S1F15() => new(1, 15) { Name = NameOf(1, 15) };

    /// <summary>S1F17 W — 온라인 요청. 본문 없음.</summary>
    public static SecsMessage S1F17() => new(1, 17) { Name = NameOf(1, 17) };

    // ---- 호스트 → 장비 (EP03 · SV · ECV) -----------------------------------
    // ID 목록이 비어 있으면 빈 리스트 <L [0]>를 보냅니다 = "전부 주세요"(SEMI E5).

    /// <summary>S1F11 W — SV 이름 목록 요청.</summary>
    public static SecsMessage S1F11(IReadOnlyList<string> svids)
    {
        RequestedSvids = svids;
        return new(1, 11) { Name = NameOf(1, 11), SecsItem = IdList(svids) };
    }

    /// <summary>S1F3 W — SV 값 요청.</summary>
    public static SecsMessage S1F3(IReadOnlyList<string> svids)
    {
        RequestedSvids = svids;
        return new(1, 3) { Name = NameOf(1, 3), SecsItem = IdList(svids) };
    }

    /// <summary>S2F29 W — ECV 이름 목록 요청.</summary>
    public static SecsMessage S2F29(IReadOnlyList<string> ecids)
    {
        RequestedEcids = ecids;
        return new(2, 29) { Name = NameOf(2, 29), SecsItem = IdList(ecids) };
    }

    /// <summary>S2F13 W — ECV 값 요청.</summary>
    public static SecsMessage S2F13(IReadOnlyList<string> ecids)
    {
        RequestedEcids = ecids;
        return new(2, 13) { Name = NameOf(2, 13), SecsItem = IdList(ecids) };
    }

    /// <summary>S2F15 W — ECV 변경: &lt;L [1] &lt;L [2] ECID ECV&gt;&gt;.</summary>
    public static SecsMessage S2F15(string ecid, Item value)
    {
        RequestedEcids = new[] { ecid };
        return new(2, 15) { Name = NameOf(2, 15), SecsItem = L(L(Id(ecid), value)) };
    }

    // ---- 호스트 → 장비 (EP04 · 이벤트 · 리포트 · 알람) ----------------------
    // 형식은 IronGEM 기본 Data Item Dictionary와 같아야 합니다(다르면 S9F7):
    //   DATAID · RPTID · VID · ALID = U2 / CEID = U4 / CEED = BOOLEAN / ALED = B

    /// <summary>이 실습 호스트가 정의하는 리포트(RPTID → VID 목록).</summary>
    public static readonly (ushort Rptid, ushort[] Vids)[] Ep04Reports =
    {
        (4001, new ushort[] { 1001, 1002, 1003 }),  // 챔버 상태: 온도 · 압력 · 공정 상태
        (4002, new ushort[] { 1101 }),              // 공정 결과: 공정 시간(DVVAL)
    };

    /// <summary>이벤트 ↔ 리포트 링크(CEID → RPTID 목록). 리포트 하나를 여러 이벤트에 걸 수 있습니다.</summary>
    public static readonly (uint Ceid, ushort[] Rptids)[] Ep04Links =
    {
        (3001, new ushort[] { 4001 }),        // ProcessStarted
        (3002, new ushort[] { 4001, 4002 }),  // ProcessCompleted
    };

    /// <summary>화면에 보여 줄 한 줄 요약.</summary>
    public static string Ep04Summary =>
        "리포트 " + string.Join(" · ", Ep04Reports.Select(r => $"{r.Rptid} = {string.Join(" ", r.Vids)}")) +
        "      링크 " + string.Join(" · ", Ep04Links.Select(l => $"{l.Ceid} → {string.Join(" ", l.Rptids)}"));

    private static int _dataId;

    private static Item DataId() => U2((ushort)Interlocked.Increment(ref _dataId));

    /// <summary>S2F33 W — 리포트 전부 삭제: &lt;L DATAID &lt;L [0]&gt;&gt;. IronGEM은 링크도 함께 지웁니다.</summary>
    public static SecsMessage S2F33DeleteAll() => new(2, 33) { Name = NameOf(2, 33), SecsItem = L(DataId(), L()) };

    /// <summary>S2F33 W — 리포트 정의: &lt;L DATAID &lt;L &lt;L RPTID &lt;L VID…&gt;&gt;…&gt;&gt;.</summary>
    public static SecsMessage S2F33Define()
    {
        foreach (var r in Ep04Reports)
        {
            DefinedReports[r.Rptid] = r.Vids;
        }
        return new(2, 33)
        {
            Name = NameOf(2, 33),
            SecsItem = L(DataId(), L(Ep04Reports.Select(r => L(U2(r.Rptid), L(r.Vids.Select(v => U2(v)).ToArray()))).ToArray())),
        };
    }

    /// <summary>S2F35 W — 이벤트 링크: &lt;L DATAID &lt;L &lt;L CEID &lt;L RPTID…&gt;&gt;…&gt;&gt;.</summary>
    public static SecsMessage S2F35Link() => new(2, 35)
    {
        Name = NameOf(2, 35),
        SecsItem = L(DataId(), L(Ep04Links.Select(l => L(U4(l.Ceid), L(l.Rptids.Select(r => U2(r)).ToArray()))).ToArray())),
    };

    /// <summary>S2F37 W — 이벤트 켜기 · 끄기: &lt;L CEED &lt;L CEID…&gt;&gt;. CEID 목록이 비면 전부.</summary>
    public static SecsMessage S2F37(bool enable, IReadOnlyList<string> ceids) => new(2, 37)
    {
        Name = NameOf(2, 37),
        SecsItem = L(Item.Boolean(enable), L(ceids.Select(c => U4(uint.Parse(c, CultureInfo.InvariantCulture))).ToArray())),
    };

    /// <summary>S5F3 W — 알람 켜기 · 끄기: &lt;L ALED ALID&gt;. ALED 128(비트 8) = 켜기, 0 = 끄기.</summary>
    public static SecsMessage S5F3(bool enable, string alid) => new(5, 3)
    {
        Name = NameOf(5, 3),
        SecsItem = L(B((byte)(enable ? 0x80 : 0x00)), string.IsNullOrWhiteSpace(alid) ? U2(Array.Empty<ushort>()) : U2(ushort.Parse(alid.Trim(), CultureInfo.InvariantCulture))),
    };

    /// <summary>호스트가 정의한 리포트(S6F11 값에 이름을 붙이는 데 씀).</summary>
    public static readonly ConcurrentDictionary<int, ushort[]> DefinedReports = new();

    /// <summary>호스트가 알고 있는 이름. S1F12를 받으면 SvNames가 우선합니다.</summary>
    private static readonly Dictionary<string, string> KnownVidNames = new()
    {
        ["1001"] = "ChamberTemp",
        ["1002"] = "ChamberPressure",
        ["1003"] = "ProcessState",
        ["1101"] = "ProcessTime",
    };

    private static readonly Dictionary<long, string> CeidNames = new()
    {
        [100] = "Offline",
        [101] = "OnlineLocal",
        [102] = "OnlineRemote",
        [103] = "ECVChanged",
        [3001] = "ProcessStarted",
        [3002] = "ProcessCompleted",
    };

    private static readonly string[] AlarmCategories =
    {
        "분류 없음", "사람 안전", "장비 안전", "파라미터 경고", "파라미터 오류", "복구 불가 오류", "장비 상태 경고", "주의", "데이터 무결성",
    };

    // ---- ID · 값 형식 ------------------------------------------------------

    /// <summary>SVID · ECID를 보낼 때의 형식. IronGEM 기본 정의는 U2(형식이 다르면 S9F7).
    /// 장비 응답(S1F12 · S2F30)을 받으면 그 형식으로 자동으로 맞춥니다.</summary>
    public static volatile string IdFormat = "U2";

    /// <summary>장비가 알려 준 ECV 값 형식(ECID → U4 · F4 …). S2F30 · S2F14를 받으면 채워집니다.</summary>
    public static readonly ConcurrentDictionary<string, string> EcvFormats = new();

    /// <summary>S1F12 · S2F30에서 받은 이름표(ID → 이름 · 단위 …).</summary>
    public static readonly ConcurrentDictionary<string, string> SvNames = new();
    public static readonly ConcurrentDictionary<string, string> EcNames = new();

    /// <summary>ID 형식이 바뀌었을 때(화면 갱신용).</summary>
    public static event Action? FormatsLearned;

    private static IReadOnlyList<string> RequestedSvids = Array.Empty<string>();
    private static IReadOnlyList<string> RequestedEcids = Array.Empty<string>();

    public static readonly string[] ValueFormats = { "F4", "F8", "U1", "U2", "U4", "I1", "I2", "I4", "Boolean", "A" };

    public static IReadOnlyList<string> ParseIds(string text)
        => text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Item IdList(IReadOnlyList<string> ids) => L(ids.Select(Id).ToArray());

    private static Item Id(string id) => Value(IdFormat, id);

    /// <summary>문자열 값을 지정한 SECS 형식의 아이템으로. 형식에 맞지 않으면 FormatException.</summary>
    public static Item Value(string format, string text)
    {
        var inv = CultureInfo.InvariantCulture;
        text = text.Trim();
        return format switch
        {
            "A" => A(text),
            "Boolean" => Item.Boolean(text is "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase)),
            "U1" => U1(byte.Parse(text, inv)),
            "U2" => U2(ushort.Parse(text, inv)),
            "U4" => U4(uint.Parse(text, inv)),
            "U8" => U8(ulong.Parse(text, inv)),
            "I1" => I1(sbyte.Parse(text, inv)),
            "I2" => I2(short.Parse(text, inv)),
            "I4" => I4(int.Parse(text, inv)),
            "I8" => I8(long.Parse(text, inv)),
            "F4" => F4(float.Parse(text, inv)),
            "F8" => F8(double.Parse(text, inv)),
            _ => throw new FormatException($"지원하지 않는 형식: {format}"),
        };
    }

    // ---- 장비 → 호스트 메시지에 대한 자동 응답 -----------------------------

    /// <summary>장비가 보낸 1차 메시지에 대한 호스트의 응답. 모르는 메시지는 SxF0(Abort).</summary>
    public static SecsMessage AutoReply(SecsMessage primary) => (primary.S, primary.F) switch
    {
        (1, 1) => new SecsMessage(1, 2, false) { Name = NameOf(1, 2), SecsItem = L() },
        (1, 13) => new SecsMessage(1, 14, false) { Name = NameOf(1, 14), SecsItem = L(B(0), L()) }, // COMMACK 0
        (2, 17) => new SecsMessage(2, 18, false) { Name = NameOf(2, 18), SecsItem = A(DateTime.Now.ToString("yyyyMMddHHmmssff")) },
        (5, 1) => new SecsMessage(5, 2, false) { Name = NameOf(5, 2), SecsItem = B(0) },   // ACKC5 0
        (6, 11) => new SecsMessage(6, 12, false) { Name = NameOf(6, 12), SecsItem = B(0) }, // ACKC6 0
        (10, 1) => new SecsMessage(10, 2, false) { Name = NameOf(10, 2), SecsItem = B(0) }, // ACKC10 0
        _ => new SecsMessage(primary.S, 0, false) { Name = NameOf(primary.S, 0) },
    };

    // ---- 로그 설명 ---------------------------------------------------------

    /// <summary>응답 코드를 사람이 읽는 문장으로. 여러 줄일 수 있음. 해당 없으면 null.</summary>
    public static string? Explain(SecsMessage m)
    {
        try
        {
            switch (m.S, m.F)
            {
                case (_, 0):
                    return $"S{m.S}F0 = 장비가 이 요청을 처리하지 않고 돌려보냄(Abort). 오프라인일 때 흔합니다";
                case (1, 2):
                    return m.SecsItem is { Count: >= 2 } item
                        ? $"MDLN = {item[0].GetString()} · SOFTREV = {item[1].GetString()}"
                        : null;
                case (1, 4):
                    return ExplainValues(m.SecsItem, RequestedSvids, SvNames, "SV");
                case (1, 12):
                    return ExplainNamelist(m.SecsItem, SvNames, isEcv: false);
                case (1, 14):
                {
                    var ack = m.SecsItem?[0].FirstValueOrDefault<byte>(255);
                    return ack == 0 ? "COMMACK 0 = 통신 확립 수락 → COMMUNICATING" : $"COMMACK {ack} = 통신 확립 거부";
                }
                case (1, 16):
                {
                    var ack = m.SecsItem?.FirstValueOrDefault<byte>(255);
                    return ack == 0 ? "OFLACK 0 = 오프라인 전환 수락" : $"OFLACK {ack}";
                }
                case (1, 18):
                {
                    var ack = m.SecsItem?.FirstValueOrDefault<byte>(255);
                    return ack switch
                    {
                        0 => "ONLACK 0 = 온라인 전환 수락",
                        1 => "ONLACK 1 = 온라인 전환 거부(지금은 허용 안 됨)",
                        2 => "ONLACK 2 = 이미 온라인",
                        _ => $"ONLACK {ack}",
                    };
                }
                case (2, 14):
                    LearnEcvFormats(m.SecsItem, RequestedEcids);
                    return ExplainValues(m.SecsItem, RequestedEcids, EcNames, "ECV");
                case (2, 16):
                {
                    var eac = m.SecsItem?.FirstValueOrDefault<byte>(255);
                    return eac switch
                    {
                        0 => "EAC 0 = 변경 수락",
                        1 => "EAC 1 = 거부 — 없는 ECID가 있습니다",
                        2 => "EAC 2 = 거부 — 장비가 지금 바꿀 수 없음(장비 프로그램이 응답하지 않음 등)",
                        3 => "EAC 3 = 거부 — 범위(Min ~ Max)를 벗어난 값",
                        _ => $"EAC {eac} = 거부(장비 고유 코드)",
                    };
                }
                case (2, 30):
                    return ExplainNamelist(m.SecsItem, EcNames, isEcv: true);
                case (2, 34):
                {
                    var ack = m.SecsItem?.FirstValueOrDefault<byte>(255);
                    return ack switch
                    {
                        0 => "DRACK 0 = 리포트 정의 수락",
                        1 => "DRACK 1 = 거부 — 공간 부족",
                        2 => "DRACK 2 = 거부 — 형식 오류",
                        3 => "DRACK 3 = 거부 — 이미 정의된 RPTID(먼저 지우세요)",
                        4 => "DRACK 4 = 거부 — 장비에 없는 VID",
                        _ => $"DRACK {ack}",
                    };
                }
                case (2, 36):
                {
                    var ack = m.SecsItem?.FirstValueOrDefault<byte>(255);
                    return ack switch
                    {
                        0 => "LRACK 0 = 이벤트 링크 수락",
                        1 => "LRACK 1 = 거부 — 공간 부족",
                        2 => "LRACK 2 = 거부 — 형식 오류",
                        3 => "LRACK 3 = 거부 — 이미 링크된 CEID(먼저 링크를 지우세요)",
                        4 => "LRACK 4 = 거부 — 장비에 없는 CEID(IronStudio 저장 · IronGEM 재시작 확인)",
                        5 => "LRACK 5 = 거부 — 정의되지 않은 RPTID",
                        _ => $"LRACK {ack}",
                    };
                }
                case (2, 38):
                {
                    var ack = m.SecsItem?.FirstValueOrDefault<byte>(255);
                    return ack switch
                    {
                        0 => "ERACK 0 = 수락",
                        1 => "ERACK 1 = 거부 — 장비에 없는 CEID",
                        _ => $"ERACK {ack}",
                    };
                }
                case (5, 1):
                    return ExplainAlarm(m.SecsItem);
                case (5, 4):
                {
                    var ack = m.SecsItem?.FirstValueOrDefault<byte>(255);
                    return ack == 0 ? "ACKC5 0 = 수락" : $"ACKC5 {ack} = 거부(장비에 없는 ALID 등)";
                }
                case (6, 11):
                    return ExplainEvent(m.SecsItem);
                case (9, 1):
                    return "S9F1 = Device ID가 서로 다릅니다. 호스트와 장비의 Device ID를 맞추세요";
                case (9, 3):
                    return "S9F3 = 장비가 모르는 Stream입니다";
                case (9, 5):
                    return "S9F5 = 장비가 모르는 Function입니다";
                case (9, 7):
                    return "S9F7 = 메시지 본문 형식이 잘못됐습니다(SVID · ECID는 U2인지 확인하세요)";
                case (9, 9):
                    return "S9F9 = 응답이 T3 안에 오지 않았습니다";
            }
        }
        catch
        {
            // 형식이 예상과 다르면 설명을 생략합니다.
        }
        return null;
    }

    private const string Indent = "             ";

    /// <summary>S6F11: &lt;L DATAID CEID &lt;L &lt;L RPTID &lt;L V…&gt;&gt;…&gt;&gt;</summary>
    private static string? ExplainEvent(Item? item)
    {
        if (item is null || item.Format != SecsFormat.List || item.Count < 3)
        {
            return null;
        }
        var ceidText = ValueText(item[1]);
        long.TryParse(ceidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ceid);
        var name = CeidNames.TryGetValue(ceid, out var n) ? $" {n}" : string.Empty;
        var reports = item[2];
        var sb = new StringBuilder();
        sb.Append($"이벤트 CEID {ceidText}{name} · 리포트 {reports.Count}개");
        if (reports.Count == 0)
        {
            sb.Append(" (이 이벤트에 링크된 리포트가 없음 — S2F35)");
        }
        for (var i = 0; i < reports.Count; i++)
        {
            var report = reports[i];
            if (report.Format != SecsFormat.List || report.Count < 2)
            {
                continue;
            }
            var rptidText = ValueText(report[0]);
            int.TryParse(rptidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rptid);
            DefinedReports.TryGetValue(rptid, out var vids);
            sb.Append($"\n{Indent}리포트 {rptidText}");
            var values = report[1];
            for (var j = 0; j < values.Count; j++)
            {
                var vid = vids is not null && j < vids.Length ? vids[j].ToString(CultureInfo.InvariantCulture) : $"#{j + 1}";
                var label = SvNames.TryGetValue(vid, out var sv) ? $"{vid} {sv}" : KnownVidNames.TryGetValue(vid, out var kn) ? $"{vid} {kn}" : vid;
                var v = values[j];
                var text = IsEmpty(v) ? $"(값 없음 <{v.Format} [0]>)" : ValueText(v);
                if (vid == "1003")
                {
                    text += text == "1" ? " (Processing)" : text == "0" ? " (Idle)" : string.Empty;
                }
                sb.Append($"\n{Indent}  {label} = {text}");
            }
        }
        return sb.ToString();
    }

    /// <summary>S5F1: &lt;L ALCD ALID ALTX&gt;. ALCD 비트 8 = 발생(1) · 해제(0), 나머지 7비트 = 분류.</summary>
    private static string? ExplainAlarm(Item? item)
    {
        if (item is null || item.Format != SecsFormat.List || item.Count < 3)
        {
            return null;
        }
        var alcd = item[0].FirstValueOrDefault<byte>(0);
        var set = (alcd & 0x80) != 0;
        var category = alcd & 0x7F;
        var categoryText = category < AlarmCategories.Length ? AlarmCategories[category] : "장비 고유";
        return $"알람 {(set ? "발생" : "해제")} · ALID {ValueText(item[1])} · {ValueText(item[2])}" +
               $"\n{Indent}ALCD 0x{alcd:X2} = 비트 8 {(set ? "1(발생)" : "0(해제)")} + 분류 {category}({categoryText})";
    }

    /// <summary>S1F4 · S2F14: 요청한 ID 순서대로 값이 옵니다. 빈 값 &lt;L [0]&gt;은 장비가 모르는 ID.</summary>
    private static string? ExplainValues(Item? item, IReadOnlyList<string> requested, ConcurrentDictionary<string, string> names, string kind)
    {
        if (item is null || item.Format != SecsFormat.List)
        {
            return null;
        }
        var sb = new StringBuilder();
        sb.Append($"{kind} 값 {item.Count}개 (요청한 ID 순서대로)");
        for (var i = 0; i < item.Count; i++)
        {
            var id = i < requested.Count ? requested[i] : $"#{i + 1}";
            var label = names.TryGetValue(id, out var n) ? $"{id} {n}" : id;
            var v = item[i];
            var text = v.Format == SecsFormat.List && v.Count == 0
                ? "(빈 리스트 <L [0]> — 장비에 없는 ID)"
                : IsEmpty(v)
                    ? $"(값 없음 <{v.Format} [0]> — 아직 아무도 값을 넣지 않음)"
                    : $"{ValueText(v)}  ({v.Format})";
            sb.Append($"\n{Indent}{label} = {text}");
        }
        return sb.ToString();
    }

    /// <summary>S1F12: &lt;L &lt;L SVID SVNAME UNITS&gt;…&gt; · S2F30: &lt;L &lt;L ECID ECNAME ECMIN ECMAX ECDEF UNITS&gt;…&gt;</summary>
    private static string? ExplainNamelist(Item? item, ConcurrentDictionary<string, string> names, bool isEcv)
    {
        if (item is null || item.Format != SecsFormat.List)
        {
            return null;
        }
        var sb = new StringBuilder();
        sb.Append(isEcv ? $"ECV {item.Count}개 (ECID · 이름 · Min ~ Max · 기본값 · 단위)" : $"SV {item.Count}개 (SVID · 이름 · 단위)");
        for (var i = 0; i < item.Count; i++)
        {
            var row = item[i];
            if (row.Format != SecsFormat.List || row.Count < 3)
            {
                continue;
            }
            var id = ValueText(row[0]);
            LearnIdFormat(row[0].Format);
            var name = ValueText(row[1]);
            if (isEcv && row.Count >= 6)
            {
                var units = ValueText(row[5]);
                names[id] = name;
                if (!IsEmpty(row[4]))
                {
                    EcvFormats[id] = row[4].Format.ToString();
                }
                sb.Append($"\n{Indent}{id} {name} · {ValueText(row[2])} ~ {ValueText(row[3])} · 기본 {ValueText(row[4])} · {Unit(units)}");
            }
            else
            {
                var units = ValueText(row[2]);
                names[id] = name;
                sb.Append($"\n{Indent}{id} {name} · {Unit(units)}");
            }
        }
        FormatsLearned?.Invoke();
        return sb.ToString();
    }

    private static void LearnEcvFormats(Item? item, IReadOnlyList<string> requested)
    {
        if (item is null || item.Format != SecsFormat.List)
        {
            return;
        }
        for (var i = 0; i < item.Count && i < requested.Count; i++)
        {
            if (!IsEmpty(item[i]) && item[i].Format != SecsFormat.List)
            {
                EcvFormats[requested[i]] = item[i].Format.ToString();
            }
        }
        FormatsLearned?.Invoke();
    }

    private static void LearnIdFormat(SecsFormat format)
    {
        var f = format.ToString();
        if (f is "U1" or "U2" or "U4" or "U8" or "I1" or "I2" or "I4" or "I8" or "ASCII")
        {
            IdFormat = f == "ASCII" ? "A" : f;
        }
    }

    private static string Unit(string units) => string.IsNullOrWhiteSpace(units) ? "단위 없음" : units;

    private static bool IsEmpty(Item v) => v.Count == 0;

    private static readonly Regex SmlValue = new(@"^<\s*\w+\s*(\[\d+\])?\s*(?<v>.*?)\s*>$", RegexOptions.Singleline);

    /// <summary>아이템 하나를 짧은 글자로: 문자열은 그대로, 숫자는 SML 값 부분만.</summary>
    public static string ValueText(Item v)
    {
        if (v.Format is SecsFormat.ASCII or SecsFormat.JIS8)
        {
            return v.GetString().TrimEnd();
        }
        if (v.Format == SecsFormat.List)
        {
            return $"L[{v.Count}]";
        }
        using var sw = new StringWriter();
        SmlWriter.Write(sw, v, 0);
        var sml = sw.ToString().Trim();
        var m = SmlValue.Match(sml);
        return m.Success ? m.Groups["v"].Value.Trim('\'', '"') : sml;
    }
}
