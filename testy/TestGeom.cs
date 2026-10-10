using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using ObrysCNC;

// Geometricky test prevodu obrazek -> CAD obrys. Vyrobi presne obrazky se znamou geometrii (zaobleny obdelnik,
// kruh, pulkruh, otvory...), prevede je stejnou cestou jako program a meri: pocet primek / oblouku / rohu,
// polomery, tecnost napojeni, kratke useky, odchylku od skutecneho tvaru (obema smery), uzavrenost, orientaci.
// Pak zapise DXF, nacte ho zpet a overi, ze v nem jsou opravdu oblouky (bulge) a stejna geometrie.
// Pouziti: TestGeom <vystupni slozka> [logo.png] [strom.webp]
static class TestGeom
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int chyb = 0;

    class Tvar
    {
        public string Nazev;
        public List<Obrys> Gt = new List<Obrys>();      // skutecna geometrie v px obrazku (Y dolu)
        public int W, H;
        public int Primek = -1, Oblouku = -1, Rohu = -1;   // ocekavane pocty (-1 = nekontrolovat)
        public double[] Polomery = new double[0];
        public int Kruznic = -1;                            // ocekavany pocet celych kruznic (2 pulkruhy)
        public int MinOblouku = -1;                         // aspon tolik oblouku (elipsa: nesmi byt kruznice)
        public double[] Stredy = new double[0];             // ocekavane kruznice: cx, cy, R, cx, cy, R ...
        public double MaxOdch = 0.35;                       // povolena odchylka od skutecneho tvaru (px)
        public int Sum;                                     // sum obrazku (stupne sedi +-)
    }

    static Obrys Krivka(Func<double, P> f, int n)
    {
        var o = new Obrys { Zavreny = true };
        for (int i = 0; i < n; i++) { o.Pts.Add(f((double)i / n)); o.B.Add(0); }
        return o;
    }
    static P Bez(P a, P b, P c, P d, double t)
    {
        double u = 1 - t;
        return new P(u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X, u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
    }

    // ---------- tvary ----------
    static Obrys ZaoblenyObdelnik(double x0, double y0, double x1, double y1, double r0, double r1, double r2, double r3)
    {
        var o = new Obrys { Zavreny = true };
        double b = Math.Tan(Math.PI / 8);
        Action<P, double> pridej = (p, bb) => { o.Pts.Add(p); o.B.Add(bb); };
        pridej(new P(x0 + r0, y0), 0);
        if (r1 > 0) pridej(new P(x1 - r1, y0), b);
        pridej(new P(x1, y0 + r1), 0);
        if (r2 > 0) pridej(new P(x1, y1 - r2), b);
        pridej(new P(x1 - r2, y1), 0);
        if (r3 > 0) pridej(new P(x0 + r3, y1), b);
        pridej(new P(x0, y1 - r3), 0);
        if (r0 > 0) pridej(new P(x0, y0 + r0), b);
        return o;
    }
    static Obrys Kruh(double cx, double cy, double r)
    {
        var o = new Obrys { Zavreny = true };
        o.Pts.Add(new P(cx - r, cy)); o.B.Add(1);
        o.Pts.Add(new P(cx + r, cy)); o.B.Add(1);
        return o;
    }
    static Obrys Polygon(params double[] xy)
    {
        var o = new Obrys { Zavreny = true };
        for (int i = 0; i < xy.Length; i += 2) { o.Pts.Add(new P(xy[i], xy[i + 1])); o.B.Add(0); }
        return o;
    }
    static Obrys Obrat(Obrys o) { return Geo.Obrat(o); }

    static List<Tvar> Tvary()
    {
        var t = new List<Tvar>();
        // obdelnik s hornimi rohy R32 (nakres od uzivatele): 4 primky, 2 oblouky R32, 2 ostre rohy
        t.Add(new Tvar { Nazev = "horni_rohy_R32", W = 440, H = 260, Primek = 4, Oblouku = 2, Rohu = 2, Polomery = new[] { 32.0, 32 },
            Gt = { ZaoblenyObdelnik(20, 20, 420, 240, 32, 32, 0, 0) } });
        t.Add(new Tvar { Nazev = "obdelnik_R32", W = 440, H = 260, Primek = 4, Oblouku = 4, Rohu = 0, Polomery = new[] { 32.0, 32, 32, 32 },
            Gt = { ZaoblenyObdelnik(20, 20, 420, 240, 32, 32, 32, 32) } });
        t.Add(new Tvar { Nazev = "kruh_R100", W = 240, H = 240, Primek = 0, Oblouku = 2, Rohu = 0, Polomery = new[] { 100.0, 100 }, Kruznic = 1,
            Stredy = new[] { 120.0, 120, 100 }, Gt = { Kruh(120, 120, 100) } });
        // kruh se sumem obrazku (+-40 stupnu sedi): porad 2 pulkruhy
        t.Add(new Tvar { Nazev = "kruh_sum", W = 240, H = 240, Primek = 0, Oblouku = 2, Rohu = 0, Kruznic = 1, Sum = 40, MaxOdch = 0.5,
            Stredy = new[] { 120.0, 120, 100 }, Gt = { Kruh(120, 120, 100) } });
        // elipsa a skoro-kruhy: nesmi se z nich stat kruznice
        t.Add(new Tvar { Nazev = "elipsa_110x80", W = 260, H = 200, Rohu = 0, Kruznic = 0, MinOblouku = 4, MaxOdch = 0.9,
            Gt = { Krivka(u => new P(130 + 110 * Math.Cos(2 * Math.PI * u), 100 + 80 * Math.Sin(2 * Math.PI * u)), 1440) } });
        t.Add(new Tvar { Nazev = "elipsa_103x100", W = 250, H = 240, Rohu = 0, Kruznic = 0, MinOblouku = 3, MaxOdch = 0.9,
            Gt = { Krivka(u => new P(125 + 103 * Math.Cos(2 * Math.PI * u), 120 + 100 * Math.Sin(2 * Math.PI * u)), 1440) } });
        t.Add(new Tvar { Nazev = "kruh_nepravidelny", W = 240, H = 240, Rohu = 0, Kruznic = 0, MinOblouku = 5, MaxOdch = 0.9,
            Gt = { Krivka(u => { double f = 2 * Math.PI * u, r = 100 + 1.5 * Math.Sin(5 * f); return new P(120 + r * Math.Cos(f), 120 + r * Math.Sin(f)); }, 1440) } });
        // pulkruh: primka + oblouk 180 st., 2 ostre rohy
        var pul = new Obrys { Zavreny = true };
        pul.Pts.Add(new P(20, 120)); pul.B.Add(0);
        pul.Pts.Add(new P(220, 120)); pul.B.Add(-1);
        t.Add(new Tvar { Nazev = "pulkruh_R100", W = 240, H = 140, Primek = 1, Oblouku = 1, Rohu = 2, Polomery = new[] { 100.0 }, Gt = { pul } });
        // kapsle: dlouha primka + pulkruh tecne
        t.Add(new Tvar { Nazev = "kapsle_R50", W = 440, H = 140, Primek = 2, Oblouku = 2, Rohu = 0, Polomery = new[] { 50.0, 50 },
            Gt = { ZaoblenyObdelnik(20, 20, 420, 120, 50, 50, 50, 50) } });
        // L profil s vnitrnim zaoblenim R30 (vydute): 6 primek, 1 oblouk, 5 ostrych rohu
        var L = new Obrys { Zavreny = true };
        foreach (var bb in new[] { new[] { 20.0, 20, 0 }, new[] { 100.0, 20, 0 }, new[] { 100.0, 130, -Math.Tan(Math.PI / 8) }, new[] { 130.0, 160, 0 },
                                    new[] { 300.0, 160, 0 }, new[] { 300.0, 240, 0 }, new[] { 20.0, 240, 0 } }) { L.Pts.Add(new P(bb[0], bb[1])); L.B.Add(bb[2]); }
        t.Add(new Tvar { Nazev = "L_profil_R30", W = 320, H = 260, Primek = 6, Oblouku = 1, Rohu = 5, Polomery = new[] { 30.0 }, Gt = { L } });
        // hvezda: 10 primek, 10 ostrych rohu
        var hv = new Obrys { Zavreny = true };
        for (int i = 0; i < 10; i++) { double f = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? 115 : 46; hv.Pts.Add(new P(130 + r * Math.Cos(f), 130 + r * Math.Sin(f))); hv.B.Add(0); }
        t.Add(new Tvar { Nazev = "hvezda", W = 260, H = 250, Primek = 10, Oblouku = 0, Rohu = 10, Gt = { hv } });
        // organicky list ze dvou Bezierovych krivek: 2 spicky, jinak plynule
        P la = new P(30, 210), lb = new P(330, 30);
        var list = Krivka(u => u < 0.5 ? Bez(la, new P(40, 90), new P(200, 10), lb, 2 * u) : Bez(lb, new P(320, 170), new P(170, 250), la, 2 * u - 1), 2000);
        t.Add(new Tvar { Nazev = "list", W = 360, H = 250, Rohu = 2, MaxOdch = 0.9, Gt = { list } });
        // maly zaobleny roh R4 (jeden), ostatni ostre
        t.Add(new Tvar { Nazev = "maly_roh_R4", W = 240, H = 180, Primek = 4, Oblouku = 1, Rohu = 3, Polomery = new[] { 4.0 },
            Gt = { ZaoblenyObdelnik(20, 20, 220, 160, 0, 4, 0, 0) } });
        // ostry pravouhly a pootoceny ctverec
        t.Add(new Tvar { Nazev = "ctverec_ostry", W = 240, H = 240, Primek = 4, Oblouku = 0, Rohu = 4, Gt = { Polygon(20, 20, 220, 20, 220, 220, 20, 220) } });
        double c = Math.Cos(17 * Math.PI / 180) * 90, s = Math.Sin(17 * Math.PI / 180) * 90;
        t.Add(new Tvar { Nazev = "ctverec_pootoceny", W = 240, H = 240, Primek = 4, Oblouku = 0, Rohu = 4,
            Gt = { Polygon(120 - c + s, 120 - s - c, 120 + c + s, 120 + s - c, 120 + c - s, 120 + s + c, 120 - c - s, 120 - s + c) } });
        // uzavreny obrys s otvorem: mezikruzi a obdelnik se zaoblenym otvorem
        t.Add(new Tvar { Nazev = "mezikruzi", W = 280, H = 280, Primek = 0, Oblouku = 4, Rohu = 0, Polomery = new[] { 120.0, 120, 60, 60 }, Kruznic = 2,
            Stredy = new[] { 140.0, 140, 120, 140, 140, 60 }, Gt = { Kruh(140, 140, 120), Obrat(Kruh(140, 140, 60)) } });
        t.Add(new Tvar { Nazev = "deska_s_otvorem", W = 440, H = 280, Primek = 8, Oblouku = 4, Rohu = 4, Polomery = new[] { 20.0, 20, 20, 20 },
            Gt = { Polygon(20, 20, 420, 20, 420, 260, 20, 260), Obrat(ZaoblenyObdelnik(100, 80, 340, 200, 20, 20, 20, 20)) } });
        // vice kontur: deska, 3 kruhove otvory (R25, R25, R15) a drazka
        t.Add(new Tvar { Nazev = "deska_otvory", W = 440, H = 300, Primek = 6, Oblouku = 8, Rohu = 4, Kruznic = 3,
            Polomery = new[] { 25.0, 25, 25, 25, 15, 15, 30, 30 }, Stredy = new[] { 90.0, 90, 25, 200, 90, 25, 320, 90, 15 },
            Gt = { Polygon(20, 20, 420, 20, 420, 280, 20, 280), Obrat(Kruh(90, 90, 25)), Obrat(Kruh(200, 90, 25)), Obrat(Kruh(320, 90, 15)),
                   Obrat(ZaoblenyObdelnik(80, 170, 360, 230, 30, 30, 30, 30)) } });
        return t;
    }

    // presny antialiasing (GDI+ ma u sikmych hran a kruznic posun az 0,4 px): pokryti pixelu tvarem
    // spocitane po 32 radcich na pixel, ve vodorovnem smeru presne; pixel i zabira <i, i+1>
    static Bitmap Vykresli(Tvar t)
    {
        var hrany = new List<P[]>();
        foreach (var o in t.Gt)
        {
            var pts = Geo.RozlozObrys(o, 0.002);
            for (int i = 0; i + 1 < pts.Count; i++) hrany.Add(new[] { pts[i], pts[i + 1] });
        }
        const int SUB = 32;
        var pokryti = new double[t.W * t.H];
        var xs = new List<double>();
        for (int y = 0; y < t.H; y++)
            for (int sy = 0; sy < SUB; sy++)
            {
                double yy = y + (sy + 0.5) / SUB;
                xs.Clear();
                foreach (var h in hrany)
                {
                    P a = h[0], b = h[1];
                    if ((a.Y > yy) == (b.Y > yy)) continue;
                    xs.Add(a.X + (yy - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)     // sudy-lichy: otvory zustanou prazdne
                {
                    double x0 = Math.Max(0, xs[i]), x1 = Math.Min(t.W, xs[i + 1]);
                    for (int x = (int)Math.Floor(x0); x < x1 && x < t.W; x++)
                    {
                        double c = Math.Min(x + 1, x1) - Math.Max(x, x0);
                        if (c > 0) pokryti[y * t.W + x] += c / SUB;
                    }
                }
            }
        var bmp = new Bitmap(t.W, t.H, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        var rnd = new Random(1);
        for (int y = 0; y < t.H; y++)
            for (int x = 0; x < t.W; x++)
            {
                int v = (int)Math.Round(255 * (1 - Math.Min(1, pokryti[y * t.W + x])) + (t.Sum > 0 ? (rnd.NextDouble() * 2 - 1) * t.Sum : 0));
                v = Math.Max(0, Math.Min(255, v));
                bmp.SetPixel(x, y, Color.FromArgb(v, v, v));
            }
        return bmp;
    }

    // ---------- mereni ----------
    class Mereni
    {
        public int Primek, Oblouku, Rohu, Zlomu, Kratkych, Segmentu, Obrysu, Otevrenych, Plochych;
        public double MaxZlomHladky;             // nejvetsi uhel napojeni, ktere melo byt tecne (st.)
        public List<double> Polomery = new List<double>();
    }

    // obrysy v px (Y dolu) -> spocitat entity
    static Mereni Zmer(List<Obrys> obr, double kratkyPx)
    {
        var m = new Mereni();
        foreach (var o in obr)
        {
            m.Obrysu++;
            if (!o.Zavreny) m.Otevrenych++;
            for (int i = 0; i < o.PocetSegmentu; i++)
            {
                m.Segmentu++;
                double d = Geo.Vzd(o.Pts[i], o.KonecSegmentu(i));
                if (d < kratkyPx) m.Kratkych++;
                if (Math.Abs(o.B[i]) > 1e-9) { m.Oblouku++; m.Polomery.Add(Geo.Oblouk(o.Pts[i], o.KonecSegmentu(i), o.B[i]).R); if (Math.Abs(4 * Math.Atan(o.B[i])) < 0.08 && d >= 12) m.Plochych++; }
                else m.Primek++;
            }
            for (int k = 0; k < o.Pts.Count; k++)
            {
                if (!o.Zavreny && (k == 0 || k == o.Pts.Count - 1)) continue;
                double z = Geo.Zlom(o, k);
                if (z >= 15) m.Rohu++;
                else if (z >= 0.05) m.Zlomu++;
                else m.MaxZlomHladky = Math.Max(m.MaxZlomHladky, z);
            }
        }
        return m;
    }

    // cele kruznice ve vysledku: uzavreny obrys ze 2 pulkruhu (bulge +-1) se spolecnym stredem; vraci cx, cy, R
    static List<double[]> CeleKruznice(List<Obrys> obr)
    {
        var res = new List<double[]>();
        foreach (var o in obr)
        {
            if (!o.Zavreny || o.Pts.Count != 2 || Math.Abs(Math.Abs(o.B[0]) - 1) > 1e-6 || Math.Abs(Math.Abs(o.B[1]) - 1) > 1e-6 || Math.Sign(o.B[0]) != Math.Sign(o.B[1])) continue;
            var a = Geo.Oblouk(o.Pts[0], o.Pts[1], o.B[0]); var b = Geo.Oblouk(o.Pts[1], o.Pts[0], o.B[1]);
            if (Math.Abs(a.Cx - b.Cx) + Math.Abs(a.Cy - b.Cy) + Math.Abs(a.R - b.R) > 1e-6) continue;
            res.Add(new[] { a.Cx, a.Cy, a.R });
        }
        return res;
    }

    // odchylka obou smeru: vysledek -> skutecnost a skutecnost -> vysledek (Hausdorff), v px
    static double Odchylka(List<Obrys> a, List<Obrys> b, out double prumer)
    {
        double mx = 0, sum = 0; int c = 0;
        foreach (var o in a)
            foreach (var p in Geo.RozlozObrys(o, 0.05))
            {
                double d = b.Min(x => Geo.VzdalenostKObrysu(p, x));
                mx = Math.Max(mx, d); sum += d; c++;
            }
        prumer = c > 0 ? sum / c : 0;
        return mx;
    }

    static List<Obrys> DoPx(IEnumerable<Obrys> mm, VysledekObrazku v)
    {
        double s = v.MmNaPx;
        return mm.Select(o => new Obrys
        {
            Zavreny = o.Zavreny, Vrstva = o.Vrstva,
            Pts = o.Pts.Select(p => new P((p.X - v.ObrX) / s, (v.ObrY - p.Y) / s)).ToList(),
            B = o.B.Select(b => -b).ToList()
        }).ToList();
    }

    static void Over(bool ok, string co)
    {
        Console.WriteLine("    [{0}] {1}", ok ? " OK " : "CHYBA", co);
        if (!ok) chyb++;
    }

    static string Diag(VysledekObrazku v)
    {
        var f = typeof(VysledekObrazku).GetField("Diagnostika");
        if (f == null) return "(diagnostika v teto verzi neni)";
        var d = f.GetValue(v);
        return d == null ? "(zadna)" : d.ToString();
    }

    // DXF: zapsat, nacist zpet, porovnat
    static void OverDxf(List<Obrys> mm, string soubor, double tolMm)
    {
        Dxf.Zapis(soubor, mm, new Dictionary<string, int>(), false);
        var txt = File.ReadAllLines(soubor);
        int vertexu = 0, bulge = 0;
        for (int i = 0; i + 1 < txt.Length; i += 2)
        {
            if (txt[i].Trim() == "0" && txt[i + 1].Trim() == "VERTEX") vertexu++;
            if (txt[i].Trim() == "42") bulge++;
        }
        var zpet = Dxf.Nacti(soubor).Prvky;
        double maxRoz = 0;
        bool shoda = zpet.Count == mm.Count;
        for (int k = 0; k < Math.Min(zpet.Count, mm.Count); k++)
        {
            var a = mm[k]; var b = zpet[k];
            if (a.Pts.Count != b.Pts.Count || a.Zavreny != b.Zavreny) { shoda = false; continue; }
            for (int i = 0; i < a.Pts.Count; i++)
            {
                maxRoz = Math.Max(maxRoz, Geo.Vzd(a.Pts[i], b.Pts[i]));
                // stejny tvar segmentu: porovnat stred segmentu (u oblouku vrchol oblouku)
                var sa = Geo.Rozloz(a.Pts[i], a.KonecSegmentu(i), a.B[i], 1e9);
                var sb = Geo.Rozloz(b.Pts[i], b.KonecSegmentu(i), b.B[i], 1e9);
                maxRoz = Math.Max(maxRoz, Geo.Vzd(sa[sa.Count / 2], sb[sb.Count / 2]));
            }
        }
        int obl = mm.Sum(o => o.B.Count(x => Math.Abs(x) > 1e-9));
        // zaokrouhleni souradnic (4 desetinna mista): polomery a tecnost po nacteni
        double maxR = 0, maxZl = 0;
        for (int k = 0; shoda && k < mm.Count; k++)
        {
            var a = mm[k]; var b = zpet[k];
            for (int i = 0; i < a.PocetSegmentu; i++)
                if (Math.Abs(a.B[i]) > 1e-9) maxR = Math.Max(maxR, Math.Abs(Geo.Oblouk(a.Pts[i], a.KonecSegmentu(i), a.B[i]).R - Geo.Oblouk(b.Pts[i], b.KonecSegmentu(i), b.B[i]).R));
            for (int i = 0; i < a.Pts.Count; i++) if (Geo.Zlom(a, i) < 0.05) maxZl = Math.Max(maxZl, Geo.Zlom(b, i));
        }
        Console.WriteLine("    DXF: {0} POLYLINE, {1} VERTEX, {2} s bulge (v pameti {3} oblouku), max rozdil po nacteni {4:0.00000} mm, polomeru {5:0.00000} mm, netecnost po zaokrouhleni {6:0.0000} st.",
            zpet.Count, vertexu, bulge, obl, maxRoz, maxR, maxZl);
        Over(shoda && bulge == obl && maxRoz <= tolMm && maxR <= tolMm && maxZl <= 0.01, "DXF obsahuje stejne entity, oblouky a polomery; tecnost po zaokrouhleni do 0,01 st.");
    }

    static void Vykresli(string soubor, Bitmap podklad, List<Obrys> gt, List<Obrys> obr, double meritko)
    {
        int W = (int)(podklad.Width * meritko), H = (int)(podklad.Height * meritko);
        using (var bmp = new Bitmap(W, H))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.25f };
            var ia = new System.Drawing.Imaging.ImageAttributes(); ia.SetColorMatrix(cm);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(podklad, new Rectangle(0, 0, W, H), 0, 0, podklad.Width, podklad.Height, GraphicsUnit.Pixel, ia);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            Func<P, PointF> S = p => new PointF((float)(p.X * meritko), (float)(p.Y * meritko));
            if (gt != null)
                foreach (var o in gt)
                    using (var pen = new Pen(Color.FromArgb(120, 255, 160, 0), 5)) g.DrawLines(pen, Geo.RozlozObrys(o, 0.05).Select(S).ToArray());
            foreach (var o in obr)
                for (int i = 0; i < o.PocetSegmentu; i++)
                {
                    var pts = Geo.Rozloz(o.Pts[i], o.KonecSegmentu(i), o.B[i], 0.02).Select(S).ToArray();
                    using (var pen = new Pen(Math.Abs(o.B[i]) > 1e-9 ? Color.Magenta : Color.Blue, 1.6f)) g.DrawLines(pen, pts);
                }
            foreach (var o in obr)
                for (int k = 0; k < o.Pts.Count; k++)
                {
                    var q = S(o.Pts[k]);
                    double z = Geo.Zlom(o, k);
                    var br = z >= 15 ? Brushes.Green : z >= 0.05 ? Brushes.Red : Brushes.Black;
                    float r = z >= 0.05 && z < 15 ? 5 : 3;
                    g.FillEllipse(br, q.X - r, q.Y - r, 2 * r, 2 * r);
                }
            bmp.Save(soubor);
        }
    }

    static VysledekObrazku Preved(string soubor, double sirkaMm, out Obrazek o, out long ms)
    {
        o = new Obrazek(soubor);
        o.Rozpoznej(new NastaveniRozpoznani());
        var n = new NastaveniObrysu { SirkaMm = sirkaMm };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var v = o.VytvorObrysy(n);
        ms = sw.ElapsedMilliseconds;
        return v;
    }

    static void Main(string[] a)
    {
        string dir = a.Length > 0 ? a[0] : ".";
        Directory.CreateDirectory(dir);
        foreach (var t in Tvary())
        {
            string png = Path.Combine(dir, t.Nazev + ".png");
            Bitmap bmp = Vykresli(t);
            bmp.Save(png);
            double sirkaGt;
            {
                double minx, miny, maxx, maxy;
                Geo.Rozsah(t.Gt, out minx, out miny, out maxx, out maxy);
                sirkaGt = maxx - minx;
            }
            Obrazek ob; long ms;
            var v = Preved(png, sirkaGt, out ob, out ms);         // 1 px = 1 mm
            var mm = ob.Vrstvy[0].Obrysy;
            var px = DoPx(mm, v);
            var m = Zmer(px, 1.0);
            double prum1, prum2;
            double h1 = Odchylka(px, t.Gt, out prum1), h2 = Odchylka(t.Gt, px, out prum2);
            Console.WriteLine("== {0} ({1} ms)", t.Nazev, ms);
            Console.WriteLine("    primek {0}, oblouku {1}, rohu {2}, ZLOMU (netecnych napojeni) {3}, kratkych (<1 px) {4}, segmentu celkem {5}",
                m.Primek, m.Oblouku, m.Rohu, m.Zlomu, m.Kratkych, m.Segmentu);
            Console.WriteLine("    polomery: {0}", string.Join(", ", m.Polomery.Select(r => r.ToString("0.00", Inv))));
            Console.WriteLine("    odchylka od skutecneho tvaru: max {0:0.000} px (zpet {1:0.000}), prumer {2:0.000} px; max uhel tecneho napojeni {3:0.0000} st.",
                h1, h2, (prum1 + prum2) / 2, m.MaxZlomHladky);
            Console.WriteLine("    diagnostika: " + Diag(v));
            var kruhy = CeleKruznice(px);
            if (t.Primek >= 0) Over(m.Primek == t.Primek && m.Oblouku == t.Oblouku && m.Rohu == t.Rohu,
                string.Format("ocekavano {0} primek, {1} oblouku, {2} rohu", t.Primek, t.Oblouku, t.Rohu));
            else if (t.Rohu >= 0) Over(m.Rohu == t.Rohu, string.Format("ocekavano {0} ostrych rohu", t.Rohu));
            if (t.Kruznic >= 0) Over(kruhy.Count == t.Kruznic, string.Format("celych kruznic (2 pulkruhy): {0}, ocekavano {1}", kruhy.Count, t.Kruznic));
            if (t.MinOblouku >= 0) Over(m.Oblouku >= t.MinOblouku, string.Format("tvar neni nahrazen kruznici: {0} oblouku (aspon {1})", m.Oblouku, t.MinOblouku));
            for (int q = 0; q + 2 < t.Stredy.Length; q += 3)
            {
                double cx = t.Stredy[q], cy = t.Stredy[q + 1], R = t.Stredy[q + 2];
                var nej = kruhy.OrderBy(k => Math.Abs(k[0] - cx) + Math.Abs(k[1] - cy) + Math.Abs(k[2] - R)).FirstOrDefault();
                double ds = nej == null ? 999 : Math.Sqrt((nej[0] - cx) * (nej[0] - cx) + (nej[1] - cy) * (nej[1] - cy)), dr = nej == null ? 999 : Math.Abs(nej[2] - R);
                Over(ds <= 0.1 && dr <= 0.1, string.Format(Inv, "kruznice stred {0},{1} R{2}: odchylka stredu {3:0.000} px, polomeru {4:0.000} px", cx, cy, R, ds, dr));
            }
            Console.WriteLine(string.Format(Inv, "TAB|{0}|{1}|{2}|{3}|{4}|{5:0.000}", t.Nazev, m.Primek, m.Oblouku, m.Rohu, kruhy.Count, Math.Max(h1, h2)));
            var ocek = t.Polomery.OrderBy(x => x).ToList(); var nam = m.Polomery.OrderBy(x => x).ToList();
            bool polOk = ocek.Count == nam.Count;
            // male polomery: rozmazani obrazku (sigma 0,8 px) je zvetsi o ~0,3 px -> tolerance 1 % nebo 0,35 px
            for (int i = 0; polOk && i < ocek.Count; i++) polOk = Math.Abs(ocek[i] - nam[i]) <= Math.Max(0.35, 0.01 * ocek[i]);
            if (t.Polomery.Length > 0) Over(polOk, "polomery " + string.Join(", ", t.Polomery.Select(r => "R" + r)) + " (+-1 %, min. 0,35 px)");
            Over(m.Zlomu == 0, "zadne netecne zlomy mimo ostre rohy");
            Over(m.Kratkych == 0, "zadne kratke useky");
            Over(Math.Max(h1, h2) <= t.MaxOdch, string.Format(Inv, "odchylka od skutecneho tvaru do {0} px", t.MaxOdch));
            Over(m.Otevrenych == 0 && m.Obrysu == t.Gt.Count, "vsechny obrysy uzavrene, pocet " + t.Gt.Count);
            // orientace: v mm (Y nahoru) vnejsi proti smeru hodin, otvory po smeru
            bool orient = true;
            for (int k = 0; k < mm.Count; k++)
            {
                bool dira = mm.Any(x => !ReferenceEquals(x, mm[k]) && Geo.Uvnitr(mm[k].Pts[0], x));
                orient &= dira ? Geo.Plocha(mm[k]) < 0 : Geo.Plocha(mm[k]) > 0;
            }
            Over(orient, "orientace: vnejsi CCW, otvory CW");
            OverDxf(mm, Path.Combine(dir, t.Nazev + ".dxf"), 1e-3);
            Vykresli(Path.Combine(dir, t.Nazev + "_vysledek.png"), bmp, t.Gt, px, 2.5);
            bmp.Dispose();
        }
        // skutecne obrazky bez znameho tvaru: statistika
        for (int i = 1; i < a.Length; i++)
        {
            Obrazek ob; long ms;
            var v = Preved(a[i], 500, out ob, out ms);
            if (!v.Diagnostika.SplnujeToleranci || v.Diagnostika.Samopruseciky + v.Diagnostika.PrusecikyObrysu > 0)
                Console.WriteLine("VAROVANI KVALITY: " + Path.GetFileName(a[i]) + ": nesplnena tolerance nebo pruseciky. Uspesny test DXF neni potvrzenim kvality kontury.");
            var mm = ob.Vrstvy[0].Obrysy;
            var px = DoPx(mm, v);
            var m = Zmer(px, 1.0);
            // presnost vuci izokrivce obrazku (stejne jako TestRender)
            var f = Obrazek.Rozmazat(ob.Vrstvy[0].Maska, ob.W, ob.H, 0.8);
            var odch = new List<double>();
            foreach (var loop in Obrazek.MarchingSquares(f, ob.W, ob.H))
            {
                if (loop.Count < 20) continue;
                for (int k = 0; k < loop.Count; k += 2) odch.Add(px.Min(x => Geo.VzdalenostKObrysu(loop[k], x)));
            }
            // kde jsou nejhorsi mista (pro kontrolu vyrezem)
            var mista = new List<KeyValuePair<double, P>>();
            foreach (var loop in Obrazek.MarchingSquares(f, ob.W, ob.H))
            {
                if (loop.Count < 20) continue;
                for (int k = 0; k < loop.Count; k += 2) mista.Add(new KeyValuePair<double, P>(px.Min(x => Geo.VzdalenostKObrysu(loop[k], x)), loop[k]));
            }
            var vybrana = new List<KeyValuePair<double, P>>();
            foreach (var kv in mista.OrderByDescending(x => x.Key))
            {
                if (vybrana.Count >= 6) break;
                if (vybrana.Any(x => Geo.Vzd(x.Value, kv.Value) < 15)) continue;
                vybrana.Add(kv);
            }
            Console.WriteLine("    nejhorsi mista: " + string.Join("; ", vybrana.Select(x => string.Format(Inv, "{0:0.00} px @ {1:0},{2:0}", x.Key, x.Value.X, x.Value.Y))));
            var zl = new List<string>();
            foreach (var o in px)
                for (int k = 0; k < o.Pts.Count; k++) { double z = Geo.Zlom(o, k); if (z >= 0.05 && z < 15) zl.Add(string.Format(Inv, "{0:0.0} st. @ {1:0},{2:0}", z, o.Pts[k].X, o.Pts[k].Y)); }
            var kr = new List<string>();
            foreach (var o in px)
                for (int k = 0; k < o.PocetSegmentu; k++) if (Geo.Vzd(o.Pts[k], o.KonecSegmentu(k)) < 1) kr.Add(string.Format(Inv, "{0:0},{1:0}", o.Pts[k].X, o.Pts[k].Y));
            if (zl.Count > 0) Console.WriteLine("    zlomy: " + string.Join("; ", zl.Take(12)));
            if (kr.Count > 0) Console.WriteLine("    kratke: " + string.Join("; ", kr.Take(12)));
            odch.Sort();
            string jm = Path.GetFileNameWithoutExtension(a[i]);
            Console.WriteLine("== {0} ({1} ms)", jm, ms);
            Console.WriteLine("    obrysu {0}, primek {1}, oblouku {2} (z toho skoro rovnych {7}), rohu {3}, ZLOMU {4}, kratkych (<1 px) {5}, segmentu {6}",
                m.Obrysu, m.Primek, m.Oblouku, m.Rohu, m.Zlomu, m.Kratkych, m.Segmentu, m.Plochych);
            Console.WriteLine("    odchylka od hrany obrazku: prumer {0:0.000} px, 95 % {1:0.000}, 99 % {2:0.000}, max {3:0.000}",
                odch.Average(), odch[(int)(odch.Count * 0.95)], odch[(int)(odch.Count * 0.99)], odch[odch.Count - 1]);
            Console.WriteLine("    diagnostika: " + Diag(v));
            // jednotlive obrysy (podle polohy v obrazku)
            var kr2 = CeleKruznice(px);
            foreach (var o in px.OrderBy(x => Math.Round(x.Pts.Average(p => p.Y) / 150)).ThenBy(x => x.Pts.Average(p => p.X)))
            {
                var mo1 = Zmer(new List<Obrys> { o }, 1.0);
                double minx = o.Pts.Min(p => p.X), maxx = o.Pts.Max(p => p.X), miny = o.Pts.Min(p => p.Y), maxy = o.Pts.Max(p => p.Y);
                Console.WriteLine(string.Format(Inv, "      obrys @ {0:0},{1:0} ({2:0}x{3:0} px): primek {4}, oblouku {5}, rohu {6}{7}",
                    (minx + maxx) / 2, (miny + maxy) / 2, maxx - minx, maxy - miny, mo1.Primek, mo1.Oblouku, mo1.Rohu, CeleKruznice(new List<Obrys> { o }).Count > 0 ? ", KRUZNICE" : ""));
            }
            Console.WriteLine(string.Format(Inv, "TAB|{0}|{1}|{2}|{3}|{4}|{5:0.000}", jm, m.Primek, m.Oblouku, m.Rohu, kr2.Count, odch[odch.Count - 1] * v.MmNaPx));
            OverDxf(mm, Path.Combine(dir, jm + ".dxf"), 1e-3);
            var bmp = new Bitmap(ob.Nahled);
            Vykresli(Path.Combine(dir, jm + "_vysledek.png"), bmp, null, px, 2.0);
            bmp.Dispose();
        }
        // ostatni vetve: jen usecky, hladky (Potrace), nahradni proklad musi byt v diagnostice oznaceny
        {
            var t = Tvary()[0];
            string png = Path.Combine(dir, t.Nazev + ".png");
            var o = new Obrazek(png); o.Rozpoznej(new NastaveniRozpoznani());
            var v1 = o.VytvorObrysy(new NastaveniObrysu { SirkaMm = 400, Oblouky = false });
            var m1 = Zmer(o.Vrstvy[0].Obrysy, 0);
            Console.WriteLine("== jen usecky: primek {0}, oblouku {1}; {2}", m1.Primek, m1.Oblouku, v1.Diagnostika);
            Over(m1.Oblouku == 0 && m1.Primek > 6 && v1.Diagnostika.ObrysuCad == 1, "volba bez oblouku: CAD obrys rozlozeny na usecky");
            var v2 = o.VytvorObrysy(new NastaveniObrysu { SirkaMm = 400, Verne = false });
            Console.WriteLine("== hladky (Potrace): " + v2.Diagnostika);
            Over(v2.Diagnostika.ObrysuHladkych == 1 && v2.Diagnostika.ObrysuCad == 0, "hladky proklad je v diagnostice oznaceny");
            // tecky 2x2 px: na CAD prilis male -> nahradni proklad, musi byt oznaceny i s duvodem
            var bmp = new Bitmap(60, 40);
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.White); g.FillRectangle(Brushes.Black, 10, 10, 30, 20); g.FillRectangle(Brushes.Black, 50, 20, 2, 2); }
            string tp = Path.Combine(dir, "tecky.png"); bmp.Save(tp); bmp.Dispose();
            var ot = new Obrazek(tp); ot.Rozpoznej(new NastaveniRozpoznani());
            var v3 = ot.VytvorObrysy(new NastaveniObrysu { SirkaMm = 42, MinPlochaMm2 = 0 });
            Console.WriteLine("== tecky: " + v3.Diagnostika);
            Over(v3.Diagnostika.ObrysuCad + v3.Diagnostika.ObrysuNahradnich == ot.Vrstvy[0].Obrysy.Count &&
                 (v3.Diagnostika.ObrysuNahradnich == 0 || v3.Diagnostika.Duvody.Count > 0), "kazdy obrys je bud CAD, nebo oznaceny nahradni (s duvodem)");
            // CAD rekonstrukce, ktera neuspeje, vraci null a duvod (Obrazek pak pouzije nahradni proklad a oznaci ho)
            string duvod; int mimo;
            var maly = new List<P> { new P(0, 0), new P(1, 0), new P(2, 0), new P(2, 1), new P(2, 2), new P(1, 2), new P(0, 2), new P(0, 1) };
            var r0 = Konstrukce.Rekonstruuj(maly, 0.6, 1.0, 3, 0.8, out duvod, out mimo);
            Console.WriteLine("== prilis maly obrys: vysledek {0}, duvod: {1}", r0 == null ? "null" : "obrys", duvod);
            Over(r0 == null && !string.IsNullOrEmpty(duvod), "neuspesna CAD rekonstrukce vraci duvod");
        }
#if !STARA
        // tolerance v mm (jen novejsi verze): musi byt skutecne dodrzena
        var pTol = typeof(NastaveniObrysu).GetField("TolMm");
        foreach (var arg in a.Skip(1).Where(x => x.Contains("motivy")))
            foreach (double tol in new[] { 0.2, 0.5, 0.8, 1.5 })
            {
                if (pTol == null) break;
                var o = new Obrazek(arg); o.Rozpoznej(new NastaveniRozpoznani());
                var n = new NastaveniObrysu { SirkaMm = 500 };
                pTol.SetValue(n, tol);
                var v = o.VytvorObrysy(n);
                var d = v.Diagnostika;
                var px = DoPx(o.Vrstvy[0].Obrysy, v);
                var m = Zmer(px, 1.0);
                // odchylka vuci hrane obrazku (mimo spicky rohu ji pocita diagnostika), v mm
                double maxMm = d.MaxOdchylkaPx * v.MmNaPx, tolSkut = d.TolMaxPx * v.MmNaPx;
                Console.WriteLine(string.Format(Inv, "== motivy, tolerance {0} mm: primek {1}, oblouku {2}, rohu {3}, kruznic {4}; max {5:0.000} mm (pouzita tolerance {6:0.000} mm), pruseciku {7}",
                    tol, m.Primek, m.Oblouku, m.Rohu, CeleKruznice(px).Count, maxMm, tolSkut, d.Samopruseciky + d.PrusecikyObrysu));
                Over(maxMm <= tolSkut * 1.001 && d.Samopruseciky + d.PrusecikyObrysu == 0 && m.Zlomu == 0,
                    string.Format(Inv, "tolerance {0} mm dodrzena, bez pruseciku a zlomu", tol));
            }
#endif
        Console.WriteLine(chyb == 0 ? "VSECHNY KONTROLY PROSLY" : ("NEPROSLO KONTROL: " + chyb));
    }
}
