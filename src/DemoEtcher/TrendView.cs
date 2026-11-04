using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DemoEtcher
{
    /// <summary>최근 2분간 챔버 온도(빨강)와 설정 온도(회색 점선)를 그립니다.</summary>
    public sealed class TrendView : Control
    {
        private readonly Queue<(double Temp, double Setpoint)> _points = new Queue<(double, double)>();
        private const int Capacity = 240; // 0.5초 × 240 = 2분
        private const double YMin = 0;
        private const double YMax = 220;

        public TrendView()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Font = new Font("Malgun Gothic", 8.5F);
        }

        public void Add(double temp, double setpoint)
        {
            _points.Enqueue((temp, setpoint));
            while (_points.Count > Capacity)
            {
                _points.Dequeue();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var area = new Rectangle(36, 6, Width - 44, Height - 22);

            using (var grid = new Pen(Color.Gainsboro))
            using (var label = new SolidBrush(Color.Gray))
            {
                for (var v = 0; v <= 200; v += 50)
                {
                    var y = Y(area, v);
                    g.DrawLine(grid, area.Left, y, area.Right, y);
                    g.DrawString(v.ToString(), Font, label, 2, y - 7);
                }
                g.DrawString("최근 2분 · ℃", Font, label, area.Right - 70, area.Bottom + 2);
            }

            if (_points.Count < 2)
            {
                return;
            }

            var temps = new List<PointF>();
            var sets = new List<PointF>();
            var i = 0;
            var offset = Capacity - _points.Count;
            foreach (var p in _points)
            {
                var x = area.Left + (float)(offset + i) / (Capacity - 1) * area.Width;
                temps.Add(new PointF(x, Y(area, p.Temp)));
                sets.Add(new PointF(x, Y(area, p.Setpoint)));
                i++;
            }

            using (var setPen = new Pen(Color.DimGray, 1.5f) { DashStyle = DashStyle.Dash })
            using (var tempPen = new Pen(Color.FromArgb(210, 40, 40), 2.2f))
            {
                g.DrawLines(setPen, sets.ToArray());
                g.DrawLines(tempPen, temps.ToArray());
            }
        }

        private static float Y(Rectangle area, double value)
        {
            var v = Math.Max(YMin, Math.Min(YMax, value));
            return (float)(area.Bottom - (v - YMin) / (YMax - YMin) * area.Height);
        }
    }
}
