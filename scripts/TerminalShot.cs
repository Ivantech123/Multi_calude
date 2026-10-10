// Renders lines of terminal output (with 24-bit color escapes) to a PNG that looks like
// a terminal window. Used by screenshots.ps1 for the README images.
//
// Block elements and box-drawing lines are drawn as shapes, so the mascot and the
// frames are crisp at any size; other symbols fall back to Segoe UI Symbol.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.RegularExpressions;

public static class TerminalShot
{
    const string Symbols = "❯✔✻✶✽✢★⌕※●▲";

    static readonly Color Background = Color.FromArgb(24, 24, 27);
    static readonly Color Chrome = Color.FromArgb(39, 39, 42);
    static readonly Color Foreground = Color.FromArgb(228, 228, 231);

    struct Cell
    {
        public char Char;
        public Color Color;
        public bool Bold;
    }

    public static void Render(string[] lines, int columns, string title, string path, float scale)
    {
        float fontSize = 15 * scale;
        using (var regular = new Font("Cascadia Mono", fontSize, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var bold = new Font("Cascadia Mono", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var symbol = new Font("Segoe UI Symbol", fontSize * 0.92f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var titleFont = new Font("Segoe UI", 13 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            float cw = MeasureCell(regular);
            float ch = (float)Math.Round(fontSize * 1.32f);
            float pad = 22 * scale, bar = 38 * scale;
            int width = (int)(columns * cw + pad * 2);
            int height = (int)(lines.Length * ch + pad * 2 + bar);

            using (var bitmap = new Bitmap(width, height))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                using (GraphicsPath window = Rounded(new RectangleF(0, 0, width - 1, height - 1), 12 * scale))
                using (var fill = new SolidBrush(Background))
                using (var edge = new Pen(Color.FromArgb(63, 63, 70), scale))
                {
                    g.FillPath(fill, window);
                    g.SetClip(window);
                    using (var chrome = new SolidBrush(Chrome)) g.FillRectangle(chrome, 0, 0, width, bar);
                    g.ResetClip();
                    g.DrawPath(edge, window);
                }
                Color[] dots = { Color.FromArgb(255, 95, 87), Color.FromArgb(254, 188, 46), Color.FromArgb(40, 200, 64) };
                for (int i = 0; i < 3; i++)
                {
                    using (var dot = new SolidBrush(dots[i]))
                        g.FillEllipse(dot, 16 * scale + i * 20 * scale, bar / 2 - 6 * scale, 12 * scale, 12 * scale);
                }
                using (var titleBrush = new SolidBrush(Color.FromArgb(161, 161, 170)))
                {
                    SizeF size = g.MeasureString(title, titleFont);
                    g.DrawString(title, titleFont, titleBrush, (width - size.Width) / 2, (bar - size.Height) / 2);
                }

                var format = StringFormat.GenericTypographic;
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                for (int row = 0; row < lines.Length; row++)
                {
                    List<Cell> cells = Parse(lines[row]);
                    float y = bar + pad + row * ch;
                    for (int col = 0; col < cells.Count && col < columns; col++)
                    {
                        Cell cell = cells[col];
                        if (cell.Char == ' ') continue;
                        var box = new RectangleF(pad + col * cw, y, cw, ch);
                        using (var brush = new SolidBrush(cell.Color))
                        using (var pen = new Pen(cell.Color, Math.Max(1, 1.1f * scale)))
                        {
                            if (DrawBlock(g, brush, box, cell.Char)) continue;
                            if (DrawLine(g, pen, box, cell.Char)) continue;
                            Font font = Symbols.IndexOf(cell.Char) >= 0 ? symbol : cell.Bold ? bold : regular;
                            float offset = font == symbol ? (ch - font.Height) / 2 : (ch - fontSize * 1.18f) / 2;
                            g.DrawString(cell.Char.ToString(), font, brush, box.X, y + offset, format);
                        }
                    }
                }
                bitmap.Save(path, ImageFormat.Png);
            }
        }
    }

    static float MeasureCell(Font font)
    {
        using (var bitmap = new Bitmap(10, 10))
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            return g.MeasureString(new string('M', 100), font, PointF.Empty, StringFormat.GenericTypographic).Width / 100;
        }
    }

    static List<Cell> Parse(string line)
    {
        var cells = new List<Cell>();
        Color color = Foreground;
        bool bold = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\u001b')
            {
                Match m = Regex.Match(line.Substring(i), @"^\u001b\[([0-9;]*)m");
                if (!m.Success) continue;
                string[] codes = m.Groups[1].Value.Split(';');
                for (int k = 0; k < codes.Length; k++)
                {
                    if (codes[k] == "0" || codes[k] == "") { color = Foreground; bold = false; }
                    else if (codes[k] == "1") bold = true;
                    else if (codes[k] == "38" && k + 4 < codes.Length && codes[k + 1] == "2")
                    {
                        color = Color.FromArgb(int.Parse(codes[k + 2]), int.Parse(codes[k + 3]), int.Parse(codes[k + 4]));
                        k += 4;
                    }
                }
                i += m.Length - 1;
                continue;
            }
            cells.Add(new Cell { Char = line[i], Color = color, Bold = bold });
        }
        return cells;
    }

    // Quadrant blocks, full and light shade, the thin text cursor.
    static bool DrawBlock(Graphics g, Brush brush, RectangleF box, char c)
    {
        int mask;
        switch (c)
        {
            case '▘': mask = 1; break;
            case '▝': mask = 2; break;
            case '▖': mask = 4; break;
            case '▗': mask = 8; break;
            case '▀': mask = 3; break;
            case '▄': mask = 12; break;
            case '▌': mask = 5; break;
            case '▐': mask = 10; break;
            case '▛': mask = 7; break;
            case '▜': mask = 11; break;
            case '▙': mask = 13; break;
            case '▟': mask = 14; break;
            case '█': mask = 15; break;
            case '░':
                using (var shade = new SolidBrush(Color.FromArgb(70, ((SolidBrush)brush).Color)))
                    g.FillRectangle(shade, box.X + 0.5f, box.Y + box.Height * 0.15f, box.Width - 1, box.Height * 0.7f);
                return true;
            case '▏':
                g.FillRectangle(brush, box.X, box.Y + 2, Math.Max(1.5f, box.Width / 8), box.Height - 4);
                return true;
            default: return false;
        }
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        float w = box.Width / 2, h = box.Height / 2;
        if ((mask & 1) != 0) g.FillRectangle(brush, box.X, box.Y, w + 0.6f, h + 0.6f);
        if ((mask & 2) != 0) g.FillRectangle(brush, box.X + w, box.Y, w + 0.6f, h + 0.6f);
        if ((mask & 4) != 0) g.FillRectangle(brush, box.X, box.Y + h, w + 0.6f, h + 0.6f);
        if ((mask & 8) != 0) g.FillRectangle(brush, box.X + w, box.Y + h, w + 0.6f, h + 0.6f);
        g.SmoothingMode = old;
        return true;
    }

    // Box-drawing lines with rounded corners, as in the Claude Code frames.
    static bool DrawLine(Graphics g, Pen pen, RectangleF box, char c)
    {
        float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
        float r = box.Width / 2;
        switch (c)
        {
            case '─': g.DrawLine(pen, box.X, cy, box.Right, cy); return true;
            case '│': g.DrawLine(pen, cx, box.Y, cx, box.Bottom); return true;
            case '╭':
                g.DrawArc(pen, cx, cy, 2 * r, 2 * r, 180, 90);
                g.DrawLine(pen, cx, cy + r, cx, box.Bottom);
                return true;
            case '╮':
                g.DrawArc(pen, cx - 2 * r, cy, 2 * r, 2 * r, 270, 90);
                g.DrawLine(pen, cx, cy + r, cx, box.Bottom);
                return true;
            case '╰':
                g.DrawArc(pen, cx, cy - 2 * r, 2 * r, 2 * r, 90, 90);
                g.DrawLine(pen, cx, box.Y, cx, cy - r);
                return true;
            case '╯':
                g.DrawArc(pen, cx - 2 * r, cy - 2 * r, 2 * r, 2 * r, 0, 90);
                g.DrawLine(pen, cx, box.Y, cx, cy - r);
                return true;
            default: return false;
        }
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
