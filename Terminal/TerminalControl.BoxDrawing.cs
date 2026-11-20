using System;
using Avalonia;
using Avalonia.Media;

namespace Snipyard.Terminal;

// Box-drawing characters (U+2500-U+257F) drawn as lines rather than taken from the font.
// A font's box glyphs meet their neighbours only when a row is exactly as tall as the font's
// ascent plus descent; with any line spacing on top, every vertical rule breaks up between
// rows. Drawn here, each line runs to the cell's edges whatever the row height and whichever
// font the terminal is set to.
public partial class TerminalControl
{
    private static bool IsBoxDrawing(int c) => c >= 0x2500 && c <= 0x257F;

    // Weight of the arm running from the cell's centre to each edge, in the order up, right,
    // down, left: 0 none, 1 light, 2 heavy, 3 double. Null where the character is drawn some
    // other way (dashes, arcs, diagonals).
    private static readonly string?[] BoxArms =
    {
        // 2500
        "0101", "0202", "1010", "2020", null, null, null, null,
        null, null, null, null, "0110", "0210", "0120", "0220",
        // 2510
        "0011", "0012", "0021", "0022", "1100", "1200", "2100", "2200",
        "1001", "1002", "2001", "2002", "1110", "1210", "2110", "1120",
        // 2520
        "2120", "2210", "1220", "2220", "1011", "1012", "2011", "1021",
        "2021", "2012", "1022", "2022", "0111", "0112", "0211", "0212",
        // 2530
        "0121", "0122", "0221", "0222", "1101", "1102", "1201", "1202",
        "2101", "2102", "2201", "2202", "1111", "1112", "1211", "1212",
        // 2540
        "2111", "1121", "2121", "2112", "2211", "1122", "1221", "2212",
        "1222", "2122", "2221", "2222", null, null, null, null,
        // 2550
        "0303", "3030", "0310", "0130", "0330", "0013", "0031", "0033",
        "1300", "3100", "3300", "1003", "3001", "3003", "1310", "3130",
        // 2560
        "3330", "1013", "3031", "3033", "0313", "0131", "0333", "1303",
        "3101", "3303", "1313", "3131", "3333", null, null, null,
        // 2570
        null, null, null, null, "0001", "1000", "0100", "0010",
        "0002", "2000", "0200", "0020", "0201", "1020", "0102", "2010",
    };

    private static void DrawBoxDrawing(DrawingContext ctx, int c, double x, double y, double w, double h, IBrush brush)
    {
        // Cell edges snapped to whole pixels. Columns start at fractions of a pixel, and a rule
        // ending there leaves the boundary pixel half covered by each neighbour; the two blends
        // come out lighter than a solid pixel, a faint seam at every column along a ─ border.
        // Rounding both edges the same way makes neighbours meet exactly.
        double right = Math.Round(x + w), bottom = Math.Round(y + h);
        x = Math.Round(x);
        y = Math.Round(y);
        w = right - x;
        h = bottom - y;

        double light = Math.Max(1, Math.Round(w / 8));
        double heavy = light * 2;
        // Whole pixels, so a rule lands on the same pixels in every row and column it crosses
        double cx = Math.Floor(x + w / 2), cy = Math.Floor(y + h / 2);

        if (BoxArms[c - 0x2500] is { } arms)
        {
            var box = new BoxCell(ctx, brush, x, y, w, h, cx, cy, light, heavy,
                arms[0] - '0', arms[1] - '0', arms[2] - '0', arms[3] - '0');
            box.Draw();
            return;
        }

        switch (c)
        {
            case 0x2504: DrawDashes(ctx, brush, x, y, w, h, cx, cy, false, 3, light); break;
            case 0x2505: DrawDashes(ctx, brush, x, y, w, h, cx, cy, false, 3, heavy); break;
            case 0x2506: DrawDashes(ctx, brush, x, y, w, h, cx, cy, true, 3, light); break;
            case 0x2507: DrawDashes(ctx, brush, x, y, w, h, cx, cy, true, 3, heavy); break;
            case 0x2508: DrawDashes(ctx, brush, x, y, w, h, cx, cy, false, 4, light); break;
            case 0x2509: DrawDashes(ctx, brush, x, y, w, h, cx, cy, false, 4, heavy); break;
            case 0x250A: DrawDashes(ctx, brush, x, y, w, h, cx, cy, true, 4, light); break;
            case 0x250B: DrawDashes(ctx, brush, x, y, w, h, cx, cy, true, 4, heavy); break;
            case 0x254C: DrawDashes(ctx, brush, x, y, w, h, cx, cy, false, 2, light); break;
            case 0x254D: DrawDashes(ctx, brush, x, y, w, h, cx, cy, false, 2, heavy); break;
            case 0x254E: DrawDashes(ctx, brush, x, y, w, h, cx, cy, true, 2, light); break;
            case 0x254F: DrawDashes(ctx, brush, x, y, w, h, cx, cy, true, 2, heavy); break;
            case >= 0x256D and <= 0x2570: DrawArc(ctx, brush, c, x, y, w, h, cx, cy, light); break;
            case 0x2571:
            case 0x2572:
            case 0x2573:
            {
                var pen = new Pen(brush, light);
                if (c != 0x2572) ctx.DrawLine(pen, new Point(x + w, y), new Point(x, y + h));
                if (c != 0x2571) ctx.DrawLine(pen, new Point(x, y), new Point(x + w, y + h));
                break;
            }
        }
    }

