using System;
using System.Collections.Generic;
using System.Linq;

namespace ObrysCNC
{
    // Hladky obrys ve stylu Potrace (P. Selinger, potrace.sourceforge.net):
    //  1. lomena cara s co nejmene useky (rovna hrana = jeden usek i pri sumu obrazku),
    //  2. vrcholy posunute do pruseciku primek prolozenych body hran,
    //  3. u kazdeho vrcholu "alpha": ostry roh, nebo hladka Bezierova krivka tecne navazujici na hrany,
    //  4. hladky obrys se hustě navzorkuje a mezi rohy prolozi useckami/oblouky s malou toleranci
    //     (obdoba "opttolerance" v Potrace a "Fit Curves to Vectors" ve Vectric).
    public static class Hladke
    {
        public static Obrys Vyhlad(List<P> loop, double tol, double alphaMax, double narovnat, bool jenUsecky)
        {
            var poly = LomenaCara(loop, tol);
            if (poly.Count < 3) return null;
            var po = new Obrys { Zavreny = true, Pts = poly, B = poly.Select(p => 0.0).ToList() };
            po = Geo.Narovnej(po, narovnat, 6);
            poly = BezDuplicit(po.Pts);
            int m = poly.Count;
            if (m < 3) return null;

            var smer = new P[m];
            var delka = new double[m];
            for (int j = 0; j < m; j++)
            {
                P a = poly[j], b = poly[(j + 1) % m];
                delka[j] = Geo.Vzd(a, b);
                smer[j] = new P((b.X - a.X) / delka[j], (b.Y - a.Y) / delka[j]);
            }
            var roh = new bool[m];
            var alfa = new double[m];
            for (int j = 0; j < m; j++)
            {
                alfa[j] = Alfa(poly[(j - 1 + m) % m], poly[j], poly[(j + 1) % m]);
                roh[j] = alfa[j] >= alphaMax;
            }

            // husté vzorky hladkeho obrysu; indexy rohu si pamatujeme (tam se proklad nesmi slit)
            var vz = new List<P>();
            var rohyVz = new List<int>();
            double krok = 0.35;
            for (int j = 0; j < m; j++)
            {
                int pj = (j - 1 + m) % m;
                P v = poly[j];
                P konec;
                if (roh[j])
                {
                    rohyVz.Add(vz.Count);
                    vz.Add(v);
                    konec = v;
                }
                else
                {
                    double dIn = Math.Min(delka[pj] / 2, delka[j]);
                    double dOut = Math.Min(delka[j] / 2, delka[pj]);
                    P s = new P(v.X - dIn * smer[pj].X, v.Y - dIn * smer[pj].Y);
                    P e = new P(v.X + dOut * smer[j].X, v.Y + dOut * smer[j].Y);
                    double a = Math.Max(0.55, Math.Min(1.0, alfa[j]));
                    P c1 = new P(s.X + a * (v.X - s.X), s.Y + a * (v.Y - s.Y));
                    P c2 = new P(e.X + a * (v.X - e.X), e.Y + a * (v.Y - e.Y));
                    int n = Math.Max(4, (int)Math.Ceiling((dIn + dOut) / krok));
                    for (int k = 0; k <= n; k++) vz.Add(Bez(s, c1, c2, e, (double)k / n));
                    konec = e;
                }
                // rovny kus k zacatku dalsiho useku
                int nj = (j + 1) % m;
                double dInDalsi = Math.Min(delka[j] / 2, delka[nj]);          // stejne jako dIn u vrcholu nj
                P dalsi = roh[nj] ? poly[nj] : new P(poly[nj].X - dInDalsi * smer[j].X, poly[nj].Y - dInDalsi * smer[j].Y);
                double l = Geo.Vzd(konec, dalsi);
                int nl = (int)Math.Floor(l / 2.0);
                for (int k = 1; k < nl; k++) vz.Add(new P(konec.X + (dalsi.X - konec.X) * k / nl, konec.Y + (dalsi.Y - konec.Y) * k / nl));
            }
            vz = BezDuplicitIdx(vz, rohyVz);
            int nv = vz.Count;
            if (nv < 4) return null;

            double tolFit = Math.Min(0.3, 0.5 * tol);
            var res = new Obrys { Zavreny = true };
            if (rohyVz.Count == 0) rohyVz.Add(0);
            for (int r = 0; r < rohyVz.Count; r++)
            {
                int od = rohyVz[r], @do = rohyVz[(r + 1) % rohyVz.Count];
                if (@do <= od) @do += nv;
                var beh = new List<P>();
                for (int k = od; k <= @do; k++) beh.Add(vz[k % nv]);
                if (jenUsecky)
                {
                    var idx = Geo.DouglasPeucker(beh, 0, beh.Count - 1, tolFit);
                    for (int k = 0; k < idx.Count - 1; k++) { res.Pts.Add(beh[idx[k]]); res.B.Add(0); }
                }
                else Geo.ProlozBeh(beh, tolFit, res.Pts, res.B);
            }
            res = Vycisti(res);
            return res.Pts.Count >= 3 ? res : null;
        }

