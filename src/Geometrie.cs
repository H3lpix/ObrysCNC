using System;
using System.Collections.Generic;
using System.Linq;

namespace ObrysCNC
{
    public struct P
    {
        public double X, Y;
        public P(double x, double y) { X = x; Y = y; }
    }

    // Obrys = body + bulge pro segment z bodu i do bodu i+1 (u uzavreneho i posledni -> prvni).
    // bulge 0 = usecka, jinak oblouk (tan(uhel/4), kladny = proti smeru hodin), stejne jako v DXF.
    public class Obrys
    {
        public List<P> Pts = new List<P>();
        public List<double> B = new List<double>();
        public bool Zavreny;
        public string Vrstva = "0";

        public int PocetSegmentu { get { return Zavreny ? Pts.Count : Pts.Count - 1; } }
        public P KonecSegmentu(int i) { return Pts[(i + 1) % Pts.Count]; }
    }

    public class ObloukInfo
    {
        public double Cx, Cy, R, A0, Sw;
    }

    public static class Geo
    {
        public static double Vzd(P a, P b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double Norm(double a)
        {
            while (a <= -Math.PI) a += 2 * Math.PI;
            while (a > Math.PI) a -= 2 * Math.PI;
            return a;
        }

        public static double Mod2Pi(double a)
        {
            double r = a % (2 * Math.PI);
            return r < 0 ? r + 2 * Math.PI : r;
        }

        public static ObloukInfo Oblouk(P a, P b, double bulge)
        {
            double th = 4 * Math.Atan(bulge), c = Vzd(a, b);
            double r = c / (2 * Math.Sin(Math.Abs(th) / 2));
            double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
            double d = r * Math.Cos(Math.Abs(th) / 2);
            double ux = (b.X - a.X) / c, uy = (b.Y - a.Y) / c;
            double s = bulge > 0 ? 1 : -1;
            var o = new ObloukInfo();
            o.Cx = mx - s * uy * d;
            o.Cy = my + s * ux * d;
            o.R = r;
            o.A0 = Math.Atan2(a.Y - o.Cy, a.X - o.Cx);
            o.Sw = th;
            return o;
        }

        public static double SmerNaZacatku(P a, P b, double bulge)
        {
            return Math.Atan2(b.Y - a.Y, b.X - a.X) - 2 * Math.Atan(bulge);
        }

        public static double SmerNaKonci(P a, P b, double bulge)
        {
            return Math.Atan2(b.Y - a.Y, b.X - a.X) + 2 * Math.Atan(bulge);
        }

        // body segmentu vcetne obou koncu
        public static List<P> Rozloz(P a, P b, double bulge, double tetiva)
        {
            var res = new List<P>();
            res.Add(a);
            if (Math.Abs(bulge) > 1e-12 && Vzd(a, b) > 1e-9)
            {
                var o = Oblouk(a, b, bulge);
                double krok = 2 * Math.Acos(Math.Max(-1, 1 - tetiva / o.R));
                int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(o.Sw) / krok));
                n = Math.Min(n, 2000);
                for (int i = 1; i < n; i++)
                {
                    double t = o.A0 + o.Sw * i / n;
                    res.Add(new P(o.Cx + o.R * Math.Cos(t), o.Cy + o.R * Math.Sin(t)));
                }
            }
            res.Add(b);
            return res;
        }

        // vsechny body obrysu, u uzavreneho se posledni bod rovna prvnimu
        public static List<P> RozlozObrys(Obrys o, double tetiva)
        {
            var res = new List<P>();
            for (int i = 0; i < o.PocetSegmentu; i++)
            {
                var r = Rozloz(o.Pts[i], o.KonecSegmentu(i), o.B[i], tetiva);
                res.AddRange(i == 0 ? r : r.Skip(1));
            }
            if (res.Count == 0 && o.Pts.Count > 0) res.Add(o.Pts[0]);
            return res;
        }

