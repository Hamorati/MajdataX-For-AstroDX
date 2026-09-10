using MajSimai;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace MajdataEdit_Neo.Controls
{
    /// <summary>
    /// 圆形触区面板：按 touchspace 分布（外环 A/D、内环 B/E、中心 C）绘制 33 个触区，
    /// 并在面板上显示当前时间播放到的传感区 slide 的真实轨迹与星标位置。
    /// </summary>
    internal static class TouchSpacePanel
    {
        // ---- 归一化布局（半径 1 = 外环，0.45 = 内环），角度与 MajGeo/MajPos 一致 ----
        private static double BtnAngle(int button) => Math.PI * (5.0 / 8.0 - button / 4.0);

        private static (double x, double y) Polar(double r, double angle) =>
            (r * Math.Cos(angle), r * Math.Sin(angle));

        private static (double x, double y) SensorPos(char area, int idx)
        {
            switch (area)
            {
                case 'A': return Polar(1.0, BtnAngle(idx));
                case 'B': return Polar(0.45, BtnAngle(idx));
                case 'C': return (0, 0);
                case 'D': return Polar(1.0, BtnAngle(idx) + Math.PI / 8);
                case 'E': return Polar(0.45, BtnAngle(idx) + Math.PI / 8);
                default: return (0, 0);
            }
        }

        // ---- 面板几何 ----
        public static void Draw(
            SKCanvas canvas,
            SKPoint center,
            float radius,
            SimaiNote? activeSlide,
            double progress)
        {
            using var paint = new SKPaint { IsAntialias = true };

            // 半透明底板
            paint.Style = SKPaintStyle.Fill;
            paint.Color = new SKColor(0x33, 0x33, 0x33, 0xC0);
            canvas.DrawCircle(center, radius * 1.12f, paint);

            // 环线
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1;
            paint.Color = new SKColor(0x88, 0x88, 0x88);
            canvas.DrawCircle(center, radius, paint);
            canvas.DrawCircle(center, radius * 0.45f, paint);

            // 33 个触区点
            paint.Style = SKPaintStyle.Fill;
            for (var n = 1; n <= 8; n++)
            {
                DrawDot(canvas, center, radius, SensorPos('A', n), 2.6f, new SKColor(0x4F, 0xC3, 0xF7), paint);
                DrawDot(canvas, center, radius, SensorPos('B', n), 2.6f, new SKColor(0x02, 0x77, 0xBD), paint);
                DrawDot(canvas, center, radius, SensorPos('D', n), 2.6f, new SKColor(0xFF, 0xB3, 0x00), paint);
                DrawDot(canvas, center, radius, SensorPos('E', n), 2.6f, new SKColor(0x66, 0xBB, 0x6A), paint);
            }
            DrawDot(canvas, center, radius, SensorPos('C', 1), 3.2f, new SKColor(0xF0, 0xF0, 0xF0), paint);

            // 8 个按钮（乳白色圆环）
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 2;
            paint.Color = new SKColor(0xF0, 0xD0, 0x90);
            for (var n = 1; n <= 8; n++)
            {
                var (x, y) = SensorPos('A', n);
                var p = ToCanvas(center, radius, x, y);
                canvas.DrawCircle(p, 6.5f, paint);
            }

            if (activeSlide is null || !TryParseSensorSlidePath(activeSlide.RawContent, activeSlide, out var nodes, out var shapes))
                return;

            // 轨迹折线（环形状用圆弧采样近似）
            using var path = new SKPath();
            var prev = nodes[0];
            path.MoveTo(ToCanvas(center, radius, prev.x, prev.y));
            for (var i = 1; i < nodes.Count; i++)
            {
                var cur = nodes[i];
                var shape = i - 1 < shapes.Count ? shapes[i - 1] : '-';
                if (shape is '^' or '>' or '<' && prev.r > 0.01 && cur.r > 0.01)
                {
                    AddArcApprox(path, center, radius, prev, cur, shape);
                }
                else if (IsCurveShape(shape))
                {
                    AddCurveApprox(path, center, radius, prev, cur, shape);
                }
                else
                {
                    path.LineTo(ToCanvas(center, radius, cur.x, cur.y));
                }
                prev = cur;
            }
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 2f;
            paint.Color = new SKColor(0xE0, 0xE0, 0xE0);
            canvas.DrawPath(path, paint);

            // 节点小点
            paint.Style = SKPaintStyle.Fill;
            paint.Color = new SKColor(0xFF, 0xFF, 0xFF, 0xCC);
            foreach (var nd in nodes)
                canvas.DrawCircle(ToCanvas(center, radius, nd.x, nd.y), 2.2f, paint);

            // 头部标记：触区锚定 = 方块 + 内部星形（对应 ViewX 的 slide 头 Touch 特殊皮肤）；
            // 按钮锚定 = 星形
            var head = ToCanvas(center, radius, nodes[0].x, nodes[0].y);
            paint.Color = new SKColor(0xFF, 0xE0, 0x82);
            if (activeSlide.TouchArea != ' ')
            {
                canvas.DrawRect(head.X - 3.5f, head.Y - 3.5f, 7, 7, paint);
                paint.Color = new SKColor(0xFF, 0xFF, 0xFF);
                DrawStar(canvas, head, 2.4f, paint);
            }
            else
            {
                DrawStar(canvas, head, 6f, paint);
            }

            // 星标当前位置
            var starPos = PointAtProgress(nodes, shapes, Math.Clamp(progress, 0, 1));
            paint.Color = new SKColor(0x8F, 0xF5, 0xFF);
            canvas.DrawCircle(ToCanvas(center, radius, starPos.x, starPos.y), 3.6f, paint);
        }

        private static void DrawDot(SKCanvas canvas, SKPoint center, float radius, (double x, double y) pos,
            float dotRadius, SKColor color, SKPaint paint)
        {
            paint.Color = color;
            canvas.DrawCircle(ToCanvas(center, radius, pos.x, pos.y), dotRadius, paint);
        }

        private static void DrawStar(SKCanvas canvas, SKPoint at, float r, SKPaint paint)
        {
            var r2 = r * 1.414f / 2f;
            canvas.DrawLine(at.X - r2, at.Y - r2, at.X + r2, at.Y + r2, paint);
            canvas.DrawLine(at.X + r2, at.Y - r2, at.X - r2, at.Y + r2, paint);
            canvas.DrawLine(at.X, at.Y - r, at.X, at.Y + r, paint);
            canvas.DrawLine(at.X - r, at.Y, at.X + r, at.Y, paint);
        }

        private static void AddArcApprox(SKPath path, SKPoint center, float radius,
            PanelNode from, PanelNode to, char shape)
        {
            var isCw = shape switch
            {
                '>' => !((from.button0 + 2) % 8 >= 4),
                '<' => (from.button0 + 2) % 8 >= 4,
                _ => ShortestIsCw(from.button0, to.button0),
            };
            // AstroDX Ring 滑条：角度与半径同时线性插值（平滑键入/退出触区）
            var span = RingSpan(from, to, isCw);
            var steps = 14;
            for (var k = 1; k <= steps; k++)
            {
                var t = k / (double)steps;
                var r = from.r + (to.r - from.r) * t;
                var a = isCw ? from.a - span * t : from.a + span * t;
                var (x, y) = Polar(r, a);
                path.LineTo(ToCanvas(center, radius, x, y));
            }
            path.LineTo(ToCanvas(center, radius, to.x, to.y));
        }

        private static bool ShortestIsCw(int fromBtn0, int toBtn0)
        {
            var diff = toBtn0 - fromBtn0;
            var rotation = diff >= 0 ? (diff > 4 ? -1 : 1) : (diff < -4 ? 1 : -1);
            return rotation > 0;
        }

        private static bool IsCurveShape(char shape) => shape is 'p' or 'q' or 'P' or 'Q';
        private static bool IsEdgeCurve(char shape) => shape is 'P' or 'Q';
        private static bool CurveIsCw(char shape) => shape is 'q' or 'Q';

        private static double VertexAngleOf(PanelNode n) =>
            n.r <= 0.001 ? BtnAngle(1) : n.a;

        /// <summary>AstroDX Curve/EdgeCurve 几何（归一化面板坐标）：外圈顶点角度 → 切线 → 圆 → 切线 → 外圈终点。</summary>
        private static (double sx, double sy, double txIn, double tyIn, double txOut, double tyOut,
            double ex, double ey, double curveR, double span, bool isCw, double tanInAngle, double ccx, double ccy)
            CurveGeom(PanelNode from, PanelNode to, char shape)
        {
            var isEdge = IsEdgeCurve(shape);
            var isCw = CurveIsCw(shape);
            const double ringR = 1.0;
            var curveR = isEdge ? Math.Cos(3 * Math.PI / 8) * 1.2 : Math.Cos(3 * Math.PI / 8);
            var startAngle = VertexAngleOf(from);
            var endAngle = VertexAngleOf(to);

            var (sx, sy) = Polar(ringR, startAngle);
            var (ex, ey) = Polar(ringR, endAngle);

            double ccx = 0, ccy = 0;
            if (isEdge)
            {
                // AstroDX CenterAngularOffset = Tau/4 - Tau/16 = π/2 - π/8 = 3π/8（不是 3π/16）
                var off = Math.PI / 2.0 - Math.PI / 8.0;
                var ca = isCw ? startAngle + off : startAngle - off;
                (ccx, ccy) = Polar(0.4662, ca);
            }

            var rsx = sx - ccx;
            var rsy = sy - ccy;
            var startMag = Math.Sqrt(rsx * rsx + rsy * rsy);
            var startDelta = Math.Acos(Math.Min(1.0, curveR / startMag));
            var tanIn = Math.Atan2(rsy, rsx) + (isCw ? -startDelta : startDelta);
            var txIn = ccx + curveR * Math.Cos(tanIn);
            var tyIn = ccy + curveR * Math.Sin(tanIn);

            var rex = ex - ccx;
            var rey = ey - ccy;
            var endMag = Math.Sqrt(rex * rex + rey * rey);
            var endDelta = Math.Acos(Math.Min(1.0, curveR / endMag));
            var tanOut = Math.Atan2(rey, rex) + (isCw ? endDelta : -endDelta);
            var txOut = ccx + curveR * Math.Cos(tanOut);
            var tyOut = ccy + curveR * Math.Sin(tanOut);

            var span = isCw ? tanIn - tanOut : tanOut - tanIn;
            span = Math.IEEERemainder(span, 2 * Math.PI);
            if (span < 0) span += 2 * Math.PI;
            var wrap = isEdge ? Math.PI / 4.0 : Math.PI / 16.0;
            if (span <= wrap) span += 2 * Math.PI;

            return (sx, sy, txIn, tyIn, txOut, tyOut, ex, ey, curveR, span, isCw, tanIn, ccx, ccy);
        }

        private static void AddCurveApprox(SKPath path, SKPoint center, float radius,
            PanelNode from, PanelNode to, char shape)
        {
            var g = CurveGeom(from, to, shape);
            path.LineTo(ToCanvas(center, radius, g.sx, g.sy));
            path.LineTo(ToCanvas(center, radius, g.txIn, g.tyIn));
            var steps = 14;
            for (var k = 1; k <= steps; k++)
            {
                var t = k / (double)steps;
                var a = g.isCw ? g.tanInAngle - g.span * t : g.tanInAngle + g.span * t;
                var (x, y) = (g.ccx + g.curveR * Math.Cos(a), g.ccy + g.curveR * Math.Sin(a));
                path.LineTo(ToCanvas(center, radius, x, y));
            }
            path.LineTo(ToCanvas(center, radius, g.ex, g.ey));
        }

        /// <summary>环向角度跨度（含 Tau/32 wrap 加成，与 AstroDX GetAngleSpan 一致）。</summary>
        private static double RingSpan(PanelNode from, PanelNode to, bool isCw)
        {
            var span = from.a - to.a;
            if (isCw)
            {
                if (span <= 0) span += 2 * Math.PI;
            }
            else
            {
                if (span >= 0) span -= 2 * Math.PI;
                span = -span;
            }
            if (span <= Math.PI / 16.0) span += 2 * Math.PI;
            return span;
        }

        private static SKPoint ToCanvas(SKPoint center, float radius, double x, double y) =>
            new(center.X + (float)(x * radius), center.Y - (float)(y * radius));

        // ---- 解析（与 ViewX 的传感区 slide 解析保持一致） ----

        private readonly struct PanelNode
        {
            public readonly double x, y, r, a;
            public readonly int button0;   // 0-based 键位（C→0）
            public PanelNode(double x, double y, double r, double a, int button0)
            {
                this.x = x; this.y = y; this.r = r; this.a = a; this.button0 = button0;
            }
        }

        private static bool TryParseSensorSlidePath(
            string raw,
            SimaiNote note,
            out List<PanelNode> nodes,
            out List<char> shapes)
        {
            nodes = new List<PanelNode>();
            shapes = new List<char>();
            if (string.IsNullOrEmpty(raw)) return false;

            var i = 0;
            char area;
            int idx;
            if (raw[0] is >= 'A' and <= 'E')
            {
                area = raw[0];
                i = 1;
                if (area != 'C')
                {
                    if (i >= raw.Length || raw[i] is < '1' or > '8') return false;
                    idx = raw[i] - '0';
                    i++;
                }
                else
                {
                    idx = 1;
                    if (i < raw.Length && char.IsDigit(raw[i])) i++;
                }
            }
            else if (raw[0] is >= '1' and <= '8')
            {
                area = 'A';
                idx = raw[0] - '0';
                i = 1;
            }
            else return false;
            nodes.Add(MakeNode(area, idx));

            while (i < raw.Length)
            {
                var c = raw[i];
                if (c == '[')
                {
                    var close = raw.IndexOf(']', i);
                    if (close < 0) break;
                    i = close + 1;
                    continue;
                }
                if (c is 'f' or 'h') { i++; continue; }
                if (c is '-' or '^' or '>' or '<' or 'v' or 'V' or 'p' or 'q' or 's' or 'z' or 'w')
                {
                    var shape = c;
                    var isDouble = false;
                    i++;
                    // pp/qq 双字符形状（与 SimaiSharp 词法一致）
                    if (shape is 'p' or 'q' && i < raw.Length && raw[i] == shape)
                    {
                        isDouble = true;
                        i++;
                    }
                    if (shape == 'V')
                    {
                        if (!ReadLocation(raw, ref i, out var flexArea, out var flexIdx)) return false;
                        nodes.Add(MakeNode(flexArea, flexIdx));
                        shapes.Add('-');
                    }
                    if (!ReadLocation(raw, ref i, out var endArea, out var endIdx)) return false;
                    nodes.Add(MakeNode(endArea, endIdx));
                    var storedShape = shape == 'V' ? '-' :
                        shape is 'p' or 'q' && isDouble ? char.ToUpperInvariant(shape) : shape;
                    shapes.Add(storedShape);
                    continue;
                }
                i++;
            }
            return nodes.Count > 1;
        }

        private static bool ReadLocation(string raw, ref int i, out char area, out int idx)
        {
            area = '\0';
            idx = 0;
            if (i >= raw.Length) return false;
            if (raw[i] is >= '1' and <= '8')
            {
                area = 'A';
                idx = raw[i] - '0';
                i++;
                return true;
            }
            if (raw[i] is >= 'A' and <= 'E')
            {
                area = raw[i];
                i++;
                if (area == 'C')
                {
                    idx = 1;
                    if (i < raw.Length && char.IsDigit(raw[i])) i++;
                    return true;
                }
                if (i >= raw.Length || raw[i] is < '1' or > '8') return false;
                idx = raw[i] - '0';
                i++;
                return true;
            }
            return false;
        }

        private static PanelNode MakeNode(char area, int idx)
        {
            var (x, y) = SensorPos(area, idx);
            var r = area is 'A' or 'D' ? 1.0 : area is 'C' ? 0.0 : 0.45;
            var a = Math.Atan2(y, x);
            var btn = idx - 1;
            if (area == 'C') btn = 0;
            return new PanelNode(x, y, r, a, btn);
        }

        private static PanelNode PointAtProgress(List<PanelNode> nodes, List<char> shapes, double t)
        {
            if (nodes.Count <= 1) return nodes[0];
            var segLens = new double[nodes.Count - 1];
            var total = 0.0;
            for (var k = 0; k < segLens.Length; k++)
            {
                segLens[k] = SegmentLength(nodes[k], nodes[k + 1], k < shapes.Count ? shapes[k] : '-');
                total += segLens[k];
            }
            var target = total * t;
            var acc = 0.0;
            for (var k = 0; k < segLens.Length; k++)
            {
                if (target <= acc + segLens[k] || k == segLens.Length - 1)
                {
                    var lt = segLens[k] <= 0 ? 0 : (target - acc) / segLens[k];
                    return LerpNode(nodes[k], nodes[k + 1], k < shapes.Count ? shapes[k] : '-', lt);
                }
                acc += segLens[k];
            }
            return nodes[^1];
        }

        private static double SegmentLength(PanelNode from, PanelNode to, char shape)
        {
            if (shape is '^' or '>' or '<' && from.r > 0.01 && to.r > 0.01)
            {
                var isCw = shape switch
                {
                    '>' => !((from.button0 + 2) % 8 >= 4),
                    '<' => (from.button0 + 2) % 8 >= 4,
                    _ => ShortestIsCw(from.button0, to.button0),
                };
                // AstroDX 螺旋长度公式：角度跨度 × 平均半径
                return RingSpan(from, to, isCw) * (from.r + to.r) / 2.0;
            }
            if (IsCurveShape(shape))
            {
                var g = CurveGeom(from, to, shape);
                var jump = Math.Sqrt((g.sx - from.x) * (g.sx - from.x) + (g.sy - from.y) * (g.sy - from.y));
                var startLen = Math.Sqrt((g.txIn - g.sx) * (g.txIn - g.sx) + (g.tyIn - g.sy) * (g.tyIn - g.sy));
                var endLen = Math.Sqrt((g.ex - g.txOut) * (g.ex - g.txOut) + (g.ey - g.tyOut) * (g.ey - g.tyOut));
                return jump + startLen + g.span * g.curveR + endLen;
            }
            return Math.Sqrt((to.x - from.x) * (to.x - from.x) + (to.y - from.y) * (to.y - from.y));
        }

        private static PanelNode LerpNode(PanelNode from, PanelNode to, char shape, double t)
        {
            t = Math.Clamp(t, 0, 1);
            if (shape is '^' or '>' or '<' && from.r > 0.01 && to.r > 0.01)
            {
                var isCw = shape switch
                {
                    '>' => !((from.button0 + 2) % 8 >= 4),
                    '<' => (from.button0 + 2) % 8 >= 4,
                    _ => ShortestIsCw(from.button0, to.button0),
                };
                var span = RingSpan(from, to, isCw);
                var r = from.r + (to.r - from.r) * t;
                var a = isCw ? from.a - span * t : from.a + span * t;
                var (x, y) = Polar(r, a);
                return new PanelNode(x, y, r, a, from.button0);
            }
            if (IsCurveShape(shape))
            {
                var g = CurveGeom(from, to, shape);
                var jump = Math.Sqrt((g.sx - from.x) * (g.sx - from.x) + (g.sy - from.y) * (g.sy - from.y));
                var startLen = Math.Sqrt((g.txIn - g.sx) * (g.txIn - g.sx) + (g.tyIn - g.sy) * (g.tyIn - g.sy));
                var curveLen = g.span * g.curveR;
                var endLen = Math.Sqrt((g.ex - g.txOut) * (g.ex - g.txOut) + (g.ey - g.tyOut) * (g.ey - g.tyOut));
                var total = jump + startLen + curveLen + endLen;
                var d = total * t;
                double x, y;
                if (d < jump)
                {
                    var lt = jump <= 0 ? 0 : d / jump;
                    x = from.x + (g.sx - from.x) * lt;
                    y = from.y + (g.sy - from.y) * lt;
                }
                else if (d < jump + startLen)
                {
                    var lt = startLen <= 0 ? 0 : (d - jump) / startLen;
                    x = g.sx + (g.txIn - g.sx) * lt;
                    y = g.sy + (g.tyIn - g.sy) * lt;
                }
                else if (d < jump + startLen + curveLen)
                {
                    var lt = curveLen <= 0 ? 0 : (d - jump - startLen) / curveLen;
                    var a = g.isCw ? g.tanInAngle - g.span * lt : g.tanInAngle + g.span * lt;
                    x = g.ccx + g.curveR * Math.Cos(a);
                    y = g.ccy + g.curveR * Math.Sin(a);
                }
                else
                {
                    var lt = endLen <= 0 ? 0 : (d - jump - startLen - curveLen) / endLen;
                    x = g.txOut + (g.ex - g.txOut) * lt;
                    y = g.tyOut + (g.ey - g.tyOut) * lt;
                }
                var r = Math.Sqrt(x * x + y * y);
                var ang = Math.Atan2(y, x);
                return new PanelNode(x, y, r, ang, from.button0);
            }
            return new PanelNode(
                from.x + (to.x - from.x) * t,
                from.y + (to.y - from.y) * t,
                from.r + (to.r - from.r) * t,
                from.a + (to.a - from.a) * t,
                from.button0);
        }
    }
}