        // Verny proklad: rohy urci Potrace (alpha), ale krivky mezi rohy se prokladaji primo na skutecnou
        // (subpixelovou) hranu obrazku s malou toleranci -> zaobleni kopiruji obrazek, porad z oblouku.
        public static Obrys VyhladVerne(List<P> loop, double tol, double alphaMax, double narovnat, bool jenUsecky)
        {
            int n = loop.Count;
            if (n < 8) return null;
            var poly = LomenaCara(loop, tol);
            if (poly.Count < 3) return null;
            var po = Geo.Narovnej(new Obrys { Zavreny = true, Pts = poly, B = poly.Select(p => 0.0).ToList() }, narovnat, 6);
            poly = BezDuplicit(po.Pts);
            int m = poly.Count;
            double tolFit = Math.Max(0.12, Math.Min(0.3, 0.4 * tol));

            // rohy: vrchol lomene cary s alpha >= alphaMax, prirazeny k nejblizsimu bodu huste hrany
            var rohy = new List<KeyValuePair<int, P>>();
            for (int j = 0; j < m && m >= 3; j++)
            {
                if (Alfa(poly[(j - 1 + m) % m], poly[j], poly[(j + 1) % m]) < alphaMax) continue;
                int best = 0; double bd = double.MaxValue;
                for (int i = 0; i < n; i++) { double d = Geo.Vzd(loop[i], poly[j]); if (d < bd) { bd = d; best = i; } }
                rohy.Add(new KeyValuePair<int, P>(best, poly[j]));
            }
            rohy = rohy.GroupBy(r => r.Key).Select(g => g.First()).OrderBy(r => r.Key).ToList();

            var res = new Obrys { Zavreny = true };
            double tolUsecka = Math.Max(tolFit, tol);           // rovne hrany se srovnaji i pres sum obrazku
            Action<List<P>> proloz = beh =>
            {
                if (jenUsecky)
                {
                    var idx = Geo.DouglasPeucker(beh, 0, beh.Count - 1, tolFit);
                    for (int k = 0; k < idx.Count - 1; k++) { res.Pts.Add(beh[idx[k]]); res.B.Add(0); }
                }
                else Geo.ProlozBeh(beh, tolUsecka, tolFit, res.Pts, res.B);
            };
            if (rohy.Count == 0)
            {
                // hladky tvar bez rohu: cela smycka primo z huste hrany (vyhlazene podel delky)
                var beh = VyhladPodelHrany(loop, 1.3, true);
                beh.Add(beh[0]);
                proloz(beh);
            }
            else
            {
                const int orez = 3;   // body u rohu zakulacene vyhlazenim se neprokladaji, roh je vrchol
                for (int r = 0; r < rohy.Count; r++)
                {
                    int od = rohy[r].Key, @do = rohy[(r + 1) % rohy.Count].Key;
                    if (@do <= od) @do += n;
                    res.Pts.Add(rohy[r].Value); res.B.Add(0);
                    if (@do - od > 2 * orez + 2)
                    {
                        var beh = new List<P>();
                        for (int k = od + orez; k <= @do - orez; k++) beh.Add(loop[k % n]);
                        beh = VyhladPodelHrany(beh, 1.3, false);
                        proloz(beh);
                        res.Pts.Add(beh[beh.Count - 1]); res.B.Add(0);
                    }
                }
            }
            res = Vycisti(res);
            res = Geo.Narovnej(res, narovnat, 6);
            return res.Pts.Count >= 3 ? res : null;
        }

