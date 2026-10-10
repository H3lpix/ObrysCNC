using System;
using System.Collections.Generic;
using System.Linq;

namespace ObrysCNC
{
    // Upravy tvaru pres vzdalenostni mapu (robustni i pro prekryvy a samoprotinani):
    // obrysy se vyplni do jemne mrizky (pravidlo nenulového vinuti), spocita se znamenkova vzdalenost
    // od hrany a novy obrys se vytahne na pozadovane vzdalenosti a vyhladi (Hladke).
    //  - odsazeni ven (+) / dovnitr (-)
    //  - slouceni prekryvajicich se tvaru (odsazeni 0)
    //  - zaobleni rohu polomerem r (uzavreni + otevreni)
    public static class Rastr
    {
        public static List<Obrys> Uprav(List<Obrys> obrysy, double odsazeniMm, double zaobleniMm, double alphaMax)
        {
            var vysledek = new List<Obrys>();
            foreach (var skupina in obrysy.Where(o => o.Zavreny && o.Pts.Count >= 2).GroupBy(o => o.Vrstva))
            {
                var zavrene = skupina.ToList();
                // jednotna orientace kvuli pravidlu vinuti: dira = obrys CELY uvnitr jineho
                // (castecne prekryte tvary nejsou diry, ty se maji sloucit)
                zavrene = Orientuj(zavrene);
                double minx, miny, maxx, maxy;
                Geo.Rozsah(zavrene, out minx, out miny, out maxx, out maxy);
                double okraj = Math.Abs(odsazeniMm) + 2 * Math.Abs(zaobleniMm) + 1;
                double rozmer = Math.Max(maxx - minx, maxy - miny) + 2 * okraj;
                double h = Math.Max(0.02, Math.Min(0.25, rozmer / 3000));     // velikost bunky v mm
                int W = (int)Math.Ceiling((maxx - minx + 2 * okraj) / h) + 4;
                int H = (int)Math.Ceiling((maxy - miny + 2 * okraj) / h) + 4;
                double x0 = minx - okraj - 2 * h, y1 = maxy + okraj + 2 * h;
                var maska = Vypln(zavrene, W, H, x0, y1, h);

                double r = zaobleniMm / h;
                if (r > 0.5)
                {
                    maska = Prah(Vzdalenost(maska, W, H), r);      // uzavreni: vnitrni rohy se zaobli
                    maska = Prah(Vzdalenost(maska, W, H), -r);
                    maska = Prah(Vzdalenost(maska, W, H), -r);     // otevreni: vnejsi rohy se zaobli
                    maska = Prah(Vzdalenost(maska, W, H), r);
                }
                var sd = Vzdalenost(maska, W, H);
                double R = odsazeniMm / h;
                var f = new float[W * H];
                for (int i = 0; i < f.Length; i++) f[i] = (float)(R - sd[i] + 0.5);
                var smycky = Obrazek.MarchingSquares(f, W, H).Where(l => l.Count >= 4 && Math.Abs(Geo.PlochaPolygonu(l)) >= 4).ToList();
                var vysl = new Obrys[smycky.Count];
                string vrstva = skupina.Key;
                System.Threading.Tasks.Parallel.For(0, smycky.Count, i =>
                {
                    // CAD: primky + tecne oblouky (odsazeny roh = presny oblouk); kdyz to nejde, hladky proklad
                    string duvod; int mimo;
                    var o = Konstrukce.Rekonstruuj(smycky[i], 0.5, alphaMax, 0, 0.6, out duvod, out mimo) ?? Hladke.Vyhlad(smycky[i], 0.5, alphaMax, 0, false);
                    if (o == null) return;
                    vysl[i] = new Obrys
                    {
                        Zavreny = true,
                        Vrstva = vrstva,
                        Pts = o.Pts.Select(p => new P(x0 + p.X * h, y1 - p.Y * h)).ToList(),
                        B = o.B.Select(b => -b).ToList()
                    };
                });
                var nove = vysl.Where(o => o != null).ToList();
                vysledek.AddRange(nove.Select(o => Geo.UpravSmerAStart(o, nove, 25)));
            }
            return vysledek;
        }

