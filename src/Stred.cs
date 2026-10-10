using System;
using System.Collections.Generic;
using System.Linq;

namespace ObrysCNC
{
    // Stredova cara (centerline) pro kresby: tvar se ztenci na kostru siroku 1 px (Zhang-Suen),
    // kostra se rozdeli na drahy mezi konci a krizovatkami, kratke vybezky (artefakty ztenceni) se
    // odstrani, drahy se spoji pres krizovatky v co nejrovnejsim smeru (mene zvednuti frezy),
    // vyhladi a prolozi useckami a oblouky. Vysledkem jsou otevrene (nebo uzavrene) drahy.
    public static class Stred
    {
        class Draha
        {
            public List<int> Body = new List<int>();   // indexy pixelu
            public bool Uzavrena;
        }

        public static List<Obrys> Cary(bool[] maska, int W, int H, double tol, double minDelkaPx, out double sirkaPx)
        {
            var dt = Obrazek.Edt(maska, W, H);
            var sk = (bool[])maska.Clone();
            // okraj obrazku vynulovat (sousede mimo obrazek)
            for (int x = 0; x < W; x++) { sk[x] = false; sk[(H - 1) * W + x] = false; }
            for (int y = 0; y < H; y++) { sk[y * W] = false; sk[y * W + W - 1] = false; }
            ZhangSuen(sk, W, H);

            var sirky = new List<double>();
            for (int i = 0; i < W * H; i++) if (sk[i]) sirky.Add(2 * dt[i] - 1);
            sirky.Sort();
            sirkaPx = sirky.Count > 0 ? sirky[sirky.Count / 2] : double.NaN;

            // vybezky kratsi nez ~ sirka tahu jsou artefakty ztenceni (rohy, konce car)
            double vybezek = Math.Max(3, double.IsNaN(sirkaPx) ? 3 : 1.2 * sirkaPx);
            List<Draha> drahy = null;
            for (int kolo = 0; kolo < 3; kolo++)
            {
                drahy = Trasuj(sk, W, H);
                bool zmena = false;
                foreach (var d in drahy)
                {
                    if (d.Uzavrena || d.Body.Count >= vybezek) continue;
                    int a = d.Body[0], b = d.Body[d.Body.Count - 1];
                    int sa = Stupen(sk, a, W), sb = Stupen(sk, b, W);
                    bool volnyA = sa <= 1, volnyB = sb <= 1;
                    if (volnyA == volnyB) continue;                // jen vetve s jednim volnym koncem
                    for (int k = 0; k < d.Body.Count; k++)
                    {
                        int p = d.Body[k];
                        if (Stupen(sk, p, W) >= 3) continue;       // krizovatku nechat
                        sk[p] = false;
                    }
                    zmena = true;
                }
                if (!zmena) break;
                ZhangSuen(sk, W, H);
            }
            drahy = Trasuj(sk, W, H);
            drahy = Spoj(drahy, sk, W);

            var res = new List<Obrys>();
            foreach (var d in drahy)
            {
                var body = d.Body.Select(p => new P(p % W + 0.5, p / W + 0.5)).ToList();
                if (body.Count < 2) continue;
                double delka = 0;
                for (int k = 1; k < body.Count; k++) delka += Geo.Vzd(body[k - 1], body[k]);
                if (delka < minDelkaPx) continue;
                body = Vyhlad(body, d.Uzavrena);
                Obrys o;
                if (d.Uzavrena && body.Count >= 4)
                {
                    o = Geo.ProlozSmycku(body, tol);
                }
                else
                {
                    o = new Obrys { Zavreny = false };
                    Geo.ProlozBeh(body, tol, o.Pts, o.B);
                    o.Pts.Add(body[body.Count - 1]);
                }
                if (o.Pts.Count >= 2) res.Add(o);
            }
            return res;
        }

        // sousede bez "schodu": diagonalni soused jen kdyz k nemu nevede cesta pres pravouhleho souseda
        static IEnumerable<int> Sousede(bool[] sk, int p, int W)
        {
            int[] ox = { 1, 0, -1, 0 }, oy = { 0, 1, 0, -1 };
            for (int k = 0; k < 4; k++) { int q = p + ox[k] + oy[k] * W; if (sk[q]) yield return q; }
            int[] dx = { 1, -1, -1, 1 }, dy = { 1, 1, -1, -1 };
            for (int k = 0; k < 4; k++)
            {
                int q = p + dx[k] + dy[k] * W;
                if (!sk[q]) continue;
                if (sk[p + dx[k]] || sk[p + dy[k] * W]) continue;
                yield return q;
            }
        }

