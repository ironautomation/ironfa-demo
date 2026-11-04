using System;

namespace DemoEtcher
{
    /// <summary>
    /// 가상 식각 챔버. IronGEM과는 상관없는 '장비 자체'의 동작입니다.
    /// 온도는 설정 온도를 향해 천천히 따라가고(초당 최대 3℃), 압력은 온도에 따라 조금 변합니다.
    /// </summary>
    public sealed class Chamber
    {
        private readonly Random _random = new Random();

        /// <summary>챔버 온도 [degC] → SV 1001 ChamberTemp</summary>
        public double Temperature { get; private set; } = 25.0;

        /// <summary>챔버 압력 [mTorr] → SV 1002 ChamberPressure</summary>
        public double Pressure { get; private set; } = 2.0;

        /// <summary>공정 상태 → SV 1003 ProcessState (0 Idle — EP04부터 움직입니다)</summary>
        public byte ProcessState { get; private set; } = 0;

        /// <summary>설정 온도 [degC] ← ECV 2001 TempSetpoint (호스트가 S2F15로 바꿈)</summary>
        public double Setpoint { get; set; } = 60.0;

        public const double MaxRampPerSecond = 3.0;

        /// <summary>dt초만큼 시간을 진행합니다.</summary>
        public void Step(double dt)
        {
            var step = (Setpoint - Temperature) * dt / 8.0;
            var limit = MaxRampPerSecond * dt;
            step = Math.Max(-limit, Math.Min(limit, step));
            Temperature += step + Noise(0.05);

            Pressure = 2.0 + (Temperature - 25.0) * 0.002 + Noise(0.02);
        }

        private double Noise(double amplitude) => (_random.NextDouble() * 2.0 - 1.0) * amplitude;
    }
}
