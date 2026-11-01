using Secs4Net;
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
        [(1, 13)] = "Establish Communications Request",
        [(1, 14)] = "Establish Communications Acknowledge",
        [(1, 15)] = "Request OFF-LINE",
        [(1, 16)] = "OFF-LINE Acknowledge",
        [(1, 17)] = "Request ON-LINE",
        [(1, 18)] = "ON-LINE Acknowledge",
        [(2, 17)] = "Date and Time Request",
        [(2, 18)] = "Date and Time Data",
        [(5, 1)] = "Alarm Report Send",
        [(5, 2)] = "Alarm Report Acknowledge",
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

    /// <summary>응답 코드를 사람이 읽는 문장으로. 해당 없으면 null.</summary>
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
                case (9, 1):
                    return "S9F1 = Device ID가 서로 다릅니다. 호스트와 장비의 Device ID를 맞추세요";
                case (9, 3):
                    return "S9F3 = 장비가 모르는 Stream입니다";
                case (9, 5):
                    return "S9F5 = 장비가 모르는 Function입니다";
                case (9, 7):
                    return "S9F7 = 메시지 본문 형식이 잘못됐습니다";
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
}