    /// <summary>Where a line of thickness <paramref name="t"/> centred on <paramref name="c"/> starts.</summary>
    private static double LineStart(double c, double t) => c - Math.Floor(t / 2);

    /// <summary>
    /// One cell of straight lines: up to four arms from the centre, each light, heavy or
    /// double. Arms are worked out one at a time; where they meet, each runs far enough into
    /// the junction to join the lines crossing it, so corners and tees close without gaps.
    /// </summary>
    private readonly record struct BoxCell(
        DrawingContext Ctx, IBrush Brush,
        double X, double Y, double W, double H,
        double Cx, double Cy, double Light, double Heavy,
        int Up, int Right, int Down, int Left)
    {
        // The two lines of a double, on the negative and the positive side of the centre line
        private static readonly int[] DoubleSides = { -1, +1 };

        // Distance from the centre line to each line of a double
        private double Gap => Light + Math.Max(1, Math.Round(Light / 2));

        private double Thickness(int weight) => weight switch { 0 => 0, 2 => Heavy, _ => Light };

        public void Draw()
        {
            // Vertical arms: their perpendiculars are left (the negative side) and right
            Arm(true, -1, Up, Down, Left, Right);
            Arm(true, +1, Down, Up, Left, Right);
            // Horizontal arms: their perpendiculars are up (the negative side) and down
            Arm(false, -1, Left, Right, Up, Down);
            Arm(false, +1, Right, Left, Up, Down);
        }

        /// <summary>
        /// Draws the arm running from the centre towards the negative (up/left, dir -1) or
        /// positive (down/right, dir +1) edge.
        /// </summary>
        private void Arm(bool vertical, int dir, int weight, int opposite, int perpNeg, int perpPos)
        {
            if (weight == 0) return;
            double along = vertical ? Cy : Cx;
            double across = vertical ? Cx : Cy;
            double edge = vertical ? (dir < 0 ? Y : Y + H) : (dir < 0 ? X : X + W);

            if (weight != 3)
            {
                double t = Thickness(weight);
                double end;
                if (perpNeg == 3 && perpPos == 3 && opposite == 0)
                    end = CoverLine(along + dir * Gap, Light, dir);   // a tee onto a double: stop at its near line
                else if ((perpNeg == 3) != (perpPos == 3))
                    end = CoverLine(along - dir * Gap, Light, dir);   // a corner onto a double: reach its far line
                else if (perpNeg == 3)
                    end = along;                                     // crossing a double: run through
                else
                {
                    double m = Math.Max(Thickness(perpNeg), Thickness(perpPos));
                    end = CoverLine(along, m > 0 ? m : t, dir);
                }
                Fill(vertical, edge, end, LineStart(across, t), t);
                return;
            }

            // Double: two light lines either side of the centre line, each stopping on its own
            foreach (int side in DoubleSides)
            {
                int same = side < 0 ? perpNeg : perpPos;
                int other = side < 0 ? perpPos : perpNeg;
                double end;
                if (same == 3)
                    end = CoverLine(along + dir * Gap, Light, dir);   // the inner corner of a double junction
                else if (same != 0)
                    end = CoverLine(along, Thickness(same), dir);     // meet the single line on this side
                else if (other == 3)
                    end = CoverLine(along - dir * Gap, Light, dir);   // the outer corner of a double junction
                else if (other != 0)
                    end = CoverLine(along, Thickness(other), dir);
                else
                    end = along;                                     // nothing crossing: run to the centre
                Fill(vertical, edge, end, LineStart(across + side * Gap, Light), Light);
            }
        }

        /// <summary>
        /// The coordinate an arm coming from the <paramref name="dir"/> side stops at so that it
        /// covers the line of thickness <paramref name="t"/> centred on <paramref name="c"/>.
        /// </summary>
        private static double CoverLine(double c, double t, int dir) =>
            dir < 0 ? LineStart(c, t) + t : LineStart(c, t);

        private void Fill(bool vertical, double from, double to, double crossStart, double thickness)
        {
            double a = Math.Min(from, to), b = Math.Max(from, to);
            Ctx.FillRectangle(Brush, vertical
                ? new Rect(crossStart, a, thickness, b - a)
                : new Rect(a, crossStart, b - a, thickness));
        }
    }