        // Gaussovo vyhlazeni bodu podel hrany (sigma v bodech, body jsou ~1 px od sebe): odstrani sum
        // obrazku, tvar zaobleni zustane. U otevreneho useku zustanou krajni body na miste a okno se u kraju zuzi.
        internal static List<P> VyhladPodelHrany(List<P> b, double sigma, bool uzavrena)
        {
            int n = b.Count, r = (int)Math.Ceiling(2.5 * sigma);
            if (n < 5) return new List<P>(b);
            var vahy = new double[2 * r + 1];
            for (int k = -r; k <= r; k++) vahy[k + r] = Math.Exp(-k * k / (2 * sigma * sigma));
            var res = new List<P>(n);
            for (int i = 0; i < n; i++)
            {
                int rr = uzavrena ? r : Math.Min(r, Math.Min(i, n - 1 - i));   // symetricke okno -> konce se neposouvaji
                double sx = 0, sy = 0, sw = 0;
                for (int k = -rr; k <= rr; k++)
                {
                    var q = b[((i + k) % n + n) % n];
                    double w = vahy[k + r];
                    sx += w * q.X; sy += w * q.Y; sw += w;
                }
                res.Add(new P(sx / sw, sy / sw));
            }
            return res;
        }

        // lomena cara: DP, pak slucovani vrcholu (co nejmene useku v toleranci), pak upresneni vrcholu
        static List<P> LomenaCara(List<P> loop, double tol)
        {
            int n = loop.Count;
            var idx = Geo.ZjednodusSmycku(loop, tol);
            idx.Sort();
            double dMax = tol + 0.3;
            while (idx.Count > 3)
            {
                int m = idx.Count, nej = -1;
                double nejOdch = double.MaxValue;
                for (int a = 0; a < m; a++)
                {
                    int od = idx[(a - 1 + m) % m], @do = idx[(a + 1) % m];
                    if (@do <= od) @do += n;
                    P A = loop[od % n], B = loop[@do % n];
                    double mx = 0;
                    for (int k = od + 1; k < @do && mx <= dMax; k++) mx = Math.Max(mx, Geo.VzdalenostKSegmentu(loop[k % n], A, B, 0));
                    if (mx < nejOdch) { nejOdch = mx; nej = a; }
                }
                if (nej < 0 || nejOdch > dMax) break;
                idx.RemoveAt(nej);
            }
            // skoro rovne vrcholy (alpha = 0, sum obrazku) pryc, pokud hrana porad sedi na body
            double dMax2 = tol + 0.6;
            bool zmena = true;
            while (zmena && idx.Count > 3)
            {
                zmena = false;
                int m = idx.Count;
                for (int a = 0; a < m && idx.Count > 3; a++)
                {
                    int ia = idx[(a - 1 + m) % m], ib = idx[a], ic = idx[(a + 1) % m];
                    if (Alfa(loop[ia], loop[ib], loop[ic]) > 0) continue;
                    int od = ia, @do = ic;
                    if (@do <= od) @do += n;
                    P A = loop[od % n], B = loop[@do % n];
                    double mx = 0;
                    for (int k = od + 1; k < @do && mx <= dMax2; k++) mx = Math.Max(mx, Geo.VzdalenostKSegmentu(loop[k % n], A, B, 0));
                    if (mx > dMax2) continue;
                    idx.RemoveAt(a);
                    zmena = true;
                    break;
                }
            }
            int mm = idx.Count;
            if (mm < 3) return idx.Select(i => loop[i]).ToList();

            // primky prolozene body kazde hrany (PCA), vrcholy do jejich pruseciku
            var bodHrany = new P[mm];
            var smerHrany = new P[mm];
            for (int a = 0; a < mm; a++)
            {
                int od = idx[a], @do = idx[(a + 1) % mm];
                if (@do <= od) @do += n;
                double sx = 0, sy = 0; int c = 0;
                for (int k = od; k <= @do; k++) { sx += loop[k % n].X; sy += loop[k % n].Y; c++; }
                double mx = sx / c, my = sy / c, xx = 0, yy = 0, xy = 0;
                for (int k = od; k <= @do; k++) { double dx = loop[k % n].X - mx, dy = loop[k % n].Y - my; xx += dx * dx; yy += dy * dy; xy += dx * dy; }
                double uhel = 0.5 * Math.Atan2(2 * xy, xx - yy);
                bodHrany[a] = new P(mx, my);
                smerHrany[a] = new P(Math.Cos(uhel), Math.Sin(uhel));
            }
            var res = new List<P>();
            double maxPosun = 1.5 * Math.Max(0.5, tol);
            for (int a = 0; a < mm; a++)
            {
                int pa = (a - 1 + mm) % mm;
                P puvodni = loop[idx[a]];
                P x;
                if (Prusecik(bodHrany[pa], smerHrany[pa], bodHrany[a], smerHrany[a], out x) && Geo.Vzd(x, puvodni) <= maxPosun) res.Add(x);
                else res.Add(puvodni);
            }
            return BezDuplicit(res);
        }