        static List<Obrys> Orientuj(List<Obrys> obrysy)
        {
            var vzorky = obrysy.Select(o => Geo.RozlozObrys(o, 0.5)).ToList();
            var res = new List<Obrys>();
            for (int i = 0; i < obrysy.Count; i++)
            {
                int hloubka = 0;
                for (int j = 0; j < obrysy.Count; j++)
                {
                    if (i == j) continue;
                    var o = obrysy[j];
                    if (vzorky[i].All(p => Geo.Uvnitr(p, o))) hloubka++;
                }
                var x = obrysy[i];
                bool ccw = Geo.Plocha(x) > 0;
                if ((hloubka % 2 == 0) != ccw) x = Geo.Obrat(x);
                res.Add(x);
            }
            return res;
        }

        // vyplneni obrysu do mrizky (stredy bunek), pravidlo nenuloveho vinuti
        static bool[] Vypln(List<Obrys> obrysy, int W, int H, double x0, double y1, double h)
        {
            var hrany = new List<double[]>();   // x1, y1, x2, y2
            foreach (var o in obrysy)
            {
                var b = Geo.RozlozObrys(o, h * 0.25);
                for (int i = 0; i + 1 < b.Count; i++) hrany.Add(new[] { b[i].X, b[i].Y, b[i + 1].X, b[i + 1].Y });
                if (b.Count > 1 && Geo.Vzd(b[0], b[b.Count - 1]) > 1e-9) hrany.Add(new[] { b[b.Count - 1].X, b[b.Count - 1].Y, b[0].X, b[0].Y });
            }
            // hrany podle radku, aby se nemusely prochazet vsechny pro kazdy radek
            var poRadcich = new List<int>[H];
            for (int e = 0; e < hrany.Count; e++)
            {
                var g = hrany[e];
                double ya = Math.Min(g[1], g[3]), yb = Math.Max(g[1], g[3]);
                int ja = Math.Max(0, (int)Math.Floor((y1 - yb) / h - 0.5)), jb = Math.Min(H - 1, (int)Math.Ceiling((y1 - ya) / h - 0.5));
                for (int j = ja; j <= jb; j++) { if (poRadcich[j] == null) poRadcich[j] = new List<int>(); poRadcich[j].Add(e); }
            }
            var m = new bool[W * H];
            var pruseciky = new List<KeyValuePair<double, int>>();
            for (int j = 0; j < H; j++)
            {
                if (poRadcich[j] == null) continue;
                double yc = y1 - (j + 0.5) * h;
                pruseciky.Clear();
                foreach (int e in poRadcich[j])
                {
                    var g = hrany[e];
                    bool nahoru = g[3] > g[1];
                    double ya = nahoru ? g[1] : g[3], yb = nahoru ? g[3] : g[1];
                    if (!(yc >= ya && yc < yb)) continue;
                    double x = g[0] + (yc - g[1]) * (g[2] - g[0]) / (g[3] - g[1]);
                    pruseciky.Add(new KeyValuePair<double, int>(x, nahoru ? 1 : -1));
                }
                if (pruseciky.Count < 2) continue;
                pruseciky.Sort((a, b) => a.Key.CompareTo(b.Key));
                int vinuti = 0;
                for (int k = 0; k + 1 < pruseciky.Count; k++)
                {
                    vinuti += pruseciky[k].Value;
                    if (vinuti == 0) continue;
                    int ia = Math.Max(0, (int)Math.Ceiling((pruseciky[k].Key - x0) / h - 0.5));
                    int ib = Math.Min(W - 1, (int)Math.Floor((pruseciky[k + 1].Key - x0) / h - 0.5));
                    for (int i = ia; i <= ib; i++) m[j * W + i] = true;
                }
            }
            return m;
        }

        // znamenkova vzdalenost v bunkach: uvnitr zaporna, venku kladna, hrana ~ 0
        static double[] Vzdalenost(bool[] m, int W, int H)
        {
            var dIn = Obrazek.Edt(m, W, H);
            var neg = new bool[m.Length];
            for (int i = 0; i < m.Length; i++) neg[i] = !m[i];
            var dOut = Obrazek.Edt(neg, W, H);
            var sd = new double[m.Length];
            for (int i = 0; i < m.Length; i++) sd[i] = m[i] ? -(dIn[i] - 0.5) : (dOut[i] - 0.5);
            return sd;
        }

        static bool[] Prah(double[] sd, double t)
        {
            var m = new bool[sd.Length];
            for (int i = 0; i < sd.Length; i++) m[i] = sd[i] < t;
            return m;
        }
    }
}
