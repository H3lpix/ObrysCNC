using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ObrysCNC
{
    public class DxfData
    {
        public List<Obrys> Prvky = new List<Obrys>();
        public Dictionary<string, int> BarvyVrstev = new Dictionary<string, int>();
        public Dictionary<string, int> Nepodporovane = new Dictionary<string, int>();
        public Dictionary<string, int> Typy = new Dictionary<string, int>();
    }

    public class VysledekOpravy
    {
        public string Soubor;
        public DxfData Puvodni;
        public List<Obrys> Obrysy = new List<Obrys>();
        public double MaxOdchylka;
        public string Souhrn;
    }

    public class OvereniDxf
    {
        public bool Ok;                     // soubor obsahuje presne ty entity, ktere jsou v pameti
        public int Obrysu, Zavrenych, Oblouku, Usecek;
        public double MaxRozdilBodu;        // mm (zaokrouhleni souradnic)
        public double MaxRozdilPolomeru;    // mm
        public double MaxNetecnost;         // st., napojeni tecna v pameti, zmerena v souboru
    }

    public static class Dxf
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static double Num(string s)
        {
            double v;
            return double.TryParse(s.Trim(), NumberStyles.Float, Inv, out v) ? v : 0;
        }

        class Ent
        {
            public string Typ;
            public List<KeyValuePair<int, string>> D = new List<KeyValuePair<int, string>>();
            public string Get(int k) { foreach (var p in D) if (p.Key == k) return p.Value; return null; }
            public double N(int k, double def) { var s = Get(k); return s == null ? def : Num(s); }
        }

        static List<Ent> Sekce(List<KeyValuePair<int, string>> pary, string jmeno)
        {
            var res = new List<Ent>();
            int i = -1;
            for (int k = 0; k + 1 < pary.Count; k++)
                if (pary[k].Key == 0 && pary[k].Value == "SECTION" && pary[k + 1].Value == jmeno) { i = k + 2; break; }
            if (i < 0) return res;
            Ent cur = null;
            for (int k = i; k < pary.Count; k++)
            {
                if (pary[k].Key == 0)
                {
                    if (pary[k].Value == "ENDSEC") break;
                    cur = new Ent { Typ = pary[k].Value };
                    res.Add(cur);
                }
                else if (cur != null) cur.D.Add(pary[k]);
            }
            return res;
        }

        public static DxfData Nacti(string soubor)
        {
            var radky = File.ReadAllLines(soubor, Encoding.GetEncoding(1252));
            var pary = new List<KeyValuePair<int, string>>();
            for (int i = 0; i + 1 < radky.Length; i += 2)
            {
                int k;
                if (!int.TryParse(radky[i].Trim(), out k)) continue;
                pary.Add(new KeyValuePair<int, string>(k, radky[i + 1].Trim()));
            }
            var data = new DxfData();
            foreach (var e in Sekce(pary, "TABLES"))
                if (e.Typ == "LAYER" && e.Get(2) != null)
                    data.BarvyVrstev[e.Get(2)] = Math.Abs((int)e.N(62, 7));

            var ents = Sekce(pary, "ENTITIES");
            for (int j = 0; j < ents.Count; j++)
            {
                var e = ents[j];
                string vrstva = e.Get(8) ?? "0";
                bool zrcadlo = e.N(230, 1) < 0;
                Func<double, double> X = x => zrcadlo ? -x : x;
                Obrys o = null;
                if (e.Typ == "LINE")
                {
                    o = new Obrys { Vrstva = vrstva };
                    o.Pts.Add(new P(X(e.N(10, 0)), e.N(20, 0)));
                    o.Pts.Add(new P(X(e.N(11, 0)), e.N(21, 0)));
                    o.B.Add(0);
                }
                else if (e.Typ == "CIRCLE")
                {
                    double cx = X(e.N(10, 0)), cy = e.N(20, 0), r = e.N(40, 0);
                    o = new Obrys { Vrstva = vrstva, Zavreny = true };
                    o.Pts.Add(new P(cx - r, cy)); o.Pts.Add(new P(cx + r, cy));
                    o.B.Add(1); o.B.Add(1);
                }
                else if (e.Typ == "ARC")
                {
                    double cx = e.N(10, 0), cy = e.N(20, 0), r = e.N(40, 0), a0 = e.N(50, 0), a1 = e.N(51, 0);
                    if (zrcadlo) { cx = -cx; double t = a0; a0 = 180 - a1; a1 = 180 - t; }
                    double sw = ((a1 - a0) % 360 + 360) % 360;
                    if (sw == 0) sw = 360;
                    o = new Obrys { Vrstva = vrstva };
                    o.Pts.Add(new P(cx + r * Math.Cos(a0 * Math.PI / 180), cy + r * Math.Sin(a0 * Math.PI / 180)));
                    o.Pts.Add(new P(cx + r * Math.Cos((a0 + sw) * Math.PI / 180), cy + r * Math.Sin((a0 + sw) * Math.PI / 180)));
                    o.B.Add(Math.Tan(sw * Math.PI / 720));
                }
                else if (e.Typ == "POLYLINE")
                {
                    o = new Obrys { Vrstva = vrstva, Zavreny = ((int)e.N(70, 0) & 1) == 1 };
                    while (j + 1 < ents.Count && ents[j + 1].Typ == "VERTEX")
                    {
                        var v = ents[++j];
                        if (((int)v.N(70, 0) & 16) != 0) continue;     // ridici body splajnu
                        o.Pts.Add(new P(X(v.N(10, 0)), v.N(20, 0)));
                        o.B.Add(zrcadlo ? -v.N(42, 0) : v.N(42, 0));    // zrcadleni otaci smer oblouku
                    }
                    if (j + 1 < ents.Count && ents[j + 1].Typ == "SEQEND") j++;
                }
                else if (e.Typ == "LWPOLYLINE")
                {
                    o = new Obrys { Vrstva = vrstva, Zavreny = ((int)e.N(70, 0) & 1) == 1 };
                    foreach (var p in e.D)
                    {
                        if (p.Key == 10) { o.Pts.Add(new P(X(Num(p.Value)), 0)); o.B.Add(0); }
                        else if (p.Key == 20 && o.Pts.Count > 0) o.Pts[o.Pts.Count - 1] = new P(o.Pts[o.Pts.Count - 1].X, Num(p.Value));
                        else if (p.Key == 42 && o.B.Count > 0) o.B[o.B.Count - 1] = zrcadlo ? -Num(p.Value) : Num(p.Value);
                    }
                }
                else if (e.Typ != "SEQEND" && e.Typ != "VERTEX")
                {
                    int c; data.Nepodporovane.TryGetValue(e.Typ, out c); data.Nepodporovane[e.Typ] = c + 1;
                }
                if (o == null) continue;
                if ((e.Typ == "POLYLINE" || e.Typ == "LWPOLYLINE") && !o.Zavreny && o.B.Count > 0) o.B.RemoveAt(o.B.Count - 1);
                if (o.Pts.Count < 2) continue;
                int cnt; data.Typy.TryGetValue(e.Typ, out cnt); data.Typy[e.Typ] = cnt + 1;
                data.Prvky.Add(o);
            }
            return data;
        }

        // spoji navazujici otevrene kousky do obrysu (jen v ramci jedne vrstvy)
        public static List<Obrys> Spoj(List<Obrys> prvky, double tol)
        {
            var hotove = new List<Obrys>();
            foreach (var e in prvky.Where(x => x.Zavreny))
                hotove.Add(new Obrys { Pts = new List<P>(e.Pts), B = new List<double>(e.B), Zavreny = true, Vrstva = e.Vrstva });
            var ot = prvky.Where(x => !x.Zavreny).ToList();
            var pouzit = new bool[ot.Count];
            for (int s = 0; s < ot.Count; s++)
            {
                if (pouzit[s]) continue;
                pouzit[s] = true;
                var pts = new List<P>(ot[s].Pts);
                var b = new List<double>(ot[s].B);
                while (true)
                {
                    if (pts.Count > 2 && Geo.Vzd(pts[0], pts[pts.Count - 1]) <= tol) break;
                    bool nasel = false;
                    for (int k = 0; k < ot.Count && !nasel; k++)
                    {
                        if (pouzit[k] || ot[k].Vrstva != ot[s].Vrstva) continue;
                        var e = ot[k];
                        P z = e.Pts[0], k2 = e.Pts[e.Pts.Count - 1], konec = pts[pts.Count - 1], zacatek = pts[0];
                        var rp = Enumerable.Reverse(e.Pts).ToList();
                        var rb = Enumerable.Reverse(e.B).Select(x => -x).ToList();
                        if (Geo.Vzd(z, konec) <= tol) { pts.AddRange(e.Pts.Skip(1)); b.AddRange(e.B); nasel = true; }
                        else if (Geo.Vzd(k2, konec) <= tol) { pts.AddRange(rp.Skip(1)); b.AddRange(rb); nasel = true; }
                        else if (Geo.Vzd(k2, zacatek) <= tol) { pts.InsertRange(0, e.Pts.Take(e.Pts.Count - 1)); b.InsertRange(0, e.B); nasel = true; }
                        else if (Geo.Vzd(z, zacatek) <= tol) { pts.InsertRange(0, rp.Take(rp.Count - 1)); b.InsertRange(0, rb); nasel = true; }
                        if (nasel) pouzit[k] = true;
                    }
                    if (!nasel) break;
                }
                bool zavreny = pts.Count > 2 && Geo.Vzd(pts[0], pts[pts.Count - 1]) <= tol;
                if (zavreny) pts.RemoveAt(pts.Count - 1);
                hotove.Add(new Obrys { Pts = pts, B = b, Zavreny = zavreny, Vrstva = ot[s].Vrstva });
            }
            // odstraneni nulovych segmentu
            foreach (var o in hotove)
            {
                var P2 = new List<P>();
                var B2 = new List<double>();
                int n = o.Pts.Count, nseg = o.PocetSegmentu;
                for (int i = 0; i < n; i++)
                {
                    if (i < nseg && Geo.Vzd(o.Pts[i], o.Pts[(i + 1) % n]) < 1e-6) continue;
                    P2.Add(o.Pts[i]);
                    if (i < nseg) B2.Add(o.B[i]);
                }
                o.Pts = P2; o.B = B2;
            }
            return hotove.Where(o => o.Pts.Count >= 2).ToList();
        }

        public static VysledekOpravy Oprav(string soubor, double tolOblouku, double tolSpoje, double roh)
        {
            return Oprav(soubor, tolOblouku, tolSpoje, roh, 0, false);
        }

        public static VysledekOpravy Oprav(string soubor, double tolOblouku, double tolSpoje, double roh, double vyskaDesky, bool yNahore)
        {
            var v = new VysledekOpravy { Soubor = soubor, Puvodni = Nacti(soubor) };
            v.Puvodni.Prvky = SouradniceStroje.Preved(v.Puvodni.Prvky, vyskaDesky, yNahore);
            foreach (var vrstva in v.Puvodni.Prvky.Select(p => p.Vrstva).Distinct().ToList())
            {
                var sp = Spoj(v.Puvodni.Prvky.Where(p => p.Vrstva == vrstva).ToList(), tolSpoje)
                    .Select(o => Geo.NahradOblouky(o, tolOblouku, roh)).ToList();
                foreach (var o in sp) v.Obrysy.Add(Geo.UpravSmerAStart(o, sp, roh));
            }
            // kontrola presnosti
            foreach (var p in v.Puvodni.Prvky)
                foreach (var q in Geo.RozlozObrys(p, 0.05))
                {
                    double m = double.MaxValue;
                    foreach (var o in v.Obrysy.Where(x => x.Vrstva == p.Vrstva)) m = Math.Min(m, Geo.VzdalenostKObrysu(q, o));
                    if (m < double.MaxValue) v.MaxOdchylka = Math.Max(v.MaxOdchylka, m);
                }
            // i obracene: novy obrys se nesmi vzdalit od puvodni geometrie
            foreach (var o in v.Obrysy)
                foreach (var q in Geo.RozlozObrys(o, 0.05))
                {
                    double m = double.MaxValue;
                    foreach (var p in v.Puvodni.Prvky.Where(x => x.Vrstva == o.Vrstva)) m = Math.Min(m, Geo.VzdalenostKObrysu(q, p));
                    if (m < double.MaxValue) v.MaxOdchylka = Math.Max(v.MaxOdchylka, m);
                }
            var sb = new StringBuilder();
            sb.AppendFormat("{0}\r\n", Path.GetFileName(soubor));
            sb.AppendFormat("  původně: {0} kousků ({1}), {2} bodů\r\n", v.Puvodni.Prvky.Count,
                string.Join(", ", v.Puvodni.Typy.Select(t => t.Key + " " + t.Value)), v.Puvodni.Prvky.Sum(p => p.Pts.Count));
            if (v.Puvodni.Nepodporovane.Count > 0)
                sb.AppendFormat("  POZOR, nezpracováno: {0}\r\n", string.Join(", ", v.Puvodni.Nepodporovane.Select(t => t.Key + " " + t.Value)));
            int otevrenych = v.Obrysy.Count(o => !o.Zavreny);
            sb.AppendFormat("  nově: {0} obrysů ({1} uzavřených{2}), {3} oblouků, {4} úseček\r\n", v.Obrysy.Count, v.Obrysy.Count - otevrenych,
                otevrenych > 0 ? ", " + otevrenych + " OTEVŘENÝCH" : "",
                v.Obrysy.Sum(o => o.B.Count(x => Math.Abs(x) > 1e-12)), v.Obrysy.Sum(o => o.B.Count(x => Math.Abs(x) <= 1e-12)));
            double minx, miny, maxx, maxy;
            Geo.Rozsah(v.Obrysy, out minx, out miny, out maxx, out maxy);
            sb.AppendFormat("  rozměr {0:0.0} × {1:0.0} mm, max. odchylka od originálu {2:0.000} mm\r\n", maxx - minx, maxy - miny, v.MaxOdchylka);
            v.Souhrn = sb.ToString();
            return v;
        }

        static string F(double x) { return (Math.Abs(x) < 5e-9 ? 0 : x).ToString("0.00000000", Inv); }

        // Nazev vrstvy pro DXF R12: soubor je ASCII, takze diakritika se prevede na zakladni pismena
        // (FRÉZA -> FREZA) a ostatni nepovolene znaky na podtrzitko. Jinak by z nich v souboru byly otazniky.
        public static string NazevVrstvy(string nazev)
        {
            var sb = new StringBuilder();
            foreach (char ch in (nazev ?? "").Trim().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-' || ch == '$';
                sb.Append(ok ? ch : '_');
            }
            return sb.Length > 0 ? sb.ToString() : "0";
        }

        // zapis DXF R12 (POLYLINE s bulge); bezOblouku = oblouky rozlozi na kratke usecky
        public static void Zapis(string soubor, List<Obrys> obrysy, Dictionary<string, int> barvyVrstev, bool bezOblouku)
        {
            double minx, miny, maxx, maxy;
            Geo.Rozsah(obrysy, out minx, out miny, out maxx, out maxy);
            if (obrysy.Count == 0) { minx = miny = maxx = maxy = 0; }
            var sb = new StringBuilder();
            Action<int, string> g = (c, v) => { sb.Append(c).Append("\r\n").Append(v).Append("\r\n"); };
            // barva vrstvy se hleda podle puvodniho nazvu, do souboru jde nazev bez diakritiky
            var barvy = new Dictionary<string, int>();
            foreach (var o in obrysy)
            {
                string nv = NazevVrstvy(o.Vrstva);
                int cb;
                if (!barvy.ContainsKey(nv)) barvy[nv] = barvyVrstev != null && barvyVrstev.TryGetValue(o.Vrstva ?? "", out cb) ? cb : 7;
            }
            var vrstvy = barvy.Keys.ToList();
            g(0, "SECTION"); g(2, "HEADER");
            g(9, "$ACADVER"); g(1, "AC1009");
            g(9, "$INSBASE"); g(10, "0.0"); g(20, "0.0"); g(30, "0.0");
            g(9, "$EXTMIN"); g(10, F(minx)); g(20, F(miny)); g(30, "0.0");
            g(9, "$EXTMAX"); g(10, F(maxx)); g(20, F(maxy)); g(30, "0.0");
            g(0, "ENDSEC");
            g(0, "SECTION"); g(2, "TABLES");
            g(0, "TABLE"); g(2, "LTYPE"); g(70, "1");
            g(0, "LTYPE"); g(2, "CONTINUOUS"); g(70, "0"); g(3, "Solid line"); g(72, "65"); g(73, "0"); g(40, "0.0");
            g(0, "ENDTAB");
            g(0, "TABLE"); g(2, "LAYER"); g(70, vrstvy.Count.ToString(Inv));
            foreach (var v in vrstvy)
            {
                g(0, "LAYER"); g(2, v); g(70, "0"); g(62, barvy[v].ToString(Inv)); g(6, "CONTINUOUS");
            }
            g(0, "ENDTAB");
            g(0, "ENDSEC");
            g(0, "SECTION"); g(2, "ENTITIES");
            foreach (var o in obrysy)
            {
                var pts = o.Pts;
                var b = o.B;
                if (bezOblouku)
                {
                    pts = new List<P>(); b = new List<double>();
                    for (int i = 0; i < o.PocetSegmentu; i++)
                    {
                        var r = Geo.Rozloz(o.Pts[i], o.KonecSegmentu(i), o.B[i], 0.01);
                        for (int k = 0; k < r.Count - 1; k++) { pts.Add(r[k]); b.Add(0); }
                    }
                    if (!o.Zavreny) pts.Add(o.Pts[o.Pts.Count - 1]);
                }
                string vr = NazevVrstvy(o.Vrstva);
                g(0, "POLYLINE"); g(8, vr); g(66, "1"); g(10, "0.0"); g(20, "0.0"); g(30, "0.0"); g(70, o.Zavreny ? "1" : "0");
                for (int i = 0; i < pts.Count; i++)
                {
                    g(0, "VERTEX"); g(8, vr); g(10, F(pts[i].X)); g(20, F(pts[i].Y)); g(30, "0.0");
                    if (i < b.Count && Math.Abs(b[i]) > 1e-9) g(42, b[i].ToString("0.000000000000", Inv));
                }
                g(0, "SEQEND"); g(8, vr);
            }
            g(0, "ENDSEC");
            g(0, "EOF");
            File.WriteAllText(soubor, sb.ToString(), Encoding.ASCII);
        }

        // Kontrola ulozeneho souboru: DXF se znovu nacte a porovna s geometrii v pameti - pocty entit,
        // polohy bodu (zaokrouhleni na 4 desetinna mista), polomery oblouku a tecnost napojeni po zaokrouhleni.
        public static OvereniDxf Over(string soubor, List<Obrys> obrysy)
        {
            var v = new OvereniDxf();
            var zpet = Nacti(soubor).Prvky;
            v.Obrysu = zpet.Count;
            v.Zavrenych = zpet.Count(o => o.Zavreny);
            v.Oblouku = zpet.Sum(o => o.B.Count(b => Math.Abs(b) > 1e-9));
            v.Usecek = zpet.Sum(o => o.B.Count(b => Math.Abs(b) <= 1e-9));
            v.Ok = zpet.Count == obrysy.Count;
            for (int k = 0; k < Math.Min(zpet.Count, obrysy.Count); k++)
            {
                var a = obrysy[k]; var b = zpet[k];
                if (a.Pts.Count != b.Pts.Count || a.Zavreny != b.Zavreny || a.B.Count != b.B.Count) { v.Ok = false; continue; }
                for (int i = 0; i < a.Pts.Count; i++) v.MaxRozdilBodu = Math.Max(v.MaxRozdilBodu, Geo.Vzd(a.Pts[i], b.Pts[i]));
                for (int i = 0; i < a.PocetSegmentu; i++)
                {
                    bool oa = Math.Abs(a.B[i]) > 1e-9, ob = Math.Abs(b.B[i]) > 1e-9;
                    if (oa != ob || (oa && Math.Sign(a.B[i]) != Math.Sign(b.B[i]))) { v.Ok = false; continue; }
                    if (oa)
                    {
                        double ra = Geo.Oblouk(a.Pts[i], a.KonecSegmentu(i), a.B[i]).R, rb = Geo.Oblouk(b.Pts[i], b.KonecSegmentu(i), b.B[i]).R;
                        v.MaxRozdilPolomeru = Math.Max(v.MaxRozdilPolomeru, Math.Abs(ra - rb));
                    }
                }
                for (int i = 0; i < a.Pts.Count; i++)
                {
                    if (!a.Zavreny && (i == 0 || i == a.Pts.Count - 1)) continue;
                    if (Geo.Zlom(a, i) < 0.05) v.MaxNetecnost = Math.Max(v.MaxNetecnost, Geo.Zlom(b, i));
                }
            }
            if (v.MaxRozdilBodu > 1e-3 || v.MaxRozdilPolomeru > 1e-3 || v.MaxNetecnost > 0.01) v.Ok = false;
            return v;
        }

        // nejblizsi zakladni barva AutoCADu (ACI) k barve RGB
        public static int Aci(System.Drawing.Color c)
        {
            var tab = new[] {
                new[] { 1, 255, 0, 0 }, new[] { 2, 255, 255, 0 }, new[] { 3, 0, 255, 0 }, new[] { 4, 0, 255, 255 },
                new[] { 5, 0, 0, 255 }, new[] { 6, 255, 0, 255 }, new[] { 7, 0, 0, 0 }, new[] { 8, 128, 128, 128 },
                new[] { 9, 192, 192, 192 }, new[] { 30, 255, 127, 0 }, new[] { 7, 255, 255, 255 } };
            int best = 7;
            double bd = double.MaxValue;
            foreach (var t in tab)
            {
                double d = Math.Pow(c.R - t[1], 2) + Math.Pow(c.G - t[2], 2) + Math.Pow(c.B - t[3], 2);
                if (d < bd) { bd = d; best = t[0]; }
            }
            return best;
        }
    }
}
