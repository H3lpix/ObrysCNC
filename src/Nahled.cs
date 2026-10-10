using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace ObrysCNC
{
    // koty (meritko): sirka pod obrysem, vyska vpravo; volitelne vzdalenost od okraje desky (pocatek 0,0)
    public static class Koty
    {
        static readonly Color Zelena = Color.FromArgb(0, 150, 90);
        static readonly Color Hneda = Color.FromArgb(120, 80, 40);

        public static void Kresli(Graphics g, Font font, float k, Func<P, PointF> S, IEnumerable<Obrys> obrysy, bool odDesky)
        {
            Kresli(g, font, k, S, obrysy, odDesky, false, 0);
        }

        public static void Kresli(Graphics g, Font font, float k, Func<P, PointF> S, IEnumerable<Obrys> obrysy, bool odDesky, bool yNahore, double vyskaDesky)
        {
            var seznam = obrysy.Where(o => o.Pts.Count > 1).ToList();
            if (seznam.Count == 0) return;
            double minx, miny, maxx, maxy;
            Geo.Rozsah(seznam, out minx, out miny, out maxx, out maxy);
            if (maxx <= minx) return;
            PointF lb = S(new P(minx, miny)), rt = S(new P(maxx, maxy)), o0 = S(new P(0, 0));
            float yk = lb.Y + 26 * k, xk = rt.X + 26 * k;
            Vodorovna(g, font, k, lb.X, rt.X, lb.Y, yk, Mm(maxx - minx), Zelena);
            Svisla(g, font, k, rt.Y, lb.Y, rt.X, xk, Mm(maxy - miny), Zelena);
            if (!odDesky) return;
            if (Math.Abs(minx) > 0.05) Vodorovna(g, font, k, o0.X, lb.X, float.NaN, yk, "X " + Mm(minx), Hneda);
            double odstupY = yNahore ? vyskaDesky - maxy : miny;
            if (Math.Abs(odstupY) > 0.05) Svisla(g, font, k, yNahore ? rt.Y : lb.Y, yNahore ? S(new P(0, vyskaDesky)).Y : o0.Y, float.NaN, xk, "Y " + Mm(odstupY), Hneda);
        }

        static string Mm(double v) { return v.ToString("0.#") + " mm"; }

        static void Vodorovna(Graphics g, Font font, float k, float x1, float x2, float yObjekt, float y, string text, Color c)
        {
            if (Math.Abs(x2 - x1) < 2) return;
            using (var pen = new Pen(c, 1.2f * k))
            using (var tenke = new Pen(Color.FromArgb(120, c), 1))
            {
                if (!float.IsNaN(yObjekt)) { g.DrawLine(tenke, x1, yObjekt + 4 * k, x1, y + 5 * k); g.DrawLine(tenke, x2, yObjekt + 4 * k, x2, y + 5 * k); }
                g.DrawLine(pen, x1, y, x2, y);
                g.DrawLine(pen, x1, y - 5 * k, x1, y + 5 * k);
                g.DrawLine(pen, x2, y - 5 * k, x2, y + 5 * k);
            }
            var vel = TextRenderer.MeasureText(text, font);
            TextRenderer.DrawText(g, text, font, new Point((int)((x1 + x2) / 2 - vel.Width / 2), (int)(y + 3 * k)), c);
        }

        static void Svisla(Graphics g, Font font, float k, float y1, float y2, float xObjekt, float x, string text, Color c)
        {
            if (Math.Abs(y2 - y1) < 2) return;
            using (var pen = new Pen(c, 1.2f * k))
            using (var tenke = new Pen(Color.FromArgb(120, c), 1))
            {
                if (!float.IsNaN(xObjekt)) { g.DrawLine(tenke, xObjekt + 4 * k, y1, x + 5 * k, y1); g.DrawLine(tenke, xObjekt + 4 * k, y2, x + 5 * k, y2); }
                g.DrawLine(pen, x, y1, x, y2);
                g.DrawLine(pen, x - 5 * k, y1, x + 5 * k, y1);
                g.DrawLine(pen, x - 5 * k, y2, x + 5 * k, y2);
            }
            var vel = TextRenderer.MeasureText(text, font);
            TextRenderer.DrawText(g, text, font, new Point((int)(x + 7 * k), (int)((y1 + y2) / 2 - vel.Height / 2)), c);
        }
    }

    public class KreslenaVrstva
    {
        public Color Barva;
        public List<Obrys> Obrysy = new List<Obrys>();
        public bool Vyplnit = true;
        public bool Prerusovane;
    }

    // nahled v mm: kolecko mysi = zoom, tazeni = posun, dvojklik = cele
    public class Nahled : Panel
    {
        public List<KreslenaVrstva> Vrstvy = new List<KreslenaVrstva>();
        public Image Obrazek;
        public RectangleF ObrazekMm;          // X, Y = levy horni roh v mm (Y nahoru), sirka, vyska
        public bool ZobrazitObrazek = true;
        public bool ZobrazitBody;
        public string Text2 = "";
        public SizeF Deska;                   // imaginarni deska v mm (0 = nekreslit), levy dolni roh = 0,0
        public bool YNahore;
        public List<P> UzkaMista;             // stredy tahu/mezer (mm) a jejich sirka - cervene, kdyz < Freza
        public List<double> UzkaSirky;
        public double Freza;
        public string Prazdny = "";

        double meritko = 1, ox, oy;           // obrazovka = ox + x*meritko, oy - y*meritko
        Point tah;
        bool tahne;
        bool prizpusobeno;

        public Nahled()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
            MouseWheel += (s, e) =>
            {
                double k = e.Delta > 0 ? 1.25 : 0.8;
                ox = e.X - (e.X - ox) * k;
                oy = e.Y - (e.Y - oy) * k;
                meritko *= k;
                Invalidate();
            };
            MouseDown += (s, e) => { tah = e.Location; tahne = true; Focus(); };
            MouseMove += (s, e) =>
            {
                if (!tahne) return;
                ox += e.X - tah.X; oy += e.Y - tah.Y;
                tah = e.Location;
                Invalidate();
            };
            MouseUp += (s, e) => tahne = false;
            DoubleClick += (s, e) => Prizpusob();
        }

        public void NovaData(bool prizpusobit)
        {
            if (prizpusobit || !prizpusobeno) Prizpusob(); else Invalidate();
        }

        public void Prizpusob()
        {
            double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
            foreach (var v in Vrstvy)
                foreach (var o in v.Obrysy)
                    foreach (var p in o.Pts) { minx = Math.Min(minx, p.X); maxx = Math.Max(maxx, p.X); miny = Math.Min(miny, p.Y); maxy = Math.Max(maxy, p.Y); }
            if (Deska.Width > 0 && Deska.Height > 0)
            {
                minx = Math.Min(minx, 0); maxx = Math.Max(maxx, Deska.Width);
                miny = Math.Min(miny, 0); maxy = Math.Max(maxy, Deska.Height);
            }
            if (Obrazek != null && ZobrazitObrazek && ObrazekMm.Width > 0)
            {
                minx = Math.Min(minx, ObrazekMm.X); maxx = Math.Max(maxx, ObrazekMm.Right);
                miny = Math.Min(miny, ObrazekMm.Y - ObrazekMm.Height); maxy = Math.Max(maxy, ObrazekMm.Y);
            }
            if (minx > maxx || Width < 20 || Height < 20) { Invalidate(); return; }
            double w = Math.Max(maxx - minx, 1e-3), h = Math.Max(maxy - miny, 1e-3);
            float kk; using (var g = CreateGraphics()) kk = g.DpiX / 96f;
            double okraj = 40 * kk;
            double vpravo = 90 * kk, dole = 45 * kk;      // misto pro koty
            meritko = Math.Min((Width - 2 * okraj - vpravo) / w, (Height - 2 * okraj - 30 * kk - dole) / h);
            ox = (Width - vpravo - w * meritko) / 2 - minx * meritko;
            oy = (Height - 30 * kk - dole + h * meritko) / 2 + miny * meritko;
            prizpusobeno = true;
            Invalidate();
        }

        PointF S(P p) { return new PointF((float)(ox + p.X * meritko), (float)(oy - p.Y * meritko)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float k = g.DpiX / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool nic = Vrstvy.All(v => v.Obrysy.Count == 0) && Obrazek == null;
            if (nic)
            {
                using (var f = new Font(Font.FontFamily, 12))
                    TextRenderer.DrawText(g, Prazdny, f, ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            if (Deska.Width > 0 && Deska.Height > 0)
            {
                var d0 = S(new P(0, Deska.Height));
                var dr = new RectangleF(d0.X, d0.Y, (float)(Deska.Width * meritko), (float)(Deska.Height * meritko));
                using (var br = new SolidBrush(Color.FromArgb(232, 211, 168))) g.FillRectangle(br, dr);
                using (var pen = new Pen(Color.FromArgb(160, 120, 70), 2 * k)) g.DrawRectangle(pen, dr.X, dr.Y, dr.Width, dr.Height);
                TextRenderer.DrawText(g, string.Format("Deska X {0:0.#} × Y {1:0.#} mm · Y=0 {2}", Deska.Width, Deska.Height, YNahore ? "nahoře, Y dolů" : "dole, Y nahoru"), Font,
                    new Point((int)dr.Left, (int)(dr.Top - 20 * k)), Color.FromArgb(90, 60, 30));
            }
            if (Obrazek != null && ZobrazitObrazek && ObrazekMm.Width > 0)
            {
                var lt = S(new P(ObrazekMm.X, ObrazekMm.Y));
                var rect = new RectangleF(lt.X, lt.Y, (float)(ObrazekMm.Width * meritko), (float)(ObrazekMm.Height * meritko));
                var cm = new ColorMatrix { Matrix33 = 0.35f };
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(Obrazek, Rectangle.Round(rect), 0, 0, Obrazek.Width, Obrazek.Height, GraphicsUnit.Pixel, ia);
                }
            }
            // osy v pocatku
            var o0 = S(new P(0, 0));
            using (var pen = new Pen(Color.FromArgb(120, 0, 150, 80), 1))
            {
                g.DrawLine(pen, o0.X - 12 * k, o0.Y, o0.X + 25 * k, o0.Y);
                g.DrawLine(pen, o0.X, o0.Y + 12 * k, o0.X, o0.Y - 25 * k);
            }
            double tetiva = Math.Max(0.005, 0.4 / meritko);
            foreach (var v in Vrstvy)
            {
                using (var path = new GraphicsPath(FillMode.Alternate))
                using (var vypln = new GraphicsPath(FillMode.Alternate))
                {
                    foreach (var o in v.Obrysy)
                    {
                        var body = Geo.RozlozObrys(o, tetiva).Select(p => S(p)).ToArray();
                        if (body.Length < 2) continue;
                        path.StartFigure();
                        path.AddLines(body);
                        if (!o.Zavreny) continue;                       // otevrene drahy se nevyplnuji
                        path.CloseFigure();
                        vypln.StartFigure();
                        vypln.AddLines(body);
                        vypln.CloseFigure();
                    }
                    if (v.Vyplnit)
                        using (var br = new SolidBrush(Color.FromArgb(70, v.Barva))) g.FillPath(br, vypln);
                    using (var pen = new Pen(v.Barva, v.Prerusovane ? 1.2f * k : 1.8f * k))
                    {
                        if (v.Prerusovane) pen.DashStyle = DashStyle.Dash;
                        g.DrawPath(pen, path);
                    }
                }
                if (v.Prerusovane) continue;
                foreach (var o in v.Obrysy)
                {
                    if (o.Pts.Count < 2) continue;
                    if (ZobrazitBody)
                        for (int i = 0; i < o.Pts.Count; i++)
                        {
                            var q = S(o.Pts[i]);
                            bool obl = i < o.B.Count && Math.Abs(o.B[i]) > 1e-12;
                            using (var br = new SolidBrush(obl ? Color.RoyalBlue : Color.Black)) g.FillEllipse(br, q.X - 2.5f * k, q.Y - 2.5f * k, 5 * k, 5 * k);
                        }
                    // start a smer
                    var s = S(o.Pts[0]);
                    double t = Geo.SmerNaZacatku(o.Pts[0], o.Pts[1 % o.Pts.Count], o.B[0]);
                    float dx = (float)Math.Cos(t), dy = -(float)Math.Sin(t), L = 18 * k;
                    var h = new PointF(s.X + dx * L, s.Y + dy * L);
                    using (var pen = new Pen(Color.FromArgb(10, 138, 58), 2f * k))
                    {
                        pen.CustomEndCap = new AdjustableArrowCap(4, 4);
                        g.DrawLine(pen, s, h);
                    }
                    using (var br = new SolidBrush(Color.FromArgb(10, 138, 58))) g.FillEllipse(br, s.X - 4 * k, s.Y - 4 * k, 8 * k, 8 * k);
                }
            }
            // mista uzsi nez freza: cervene krouzky o prumeru skutecne sirky
            if (UzkaMista != null && Freza > 0)
                using (var br = new SolidBrush(Color.FromArgb(150, 230, 20, 20)))
                    for (int i = 0; i < UzkaMista.Count; i++)
                    {
                        if (UzkaSirky[i] >= Freza) continue;
                        var q = S(UzkaMista[i]);
                        if (q.X < -10 || q.Y < -10 || q.X > Width + 10 || q.Y > Height + 10) continue;
                        float r = Math.Max(1.5f * k, (float)(UzkaSirky[i] * meritko / 2));
                        g.FillEllipse(br, q.X - r, q.Y - r, 2 * r, 2 * r);
                    }
            Koty.Kresli(g, Font, k, p => S(p), Vrstvy.Where(v => !v.Prerusovane).SelectMany(v => v.Obrysy), Deska.Width > 0, YNahore, Deska.Height);
            if (!string.IsNullOrEmpty(Text2))
            {
                var r = new Rectangle((int)(8 * k), Height - (int)(28 * k), Width - (int)(16 * k), (int)(24 * k));
                TextRenderer.DrawText(g, Text2, Font, r, Color.FromArgb(0, 110, 60), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, "kolečko = zoom · tažení = posun · dvojklik = celé", Font, new Rectangle((int)(8 * k), (int)(4 * k), Width - (int)(16 * k), (int)(20 * k)), Color.Gray, TextFormatFlags.Right);
        }
    }
}