        static int Stupen(bool[] sk, int p, int W) { return Sousede(sk, p, W).Count(); }

        static List<Draha> Trasuj(bool[] sk, int W, int H)
        {
            var res = new List<Draha>();
            var hrany = new HashSet<long>();
            Func<int, int, long> hrana = (a, b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            var navstiveno = new bool[W * H];
            for (int p = 0; p < W * H; p++)
            {
                if (!sk[p]) continue;
                int st = Stupen(sk, p, W);
                if (st == 2) continue;
                if (st == 0) { res.Add(new Draha { Body = { p } }); navstiveno[p] = true; continue; }
                foreach (int q0 in Sousede(sk, p, W).ToList())
                {
                    if (hrany.Contains(hrana(p, q0))) continue;
                    var d = new Draha();
                    d.Body.Add(p);
                    int pred = p, cur = q0;
                    hrany.Add(hrana(p, q0));
                    navstiveno[p] = true;
                    while (true)
                    {
                        d.Body.Add(cur);
                        navstiveno[cur] = true;
                        if (Stupen(sk, cur, W) != 2) break;
                        int dalsi = -1;
                        foreach (int q in Sousede(sk, cur, W)) if (q != pred && !hrany.Contains(hrana(cur, q))) { dalsi = q; break; }
                        if (dalsi < 0) break;
                        hrany.Add(hrana(cur, dalsi));
                        pred = cur; cur = dalsi;
                    }
                    res.Add(d);
                }
            }
            // zbyle smycky (vsechny body stupne 2)
            for (int p = 0; p < W * H; p++)
            {
                if (!sk[p] || navstiveno[p]) continue;
                var d = new Draha { Uzavrena = true };
                int pred = -1, cur = p;
                while (true)
                {
                    d.Body.Add(cur);
                    navstiveno[cur] = true;
                    int dalsi = -1;
                    foreach (int q in Sousede(sk, cur, W)) if (q != pred && !navstiveno[q]) { dalsi = q; break; }
                    if (dalsi < 0) break;
                    pred = cur; cur = dalsi;
                }
                if (d.Body.Count >= 3) res.Add(d);
            }
            return res;
        }

        // spoji drahy v krizovatkach: dvojice s nejrovnejsim pokracovanim (mene zvednuti frezy)
        static List<Draha> Spoj(List<Draha> drahy, bool[] sk, int W)
        {
            var otevrene = drahy.Where(d => !d.Uzavrena && d.Body.Count >= 2).ToList();
            var hotove = drahy.Where(d => d.Uzavrena || d.Body.Count < 2).ToList();
            bool zmena = true;
            while (zmena)
            {
                zmena = false;
                // konce drah podle pixelu
                var konce = new Dictionary<int, List<KeyValuePair<Draha, bool>>>();
                foreach (var d in otevrene)
                {
                    Pridej(konce, d.Body[0], d, true);
                    Pridej(konce, d.Body[d.Body.Count - 1], d, false);
                }
                foreach (var kv in konce)
                {
                    var list = kv.Value.Where(x => otevrene.Contains(x.Key)).ToList();
                    if (list.Count < 2) continue;
                    double nej = -2; int ia = -1, ib = -1;
                    for (int a = 0; a < list.Count; a++)
                        for (int b = a + 1; b < list.Count; b++)
                        {
                            if (ReferenceEquals(list[a].Key, list[b].Key)) continue;
                            P sa = Smer(list[a].Key, list[a].Value, W), sb = Smer(list[b].Key, list[b].Value, W);
                            double c = -(sa.X * sb.X + sa.Y * sb.Y);            // 1 = primo proti sobe = rovne pokracovani
                            if (c > nej) { nej = c; ia = a; ib = b; }
                        }
                    if (ia < 0 || (list.Count > 2 && nej < 0.3)) continue;     // v krizovatce jen kdyz to jde rovne
                    var A = list[ia].Key; var B = list[ib].Key;
                    var ba = new List<int>(A.Body); if (list[ia].Value) ba.Reverse();       // A konci v uzlu
                    var bb = new List<int>(B.Body); if (!list[ib].Value) bb.Reverse();      // B zacina v uzlu
                    ba.AddRange(bb.Skip(1));
                    var nova = new Draha { Body = ba, Uzavrena = ba.Count > 3 && ba[0] == ba[ba.Count - 1] };
                    if (nova.Uzavrena) nova.Body.RemoveAt(nova.Body.Count - 1);
                    otevrene.Remove(A); otevrene.Remove(B);
                    if (nova.Uzavrena) hotove.Add(nova); else otevrene.Add(nova);
                    zmena = true;
                    break;
                }
            }
            hotove.AddRange(otevrene);
            return hotove;
        }

        static void Pridej(Dictionary<int, List<KeyValuePair<Draha, bool>>> d, int p, Draha dr, bool zacatek)
        {
            List<KeyValuePair<Draha, bool>> l;
            if (!d.TryGetValue(p, out l)) { l = new List<KeyValuePair<Draha, bool>>(); d[p] = l; }
            l.Add(new KeyValuePair<Draha, bool>(dr, zacatek));
        }

        // jednotkovy smer od konce drahy dovnitr (asi 6 px)
        static P Smer(Draha d, bool zacatek, int W)
        {
            int n = d.Body.Count, k = Math.Min(6, n - 1);
            int a = zacatek ? d.Body[0] : d.Body[n - 1], b = zacatek ? d.Body[k] : d.Body[n - 1 - k];
            double dx = b % W - a % W, dy = b / W - a / W, l = Math.Sqrt(dx * dx + dy * dy);
            return l > 0 ? new P(dx / l, dy / l) : new P(1, 0);
        }

        // klouzavy prumer (2x okno 5 bodu), konce otevrene drahy zustanou
        static List<P> Vyhlad(List<P> b, bool uzavrena)
        {
            int n = b.Count;
            if (n < 5) return b;
            var cur = b;
            for (int kolo = 0; kolo < 2; kolo++)
            {
                var nova = new List<P>(n);
                for (int i = 0; i < n; i++)
                {
                    if (!uzavrena && (i < 2 || i > n - 3)) { nova.Add(cur[i]); continue; }
                    double sx = 0, sy = 0;
                    for (int k = -2; k <= 2; k++) { var q = cur[((i + k) % n + n) % n]; sx += q.X; sy += q.Y; }
                    nova.Add(new P(sx / 5, sy / 5));
                }
                cur = nova;
            }
            return cur;
        }

        // ztenceni Zhang-Suen na kostru sirokou 1 px
        static void ZhangSuen(bool[] m, int W, int H)
        {
            var smazat = new List<int>();
            bool zmena = true;
            while (zmena)
            {
                zmena = false;
                for (int krok = 0; krok < 2; krok++)
                {
                    smazat.Clear();
                    for (int y = 1; y < H - 1; y++)
                        for (int x = 1; x < W - 1; x++)
                        {
                            int i = y * W + x;
                            if (!m[i]) continue;
                            bool p2 = m[i - W], p3 = m[i - W + 1], p4 = m[i + 1], p5 = m[i + W + 1],
                                 p6 = m[i + W], p7 = m[i + W - 1], p8 = m[i - 1], p9 = m[i - W - 1];
                            int b = (p2 ? 1 : 0) + (p3 ? 1 : 0) + (p4 ? 1 : 0) + (p5 ? 1 : 0) + (p6 ? 1 : 0) + (p7 ? 1 : 0) + (p8 ? 1 : 0) + (p9 ? 1 : 0);
                            if (b < 2 || b > 6) continue;
                            int a = (!p2 && p3 ? 1 : 0) + (!p3 && p4 ? 1 : 0) + (!p4 && p5 ? 1 : 0) + (!p5 && p6 ? 1 : 0) +
                                    (!p6 && p7 ? 1 : 0) + (!p7 && p8 ? 1 : 0) + (!p8 && p9 ? 1 : 0) + (!p9 && p2 ? 1 : 0);
                            if (a != 1) continue;
                            if (krok == 0) { if ((p2 && p4 && p6) || (p4 && p6 && p8)) continue; }
                            else { if ((p2 && p4 && p8) || (p2 && p6 && p8)) continue; }
                            smazat.Add(i);
                        }
                    foreach (int i in smazat) m[i] = false;
                    if (smazat.Count > 0) zmena = true;
                }
            }
        }
    }
}