        public static double Plocha(Obrys o)
        {
            double s = 0;
            int n = o.Pts.Count;
            for (int i = 0; i < n; i++)
            {
                P a = o.Pts[i], b = o.Pts[(i + 1) % n];
                s += (a.X * b.Y - b.X * a.Y) / 2;
                if (Math.Abs(o.B[i]) > 1e-12)
                {
                    double th = Math.Abs(4 * Math.Atan(o.B[i])), c = Vzd(a, b), r = c / (2 * Math.Sin(th / 2));
                    s += Math.Sign(o.B[i]) * (r * r / 2) * (th - Math.Sin(th));
                }
            }
            return s;
        }

        public static double PlochaPolygonu(List<P> pts)
        {
            double s = 0;
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++) s += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
            return s / 2;
        }

        public static double VzdalenostKSegmentu(P p, P a, P b, double bulge)
        {
            if (Math.Abs(bulge) < 1e-12)
            {
                double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2 : 0;
                t = Math.Max(0, Math.Min(1, t));
                double ex = p.X - a.X - t * dx, ey = p.Y - a.Y - t * dy;
                return Math.Sqrt(ex * ex + ey * ey);
            }
            var o = Oblouk(a, b, bulge);
            double ap = Math.Atan2(p.Y - o.Cy, p.X - o.Cx);
            double rel = o.Sw > 0 ? Mod2Pi(ap - o.A0) : Mod2Pi(o.A0 - ap);
            if (rel <= Math.Abs(o.Sw)) return Math.Abs(Vzd(p, new P(o.Cx, o.Cy)) - o.R);
            return Math.Min(Vzd(p, a), Vzd(p, b));
        }

        public static double VzdalenostKObrysu(P p, Obrys o)
        {
            double m = double.MaxValue;
            for (int k = 0; k < o.PocetSegmentu; k++) m = Math.Min(m, VzdalenostKSegmentu(p, o.Pts[k], o.KonecSegmentu(k), o.B[k]));
            return m;
        }

        // ---------- zjednoduseni ----------

        // vraci indexy ponechanych bodu
        public static List<int> DouglasPeucker(List<P> pts, int od, int @do, double tol)
        {
            var keep = new bool[pts.Count];
            keep[od] = keep[@do] = true;
            var st = new Stack<int[]>();
            st.Push(new[] { od, @do });
            while (st.Count > 0)
            {
                var ab = st.Pop();
                int a = ab[0], b = ab[1], mi = -1;
                double mx = -1;
                for (int i = a + 1; i < b; i++)
                {
                    double d = VzdalenostKSegmentu(pts[i], pts[a], pts[b], 0);
                    if (d > mx) { mx = d; mi = i; }
                }
                if (mx > tol) { keep[mi] = true; st.Push(new[] { a, mi }); st.Push(new[] { mi, b }); }
            }
            var res = new List<int>();
            for (int i = od; i <= @do; i++) if (keep[i]) res.Add(i);
            return res;
        }

        // zjednodusi uzavrenou smycku (bez opakovaneho posledniho bodu); vraci indexy do loop
        public static List<int> ZjednodusSmycku(List<P> loop, double tol)
        {
            int n = loop.Count;
            double cx = loop.Average(q => q.X), cy = loop.Average(q => q.Y);
            int i0 = 0;
            double m = -1;
            for (int i = 0; i < n; i++) { double d = Vzd(loop[i], new P(cx, cy)); if (d > m) { m = d; i0 = i; } }
            var l = new List<P>();
            for (int i = 0; i <= n; i++) l.Add(loop[(i0 + i) % n]);   // vcetne navratu na zacatek
            int i1 = 0;
            m = -1;
            for (int i = 0; i < n; i++) { double d = Vzd(l[i], l[0]); if (d > m) { m = d; i1 = i; } }
            var a = DouglasPeucker(l, 0, i1, tol);
            var b = DouglasPeucker(l, i1, n, tol);
            var res = new List<int>();
            foreach (int k in a) res.Add((k + i0) % n);
            for (int k = 1; k < b.Count - 1; k++) res.Add((b[k] + i0) % n);
            return res;
        }

        // ---------- proklad oblouky ----------

        public static bool ZkusOblouk(List<P> q, int i, int j, double tol, out double bulge)
        {
            bulge = 0;
            P A = q[i], B = q[j];
            bool primka = true;
            for (int k = i + 1; k < j; k++) if (VzdalenostKSegmentu(q[k], A, B, 0) > tol) { primka = false; break; }
            if (primka) return true;
            P M = q[(i + j) / 2];
            double ax = A.X, ay = A.Y, bx = M.X, by = M.Y, cx = B.X, cy = B.Y;
            double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (Math.Abs(d) < 1e-12) return false;
            double ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
            double uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
            double r = Math.Sqrt((ax - ux) * (ax - ux) + (ay - uy) * (ay - uy));
            bool ccw = (M.X - A.X) * (B.Y - M.Y) - (M.Y - A.Y) * (B.X - M.X) > 0;
            double a0 = Math.Atan2(ay - uy, ax - ux), a1 = Math.Atan2(cy - uy, cx - ux);
            double sw = ccw ? Mod2Pi(a1 - a0) : -Mod2Pi(a0 - a1);
            if (Math.Abs(sw) > Math.PI * 1.5) return false;
            // oblouk se nesmi vzdalit ani od puvodnich usecek (stredy tetiv): u ridkych bodu by se jinak vyboulil
            for (int k = i; k < j; k++)
            {
                double sx = (q[k].X + q[k + 1].X) / 2 - ux, sy = (q[k].Y + q[k + 1].Y) / 2 - uy;
                if (Math.Abs(Math.Sqrt(sx * sx + sy * sy) - r) > 3 * tol) return false;
            }
            double posl = 0;
            for (int k = i + 1; k < j; k++)
            {
                double dk = Math.Sqrt((q[k].X - ux) * (q[k].X - ux) + (q[k].Y - uy) * (q[k].Y - uy));
                if (Math.Abs(dk - r) > tol) return false;
                double ak = Math.Atan2(q[k].Y - uy, q[k].X - ux);
                double rel = ccw ? Mod2Pi(ak - a0) : Mod2Pi(a0 - ak);
                if (rel < posl || rel > Math.Abs(sw)) return false;
                posl = rel;
            }
            bulge = Math.Tan(sw / 4);
            return true;
        }

        // proklad behu bodu q usecek/oblouku; pridava segmenty bez posledniho bodu q
        public static void ProlozBeh(List<P> q, double tol, List<P> outP, List<double> outB)
        {
            ProlozBeh(q, tol, tol, outP, outB);
        }

        // tolUsecka: povolena odchylka usecky (vetsi = rovne hrany se srovnaji i pres sum obrazku),
        // tolOblouk: povolena odchylka oblouku (mensi = zaobleni presne kopiruji tvar)
        public static void ProlozBeh(List<P> q, double tolUsecka, double tolOblouk, List<P> outP, List<double> outB)
        {
            double tol = tolOblouk;
            int i = 0;
            while (i < q.Count - 1)
            {
                // nejdelsi usecka
                int jUs = i + 1;
                for (int j = i + 2; j < q.Count; j++)
                {
                    bool ok = true;
                    for (int k = i + 1; k < j && ok; k++) if (VzdalenostKSegmentu(q[k], q[i], q[j], 0) > tolUsecka) ok = false;
                    if (!ok) break;
                    jUs = j;
                }
                // nejdelsi oblouk (nebo usecka)
                int jOb = jUs;
                double bOb = 0, b;
                for (int j = jUs + 1; j < q.Count; j++)
                {
                    if (!ZkusOblouk(q, i, j, tol, out b)) break;
                    jOb = j;
                    bOb = b;
                }
                // oblouk jen kdyz je oblina opravdu znat; skoro rovne hrany zustanou useckami (pak se srovnaji).
                // Mirna krivka: oblouk sahá aspoň 2× dál než úsečka -> radši oblouk (plynulá křivka bez plošek).
                double sipka = Math.Abs(bOb) * Vzd(q[i], q[jOb]) / 2;
                bool oblouk = Math.Abs(bOb) > 1e-12 && jOb > jUs &&
                    (sipka > 2.5 * tol || (jOb - i >= 2 * (jUs - i) && sipka > 0.8 * tol));
                outP.Add(q[i]);
                outB.Add(oblouk ? bOb : 0);
                i = oblouk ? jOb : jUs;
            }
        }

        // hustou uzavrenou smycku (zacinajici v rohu) prolozi useckami a oblouky
        public static Obrys ProlozSmycku(List<P> loop, double tol)
        {
            var q = new List<P>(loop);
            q.Add(loop[0]);
            var o = new Obrys();
            o.Zavreny = true;
            ProlozBeh(q, tol, o.Pts, o.B);
            return o;
        }

        // uhel zlomu ve vrcholu k (stupne)
        public static double Zlom(Obrys o, int k)
        {
            int n = o.Pts.Count;
            if (!o.Zavreny && (k == 0 || k == n - 1)) return 180;
            int pk = (k - 1 + n) % n;
            double t1 = SmerNaKonci(o.Pts[pk], o.Pts[k], o.B[pk]);
            double t2 = SmerNaZacatku(o.Pts[k], o.Pts[(k + 1) % n], o.B[k]);
            return Math.Abs(Norm(t2 - t1)) * 180 / Math.PI;
        }

        // nahradi rady usecek oblouky (zachova rohy a existujici oblouky)
        public static Obrys NahradOblouky(Obrys o, double tol, double roh)
        {
            int n = o.Pts.Count, nseg = o.PocetSegmentu;
            if (n < 3) return o;
            var pevny = new bool[n];
            for (int k = 0; k < n; k++)
            {
                int pk = (k - 1 + n) % n;
                if (!o.Zavreny && (k == 0 || k == n - 1)) pevny[k] = true;
                else if ((o.Zavreny || k > 0) && Math.Abs(o.B[pk]) > 1e-12) pevny[k] = true;
                else if (k < nseg && Math.Abs(o.B[k]) > 1e-12) pevny[k] = true;
                else pevny[k] = Zlom(o, k) > roh;
            }
            int start = Array.IndexOf(pevny, true);
            if (start < 0)
            {
                start = 0;
                for (int k = 1; k < n; k++) if (o.Pts[k].Y < o.Pts[start].Y) start = k;
                pevny[start] = true;
            }
            if (!o.Zavreny) start = 0;
            var res = new Obrys();
            res.Zavreny = o.Zavreny;
            res.Vrstva = o.Vrstva;
            int kk = start;
            for (int s = 0; s < nseg; )
            {
                if (Math.Abs(o.B[kk]) > 1e-12)
                {
                    res.Pts.Add(o.Pts[kk]); res.B.Add(o.B[kk]);
                    kk = (kk + 1) % n; s++;
                    continue;
                }
                var q = new List<P>();
                q.Add(o.Pts[kk]);
                int m = kk;
                do { m = (m + 1) % n; q.Add(o.Pts[m]); s++; } while (s < nseg && !pevny[m] && Math.Abs(o.B[m]) < 1e-12);
                ProlozBeh(q, tol, res.Pts, res.B);
                kk = m;
            }
            if (!o.Zavreny) res.Pts.Add(o.Pts[n - 1]);
            return res;
        }

        // Obnovi ostre rohy, ktere vyhlazeni obrazku zakulatilo: mezi dvema dlouhymi useckami s jasnym
        // zlomem je jen kratky retizek segmentu -> nahradi se pruseckem obou usecek.
        public static Obrys OstreRohy(Obrys o, double maxRetizek, double minUhel)
        {
            if (!o.Zavreny || o.Pts.Count < 4 || maxRetizek <= 0) return o;
            var pts = new List<P>(o.Pts);
            var bl = new List<double>(o.B);
            bool zmena = true;
            int pojistka = 0;
            while (zmena && pojistka++ < 1000)
            {
                zmena = false;
                int n = pts.Count;
                if (n < 4) break;
                Func<int, double> delka = s => Vzd(pts[s % n], pts[(s + 1) % n]);
                Func<int, bool> dlouha = s => Math.Abs(bl[s % n]) < 1e-12 && delka(s) >= 1.5 * maxRetizek;
                for (int i = 0; i < n && !zmena; i++)
                {
                    if (!dlouha(i)) continue;
                    // retizek kratkych segmentu za useckou i
                    double soucet = 0;
                    int j = i + 1, pocet = 0;
                    while (pocet < 6 && !dlouha(j) && soucet <= 2 * maxRetizek) { soucet += delka(j); j++; pocet++; }
                    if (pocet == 0 || pocet >= 6 || soucet > 2 * maxRetizek || !dlouha(j) || (j - i) >= n - 1) continue;
                    P a0 = pts[i % n], a1 = pts[(i + 1) % n], b0 = pts[j % n], b1 = pts[(j + 1) % n];
                    double d1x = a1.X - a0.X, d1y = a1.Y - a0.Y, d2x = b1.X - b0.X, d2y = b1.Y - b0.Y;
                    double uhel = Math.Abs(Norm(Math.Atan2(d2y, d2x) - Math.Atan2(d1y, d1x))) * 180 / Math.PI;
                    if (uhel < minUhel || uhel > 160) continue;
                    double det = d1x * d2y - d1y * d2x;
                    if (Math.Abs(det) < 1e-12) continue;
                    double t = ((b0.X - a0.X) * d2y - (b0.Y - a0.Y) * d2x) / det;
                    var X = new P(a0.X + t * d1x, a0.Y + t * d1y);
                    if (Vzd(X, a1) > 2 * maxRetizek || Vzd(X, b0) > 2 * maxRetizek) continue;
                    // body i+1 .. j nahradit jednim rohem X
                    var np = new List<P>();
                    var nb = new List<double>();
                    for (int k = 0; k < n; k++)
                    {
                        int rel = ((k - i) % n + n) % n;               // poloha vuci useckam
                        if (rel >= 1 && rel <= j - i) continue;
                        np.Add(pts[k]); nb.Add(bl[k]);
                        if (rel == 0) { np.Add(X); nb.Add(0); }
                    }
                    pts = np; bl = nb;
                    zmena = true;
                }
            }
            var r = new Obrys { Zavreny = true, Vrstva = o.Vrstva, Pts = pts, B = bl };
            return r;
        }

        // slouci za sebou jdouci usecky, pokud se prostredni bod odchyluje od spojnice mene nez tol
        public static Obrys SlucUsecky(Obrys o, double tol)
        {
            if (!o.Zavreny || o.Pts.Count < 4) return o;
            var pts = new List<P>(o.Pts);
            var bl = new List<double>(o.B);
            bool zmena = true;
            while (zmena && pts.Count > 3)
            {
                zmena = false;
                double nejOdch = double.MaxValue;
                int nej = -1, n = pts.Count;
                for (int k = 0; k < n; k++)
                {
                    int pk = (k - 1 + n) % n;
                    if (Math.Abs(bl[pk]) > 1e-12 || Math.Abs(bl[k]) > 1e-12) continue;
                    double d = VzdalenostKSegmentu(pts[k], pts[pk], pts[(k + 1) % n], 0);
                    if (d < tol && d < nejOdch) { nejOdch = d; nej = k; }
                }
                if (nej >= 0) { pts.RemoveAt(nej); bl.RemoveAt(nej); zmena = true; }
            }
            return new Obrys { Zavreny = true, Vrstva = o.Vrstva, Pts = pts, B = bl };
        }

        // srovna temer vodorovne/svisle usecky do presne vodorovne/svisle
        public static Obrys Narovnej(Obrys o, double uhelMax, double minDelka)
        {
            if (!o.Zavreny || uhelMax <= 0 || o.Pts.Count < 3) return o;
            int n = o.Pts.Count;
            var typ = new char[n];
            for (int i = 0; i < n; i++)
            {
                typ[i] = ' ';
                if (Math.Abs(o.B[i]) > 1e-12) continue;
                P a = o.Pts[i], b = o.Pts[(i + 1) % n];
                double u = Math.Atan2(Math.Abs(b.Y - a.Y), Math.Abs(b.X - a.X)) * 180 / Math.PI;
                if (u < uhelMax) typ[i] = 'H'; else if (u > 90 - uhelMax) typ[i] = 'V';
            }
            int r = -1;
            for (int i = 0; i < n; i++) if (typ[i] != typ[(i - 1 + n) % n]) { r = i; break; }
            if (r < 0) return o;
            var pts = new List<P>();
            var bl = new List<double>();
            var t = new char[n];
            for (int i = 0; i < n; i++) { pts.Add(o.Pts[(i + r) % n]); bl.Add(o.B[(i + r) % n]); t[i] = typ[(i + r) % n]; }
            var smazat = new bool[n];
            for (int i = 0; i < n; )
            {
                int j = i;
                while (j + 1 < n && t[j + 1] == t[i]) j++;
                if (t[i] != ' ')
                {
                    bool h = t[i] == 'H';
                    double sum = 0, delka = 0;
                    for (int q = i; q <= j; q++)
                    {
                        P a = pts[q], b = pts[(q + 1) % n];
                        double d = Vzd(a, b);
                        sum += d * (h ? (a.Y + b.Y) : (a.X + b.X)) / 2;
                        delka += d;
                    }
                    if (delka >= minDelka && delka > 0)
                    {
                        double c = sum / delka;
                        int e = (j + 1) % n;
                        pts[i] = h ? new P(pts[i].X, c) : new P(c, pts[i].Y);
                        pts[e] = h ? new P(pts[e].X, c) : new P(c, pts[e].Y);
                        for (int q = i + 1; q <= j; q++) smazat[q] = true;
                    }
                }
                i = j + 1;
            }
            var res = new Obrys();
            res.Zavreny = true;
            res.Vrstva = o.Vrstva;
            for (int i = 0; i < n; i++) if (!smazat[i]) { res.Pts.Add(pts[i]); res.B.Add(bl[i]); }
            return res.Pts.Count >= 3 ? res : o;
        }

        // ---------- smer a start ----------

        public static Obrys Obrat(Obrys o)
        {
            int n = o.Pts.Count;
            var res = new Obrys();
            res.Zavreny = o.Zavreny;
            res.Vrstva = o.Vrstva;
            res.Pts = Enumerable.Reverse(o.Pts).ToList();
            if (!o.Zavreny)
            {
                res.B = Enumerable.Reverse(o.B).Select(x => -x).ToList();
                return res;
            }
            var b = new double[n];
            for (int k = 0; k < n - 1; k++) b[k] = -o.B[n - 2 - k];
            b[n - 1] = -o.B[n - 1];
            res.B = b.ToList();
            return res;
        }

        public static bool Uvnitr(P p, Obrys o)
        {
            var poly = RozlozObrys(o, 0.05);
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                P pi = poly[i], pj = poly[j];
                if ((pi.Y > p.Y) != (pj.Y > p.Y) && p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X) c = !c;
            }
            return c;
        }

        // vnejsi obrysy proti smeru hodin, diry po smeru; start v nejvyssim rohu (bez rohu v nejnizsim bode)
        public static Obrys UpravSmerAStart(Obrys o, List<Obrys> vsechny, double roh)
        {
            if (!o.Zavreny || o.Pts.Count < 2) return o;
            int vnoreni = vsechny.Count(x => !ReferenceEquals(x, o) && x.Zavreny && x.Pts.Count > 1 && Uvnitr(o.Pts[0], x));
            bool ccw = Plocha(o) > 0;
            if ((vnoreni % 2 == 0) != ccw) o = Obrat(o);
            int n = o.Pts.Count, s = -1;
            for (int k = 0; k < n; k++)
            {
                if (Zlom(o, k) <= roh) continue;
                if (s < 0 || o.Pts[k].Y > o.Pts[s].Y + 1e-6 || (Math.Abs(o.Pts[k].Y - o.Pts[s].Y) <= 1e-6 && o.Pts[k].X < o.Pts[s].X)) s = k;
            }
            if (s < 0) { s = 0; for (int k = 1; k < n; k++) if (o.Pts[k].Y < o.Pts[s].Y) s = k; }
            var res = new Obrys();
            res.Zavreny = true;
            res.Vrstva = o.Vrstva;
            for (int i = 0; i < n; i++) { res.Pts.Add(o.Pts[(s + i) % n]); res.B.Add(o.B[(s + i) % n]); }
            return res;
        }

        public static void Rozsah(IEnumerable<Obrys> obrysy, out double minx, out double miny, out double maxx, out double maxy)
        {
            minx = miny = double.MaxValue;
            maxx = maxy = double.MinValue;
            foreach (var o in obrysy)
                foreach (var p in RozlozObrys(o, 0.02))
                {
                    minx = Math.Min(minx, p.X); maxx = Math.Max(maxx, p.X);
                    miny = Math.Min(miny, p.Y); maxy = Math.Max(maxy, p.Y);
                }
        }

        // Pocet krizeni hran: v ramci jednoho obrysu (samoprusecik) a mezi obrysy. Oblouky se rozlozi
        // (sipka "tetiva"), dotyk v koncovem bode sousednich useku se nepocita. Mrizka kvuli rychlosti.
        public static void Pruseciky(List<Obrys> obrysy, double tetiva, out int vlastni, out int vzajemne)
        {
            vlastni = 0; vzajemne = 0;
            var A = new List<P>(); var B = new List<P>(); var obr = new List<int>(); var idx = new List<int>(); var poc = new List<int>();
            for (int o = 0; o < obrysy.Count; o++)
            {
                var b = RozlozObrys(obrysy[o], tetiva);
                int n0 = A.Count, n = b.Count - 1;
                for (int i = 0; i < n; i++) { A.Add(b[i]); B.Add(b[i + 1]); obr.Add(o); idx.Add(i); poc.Add(n); }
                if (!obrysy[o].Zavreny) for (int i = n0; i < A.Count; i++) poc[i] = int.MaxValue / 2;   // otevreny: konec nenavazuje na zacatek
            }
            if (A.Count == 0) return;
            double delka = 0;
            for (int i = 0; i < A.Count; i++) delka += Vzd(A[i], B[i]);
            double g = Math.Max(1.0, 4 * delka / A.Count);
            var mriz = new Dictionary<long, List<int>>();
            for (int i = 0; i < A.Count; i++)
            {
                int x0 = (int)Math.Floor(Math.Min(A[i].X, B[i].X) / g), x1 = (int)Math.Floor(Math.Max(A[i].X, B[i].X) / g);
                int y0 = (int)Math.Floor(Math.Min(A[i].Y, B[i].Y) / g), y1 = (int)Math.Floor(Math.Max(A[i].Y, B[i].Y) / g);
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        long kl = ((long)x << 32) ^ (uint)y;
                        List<int> l;
                        if (!mriz.TryGetValue(kl, out l)) { l = new List<int>(); mriz[kl] = l; }
                        l.Add(i);
                    }
            }
            var hotove = new HashSet<long>();
            foreach (var l in mriz.Values)
                for (int a = 0; a < l.Count; a++)
                    for (int b = a + 1; b < l.Count; b++)
                    {
                        int i = Math.Min(l[a], l[b]), j = Math.Max(l[a], l[b]);
                        if (obr[i] == obr[j])
                        {
                            int d = Math.Abs(idx[i] - idx[j]);
                            if (d <= 1 || d >= poc[i] - 1) continue;            // sousedni useky se dotykaji v koncich
                        }
                        if (!hotove.Add(((long)i << 32) | (uint)j)) continue;
                        if (!Krizi(A[i], B[i], A[j], B[j])) continue;
                        if (obr[i] == obr[j]) vlastni++; else vzajemne++;
                    }
        }

        static bool Krizi(P a, P b, P c, P d)
        {
            Func<P, P, P, double> or = (p, q, r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            double d1 = or(a, b, c), d2 = or(a, b, d), d3 = or(c, d, a), d4 = or(c, d, b);
            const double e = 1e-12;
            return ((d1 > e && d2 < -e) || (d1 < -e && d2 > e)) && ((d3 > e && d4 < -e) || (d3 < -e && d4 > e));
        }

        public static Obrys Posun(Obrys o, double dx, double dy)
        {
            var r = new Obrys();
            r.Zavreny = o.Zavreny;
            r.Vrstva = o.Vrstva;
            r.B = new List<double>(o.B);
            r.Pts = o.Pts.Select(p => new P(p.X + dx, p.Y + dy)).ToList();
            return r;
        }
    }
}
