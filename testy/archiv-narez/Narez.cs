using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ObrysCNC
{
    // Nářezový plán pro formátovací pilu (SCM Sigma 115 Plus): díly se skládají do desek tak, aby šly
    // řezat jen přímými řezy přes celou šířku (gilotina, 3 stupně): podélné pruhy -> příčné sloupce ->
    // dořez dílů ve sloupci. Počítá s prořezem pily, ořezem okrajů desky, léty (díl se nesmí otočit)
    // a hranami pro olepovačku (OTT Tornado+), volitelně odečtenými od rozměru dílu.
    public class Dil
    {
        public string Nazev = "";
        public double Delka, Sirka;       // hotový rozměr; Délka = ve směru let (podél desky X)
        public int Ks = 1;
        public bool Otacet = true;        // false = léta, díl musí zůstat délkou podél desky
        public int HranaD, HranaS;        // počet olepených hran na dlouhých (podél délky) / krátkých stranách (0–2)
    }

    public class NastaveniNarezu
    {
        public double DeskaX = 2800, DeskaY = 2070, Tloustka = 18;
        public double Prorez = 4.4, Orez = 10, Hrana = 2;
        public bool OdecistHranu = true;
    }

    public class Kus
    {
        public string Nazev;
        public double X, Y, W, H;         // v mm na desce (X podél délky desky, Y zdola), řezný rozměr
        public bool Otoceno;
        public int CisloDilu;             // index v seznamu dílů (pro barvu a štítek)
    }

    public class Sloupec { public double X, Sirka; public List<Kus> Kusy = new List<Kus>(); }
    public class Pruh { public double Y, Vyska; public List<Sloupec> Sloupce = new List<Sloupec>(); }

    public class PlanDesky
    {
        public List<Pruh> Pruhy = new List<Pruh>();
        public IEnumerable<Kus> Kusy { get { return Pruhy.SelectMany(p => p.Sloupce).SelectMany(s => s.Kusy); } }
        public double PlochaKusu { get { return Kusy.Sum(k => k.W * k.H); } }
        public bool PruhyPodelX = true;   // pruhy vedou podél délky desky (první řezy podélně)
    }

    public class VysledekNarezu
    {
        public List<PlanDesky> Desky = new List<PlanDesky>();
        public List<string> Nevejde = new List<string>();
        public int Kusu;
        public double PlochaDilu, PlochaDesek, HranyM;
        public double Vyuziti { get { return PlochaDesek > 0 ? PlochaDilu / PlochaDesek : 0; } }
    }

    public static class Narez
    {
        class Polozka { public string Nazev; public double W, H; public bool Rot; public int Cislo; }

        public static double RezDelka(Dil d, NastaveniNarezu n) { return d.Delka - (n.OdecistHranu ? d.HranaS * n.Hrana : 0); }
        public static double RezSirka(Dil d, NastaveniNarezu n) { return d.Sirka - (n.OdecistHranu ? d.HranaD * n.Hrana : 0); }

        public static VysledekNarezu Spocitej(List<Dil> dily, NastaveniNarezu n)
        {
            var v = new VysledekNarezu();
            double W = n.DeskaX - 2 * n.Orez, H = n.DeskaY - 2 * n.Orez;
            var polozky = new List<Polozka>();
            for (int i = 0; i < dily.Count; i++)
            {
                var d = dily[i];
                if (d.Ks <= 0 || d.Delka <= 0 || d.Sirka <= 0) continue;
                double w = RezDelka(d, n), h = RezSirka(d, n);
                bool vejde = (w <= W && h <= H) || (d.Otacet && h <= W && w <= H);
                if (w <= 0 || h <= 0 || !vejde) { v.Nevejde.Add(string.Format("{0} ({1:0.#} × {2:0.#})", d.Nazev, d.Delka, d.Sirka)); continue; }
                v.HranyM += d.Ks * (d.HranaD * d.Delka + d.HranaS * d.Sirka) / 1000.0;
                for (int k = 0; k < d.Ks; k++) polozky.Add(new Polozka { Nazev = d.Nazev, W = w, H = h, Rot = d.Otacet, Cislo = i });
            }
            v.Kusu = polozky.Count;
            v.PlochaDilu = polozky.Sum(p => p.W * p.H) / 1e6;
            if (polozky.Count == 0) return v;

            // vyzkouset vic poradi a orientaci, vybrat nejmene desek (pak nejvic mista na posledni desce)
            List<PlanDesky> nej = null;
            double nejSkore = double.MaxValue;
            foreach (bool podelX in new[] { true, false })
                foreach (int razeni in new[] { 0, 1, 2 })
                    foreach (bool dlouhouPodel in new[] { true, false })
                    {
                        var desky = Rozloz(polozky, W, H, n.Prorez, podelX, razeni, dlouhouPodel);
                        double posledni = desky[desky.Count - 1].PlochaKusu;
                        double skore = desky.Count * 1e13 + posledni;
                        if (skore < nejSkore) { nejSkore = skore; nej = desky; }
                    }
            // posun o orez okraju
            foreach (var d in nej)
                foreach (var k in d.Kusy) { k.X += n.Orez; k.Y += n.Orez; }
            foreach (var d in nej)
                foreach (var p in d.Pruhy)
                {
                    if (d.PruhyPodelX) p.Y += n.Orez; else p.Y += n.Orez;
                    foreach (var s in p.Sloupce) s.X += n.Orez;
                }
            v.Desky = nej;
            v.PlochaDesek = nej.Count * n.DeskaX * n.DeskaY / 1e6;
            return v;
        }

        static List<PlanDesky> Rozloz(List<Polozka> vstup, double W, double H, double kerf, bool podelX, int razeni, bool dlouhouPodel)
        {
            // pruhy pres X: pracuje se primo; pruhy pres Y: deska i dily se transponuji a na konci vrati
            double bw = podelX ? W : H, bh = podelX ? H : W;
            var pol = vstup.Select(p =>
            {
                double w = podelX ? p.W : p.H, h = podelX ? p.H : p.W;
                if (p.Rot && ((dlouhouPodel && h > w) || (!dlouhouPodel && w > h))) { double t = w; w = h; h = t; }
                return new Polozka { Nazev = p.Nazev, W = w, H = h, Rot = p.Rot, Cislo = p.Cislo };
            }).ToList();
            if (razeni == 0) pol = pol.OrderByDescending(p => p.H).ThenByDescending(p => p.W).ToList();
            else if (razeni == 1) pol = pol.OrderByDescending(p => p.W * p.H).ToList();
            else pol = pol.OrderByDescending(p => Math.Max(p.W, p.H)).ThenByDescending(p => p.H).ToList();

            var desky = new List<PlanDesky>();
            while (pol.Count > 0)
            {
                var deska = new PlanDesky { PruhyPodelX = podelX };
                double y = 0;
                while (true)
                {
                    // prvni dil, ktery se jeste vejde, urci vysku pruhu
                    int li = -1; double lw = 0, lh = 0;
                    for (int i = 0; i < pol.Count && li < 0; i++)
                    {
                        var p = pol[i];
                        if (p.W <= bw && p.H <= bh - y) { li = i; lw = p.W; lh = p.H; }
                        else if (p.Rot && p.H <= bw && p.W <= bh - y) { li = i; lw = p.H; lh = p.W; }
                    }
                    if (li < 0) break;
                    var pruh = new Pruh { Y = y, Vyska = lh };
                    var sl = new Sloupec { X = 0, Sirka = lw };
                    sl.Kusy.Add(Novy(pol[li], 0, y, lw, lh, podelX));
                    pruh.Sloupce.Add(sl);
                    pol.RemoveAt(li);
                    double vyskaSloupce = lh, x = 0;
                    bool pridano = true;
                    while (pridano)
                    {
                        pridano = false;
                        for (int i = 0; i < pol.Count; i++)
                        {
                            var p = pol[i];
                            // a) pod sebe do aktualniho sloupce (dorez)
                            double ow, oh;
                            if (Vejde(p, sl.Sirka, lh - vyskaSloupce - kerf, out ow, out oh, true))
                            {
                                sl.Kusy.Add(Novy(p, x, y + vyskaSloupce + kerf, ow, oh, podelX));
                                vyskaSloupce += kerf + oh;
                                pol.RemoveAt(i); pridano = true; break;
                            }
                            // b) novy sloupec vedle (pricny rez)
                            if (Vejde(p, bw - (x + sl.Sirka + kerf), lh, out ow, out oh, false))
                            {
                                x += sl.Sirka + kerf;
                                sl = new Sloupec { X = x, Sirka = ow };
                                sl.Kusy.Add(Novy(p, x, y, ow, oh, podelX));
                                pruh.Sloupce.Add(sl);
                                vyskaSloupce = oh;
                                pol.RemoveAt(i); pridano = true; break;
                            }
                        }
                    }
                    deska.Pruhy.Add(pruh);
                    y += lh + kerf;
                }
                if (deska.Pruhy.Count == 0) break;   // nemelo by nastat (vse se vejde jednotlive)
                desky.Add(deska);
            }
            return desky;
        }

        // vejde se dil do mista sirka x vyska? u sloupce (pod sebe) radeji orientace s mensi vyskou,
        // u noveho sloupce radeji orientace s vyskou co nejbliz vysce pruhu
        static bool Vejde(Polozka p, double sirka, double vyska, out double w, out double h, bool podSebe)
        {
            w = h = 0;
            bool a = p.W <= sirka && p.H <= vyska;
            bool b = p.Rot && p.H <= sirka && p.W <= vyska;
            if (!a && !b) return false;
            if (a && b)
            {
                bool vezmiA = podSebe ? p.H <= p.W : p.H >= p.W;
                if (vezmiA) { w = p.W; h = p.H; } else { w = p.H; h = p.W; }
            }
            else if (a) { w = p.W; h = p.H; }
            else { w = p.H; h = p.W; }
            return true;
        }

        static Kus Novy(Polozka p, double x, double y, double w, double h, bool podelX)
        {
            bool otoceno = Math.Abs(w - p.W) > 1e-9 || Math.Abs(h - p.H) > 1e-9;
            // v transponovanem pripade vratit do souradnic desky (X = delka desky)
            return podelX
                ? new Kus { Nazev = p.Nazev, X = x, Y = y, W = w, H = h, CisloDilu = p.Cislo, Otoceno = otoceno }
                : new Kus { Nazev = p.Nazev, X = y, Y = x, W = h, H = w, CisloDilu = p.Cislo, Otoceno = otoceno };
        }

        // postup rezani pro obsluhu pily
        public static List<string> Postup(PlanDesky d, double orez)
        {
            var r = new List<string>();
            string smer = d.PruhyPodelX ? "podélně (přes celou délku desky)" : "příčně (přes celou šířku desky)";
            r.Add("1. Ořez okrajů " + orez.ToString("0.#") + " mm.");
            r.Add("2. Pruhy " + smer + ": " + string.Join(", ", d.Pruhy.Select(p => p.Vyska.ToString("0.#"))) + " mm.");
            int i = 1;
            foreach (var p in d.Pruhy)
            {
                r.Add(string.Format("   Pruh {0} ({1:0.#} mm): sloupce {2} mm", i++, p.Vyska, string.Join(", ", p.Sloupce.Select(s => s.Sirka.ToString("0.#")))));
                Func<Kus, double> napric = k => d.PruhyPodelX ? k.H : k.W;      // rozmer kusu napric pruhem
                foreach (var s in p.Sloupce.Where(s => s.Kusy.Count > 1 || napric(s.Kusy[0]) + 0.01 < p.Vyska))
                    r.Add("      dořez sloupce " + s.Sirka.ToString("0.#") + ": " + string.Join(", ", s.Kusy.Select(k => napric(k).ToString("0.#"))) + " mm");
            }
            return r;
        }

        // ---------- CSV: Název;Délka;Šířka;Ks;Otáčet;HranaD;HranaŠ ----------
        public static List<Dil> NactiCsv(string soubor)
        {
            var res = new List<Dil>();
            foreach (var radek in File.ReadAllLines(soubor, Encoding.UTF8))
            {
                var c = radek.Split(';');
                if (c.Length < 3) c = radek.Split(',');
                if (c.Length < 3) continue;
                double dl, si;
                if (!Cislo(c[1], out dl) || !Cislo(c[2], out si)) continue;   // hlavicka a prazdne radky
                var d = new Dil { Nazev = c[0].Trim(), Delka = dl, Sirka = si };
                int ks; double x;
                if (c.Length > 3 && int.TryParse(c[3].Trim(), out ks)) d.Ks = ks;
                if (c.Length > 4) d.Otacet = !(c[4].Trim() == "0" || c[4].Trim().ToLowerInvariant() == "ne");
                if (c.Length > 5 && Cislo(c[5], out x)) d.HranaD = (int)Math.Max(0, Math.Min(2, x));
                if (c.Length > 6 && Cislo(c[6], out x)) d.HranaS = (int)Math.Max(0, Math.Min(2, x));
                res.Add(d);
            }
            return res;
        }

        public static void UlozCsv(string soubor, List<Dil> dily)
        {
            var sb = new StringBuilder("Název;Délka;Šířka;Ks;Otáčet;HranaD;HranaŠ\r\n");
            foreach (var d in dily)
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4};{5};{6}\r\n", d.Nazev.Replace(";", ","), d.Delka, d.Sirka, d.Ks, d.Otacet ? 1 : 0, d.HranaD, d.HranaS);
            File.WriteAllText(soubor, sb.ToString(), new UTF8Encoding(true));
        }

        static bool Cislo(string s, out double v)
        {
            s = s.Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
