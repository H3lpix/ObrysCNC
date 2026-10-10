using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;

namespace ObrysCNC
{
    // zobrazeni a tisk nářezových plánů (každá deska pod sebou)
    public class NarezNahled : Panel
    {
        public VysledekNarezu Vysledek;
        public NastaveniNarezu Nastaveni;
        public List<Dil> Dily = new List<Dil>();

        static readonly Color[] Barvy =
        {
            Color.FromArgb(214, 234, 248), Color.FromArgb(213, 245, 227), Color.FromArgb(252, 243, 207), Color.FromArgb(250, 219, 216),
            Color.FromArgb(232, 218, 239), Color.FromArgb(209, 242, 235), Color.FromArgb(253, 235, 208), Color.FromArgb(235, 222, 240)
        };

        public NarezNahled()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            AutoScroll = true;
            BackColor = Color.White;
        }

        float K { get { using (var g = CreateGraphics()) return g.DpiX / 96f; } }

        public void Obnov()
        {
            float k = K;
            if (Vysledek == null || Nastaveni == null) { AutoScrollMinSize = Size.Empty; Invalidate(); return; }
            float sirka = Math.Max(200, ClientSize.Width - 40 * k);
            float vyskaDesky = (float)(sirka * Nastaveni.DeskaY / Nastaveni.DeskaX);
            AutoScrollMinSize = new Size(0, (int)(Vysledek.Desky.Count * (vyskaDesky + 60 * k) + 20 * k));
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Obnov(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float k = g.DpiX / 96f;
            if (Vysledek == null || Vysledek.Desky.Count == 0)
            {
                TextRenderer.DrawText(g, "Zadejte díly v tabulce nahoře a klikněte „Spočítat plán“.", Font, ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            float sirka = Math.Max(200, ClientSize.Width - 40 * k);
            float y = 10 * k;
            for (int i = 0; i < Vysledek.Desky.Count; i++)
            {
                var d = Vysledek.Desky[i];
                string nadpis = string.Format("Deska {0} z {1}  ·  {2:0} × {3:0} mm  ·  využití {4:0.0} %  ·  {5} dílů",
                    i + 1, Vysledek.Desky.Count, Nastaveni.DeskaX, Nastaveni.DeskaY, 100 * d.PlochaKusu / (Nastaveni.DeskaX * Nastaveni.DeskaY), d.Kusy.Count());
                using (var f = new Font(Font, FontStyle.Bold)) TextRenderer.DrawText(g, nadpis, f, new Point((int)(20 * k), (int)y), Color.FromArgb(40, 40, 40));
                y += 24 * k;
                float vyska = (float)(sirka * Nastaveni.DeskaY / Nastaveni.DeskaX);
                KresliDesku(g, d, new RectangleF(20 * k, y, sirka, vyska), Font, k);
                y += vyska + 36 * k;
            }
        }

        public void KresliDesku(Graphics g, PlanDesky d, RectangleF r, Font font, float k)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double s = r.Width / Nastaveni.DeskaX;
            // deska = odpad (srafovany), dily pres ni
            using (var br = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(200, 175, 140), Color.FromArgb(240, 228, 205))) g.FillRectangle(br, r);
            using (var pen = new Pen(Color.FromArgb(140, 100, 60), 1.5f * k)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            Func<double, double, double, double, RectangleF> R = (x, yy, w, h) =>
                new RectangleF(r.X + (float)(x * s), r.Bottom - (float)((yy + h) * s), (float)(w * s), (float)(h * s));
            foreach (var kus in d.Kusy)
            {
                var rk = R(kus.X, kus.Y, kus.W, kus.H);
                using (var br = new SolidBrush(Barvy[kus.CisloDilu % Barvy.Length])) g.FillRectangle(br, rk);
                using (var pen = new Pen(Color.FromArgb(60, 60, 60), 1f)) g.DrawRectangle(pen, rk.X, rk.Y, rk.Width, rk.Height);
                // hrany pro olepovacku: silna modra cara na olepenych stranach (podle hotoveho dilu)
                if (kus.CisloDilu < Dily.Count)
                {
                    var dil = Dily[kus.CisloDilu];
                    using (var pen = new Pen(Color.FromArgb(0, 90, 200), 2.5f * k))
                    {
                        // dlouhe strany dilu = strany delky; u otoceneho dilu jsou svisle
                        bool dlouheVodorovne = !kus.Otoceno;
                        float o = 1.5f * k;
                        if (dlouheVodorovne)
                        {
                            if (dil.HranaD >= 1) g.DrawLine(pen, rk.Left + o, rk.Bottom - o, rk.Right - o, rk.Bottom - o);
                            if (dil.HranaD >= 2) g.DrawLine(pen, rk.Left + o, rk.Top + o, rk.Right - o, rk.Top + o);
                            if (dil.HranaS >= 1) g.DrawLine(pen, rk.Left + o, rk.Top + o, rk.Left + o, rk.Bottom - o);
                            if (dil.HranaS >= 2) g.DrawLine(pen, rk.Right - o, rk.Top + o, rk.Right - o, rk.Bottom - o);
                        }
                        else
                        {
                            if (dil.HranaD >= 1) g.DrawLine(pen, rk.Left + o, rk.Top + o, rk.Left + o, rk.Bottom - o);
                            if (dil.HranaD >= 2) g.DrawLine(pen, rk.Right - o, rk.Top + o, rk.Right - o, rk.Bottom - o);
                            if (dil.HranaS >= 1) g.DrawLine(pen, rk.Left + o, rk.Bottom - o, rk.Right - o, rk.Bottom - o);
                            if (dil.HranaS >= 2) g.DrawLine(pen, rk.Left + o, rk.Top + o, rk.Right - o, rk.Top + o);
                        }
                    }
                }
                string text = kus.Nazev + "\n" + kus.W.ToString("0.#") + " × " + kus.H.ToString("0.#") + (kus.Otoceno ? " ↻" : "");
                var vel = TextRenderer.MeasureText(g, text, font);
                if (vel.Width < rk.Width - 4 && vel.Height < rk.Height - 4)
                    TextRenderer.DrawText(g, text, font, Rectangle.Round(rk), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                else if (rk.Width > 30 * k && rk.Height > 12 * k)
                    TextRenderer.DrawText(g, kus.W.ToString("0") + "×" + kus.H.ToString("0"), font, Rectangle.Round(rk), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            // prvni (podelne) rezy pruhu cervene
            using (var pen = new Pen(Color.FromArgb(200, 220, 40, 40), 1.2f * k) { DashStyle = DashStyle.Dash })
                foreach (var p in d.Pruhy)
                {
                    double konec = p.Y + p.Vyska;
                    if (d.PruhyPodelX)
                    {
                        float yy = r.Bottom - (float)(konec * s);
                        if (yy > r.Top + 2) g.DrawLine(pen, r.Left, yy, r.Right, yy);
                    }
                    else
                    {
                        float xx = r.Left + (float)(konec * s);
                        if (xx < r.Right - 2) g.DrawLine(pen, xx, r.Top, xx, r.Bottom);
                    }
                }
        }

        // ---------- tisk: kazda deska na samostatnou stranu (na sirku) ----------
        public void Tisk(IWin32Window vlastnik)
        {
            if (Vysledek == null || Vysledek.Desky.Count == 0) return;
            int strana = 0;
            var doc = new PrintDocument { DocumentName = "Nářezový plán" };
            doc.DefaultPageSettings.Landscape = true;
            doc.BeginPrint += (s, e) => strana = 0;
            doc.PrintPage += (s, e) =>
            {
                var g = e.Graphics;
                var m = e.MarginBounds;
                var d = Vysledek.Desky[strana];
                using (var fNadpis = new Font("Segoe UI", 12, FontStyle.Bold))
                using (var f = new Font("Segoe UI", 8))
                {
                    g.DrawString(string.Format("Nářezový plán – deska {0} z {1}   ({2:0} × {3:0} × {4:0.#} mm, prořez {5:0.#} mm, ořez {6:0.#} mm)",
                        strana + 1, Vysledek.Desky.Count, Nastaveni.DeskaX, Nastaveni.DeskaY, Nastaveni.Tloustka, Nastaveni.Prorez, Nastaveni.Orez), fNadpis, Brushes.Black, m.Left, m.Top);
                    float sirka = m.Width * 0.68f;
                    float vyska = (float)(sirka * Nastaveni.DeskaY / Nastaveni.DeskaX);
                    if (vyska > m.Height - 40) { vyska = m.Height - 40; sirka = (float)(vyska * Nastaveni.DeskaX / Nastaveni.DeskaY); }
                    KresliDesku(g, d, new RectangleF(m.Left, m.Top + 30, sirka, vyska), f, 1f);
                    // vpravo seznam dilu a postup
                    float x = m.Left + sirka + 15, y = m.Top + 30;
                    g.DrawString("Díly na desce:", fNadpis, Brushes.Black, x, y); y += 22;
                    foreach (var gr in d.Kusy.GroupBy(kk => new { kk.Nazev, W = Math.Round(Math.Max(kk.W, kk.H), 1), H = Math.Round(Math.Min(kk.W, kk.H), 1) }))
                    {
                        g.DrawString(string.Format("{0}× {1}  {2:0.#} × {3:0.#}", gr.Count(), gr.Key.Nazev, gr.Key.W, gr.Key.H), f, Brushes.Black, x, y);
                        y += 15;
                    }
                    y += 10;
                    g.DrawString("Postup řezání:", fNadpis, Brushes.Black, x, y); y += 22;
                    foreach (var radek in Narez.Postup(d, Nastaveni.Orez))
                    {
                        var rr = new RectangleF(x, y, m.Right - x, 200);
                        var vel = g.MeasureString(radek, f, (int)rr.Width);
                        g.DrawString(radek, f, Brushes.Black, rr);
                        y += vel.Height + 2;
                        if (y > m.Bottom - 20) break;
                    }
                }
                strana++;
                e.HasMorePages = strana < Vysledek.Desky.Count;
            };
            using (var dlg = new PrintPreviewDialog { Document = doc, Width = 1100, Height = 800, UseAntiAlias = true })
                dlg.ShowDialog(vlastnik);
        }
    }
}