        static bool Prusecik(P p1, P d1, P p2, P d2, out P x)
        {
            x = p1;
            double det = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(det) < 0.05) return false;
            double t = ((p2.X - p1.X) * d2.Y - (p2.Y - p1.Y) * d2.X) / det;
            x = new P(p1.X + t * d1.X, p1.Y + t * d1.Y);
            return true;
        }

        static List<P> BezDuplicit(List<P> pts)
        {
            var r = new List<P>();
            foreach (var p in pts) if (r.Count == 0 || Geo.Vzd(r[r.Count - 1], p) > 1e-6) r.Add(p);
            while (r.Count > 1 && Geo.Vzd(r[0], r[r.Count - 1]) <= 1e-6) r.RemoveAt(r.Count - 1);
            return r;
        }

        // jako BezDuplicit, ale prepocita indexy rohu
        static List<P> BezDuplicitIdx(List<P> pts, List<int> rohy)
        {
            var r = new List<P>();
            var mapa = new int[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                if (r.Count == 0 || Geo.Vzd(r[r.Count - 1], pts[i]) > 1e-6) r.Add(pts[i]);
                mapa[i] = r.Count - 1;
            }
            int posl = r.Count;
            while (r.Count > 1 && Geo.Vzd(r[0], r[r.Count - 1]) <= 1e-6) r.RemoveAt(r.Count - 1);
            for (int i = 0; i < rohy.Count; i++) { int v = mapa[rohy[i]]; rohy[i] = v >= r.Count ? 0 : v; }
            var u = rohy.Distinct().OrderBy(x => x).ToList();
            rohy.Clear(); rohy.AddRange(u);
            return r;
        }

        // ostrost vrcholu podle Potrace: 0 = rovne, >= 4/3 = velmi ostry roh
        static double Alfa(P i, P j, P k)
        {
            double ry = Math.Sign(k.X - i.X), rx = -Math.Sign(k.Y - i.Y);
            double denom = ry * (k.X - i.X) - rx * (k.Y - i.Y);
            if (Math.Abs(denom) < 1e-12) return 4.0 / 3;
            double dd = Math.Abs((j.X - i.X) * (k.Y - i.Y) - (k.X - i.X) * (j.Y - i.Y)) / denom;
            double a = dd > 1 ? 1 - 1 / dd : 0;
            return a / 0.75;
        }

        static P Bez(P p0, P p1, P p2, P p3, double t)
        {
            double u = 1 - t;
            double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
            return new P(a * p0.X + b * p1.X + c * p2.X + d * p3.X, a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
        }

        static Obrys Vycisti(Obrys o)
        {
            var P1 = new List<P>();
            var B1 = new List<double>();
            int n = o.Pts.Count;
            for (int i = 0; i < n; i++)
            {
                P a = o.Pts[i], b = o.Pts[(i + 1) % n];
                double c = Geo.Vzd(a, b);
                if (c < 1e-4) continue;
                double b0 = o.B[i];
                if (Math.Abs(b0) * c / 2 < 0.35) b0 = 0;                 // skoro rovny oblouk (sipka < 0,35 px) = usecka
                P1.Add(a); B1.Add(b0);
            }
            return Geo.SlucUsecky(new Obrys { Zavreny = true, Pts = P1, B = B1 }, 0.35);
        }
    }
}
