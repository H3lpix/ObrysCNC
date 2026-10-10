using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using ObrysCNC;

// vykresli detail obrysu: podklad obrazek, usecky modre, oblouky fialove, body cerne, rohy zelene
static class TestRender
{
    static void Main(string[] a)
    {
        // a: obrazek, vystup.png, sirkaMm, x0, y0, x1, y1 (mm, Y nahoru), [sigma] [tol]
        var o = new Obrazek(a[0]);
        o.Rozpoznej(new NastaveniRozpoznani());
        var n = new NastaveniObrysu { SirkaMm = double.Parse(a[2]) };
        if (a.Length > 7) n.Sigma = double.Parse(a[7], System.Globalization.CultureInfo.InvariantCulture);
        if (a.Length > 8) n.Tol = double.Parse(a[8], System.Globalization.CultureInfo.InvariantCulture);
        if (a.Length > 9 && a[9] == "stred") n.Stredova = true;
        if (a.Length > 9 && a[9] == "hladky") n.Verne = false;
        var v = o.VytvorObrysy(n);
        Console.WriteLine("otevrenych {0}, typicka sirka {1:0.00} mm", o.Vrstvy[0].Obrysy.Count(x => !x.Zavreny), o.Vrstvy[0].TypickaSirkaMm);
        var obr = o.Vrstvy[0].Obrysy;
        if (!n.Stredova)
        {
            // presnost: husta izokrivka obrazku (subpixelova hrana) vs. vysledny obrys, v px obrazku
            var f = Obrazek.Rozmazat(o.Vrstvy[0].Maska, o.W, o.H, n.Sigma);
            double sk = v.MmNaPx;
            var odch = new System.Collections.Generic.List<double>();
            foreach (var loop in Obrazek.MarchingSquares(f, o.W, o.H))
            {
                if (loop.Count < 20) continue;
                for (int i = 0; i < loop.Count; i += 2)
                {
                    var q = new P(v.ObrX + loop[i].X * sk, v.ObrY - loop[i].Y * sk);
                    double m = double.MaxValue;
                    foreach (var x in obr) m = Math.Min(m, Geo.VzdalenostKObrysu(q, x));
                    odch.Add(m / sk);
                }
            }
            odch.Sort();
            Console.WriteLine("PRESNOST (px obrazku): prumer {0:0.000}, 95 % {1:0.000}, 99 % {2:0.000}, max {3:0.000}  (bodu hrany {4})",
                odch.Average(), odch[(int)(odch.Count * 0.95)], odch[(int)(odch.Count * 0.99)], odch[odch.Count - 1], odch.Count);
        }
        {
            // kontrola: body drah zpet na pixely a vzdalenost od stredu tahu (EDT max)
            var m = o.Vrstvy[0].Maska; int uvnitr = 0, celkem = 0; double sumOdst = 0;
            var mb = new bool[o.W * o.H]; for (int i = 0; i < mb.Length; i++) mb[i] = m[i] > 0.5f;
            var dt = Obrazek.Edt(mb, o.W, o.H);
            foreach (var x in obr) foreach (var p in x.Pts)
            {
                double pxx = (p.X - v.ObrX) / v.MmNaPx, pyy = (v.ObrY - p.Y) / v.MmNaPx;
                int ix = (int)pxx, iy = (int)pyy; if (ix < 1 || iy < 1 || ix >= o.W - 1 || iy >= o.H - 1) continue;
                celkem++; if (mb[iy * o.W + ix]) uvnitr++;
                // nejvetsi dt v okoli 6 px = stred tahu; jak daleko je bod od nej
                double best = -1; int bx = ix, by = iy;
                for (int dy = -6; dy <= 6; dy++) for (int dx = -6; dx <= 6; dx++) { int xx = ix + dx, yy = iy + dy; if (xx < 0 || yy < 0 || xx >= o.W || yy >= o.H) continue; if (dt[yy * o.W + xx] > best) { best = dt[yy * o.W + xx]; bx = xx; by = yy; } }
                sumOdst += dt[iy * o.W + ix] / Math.Max(1, best);
            }
            Console.WriteLine("bodu uvnitr tahu: {0}/{1}, stredovost (1 = presne uprostred) {2:0.00}", uvnitr, celkem, sumOdst / Math.Max(1, celkem));
        }
        Console.WriteLine("obrysu {0}, segmentu {1} (obl {2}), {3:0.0}x{4:0.0}", obr.Count, obr.Sum(x => x.B.Count), obr.Sum(x => x.B.Count(b => b != 0)), v.SirkaMm, v.VyskaMm);
        // kratke segmenty
        int kratke = 0;
        foreach (var x in obr) for (int i = 0; i < x.PocetSegmentu; i++) if (Geo.Vzd(x.Pts[i], x.KonecSegmentu(i)) < v.MmNaPx * 1.0) kratke++;
        Console.WriteLine("segmentu kratsich nez 1 px: {0}", kratke);
        double x0 = double.Parse(a[3]), y0 = double.Parse(a[4]), x1 = double.Parse(a[5]), y1 = double.Parse(a[6]);
        int W = 1400; double s = W / (x1 - x0); int H = (int)((y1 - y0) * s);
        Func<P, PointF> S = p => new PointF((float)((p.X - x0) * s), (float)((y1 - p.Y) * s));
        using (var bmp = new Bitmap(W, H))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            var lt = S(new P(v.ObrX, v.ObrY));
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.3f };
            var ia = new System.Drawing.Imaging.ImageAttributes(); ia.SetColorMatrix(cm);
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(o.Nahled, new Rectangle((int)lt.X, (int)lt.Y, (int)(v.ObrW * s), (int)(v.ObrH * s)), 0, 0, o.W, o.H, GraphicsUnit.Pixel, ia);
            foreach (var x in obr)
                for (int i = 0; i < x.PocetSegmentu; i++)
                {
                    var pts = Geo.Rozloz(x.Pts[i], x.KonecSegmentu(i), x.B[i], 0.01).Select(S).ToArray();
                    using (var pen = new Pen(x.B[i] != 0 ? Color.Magenta : Color.Blue, 2)) g.DrawLines(pen, pts);
                }
            foreach (var x in obr)
                for (int i = 0; i < x.Pts.Count; i++)
                {
                    var q = S(x.Pts[i]);
                    bool roh = Geo.Zlom(x, i) > 25;
                    g.FillEllipse(roh ? Brushes.Green : Brushes.Black, q.X - 3, q.Y - 3, 6, 6);
                }
            bmp.Save(a[1]);
        }
    }
}
