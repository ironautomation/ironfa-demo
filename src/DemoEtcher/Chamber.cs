using System;

namespace DemoEtcher
{
    /// <summary>
    /// 가상 식각 챔버. IronGEM과는 상관없는 '장비 자체'의 동작입니다.
    /// 온도는 설정 온도를 향해 천천히 따라가고(초당 최대 3℃), 압력은 온도와 공정 여부에 따라 변합니다.
    /// </summary>
    public sealed class Chamber
    {
        private readonly Random _random = new Random();

        /// <summary>챔버 온도 [degC] → SV 1001 ChamberTemp</summary>
        public double Temperature { get; private set; } = 25.0;

        /// <summary>챔버 압력 [mTorr] → SV 1002 ChamberPressure</summary>
        public double Pressure { get; private set; } = 2.0;

        /// <summary>공정 상태 → SV 1003 ProcessState (0 Idle · 1 Processing)</summary>
        public byte ProcessState { get; private set; } = Idle;

        public const byte Idle = 0;
        public const byte Processing = 1;

        /// <summary>설정 온도 [degC] ← ECV 2001 TempSetpoint (호스트가 S2F15로 바꿈)</summary>
        public double Setpoint { get; set; } = 60.0;

        /// <summary>
        /// 히터 이상(과온) 시뮬레이션. 켜면 온도가 설정 온도보다 40℃ 높은 곳까지 올라갑니다.
        /// </summary>
        public bool HeaterFault { get; set; }

        /// <summary>공정 시간 [s]. 공정을 시작하면 0부터 셉니다.</summary>
        public double ProcessElapsed { get; private set; }

        public const double MaxRampPerSecond = 3.0;

        /// <summary>과온 판정: 설정 온도보다 15℃ 이상 높으면 발생, 5℃ 아래로 내려오면 해제(히스테리시스).</summary>
        public const double OverTempOn = 15.0;
        public const double OverTempOff = 5.0;

        public bool OverTemp { get; private set; }

        /// <summary>공정 시작. 이미 공정 중이면 false.</summary>
        public bool StartProcess()
        {
            if (ProcessState == Processing)
            {
                return false;
            }
            ProcessState = Processing;
            ProcessElapsed = 0;
            return true;
        }

        /// <summary>공정 끝. 공정 시간을 돌려줍니다.</summary>
        public double EndProcess()
        {
            ProcessState = Idle;
            return ProcessElapsed;
        }

        /// <summary>dt초만큼 시간을 진행합니다. 과온 상태가 바뀌면 true.</summary>
        public bool Step(double dt)
        {
            var target = HeaterFault ? Setpoint + 40.0 : Setpoint;
            var step = (target - Temperature) * dt / 8.0;
            var limit = MaxRampPerSecond * dt;
            step = Math.Max(-limit, Math.Min(limit, step));
            Temperature += step + Noise(0.05);

            // 공정 중에는 공정 가스가 들어가 압력이 20 mTorr 근처로 올라갑니다.
            var basePressure = ProcessState == Processing ? 20.0 : 2.0;
            var pressureTarget = basePressure + (Temperature - 25.0) * 0.002;
            Pressure += (pressureTarget - Pressure) * Math.Min(1.0, dt / 1.5) + Noise(0.02);

            if (ProcessState == Processing)
            {
                ProcessElapsed += dt;
            }

            var over = OverTemp
                ? Temperature > Setpoint + OverTempOff
                : Temperature > Setpoint + OverTempOn;
            var changed = over != OverTemp;
            OverTemp = over;
            return changed;
        }

        private double Noise(double amplitude) => (_random.NextDouble() * 2.0 - 1.0) * amplitude;
    }
}