    /// <summary>
    /// A dashed rule split into <paramref name="count"/> dashes, each centred in an equal share
    /// of the cell, so the pattern carries on evenly into the next cell.
    /// </summary>
    private static void DrawDashes(DrawingContext ctx, IBrush brush, double x, double y, double w, double h,
        double cx, double cy, bool vertical, int count, double t)
    {
        double length = vertical ? h : w;
        double start = vertical ? y : x;
        double share = length / count;
        double gap = Math.Max(1, Math.Round(share * 0.35));
        for (int i = 0; i < count; i++)
        {
            double a = start + i * share + gap / 2;
            double dash = Math.Max(1, share - gap);
            ctx.FillRectangle(brush, vertical
                ? new Rect(LineStart(cx, t), a, t, dash)
                : new Rect(a, LineStart(cy, t), dash, t));
        }
    }

    /// <summary>The rounded corners ╭ ╮ ╯ ╰: a quarter circle joining two straight runs.</summary>
    private static void DrawArc(DrawingContext ctx, IBrush brush, int c, double x, double y, double w, double h,
        double cx, double cy, double t)
    {
        // The stroke is centred on its path, so the path runs down the middle of the line
        double lx = LineStart(cx, t) + t / 2, ly = LineStart(cy, t) + t / 2;
        double r = Math.Max(1, Math.Min(Math.Min(lx - x, x + w - lx), Math.Min(ly - y, y + h - ly)));

        // Each corner is walked so the turn is clockwise on screen
        (Point from, Point bendStart, Point bendEnd, Point to) = c switch
        {
            0x256D => (new Point(lx, y + h), new Point(lx, ly + r), new Point(lx + r, ly), new Point(x + w, ly)), // ╭
            0x256E => (new Point(x, ly), new Point(lx - r, ly), new Point(lx, ly + r), new Point(lx, y + h)),     // ╮
            0x256F => (new Point(lx, y), new Point(lx, ly - r), new Point(lx - r, ly), new Point(x, ly)),         // ╯
            _ => (new Point(x + w, ly), new Point(lx + r, ly), new Point(lx, ly - r), new Point(lx, y)),          // ╰
        };

        var geometry = new StreamGeometry();
        using (var gc = geometry.Open())
        {
            gc.BeginFigure(from, false);
            gc.LineTo(bendStart);
            gc.ArcTo(bendEnd, new Size(r, r), 0, false, SweepDirection.Clockwise);
            gc.LineTo(to);
            gc.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(brush, t), geometry);
    }
}
