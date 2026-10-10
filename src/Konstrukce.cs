using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ObrysCNC
{
    // Souhrn kvality prevodu za vsechny obrysy (zobrazi se v programu, vypisuji ho testy).
    // Vse v px obrazku; na mm se prevadi az pri zobrazeni (MmNaPx).
    public class Diagnostika
    {
        public int ObrysuCad, ObrysuNahradnich, ObrysuHladkych;
        // vysledna (interni) geometrie
        public int Primek, Oblouku, Rohu, Zlomu, Kratkych, PrvkuMimoTol, Kruznic;
        public P MaxOdchylkaMisto;                     // kde je nejvetsi odchylka (px obrazku)
        public double MaxOdchylkaPx, MaxZlom;          // MaxZlom = nejvetsi uhel napojeni, ktere melo byt tecne (st.)
        public double SumaOdchylek;
        public long PocetBodu;
        // pred optimalizaci entit (vsechny rozpoznane useky)
        public int PrimekPred, ObloukuPred, RohuPred;
        public double MaxOdchylkaPredPx, SumaPred;
        public long PocetPred;
        // zdrojovy rastr a tolerance
        public double SumHranyPx;                      // sum hrany obrazku (nejvetsi z obrysu, RMS px)
        public double TolMaxPx;                        // pouzita tolerance (px)
        public bool TolOmezena;                        // pozadovana tolerance je pod rozlisenim obrazku
        public int Samopruseciky, PrusecikyObrysu;
        public int UsecekAprox;                        // "jen usecky": kolik usecek jde do DXF misto oblouku
        public List<double> TemerKruznicePx = new List<double>();   // obrysy skoro kruhove, ale za toleranci: potrebna tolerance (px)
        public List<string> Duvody = new List<string>();

        public double PrumerOdchylkyPx { get { return PocetBodu > 0 ? SumaOdchylek / PocetBodu : 0; } }
        public double PrumerPredPx { get { return PocetPred > 0 ? SumaPred / PocetPred : 0; } }

        class Mereni
        {
            public int Primek, Oblouku, Rohu, Zlomu, Kratkych;
            public double MaxZlom, Max, Suma;
            public P MaxMisto;
            public long Pocet;
        }

        // pocty entit, rohy, zlomy a odchylka obrysu (px) od huste hrany obrazku; spicky ostrych rohu,
        // ktere rozmazani obrazku zakulatilo, se do odchylky nepocitaji
        static Mereni Zmer(Obrys o, List<P> hrana, double zonaRohu, double sigma)
        {
            var m = new Mereni();
            int n = o.Pts.Count;
            var rohy = new List<P>();
            var zony = new List<double>();
            for (int k = 0; k < n; k++)
            {
                if (!o.Zavreny && (k == 0 || k == n - 1)) continue;
                double z = Geo.Zlom(o, k);
                if (z >= 15) { m.Rohu++; rohy.Add(o.Pts[k]); zony.Add(Konstrukce.ZonaRohu(zonaRohu, sigma, z * Math.PI / 180)); }
                else
                {
                    if (z >= 0.05) m.Zlomu++;
                    m.MaxZlom = Math.Max(m.MaxZlom, z);
                }
            }
            int ns = o.PocetSegmentu;
            var box = new double[ns, 4];
            for (int i = 0; i < ns; i++)
            {
                P a = o.Pts[i], b = o.KonecSegmentu(i);
                double c = Geo.Vzd(a, b);
                if (Math.Abs(o.B[i]) > 1e-12) m.Oblouku++; else m.Primek++;
                if (c < 1.0) m.Kratkych++;
                double bb = Math.Abs(o.B[i]);
                double rez = bb <= 1 ? bb * c / 2 : c * (1 + bb * bb) / (2 * bb);     // sipka oblouku (nad 180 st.: prumer)
                box[i, 0] = Math.Min(a.X, b.X) - rez; box[i, 1] = Math.Min(a.Y, b.Y) - rez;
                box[i, 2] = Math.Max(a.X, b.X) + rez; box[i, 3] = Math.Max(a.Y, b.Y) + rez;
            }
            if (hrana == null || ns == 0) return m;
            // spicky rohu: presah pres obrazek po odecteni ustupu spicky rozmazanim
            for (int r = 0; r < rohy.Count; r++)
            {
                double dmin = hrana.Min(p => Geo.Vzd(p, rohy[r]));
                double z = 0;
                for (int k = 0; k < n; k++) if (o.Pts[k].X == rohy[r].X && o.Pts[k].Y == rohy[r].Y) z = Geo.Zlom(o, k);
                double presah = dmin - Konstrukce.UstupSpicky(sigma, z * Math.PI / 180);
                if (presah > m.Max) { m.Max = presah; m.MaxMisto = rohy[r]; }
            }
            int posl = 0;
            foreach (var p in hrana)
            {
                bool uRohu = false;
                for (int r = 0; r < rohy.Count; r++) if (Geo.Vzd(p, rohy[r]) < zony[r]) { uRohu = true; break; }
                if (uRohu) continue;
                // nejblizsi segment: zacit u posledniho nalezeneho, ostatni preskocit podle obalky
                double d = Geo.VzdalenostKSegmentu(p, o.Pts[posl], o.KonecSegmentu(posl), o.B[posl]);
                for (int i = 0; i < ns; i++)
                {
                    double bx = Math.Max(0, Math.Max(box[i, 0] - p.X, p.X - box[i, 2])), by = Math.Max(0, Math.Max(box[i, 1] - p.Y, p.Y - box[i, 3]));
                    if (bx * bx + by * by >= d * d) continue;
                    double di = Geo.VzdalenostKSegmentu(p, o.Pts[i], o.KonecSegmentu(i), o.B[i]);
                    if (di < d) { d = di; posl = i; }
                }
                if (d > m.Max) { m.Max = d; m.MaxMisto = p; }
                m.Suma += d;
                m.Pocet++;
            }
            return m;
        }

        public void Zapocti(Obrys o, List<P> hrana, double zonaRohu, double sigma)
        {
            var m = Zmer(o, hrana, zonaRohu, sigma);
            Primek += m.Primek; Oblouku += m.Oblouku; Rohu += m.Rohu; Zlomu += m.Zlomu; Kratkych += m.Kratkych;
            MaxZlom = Math.Max(MaxZlom, m.MaxZlom);
            if (m.Max > MaxOdchylkaPx) { MaxOdchylkaPx = m.Max; MaxOdchylkaMisto = m.MaxMisto; }
            SumaOdchylek += m.Suma; PocetBodu += m.Pocet;
        }

        // geometrie pred optimalizaci entit (pro porovnani pred/po)
        public void ZapoctiPred(Obrys o, List<P> hrana, double zonaRohu, double sigma)
        {
            var m = Zmer(o, hrana, zonaRohu, sigma);
            PrimekPred += m.Primek; ObloukuPred += m.Oblouku; RohuPred += m.Rohu;
            MaxOdchylkaPredPx = Math.Max(MaxOdchylkaPredPx, m.Max); SumaPred += m.Suma; PocetPred += m.Pocet;
        }

        public void Pricti(Diagnostika d)
        {
            if (d == null) return;
            ObrysuCad += d.ObrysuCad; ObrysuNahradnich += d.ObrysuNahradnich; ObrysuHladkych += d.ObrysuHladkych;
            Primek += d.Primek; Oblouku += d.Oblouku; Rohu += d.Rohu; Zlomu += d.Zlomu; Kratkych += d.Kratkych; PrvkuMimoTol += d.PrvkuMimoTol;
            Kruznic += d.Kruznic;
            if (d.MaxOdchylkaPx > MaxOdchylkaPx) { MaxOdchylkaPx = d.MaxOdchylkaPx; MaxOdchylkaMisto = d.MaxOdchylkaMisto; }
            MaxZlom = Math.Max(MaxZlom, d.MaxZlom);
            SumaOdchylek += d.SumaOdchylek; PocetBodu += d.PocetBodu;
            PrimekPred += d.PrimekPred; ObloukuPred += d.ObloukuPred; RohuPred += d.RohuPred;
            MaxOdchylkaPredPx = Math.Max(MaxOdchylkaPredPx, d.MaxOdchylkaPredPx); SumaPred += d.SumaPred; PocetPred += d.PocetPred;
            SumHranyPx = Math.Max(SumHranyPx, d.SumHranyPx);
            TolMaxPx = Math.Max(TolMaxPx, d.TolMaxPx);
            TolOmezena |= d.TolOmezena;
            Samopruseciky += d.Samopruseciky; PrusecikyObrysu += d.PrusecikyObrysu;
            TemerKruznicePx.AddRange(d.TemerKruznicePx);
            UsecekAprox += d.UsecekAprox;
            foreach (var s in d.Duvody) PridejDuvod(s);
        }

        public void PridejDuvod(string d)
        {
            if (!string.IsNullOrEmpty(d) && !Duvody.Contains(d)) Duvody.Add(d);
        }

        // splnuje vysledek zvolenou toleranci? (jen CAD geometrie; 0,1 % rezerva na zaokrouhleni)
        public bool SplnujeToleranci { get { return !TolOmezena && TolMaxPx > 0 && MaxOdchylkaPx <= TolMaxPx * 1.001 && PrvkuMimoTol == 0; } }

        public override string ToString()
        {
            var s = string.Format(CultureInfo.InvariantCulture,
                "CAD {0}, nahradni {1}, hladky {2}; primek {3}, oblouku {4}, rohu {5}, zlomu {6}, kratkych {7}; odchylka max {8:0.000} px, prumer {9:0.000} px; max netecnost {10:0.0000} st.",
                ObrysuCad, ObrysuNahradnich, ObrysuHladkych, Primek, Oblouku, Rohu, Zlomu, Kratkych, MaxOdchylkaPx, PrumerOdchylkyPx, MaxZlom);
            s += string.Format(CultureInfo.InvariantCulture, "; pred optimalizaci {0} primek, {1} oblouku, {2} rohu, max {3:0.000} px; kruznic {4}; sum hrany {5:0.000} px; tolerance {6:0.000} px{7}",
                PrimekPred, ObloukuPred, RohuPred, MaxOdchylkaPredPx, Kruznic, SumHranyPx, TolMaxPx, TolOmezena ? " (omezena)" : "");
            if (Samopruseciky + PrusecikyObrysu > 0) s += "; PRUSECIKY " + Samopruseciky + "/" + PrusecikyObrysu;
            if (PrvkuMimoTol > 0) s += "; prvku mimo toleranci " + PrvkuMimoTol;
            if (TemerKruznicePx.Count > 0) s += "; temer kruznice (potrebna tolerance px): " + string.Join(", ", TemerKruznicePx.Select(x => x.ToString("0.00", CultureInfo.InvariantCulture)));
            if (Duvody.Count > 0) s += "; duvody: " + string.Join(" | ", Duvody);
            return s;
        }
    }

    public class VysledekKonstrukce
    {
        public Obrys Obrys;           // vysledna geometrie (px obrazku), null = nepodarilo se
        public Obrys ObrysPred;       // pred optimalizaci entit (vsechny rozpoznane useky, uz tecne napojene)
        public string Duvod;          // proc se nepodarilo
        public int MimoTol;           // prvku, ktere se od hrany odchyluji vic nez tolerance
        public bool Kruh;             // obrys je cela kruznice (2 pulkruhy)
        public double SumPx;          // sum hrany obrazku: RMS rychle se menici odchylky (px)
        public double TolMaxPx;       // pouzita tolerance (px)
        public double KruhPotrebaPx;  // temer kruznice, ale za toleranci: jaka tolerance (px) by stacila na 2 pulkruhy
    }

    // Obrys "jako z CADu": retez primek a kruhovych oblouku, ktere na sebe navazuji TECNE (G1), a ostre
    // rohy jen tam, kde se smer hrany opravdu zlomi.
    //  1. hrana obrazku se rozdeli na useky, ktere jdou prolozit primkou nebo kruznici (prednost ma primka),
    //     velke zlomy smeru = ostre rohy, male zaobleni (R < rMin, rozmazany roh) = ostry roh,
    //  2. cely obrys se prolozi najednou (Levenberg-Marquardt): neznamé jsou uzly (poloha + smer tecny),
    //     primka/oblouk mezi uzly je dany presne, tecnost je podminka -> zadne zlomy mezi prvky,
    //  3. sousedni prvky, ktere jdou nahradit jednim, se slouci; co nesedi, se rozdeli; znovu prolozeni,
    //  4. skoro vodorovne/svisle primky se srovnaji presne.
    public static class Konstrukce
    {
        // ---------- model ----------
        class Uzel
        {
            public double X, Y, Fi, Fo;     // poloha a smer tecny na vstupu / vystupu (hladky uzel: Fi = Fo)
            public bool Roh;
            public int B;                   // index vzorku hrany, kde uzel lezi (hranice prvku)
            public Uzel Kopie() { return (Uzel)MemberwiseClone(); }
        }

        class Model
        {
            public List<Uzel> U = new List<Uzel>();
            public List<bool> Obl = new List<bool>();          // prvek j vede z uzlu j do uzlu j+1 (cyklicky)
            public double[] Cil;                               // srovnani primek (NaN = ne), jen na konci
            public int M { get { return U.Count; } }
            public Model Kopie()
            {
                var k = new Model();
                foreach (var u in U) k.U.Add(u.Kopie());
                k.Obl.AddRange(Obl);
                if (Cil != null) k.Cil = (double[])Cil.Clone();
                return k;
            }
        }

        class Kontext
        {
            public P[] Q;                   // rovnomerne navzorkovana hrana (uzavrena)
            public int N;
            public double H;                // rozestup vzorku (px)
            public double Orientace;
            public double[] W;              // vaha vzorku (0 = zakulacena spicka ostreho rohu)
            public double TolMax, TolRms, RMin, UhelRohu, ZonaRohu, PevnaPrimka, Sigma;
            public double Wc = 300;         // vaha podminky tecnosti (px); na konci se zvysi
            public bool Presne;             // zaverecny proklad: konvergovat az do konce
            public HashSet<long> Zamitnute = new HashSet<long>();   // upravy, ktere uz jednou neprosly
            public double KruhPotrebaPx;     // temer kruznice: potrebna tolerance (px), 0 = neni
        }

        // usek hrany po segmentaci (nezavisle prolozeny)
        class Kus
        {
            public int Od, Do;              // vzorky Od..Do (Do muze byt >= N)
            public bool Obl;
            public double Cx, Cy, Ux, Uy, R, Sweep, Mx, Rms;
        }

        const double Ws = 300;              // vaha srovnani do osy
        const double MaxSweepSluc = 5.2;    // nejvetsi oblouk po slouceni (rad)
        const double MaxSweepSeg = 1.75;    // nejvetsi oblouk pri segmentaci (rad, ~100 st.)

        // ---------- hlavni funkce ----------
        // loop = hustá hrana obrazku v px; vraci obrys v px nebo null (duvod v "duvod")
        // mimoTol = kolik prvku vysledku se od hrany odchyluje vic nez tolerance (obrys je presto pouzitelny)
        public static Obrys Rekonstruuj(List<P> loop, double tol, double alphaMax, double narovnat, double sigma, out string duvod, out int mimoTol)
        {
            var v = RekonstruujVse(loop, tol, 0, alphaMax, narovnat, sigma);
            duvod = v.Duvod; mimoTol = v.MimoTol;
            return v.Obrys;
        }

        // tolMaxPx > 0: povolena odchylka geometrie v px (z milimetru); jinak se odvodi z "tol" (1,5 x)
        public static VysledekKonstrukce RekonstruujVse(List<P> loop, double tol, double tolMaxPx, double alphaMax, double narovnat, double sigma)
        {
            var v = new VysledekKonstrukce();
            try
            {
                Rekonstruuj2(loop, tol, tolMaxPx, alphaMax, narovnat, sigma, v);
            }
            catch (Exception ex)
            {
                v.Obrys = null;
                v.Duvod = "chyba výpočtu (" + ex.Message + ")";
            }
            return v;
        }

        // nejmensi povolena odchylka (px): pod tim by proklad kopiroval sum hrany obrazku
        public const double MinTolPx = 0.3;
        public static Action<string> Ladeni;
        static void Vypis(Kontext k, Model mo, string co) { if (Ladeni == null) return; ObnovVahy(k, mo); var sb = new System.Text.StringBuilder(co + " M=" + mo.M + ":"); for (int j = 0; j < mo.M; j++) { double mx, rms; Chyba(k, mo, j, out mx, out rms); var A = mo.U[j]; sb.AppendFormat(CultureInfo.InvariantCulture, "\n   [{0}] {1} {2:0.0},{3:0.0} B{4} {5} len {6:0.0} sw {7:0.0} mx {8:0.00}", j, A.Roh ? "ROH" : "hl", A.X, A.Y, A.B, mo.Obl[j] ? "OBL" : "PRI", Delka(mo, j), mo.Obl[j] ? Sweep(A, mo.U[(j + 1) % mo.M]) * 180 / Math.PI : 0, mx); } Ladeni(sb.ToString()); }

        static void Rekonstruuj2(List<P> loop, double tol, double tolMaxPx, double alphaMax, double narovnat, double sigma, VysledekKonstrukce v)
        {
            double obvod = 0;
            for (int i = 0; i < loop.Count; i++) obvod += Geo.Vzd(loop[i], loop[(i + 1) % loop.Count]);
            if (obvod < 8 || loop.Count < 8) { v.Duvod = "obrys je příliš malý"; return; }

            var k = new Kontext();
            k.Orientace = Math.Sign(Geo.PlochaPolygonu(loop));
            k.H = Math.Max(0.8, obvod / 8000);
            k.Q = Prevzorkuj(loop, k.H, out k.N);
            if (k.N < 12) { v.Duvod = "obrys je příliš malý"; return; }
            k.W = new double[k.N];
            double e = Math.Max(0.2, Math.Min(3.0, tol));
            if (tolMaxPx > 0)
            {
                k.TolMax = Math.Max(1e-6, tolMaxPx);
                k.TolRms = Math.Max(0.12, k.TolMax / 3);
            }
            else
            {
                k.TolMax = Math.Max(0.35, 1.5 * e);
                k.TolRms = Math.Max(0.15, 0.5 * e);
            }
            v.TolMaxPx = k.TolMax;
            double a = Math.Max(0, Math.Min(1.34, alphaMax));
            double rozmaz = Math.Max(0.6, sigma);
            k.RMin = Math.Max(1.2, 3.25 * rozmaz) * (1 + 1.5 * (1 - a));          // pri alpha 1: 2,6 px (rozmazani 0,8)
            k.UhelRohu = Math.Max(8, Math.Min(40, 25 * a)) * Math.PI / 180;
            k.ZonaRohu = 1.0 + 1.6 * rozmaz;
            k.Sigma = rozmaz;
            k.PevnaPrimka = 12;

            // 1) segmentace vzdy jemne (podle sumu obrazku), i kdyz je povolena odchylka velka:
            //    tolerance je mez pro zjednodusovani, ne cil (hrube useky by rezaly pres rohy a uzka mista)
            double tolMax = k.TolMax, tolRms = k.TolRms;
            k.TolMax = Math.Min(tolMax, Math.Max(0.35, 1.5 * e));
            k.TolRms = Math.Min(tolRms, Math.Max(0.15, 0.5 * e));
            var kusy = Segmentuj(k);
            var mo = kusy == null || kusy.Count == 0 ? null : PostavModel(k, kusy);
            k.TolMax = tolMax; k.TolRms = tolRms;
            if (mo == null || mo.M < 2) { v.Duvod = "hranu nešlo rozdělit na přímky a oblouky"; return; }

            // 2) prolozeni celku (= geometrie "pred optimalizaci": vsechny rozpoznane useky, tecne napojene)
            Prolozit(k, mo, null, 3);
            if (!Konecne(mo)) { v.Duvod = "proklad nekonvergoval"; return; }
            v.ObrysPred = Vystup(mo);
            Vypis(k, mo, "PO PROKLADU");
            v.SumPx = SumHrany(k, mo);

            // 3) zjednoduseni a opravy (rohy / zaobleni, deleni, slucovani sousedu)
            int limit = 6 * mo.M + 60;
            var zakladni = mo.Kopie();
            Upravy(k, mo, ref limit, 8);
            Vypis(k, mo, "PO UPRAVACH");
            if (KriziSe(mo) && !KriziSe(zakladni)) Prevezmi(mo, zakladni);
            var predOpt = mo.Kopie();

            // 4) optimalizace entit: cela kruznice, skupiny oblouku na jedne kruznici, kolinearni primky.
            //    Jen kdyz nova geometrie nikde neprekroci toleranci (soustavna chyba nejvys 80 % tolerance).
            if (CelaKruznice(k, mo)) v.Kruh = true;
            else
            {
                if (SlucSkupinyOblouku(k, mo, ref limit) | SlucSkupinyPrimek(k, mo, ref limit))
                    Upravy(k, mo, ref limit, 3);
                // po slouceni muze zbyt jen kruznice ze 2 oblouku
                if (mo.M == 2 && mo.Obl[0] && mo.Obl[1] && !mo.U[0].Roh && !mo.U[1].Roh) v.Kruh = true;
                else v.KruhPotrebaPx = k.KruhPotrebaPx;
            }
            // optimalizace nesmi zmenit topologii: obrys, ktery by krizil sam sebe, se vrati
            if (KriziSe(mo) && !KriziSe(predOpt)) { Prevezmi(mo, predOpt); v.Kruh = false; ObnovVahy(k, mo); }
            // spicky rohu nesmi po zjednoduseni presahovat obrazek vic nez tolerance (+ rozmazani)
            if (!RohyOk(k, mo))
            {
                if (RohyOk(k, predOpt)) { Prevezmi(mo, predOpt); v.Kruh = false; }
                else if (RohyOk(k, zakladni)) { Prevezmi(mo, zakladni); v.Kruh = false; }
                ObnovVahy(k, mo);
            }

            // 5) srovnani do os a zaverecne prolozeni
            if (narovnat > 0 && !v.Kruh)
            {
                mo.Cil = new double[mo.M];
                bool neco = false;
                for (int j = 0; j < mo.M; j++)
                {
                    mo.Cil[j] = double.NaN;
                    if (mo.Obl[j]) continue;
                    var A = mo.U[j]; var B = mo.U[(j + 1) % mo.M];
                    double u = Math.Atan2(B.Y - A.Y, B.X - A.X);
                    for (int q = -2; q <= 2; q++)
                        if (Math.Abs(Geo.Norm(u - q * Math.PI / 2)) < narovnat * Math.PI / 180) { mo.Cil[j] = q * Math.PI / 2; neco = true; }
                }
                if (!neco) mo.Cil = null;
            }
            Prolozit(k, mo, null, 2);
            // srovnani do osy jen tam, kde se tim neprekroci tolerance
            if (mo.Cil != null)
            {
                bool zrusit = false;
                for (int j = 0; j < mo.M; j++)
                    if (!double.IsNaN(mo.Cil[j]) && (MaxChyba(k, mo, j) > k.TolMax ||
                        (mo.U[j].Roh && PresahRohu(k, mo, j) > k.TolMax) || (mo.U[(j + 1) % mo.M].Roh && PresahRohu(k, mo, (j + 1) % mo.M) > k.TolMax)))
                    { mo.Cil[j] = double.NaN; zrusit = true; }
                if (zrusit) Prolozit(k, mo, null, 2);
            }
            // prvky nad toleranci jeste jednou rozdelit (tolerance se musi dodrzet)
            bool nad = false;
            for (int j = 0; j < mo.M; j++) if (MaxChyba(k, mo, j) > k.TolMax) nad = true;
            if (nad)
            {
                var cil = mo.Cil; mo.Cil = null;
                limit = Math.Max(limit, 2 * mo.M + 20);
                for (int kolo = 0; kolo < 4; kolo++) { k.Zamitnute.RemoveWhere(x => (x >> 56) == 1); if (!Rozdel(k, mo, ref limit)) break; }
                OdstranKratke(k, mo, ref limit);
                Prolozit(k, mo, null, 2);
            }
            // zavrecny proklad muze zmensit uhel nektereho rohu pod mez -> malo zlomy jeste jednou zaoblit / vyhladit
            k.Zamitnute.RemoveWhere(x => (x >> 56) == 4);
            limit = Math.Max(limit, mo.M + 10);
            if (SlabeRohy(k, mo, ref limit)) Prolozit(k, mo, null, 1);
            k.Wc = 1e4; k.Presne = true;    // tecnost na konci prakticky presne
            Prolozit(k, mo, null, 1);
            if (!Konecne(mo)) { v.Duvod = "proklad nekonvergoval"; return; }

            // Az presny proklad muze odhalit slaby roh; znovu ho posoudit pri konecne vaze tecnosti.
            for (int pass = 0; pass < 3; pass++)
            {
                k.Zamitnute.RemoveWhere(x => (x >> 56) == 4);
                limit = Math.Max(limit, mo.M + 10);
                if (!SlabeRohy(k, mo, ref limit)) break;
                Prolozit(k, mo, null, 1);
            }
            // Finalni prijeti podle stejneho mereni, ktere vidi uzivatel.
            var overeni = new Diagnostika();
            overeni.Zapocti(Vystup(mo), loop, k.ZonaRohu, k.Sigma);
            if (overeni.MaxOdchylkaPx > k.TolMax * 1.001)
            {
                var kandidat = predOpt.Kopie();
                kandidat.Cil = null;
                Prolozit(k, kandidat, null, 2);
                k.Zamitnute.RemoveWhere(x => (x >> 56) == 4); limit = Math.Max(limit, kandidat.M + 10);
                if (SlabeRohy(k, kandidat, ref limit)) Prolozit(k, kandidat, null, 1);
                var dk = new Diagnostika(); dk.Zapocti(Vystup(kandidat), loop, k.ZonaRohu, k.Sigma);
                if (dk.MaxOdchylkaPx < overeni.MaxOdchylkaPx && !KriziSe(kandidat))
                { Prevezmi(mo, kandidat); v.Kruh = false; ObnovVahy(k, mo); }
            }
            Vypis(k, mo, "KONEC"); if (Ladeni != null) for (int j = 0; j < mo.M; j++) if (mo.U[j].Roh) Ladeni(string.Format(CultureInfo.InvariantCulture, "   roh {0:0.0},{1:0.0} zlom {2:0.0} presah {3:0.00}", mo.U[j].X, mo.U[j].Y, Geo.Norm(mo.U[j].Fo - mo.U[j].Fi) * 180 / Math.PI, PresahRohu(k, mo, j)));
            // kontrola: kazdy prvek musi sedet na hranu obrazku (tolerance = nejvetsi odchylka)
            double nejhorsi = 0;
            int mimo = 0;
            for (int j = 0; j < mo.M; j++)
            {
                double mx, rms;
                Chyba(k, mo, j, out mx, out rms);
                nejhorsi = Math.Max(nejhorsi, mx);
                if (mx > k.TolMax * 1.001) { mimo++; if (Ladeni != null) Ladeni(string.Format(CultureInfo.InvariantCulture, "MIMO TOL {0:0.0},{1:0.0} {2} len {3:0.0} mx {4:0.00} tol {5:0.00}", mo.U[j].X, mo.U[j].Y, mo.Obl[j] ? "OBL" : "PRI", Delka(mo, j), mx, k.TolMax)); }
            }
            for (int j = 0; j < mo.M; j++)
                if (mo.U[j].Roh && PresahRohu(k, mo, j) > k.TolMax * 1.001) { mimo++; if (Ladeni != null) Ladeni(string.Format(CultureInfo.InvariantCulture, "ROH MIMO TOL {0:0.0},{1:0.0} presah {2:0.00}", mo.U[j].X, mo.U[j].Y, PresahRohu(k, mo, j))); }
            if (nejhorsi > 4 * Math.Max(k.TolMax, 0.9)) { v.Duvod = string.Format(CultureInfo.InvariantCulture, "přímky a oblouky se od hrany odchylují o {0:0.0} px", nejhorsi); return; }
            var o = Vystup(mo);
            if (o.Pts.Count < 2) { v.Duvod = "příliš málo prvků"; return; }
            v.MimoTol = mimo;
            v.Obrys = o;
        }

        // Spicka ostreho rohu je pruseciku hran, data u rohu se pri prokladu vynechavaji (rozmazani). Aby se
        // tolerance dodrzela i tam: roh smi od nejblizsiho bodu obrazku lezet nejvys tolerance + ustup spicky
        // rozmazanim (0,6 sigma / sin(vnitrni uhel / 2)).
        public static double UstupSpicky(double sigma, double zlom) { return 0.6 * sigma / Math.Max(0.1, Math.Cos(zlom / 2)); }

        // Oba smery: (1) spicka rohu za obrazkem, (2) body obrazku u rohu (pri prokladu vynechane) za drahou
        // rohu - napr. kdyz se po slouceni spicka zkrati. Vraci odchylku navic proti tomu, co vysvetli rozmazani.
        static double PresahRohu(Kontext k, Model mo, int j)
        {
            int m = mo.M, N = k.N;
            var u = mo.U[j];
            var X = new P(u.X, u.Y);
            int w = (int)Math.Ceiling(40 / k.H);
            double d = double.MaxValue;
            for (int t = -w; t <= w; t++) d = Math.Min(d, Geo.Vzd(k.Q[((u.B + t) % N + N) % N], X));
            double presah = d - UstupSpicky(k.Sigma, Math.Abs(Geo.Norm(u.Fo - u.Fi)));
            var Up = mo.U[(j - 1 + m) % m]; var Un = mo.U[(j + 1) % m];
            var Pp = new P(Up.X, Up.Y); var Pn = new P(Un.X, Un.Y);
            double bp = mo.Obl[(j - 1 + m) % m] ? Math.Tan(Sweep(Up, u) / 4) : 0, bn = mo.Obl[j] ? Math.Tan(Sweep(u, Un) / 4) : 0;
            int z = (int)Math.Ceiling(ZonaRohu(k.ZonaRohu, k.Sigma, Math.Abs(Geo.Norm(u.Fo - u.Fi))) / k.H);
            int zp = Math.Min(z, (int)(0.45 * PocetVzorku(k, mo, (j - 1 + m) % m))), zn = Math.Min(z, (int)(0.45 * PocetVzorku(k, mo, j)));
            // rozmazani body jen vtahuje DOVNITR tvaru (zkrati spicku); bod venku za drahou rohu je odchylka bez slevy
            double ustup = UstupSpicky(k.Sigma, Math.Abs(Geo.Norm(u.Fo - u.Fi)));
            double sp = mo.Obl[(j - 1 + m) % m] ? Sweep(Up, u) : 0, sn = mo.Obl[j] ? Sweep(u, Un) : 0;
            for (int t = -zp; t <= zn; t++)
            {
                var q = k.Q[((u.B + t) % N + N) % N];
                double d1 = Geo.VzdalenostKSegmentu(q, Pp, X, bp), d2 = Geo.VzdalenostKSegmentu(q, X, Pn, bn);
                double sd = d1 < d2 ? SD(q.X, q.Y, Up.X, Up.Y, X.X, X.Y, sp) : SD(q.X, q.Y, X.X, X.Y, Un.X, Un.Y, sn);
                bool uvnitr = sd * k.Orientace > 0;
                presah = Math.Max(presah, uvnitr ? Math.Min(d1, d2) - ustup : Math.Min(d1, d2));
            }
            return presah;
        }

        static bool RohyOk(Kontext k, Model mo)
        {
            for (int j = 0; j < mo.M; j++) if (mo.U[j].Roh && PresahRohu(k, mo, j) > k.TolMax) return false;
            return true;
        }

        static bool KriziSe(Model mo)
        {
            int vlastni, vzajemne;
            Geo.Pruseciky(new List<Obrys> { Vystup(mo) }, 0.05, out vlastni, out vzajemne);
            return vlastni > 0;
        }

        static void Upravy(Kontext k, Model mo, ref int limit, int kol)
        {
            for (int kolo = 0; kolo < kol && limit > 0; kolo++)
            {
                bool zmena = false;
                k.Zamitnute.RemoveWhere(x => (x >> 56) == 1);    // deleni zkusit znovu (sousede se mezitim zmenili)
                zmena |= RohyNaZaobleni(k, mo, ref limit);
                zmena |= MaleObloukyNaRohy(k, mo, ref limit);
                zmena |= SlabeRohy(k, mo, ref limit);
                zmena |= Rozdel(k, mo, ref limit);
                zmena |= Sluc(k, mo, ref limit);
                zmena |= ObloukyNaPrimky(k, mo, ref limit);
                zmena |= OdstranKratke(k, mo, ref limit);
                if (!zmena) break;
            }
        }

        // ---------- optimalizace entit ----------
        // Odchylku vzorku hrany od geometrie lze rozdelit na rychle se menici cast (sum obrazku, pixely)
        // a pomalu se menici (vyhlazenou) cast = soustavna chyba tvaru. Zjednoduseni (mene entit) smi pridat
        // soustavnou chybu nejvys 0,35 tolerance - jinak by menilo tvar (elipsa != kruznice).
        const double PodilSoustavne = 0.8;     // 20 % tolerance zustava jako rezerva na sum obrazku

        static double SigVz(Kontext k) { return Math.Max(2, 6 / k.H); }     // vyhlazeni ~6 px

        static List<double> Vyhlad(List<double> d, double sig, bool uzavrena)
        {
            int n = d.Count;
            var res = new List<double>(n);
            int r = Math.Max(1, (int)Math.Ceiling(2.5 * sig));
            var w = new double[2 * r + 1];
            for (int i = -r; i <= r; i++) w[i + r] = Math.Exp(-i * i / (2 * sig * sig));
            for (int i = 0; i < n; i++)
            {
                double s = 0, sw = 0;
                for (int t = -r; t <= r; t++)
                {
                    int j = i + t;
                    if (uzavrena) j = ((j % n) + n) % n;
                    else if (j < 0 || j >= n) continue;
                    s += w[t + r] * d[j]; sw += w[t + r];
                }
                res.Add(s / sw);
            }
            return res;
        }

        // soustavna chyba = nejvetsi vyhlazena odchylka
        static double Soustavna(Kontext k, List<double> d, bool uzavrena)
        {
            if (d.Count == 0) return 0;
            return Vyhlad(d, SigVz(k), uzavrena).Max(x => Math.Abs(x));
        }

        // znamenkove odchylky vzorku prvku j (vzorky s vahou 0 = spicky rohu se vynechaji)
        static void Odchylky(Kontext k, Model mo, int j, List<double> d)
        {
            var A = mo.U[j]; var B = mo.U[(j + 1) % mo.M];
            double s = mo.Obl[j] ? Sweep(A, B) : 0;
            int od = A.B, @do = B.B;
            if (@do <= od) @do += k.N;
            for (int i = od; i < @do; i++)
            {
                int ii = i % k.N;
                if (k.W[ii] <= 0) continue;
                d.Add(SD(k.Q[ii].X, k.Q[ii].Y, A.X, A.Y, B.X, B.Y, s));
            }
        }

        // sum hrany obrazku = RMS rychle se menici casti odchylek (px)
        static double SumHrany(Kontext k, Model mo)
        {
            ObnovVahy(k, mo);
            double s2 = 0; int n = 0;
            var d = new List<double>();
            for (int j = 0; j < mo.M; j++)
            {
                d.Clear();
                Odchylky(k, mo, j, d);
                if (d.Count < 5) continue;
                var hl = Vyhlad(d, SigVz(k), false);
                for (int i = 0; i < d.Count; i++) { s2 += (d[i] - hl[i]) * (d[i] - hl[i]); n++; }
            }
            return n > 0 ? Math.Sqrt(s2 / n) : 0;
        }

        // vzorky <od, do> bez krajnich vzorku s vahou 0 (spicky rohu)
        static void Orizni(Kontext k, ref int od, ref int @do)
        {
            while (od < @do && k.W[((od % k.N) + k.N) % k.N] <= 0) od++;
            while (@do > od && k.W[((@do % k.N) + k.N) % k.N] <= 0) @do--;
        }

        // Cely obrys bez rohu = kruznice? Spolecny geometricky proklad vsech vzorku; prijmout jen kdyz
        // nejvetsi radialni odchylka <= tolerance a soustavna (vyhlazena) <= 0,35 tolerance (elipsa neprojde).
        static bool CelaKruznice(Kontext k, Model mo)
        {
            foreach (var u in mo.U) if (u.Roh) return false;
            var c = new Kus();
            FitKruh(k, 0, k.N, c, 8);
            if (!(c.R > 0) || double.IsNaN(c.Cx) || c.R > 1e5) return false;
            double sg = Math.Sign(c.Sweep);
            if (sg == 0 || Math.Abs(Math.Abs(c.Sweep) - 2 * Math.PI) > 0.5) return false;
            var d = new List<double>(k.N);
            for (int i = 0; i < k.N; i++)
            {
                var q = k.Q[i];
                d.Add(Math.Sqrt((q.X - c.Cx) * (q.X - c.Cx) + (q.Y - c.Cy) * (q.Y - c.Cy)) - c.R);
            }
            double mx = d.Max(x => Math.Abs(x));
            double soust = Soustavna(k, d, true);
            // temer kruznice: zapamatovat, pri jake toleranci by z ni byly 2 pulkruhy (informace pro uzivatele)
            if (mx <= 4 * k.TolMax) k.KruhPotrebaPx = Math.Max(mx, soust / PodilSoustavne);
            if (mx > k.TolMax || soust > PodilSoustavne * k.TolMax) return false;
            double f0 = Math.Atan2(k.Q[0].Y - c.Cy, k.Q[0].X - c.Cx);
            int b1 = 0; double bd = double.MaxValue;
            var prot = new P(c.Cx - c.R * Math.Cos(f0), c.Cy - c.R * Math.Sin(f0));
            for (int i = 1; i < k.N; i++) { double dd = Geo.Vzd(k.Q[i], prot); if (dd < bd) { bd = dd; b1 = i; } }
            var u0 = new Uzel { X = c.Cx + c.R * Math.Cos(f0), Y = c.Cy + c.R * Math.Sin(f0), B = 0 };
            u0.Fi = u0.Fo = f0 + sg * Math.PI / 2;
            var u1 = new Uzel { X = prot.X, Y = prot.Y, B = b1 };
            u1.Fi = u1.Fo = f0 + Math.PI + sg * Math.PI / 2;
            mo.U = new List<Uzel> { u0, u1 };
            mo.Obl = new List<bool> { true, true };
            mo.Cil = null;
            ObnovVahy(k, mo);
            return true;
        }

        // Rady tecne navazujicich oblouku stejneho smeru, ktere lezi na jedne kruznici -> jeden oblouk.
        // Spolecny proklad cele rady (ne jen sousedu); nejdriv nejdelsi rada.
        static bool SlucSkupinyOblouku(Kontext k, Model mo, ref int limit)
        {
            bool zmena = false;
            while (limit > 0 && mo.M > 2)
            {
                int m = mo.M;
                ObnovVahy(k, mo);
                int best = -1, bestDel = 0;
                for (int j = 0; j < m; j++)
                {
                    if (!mo.Obl[j]) continue;
                    double sg = Math.Sign(Sweep(mo.U[j], mo.U[(j + 1) % m]));
                    int del = 1;
                    for (int t = 1; t < m - 1; t++)
                    {
                        int e = (j + t) % m;
                        if (!mo.Obl[e] || mo.U[e].Roh || Math.Sign(Sweep(mo.U[e], mo.U[(e + 1) % m])) != sg) break;
                        if (k.Zamitnute.Contains(Klic(8, mo.U[j].B, mo.U[(e + 1) % m].B))) break;
                        int od = mo.U[j].B, @do = mo.U[(e + 1) % m].B;
                        if (@do <= od) @do += k.N;
                        Orizni(k, ref od, ref @do);
                        var c = new Kus();
                        FitKruh(k, od, @do, c, MaxSweepSluc);
                        if (!(c.R > 0) || Math.Abs(c.Sweep) > MaxSweepSluc || Math.Sign(c.Sweep) != sg) break;
                        var d = new List<double>();
                        for (int i = od; i <= @do; i++)
                        {
                            var q = k.Q[i % k.N];
                            d.Add(Math.Sqrt((q.X - c.Cx) * (q.X - c.Cx) + (q.Y - c.Cy) * (q.Y - c.Cy)) - c.R);
                        }
                        if (d.Max(x => Math.Abs(x)) > k.TolMax || Soustavna(k, d, false) > PodilSoustavne * k.TolMax) break;
                        del = t + 1;
                    }
                    if (del >= 2 && del > bestDel) { bestDel = del; best = j; }
                }
                if (best < 0) break;
                limit--;
                long klic = Klic(8, mo.U[best].B, mo.U[(best + bestDel) % m].B);
                var z = mo.Kopie();
                Otoc(z, best - 1);                    // rada = prvky 1 .. bestDel
                z.U.RemoveRange(2, bestDel - 1);
                z.Obl.RemoveRange(2, bestDel - 1);
                z.Obl[1] = true;
                if (OverSlouceni(k, mo, z)) { Prevezmi(mo, z); ObnovVahy(k, mo); zmena = true; }
                else { k.Zamitnute.Add(klic); ObnovVahy(k, mo); }
            }
            return zmena;
        }

        // Rady primek (a skoro rovnych oblouku) s malymi zlomy mezi nimi, ktere jsou opravdu kolinearni -> jedna primka.
        static bool SlucSkupinyPrimek(Kontext k, Model mo, ref int limit)
        {
            bool zmena = false;
            Func<Model, int, bool> rovny = (x, j) => !x.Obl[j] || Math.Abs(Sweep(x.U[j], x.U[(j + 1) % x.M])) < 0.1;
            while (limit > 0 && mo.M > 3)
            {
                int m = mo.M;
                ObnovVahy(k, mo);
                int best = -1, bestDel = 0;
                for (int j = 0; j < m; j++)
                {
                    if (!rovny(mo, j)) continue;
                    // hladce navazujici primka pred radou by musela byt v zakrytu -> rada musi zacit jinde
                    int pj = (j - 1 + m) % m;
                    if (!mo.U[j].Roh && !mo.Obl[pj]) continue;
                    int del = 1;
                    for (int t = 1; t < m - 2; t++)
                    {
                        int e = (j + t) % m;
                        var u = mo.U[e];
                        if (!rovny(mo, e) || (u.Roh && Math.Abs(Geo.Norm(u.Fo - u.Fi)) > 20 * Math.PI / 180)) break;
                        int ne = (e + 1) % m;
                        if (k.Zamitnute.Contains(Klic(9, mo.U[j].B, mo.U[ne].B))) break;
                        int od = mo.U[j].B, @do = mo.U[ne].B;
                        if (@do <= od) @do += k.N;
                        Orizni(k, ref od, ref @do);
                        var p = new Kus();
                        FitPrimka(k, od, @do, p);
                        var d = new List<double>();
                        for (int i = od; i <= @do; i++) { var q = k.Q[i % k.N]; d.Add((q.X - p.Cx) * p.Uy - (q.Y - p.Cy) * p.Ux); }
                        if (d.Count < 3 || d.Max(x => Math.Abs(x)) > k.TolMax || Soustavna(k, d, false) > PodilSoustavne * k.TolMax) break;
                        // konec rady: hladce navazujici primka za ni by musela byt v zakrytu
                        if (mo.U[ne].Roh || mo.Obl[ne] && !rovny(mo, ne)) del = t + 1;
                    }
                    if (del >= 2 && del > bestDel) { bestDel = del; best = j; }
                }
                if (best < 0) break;
                limit--;
                long klic = Klic(9, mo.U[best].B, mo.U[(best + bestDel) % m].B);
                var z = mo.Kopie();
                Otoc(z, best - 1);
                z.U.RemoveRange(2, bestDel - 1);
                z.Obl.RemoveRange(2, bestDel - 1);
                z.Obl[1] = false;
                if (OverSlouceni(k, mo, z)) { Prevezmi(mo, z); ObnovVahy(k, mo); zmena = true; }
                else { k.Zamitnute.Add(klic); ObnovVahy(k, mo); }
            }
            return zmena;
        }

        // slouceny prvek z (index 1) po prolozeni: v toleranci, bez soustavne chyby, okoli se nezhorsi
        static bool OverSlouceni(Kontext k, Model mo, Model z)
        {
            Prolozit(k, z, Okno(z, 1, 2), 2);
            if (!Konecne(z)) return false;
            double pred, po;
            Porovnej(k, mo, z, 1, out pred, out po);
            ObnovVahy(k, z);
            var d = new List<double>();
            Odchylky(k, z, 1, d);
            if (d.Count == 0) return false;
            double mx = d.Max(x => Math.Abs(x));
            if (z.Obl[1] && Math.Abs(Sweep(z.U[1], z.U[2 % z.M])) > MaxSweepSluc) return false;
            for (int t = 0; t <= 3; t++)
            {
                int jj = t % z.M;
                if (z.U[jj].Roh && PresahRohu(k, z, jj) > k.TolMax) return false;
            }
            return mx <= k.TolMax && Soustavna(k, d, false) <= PodilSoustavne * k.TolMax && Snese(k, pred, po);
        }

        // ---------- vystup ----------
        static Obrys Vystup(Model mo)
        {
            var o = new Obrys { Zavreny = true };
            int m = mo.M;
            if (m == 2 && mo.Obl[0] && mo.Obl[1] && !mo.U[0].Roh && !mo.U[1].Roh)
            {
                // dva tecne navazujici oblouky = kruznice: dve presne poloviny
                var A = mo.U[0]; var B = mo.U[1];
                double s = Sweep(A, B);
                var ob = Geo.Oblouk(new P(A.X, A.Y), new P(B.X, B.Y), Math.Tan(s / 4));
                double zn = Math.Sign(s);
                o.Pts.Add(new P(A.X, A.Y)); o.B.Add(zn);
                o.Pts.Add(new P(2 * ob.Cx - A.X, 2 * ob.Cy - A.Y)); o.B.Add(zn);
                return o;
            }
            for (int j = 0; j < m; j++)
            {
                var A = mo.U[j]; var B = mo.U[(j + 1) % m];
                var a = new P(A.X, A.Y); var b = new P(B.X, B.Y);
                // degenerovana usecka (tisicinky px) nema smer a v DXF by po zaokrouhleni udelala zlom
                if (Geo.Vzd(a, b) < (mo.Obl[j] ? 1e-6 : 0.05)) continue;
                double s = mo.Obl[j] ? Sweep(A, B) : 0;
                if (Math.Abs(s) > Math.PI + 1e-3)
                {
                    // oblouk pres 180 st. rozdelit na dve poloviny (bezpecne pro CAM)
                    var ob = Geo.Oblouk(a, b, Math.Tan(s / 4));
                    double t = ob.A0 + ob.Sw / 2;
                    var mid = new P(ob.Cx + ob.R * Math.Cos(t), ob.Cy + ob.R * Math.Sin(t));
                    o.Pts.Add(a); o.B.Add(Math.Tan(s / 8));
                    o.Pts.Add(mid); o.B.Add(Math.Tan(s / 8));
                }
                else
                {
                    o.Pts.Add(a);
                    o.B.Add(Math.Abs(s) < 1e-9 ? 0 : Math.Tan(s / 4));
                }
            }
            return o;
        }

        static bool Konecne(Model mo)
        {
            foreach (var u in mo.U)
                if (double.IsNaN(u.X) || double.IsNaN(u.Y) || double.IsNaN(u.Fi) || double.IsNaN(u.Fo) || double.IsInfinity(u.X) || double.IsInfinity(u.Y)) return false;
            return true;
        }

        // ---------- geometrie prvku ----------
        // uhel oblouku z uzlu A do B: smery tecen vuci tetive (pri presne tecnosti d2 = -d1)
        static double Sweep(Uzel A, Uzel B)
        {
            double al = Math.Atan2(B.Y - A.Y, B.X - A.X);
            return Geo.Norm(B.Fi - al) - Geo.Norm(A.Fo - al);
        }

        // znamenkova vzdalenost bodu od prvku (kladna = vlevo od smeru obrysu); oblouk jako cela kruznice,
        // vzorec spojity i pro skoro rovny oblouk (zadne deleni polomerem)
        static double SD(double qx, double qy, double ax, double ay, double bx, double by, double s)
        {
            double cx = bx - ax, cy = by - ay, c = Math.Sqrt(cx * cx + cy * cy);
            if (c < 1e-9) return Math.Sqrt((qx - ax) * (qx - ax) + (qy - ay) * (qy - ay));
            double nx = -cy / c, ny = cx / c;
            double px = qx - (ax + bx) / 2, py = qy - (ay + by) / 2;
            double pn = px * nx + py * ny;
            if (s == 0) return pn;
            double kk = 2 * Math.Sin(s / 2) / c;
            double dk = kk * (px * px + py * py - c * c / 4) - 2 * Math.Cos(s / 2) * pn;
            double disc = 1 + dk * kk;
            if (disc < 0) disc = 0;
            return -dk / (1 + Math.Sqrt(disc));
        }

        // radky rezidui prvku j: vzorky hrany (vazene), podminky tecnosti, srovnani do osy
        static int Rezidua(Kontext k, Model mo, int j, double[] r)
        {
            int m = mo.M;
            var A = mo.U[j]; var B = mo.U[(j + 1) % m];
            bool obl = mo.Obl[j];
            double cx = B.X - A.X, cy = B.Y - A.Y, c = Math.Sqrt(cx * cx + cy * cy);
            double al = Math.Atan2(cy, cx);
            double d1 = Geo.Norm(A.Fo - al), d2 = Geo.Norm(B.Fi - al);
            double s = obl ? d2 - d1 : 0;
            int n = 0;
            int od = A.B, @do = B.B;
            if (@do <= od) @do += k.N;
            for (int i = od; i < @do; i++)
            {
                int ii = i % k.N;
                double w = k.W[ii];
                if (w <= 0) continue;
                r[n++] = w * SD(k.Q[ii].X, k.Q[ii].Y, A.X, A.Y, B.X, B.Y, s);
            }
            double cc = Math.Max(c, 0.5);
            if (obl) r[n++] = k.Wc * cc * Math.Sin(Geo.Norm(d1 + d2) / 2);
            else
            {
                r[n++] = k.Wc * cc * 2 * Math.Sin(d1 / 2);
                r[n++] = k.Wc * cc * 2 * Math.Sin(d2 / 2);
                if (mo.Cil != null && j < mo.Cil.Length && !double.IsNaN(mo.Cil[j])) r[n++] = Ws * cc * Math.Sin(Geo.Norm(al - mo.Cil[j]));
            }
            return n;
        }

        static int PocetVzorku(Kontext k, Model mo, int j)
        {
            int od = mo.U[j].B, @do = mo.U[(j + 1) % mo.M].B;
            if (@do <= od) @do += k.N;
            return @do - od;
        }

        // max a RMS odchylka vzorku prvku j od nej
        static void Chyba(Kontext k, Model mo, int j, out double mx, out double rms)
        {
            int m = mo.M;
            var A = mo.U[j]; var B = mo.U[(j + 1) % m];
            double s = mo.Obl[j] ? Sweep(A, B) : 0;
            int od = A.B, @do = B.B;
            if (@do <= od) @do += k.N;
            mx = 0; double sum = 0; int c = 0;
            for (int i = od; i < @do; i++)
            {
                int ii = i % k.N;
                if (k.W[ii] <= 0) continue;
                double d = Math.Abs(SD(k.Q[ii].X, k.Q[ii].Y, A.X, A.Y, B.X, B.Y, s));
                // bod za koncem oblouku: vzdalenost ke koncovemu bodu
                if (mo.Obl[j] && !NaOblouku(k.Q[ii], A, B, s)) d = Math.Min(Geo.Vzd(k.Q[ii], new P(A.X, A.Y)), Geo.Vzd(k.Q[ii], new P(B.X, B.Y)));
                mx = Math.Max(mx, d); sum += d * d; c++;
            }
            rms = c > 0 ? Math.Sqrt(sum / c) : 0;
        }

        static bool NaOblouku(P q, Uzel A, Uzel B, double s)
        {
            if (Math.Abs(s) < 0.05) return true;
            var a = new P(A.X, A.Y); var b = new P(B.X, B.Y);
            if (Geo.Vzd(a, b) < 1e-9) return true;
            var o = Geo.Oblouk(a, b, Math.Tan(s / 4));
            double ap = Math.Atan2(q.Y - o.Cy, q.X - o.Cx);
            double rel = o.Sw > 0 ? Geo.Mod2Pi(ap - o.A0) : Geo.Mod2Pi(o.A0 - ap);
            double rez = 0.3;   // rad tolerance na koncich
            return rel <= Math.Abs(o.Sw) + rez || rel >= 2 * Math.PI - rez;
        }

        // ---------- Levenberg-Marquardt ----------
        // parametry uzlu: X, Y, F (hladky) nebo X, Y, Fi, Fo (roh)
        static double GetP(Uzel u, int slot) { return slot == 0 ? u.X : slot == 1 ? u.Y : slot == 2 ? u.Fi : u.Fo; }
        static void SetP(Uzel u, int slot, double v)
        {
            if (slot == 0) u.X = v;
            else if (slot == 1) u.Y = v;
            else if (slot == 2) { u.Fi = v; if (!u.Roh) u.Fo = v; }
            else u.Fo = v;
        }

        // prolozi model; volne = uzly, ktere se smi hybat (null = vsechny); kola = kolikrat znovu priradit vzorky
        static void Prolozit(Kontext k, Model mo, bool[] volne, int kola)
        {
            for (int kolo = 0; kolo < kola; kolo++)
            {
                ObnovVahy(k, mo);
                LM(k, mo, volne, k.Presne ? 100 : volne == null ? 40 : 15);
                if (!Konecne(mo)) return;
                if (!Prirad(k, mo, volne)) break;
            }
            ObnovVahy(k, mo);
        }

        static void LM(Kontext k, Model mo, bool[] volne, int iteraci)
        {
            int m = mo.M;
            if (m < 2) return;
            var ofs = new int[m];
            for (int j = 0; j < m; j++) ofs[j] = -1;
            int start = 0;
            if (volne != null)
            {
                bool any = false;
                for (int j = 0; j < m; j++) if (volne[j]) any = true;
                if (!any) return;
                for (int j = 0; j < m; j++) if (volne[j] && !volne[(j - 1 + m) % m]) { start = j; break; }
            }
            int P = 0;
            for (int t = 0; t < m; t++)
            {
                int j = (start + t) % m;
                if (volne != null && !volne[j]) continue;
                ofs[j] = P;
                P += mo.U[j].Roh ? 4 : 3;
            }
            // prvky, ktere zavisi na volnych uzlech
            var prvky = new List<int>();
            for (int j = 0; j < m; j++) if (ofs[j] >= 0 || ofs[(j + 1) % m] >= 0) prvky.Add(j);
            // indexy parametru prvku: (uzel, slot, globalni index)
            Func<int, int[]> uzlyPrvku = j => new[] { j, j, j, (j + 1) % m, (j + 1) % m, (j + 1) % m };
            Func<int, int[]> slotyPrvku = j => new[] { 0, 1, mo.U[j].Roh ? 3 : 2, 0, 1, 2 };
            Func<int, int, int> gidx = (u, slot) => ofs[u] < 0 ? -1 : ofs[u] + (slot == 3 ? 3 : slot);
            // obalka matice (prvni nenulovy sloupec v radku)
            var f = new int[P];
            for (int i = 0; i < P; i++) f[i] = i;
            for (int j = 0; j < m; j++)
            {
                if (ofs[j] < 0) continue;
                int sz = mo.U[j].Roh ? 4 : 3;
                for (int a = 0; a < sz; a++) f[ofs[j] + a] = Math.Min(f[ofs[j] + a], ofs[j]);
            }
            foreach (int j in prvky)
            {
                var uu = uzlyPrvku(j); var ss = slotyPrvku(j);
                for (int a = 0; a < 6; a++)
                {
                    int ia = gidx(uu[a], ss[a]);
                    if (ia < 0) continue;
                    for (int b = 0; b < 6; b++)
                    {
                        int ib = gidx(uu[b], ss[b]);
                        if (ib < 0 || ib >= ia) continue;
                        f[ia] = Math.Min(f[ia], ib);
                    }
                }
            }
            int maxR = 0;
            foreach (int j in prvky) maxR = Math.Max(maxR, PocetVzorku(k, mo, j) + 4);
            var r0 = new double[maxR];
            var r1 = new double[maxR];
            var g = new double[6][];
            for (int a = 0; a < 6; a++) g[a] = new double[maxR];

            Func<double> cena = () =>
            {
                double s = 0;
                foreach (int j in prvky) { int n = Rezidua(k, mo, j, r0); for (int i = 0; i < n; i++) s += r0[i] * r0[i]; }
                return s;
            };
            double c0 = cena();
            double lambda = 1e-3;
            var A = new double[P][];
            for (int i = 0; i < P; i++) A[i] = new double[i - f[i] + 1];
            var Jr = new double[P];
            var zaloha = new Uzel[m];
            for (int it = 0; it < iteraci; it++)
            {
                for (int i = 0; i < P; i++) { Array.Clear(A[i], 0, A[i].Length); Jr[i] = 0; }
                foreach (int j in prvky)
                {
                    var uu = uzlyPrvku(j); var ss = slotyPrvku(j);
                    int n = Rezidua(k, mo, j, r0);
                    var gi = new int[6];
                    for (int a = 0; a < 6; a++)
                    {
                        gi[a] = gidx(uu[a], ss[a]);
                        if (gi[a] < 0) continue;
                        var u = mo.U[uu[a]];
                        double v = GetP(u, ss[a]);
                        double h = 1e-6 * Math.Max(1, Math.Abs(v) * (ss[a] < 2 ? 1e-3 : 0));
                        SetP(u, ss[a], v + h);
                        Rezidua(k, mo, j, r1);
                        SetP(u, ss[a], v);
                        for (int i = 0; i < n; i++) g[a][i] = (r1[i] - r0[i]) / h;
                    }
                    for (int a = 0; a < 6; a++)
                    {
                        int ia = gi[a];
                        if (ia < 0) continue;
                        double sr = 0;
                        for (int i = 0; i < n; i++) sr += g[a][i] * r0[i];
                        Jr[ia] += sr;
                        for (int b = 0; b < 6; b++)
                        {
                            int ib = gi[b];
                            if (ib < 0 || ib > ia) continue;
                            // stejny parametr muze byt v prvku dvakrat jen u m = 2 s jinym uzlem -> pres index OK
                            double sg = 0;
                            for (int i = 0; i < n; i++) sg += g[a][i] * g[b][i];
                            A[ia][ib - f[ia]] += sg;
                        }
                    }
                }
                double maxDiag = 0;
                for (int i = 0; i < P; i++) maxDiag = Math.Max(maxDiag, A[i][i - f[i]]);
                bool prijato = false;
                for (int pokus = 0; pokus < 12; pokus++)
                {
                    var L = new double[P][];
                    for (int i = 0; i < P; i++)
                    {
                        L[i] = (double[])A[i].Clone();
                        L[i][i - f[i]] += lambda * (A[i][i - f[i]] + 1e-6 * maxDiag) + 1e-12;
                    }
                    var dx = new double[P];
                    for (int i = 0; i < P; i++) dx[i] = -Jr[i];
                    if (!Cholesky(L, f, P) ) { lambda *= 10; continue; }
                    Res(L, f, P, dx);
                    for (int j = 0; j < m; j++) zaloha[j] = ofs[j] >= 0 ? mo.U[j].Kopie() : null;
                    double krok = 0;
                    for (int j = 0; j < m; j++)
                    {
                        if (ofs[j] < 0) continue;
                        var u = mo.U[j];
                        u.X += dx[ofs[j]]; u.Y += dx[ofs[j] + 1];
                        krok = Math.Max(krok, Math.Abs(dx[ofs[j]]) + Math.Abs(dx[ofs[j] + 1]));
                        if (u.Roh) { u.Fi += dx[ofs[j] + 2]; u.Fo += dx[ofs[j] + 3]; }
                        else { u.Fi += dx[ofs[j] + 2]; u.Fo = u.Fi; }
                    }
                    double c1 = cena();
                    if (!double.IsNaN(c1) && c1 <= c0)
                    {
                        bool konec = k.Presne ? (c0 - c1 <= 1e-12 * (1 + c0) || krok < 1e-9) : (c0 - c1 <= 1e-6 * (1 + c0) || krok < 1e-5);
                        c0 = c1;
                        lambda = Math.Max(1e-9, lambda / 3);
                        prijato = true;
                        if (konec) return;
                        break;
                    }
                    for (int j = 0; j < m; j++) if (zaloha[j] != null) mo.U[j] = zaloha[j];
                    lambda *= 6;
                }
                if (!prijato) return;
            }
        }

        // Cholesky v obalce (radek i ma nenulove sloupce f[i]..i)
        static bool Cholesky(double[][] L, int[] f, int P)
        {
            for (int i = 0; i < P; i++)
            {
                for (int j = f[i]; j <= i; j++)
                {
                    double s = L[i][j - f[i]];
                    int k0 = Math.Max(f[i], f[j]);
                    var Li = L[i]; var Lj = L[j];
                    int oi = f[i], oj = f[j];
                    for (int kk = k0; kk < j; kk++) s -= Li[kk - oi] * Lj[kk - oj];
                    if (j < i) Li[j - oi] = s / Lj[j - oj];
                    else
                    {
                        if (!(s > 1e-300)) return false;
                        Li[i - oi] = Math.Sqrt(s);
                    }
                }
            }
            return true;
        }

        static void Res(double[][] L, int[] f, int P, double[] b)
        {
            for (int i = 0; i < P; i++)
            {
                double s = b[i];
                for (int kk = f[i]; kk < i; kk++) s -= L[i][kk - f[i]] * b[kk];
                b[i] = s / L[i][i - f[i]];
            }
            for (int i = P - 1; i >= 0; i--)
            {
                b[i] /= L[i][i - f[i]];
                double bi = b[i];
                for (int kk = f[i]; kk < i; kk++) b[kk] -= L[i][kk - f[i]] * bi;
            }
        }

        // vzorky znovu priradit k prvkum: uzel lezi na nejblizsim vzorku (v okoli), poradi se zachova
        static bool Prirad(Kontext k, Model mo, bool[] volne)
        {
            int m = mo.M, N = k.N;
            int okno = Math.Max(2, (int)Math.Ceiling(5 / k.H));
            bool zmena = false;
            for (int j = 0; j < m; j++)
            {
                if (volne != null && !volne[j]) continue;
                var u = mo.U[j];
                int pred = mo.U[(j - 1 + m) % m].B, dal = mo.U[(j + 1) % m].B;
                // dovoleny rozsah (pred, dal) cyklicky
                int lo = pred, hi = dal;
                if (hi <= lo) hi += N;
                int b = u.B;
                if (b <= lo) b += N;
                int best = b; double bd = double.MaxValue;
                for (int t = Math.Max(lo + 1, b - okno); t <= Math.Min(hi - 1, b + okno); t++)
                {
                    var q = k.Q[t % N];
                    double d = (q.X - u.X) * (q.X - u.X) + (q.Y - u.Y) * (q.Y - u.Y);
                    if (d < bd) { bd = d; best = t; }
                }
                int nb = best % N;
                if (nb != u.B) { u.B = nb; zmena = true; }
            }
            return zmena;
        }

        // Jak daleko od ostreho rohu je hrana obrazku zkreslena rozmazanim (px). U spicky (velky zlom smeru)
        // jsou obe hrany blizko u sebe a rozmazani je slije az do vzdalenosti ~1,5 sigma / tan(vnitrni uhel / 2).
        public static double ZonaRohu(double zakladni, double sigma, double zlom)
        {
            double vnitrni = Math.Max(0.05, Math.PI - Math.Min(Math.PI, zlom));
            return zakladni + Math.Min(25, 1.5 * sigma / Math.Tan(vnitrni / 2));
        }

        static void ObnovVahy(Kontext k, Model mo)
        {
            int N = k.N, m = mo.M;
            for (int i = 0; i < N; i++) k.W[i] = 1;
            for (int j = 0; j < m; j++)
            {
                var u = mo.U[j];
                if (!u.Roh) continue;
                // zona nesmi zabrat vic nez 35 % sousednich prvku
                int z = (int)Math.Ceiling(ZonaRohu(k.ZonaRohu, k.Sigma, Math.Abs(Geo.Norm(u.Fo - u.Fi))) / k.H);
                int zp = (int)(0.35 * PocetVzorku(k, mo, (j - 1 + m) % m)), zd = (int)(0.35 * PocetVzorku(k, mo, j));
                for (int t = -Math.Min(z, zp); t <= Math.Min(z, zd); t++) k.W[((u.B + t) % N + N) % N] = 0;
            }
        }

        // ---------- segmentace ----------
        static P[] Prevzorkuj(List<P> loop, double h, out int N)
        {
            int n = loop.Count;
            double obvod = 0;
            for (int i = 0; i < n; i++) obvod += Geo.Vzd(loop[i], loop[(i + 1) % n]);
            N = Math.Max(3, (int)Math.Round(obvod / h));
            double krok = obvod / N;
            var res = new P[N];
            int seg = 0; double poz = 0, zac = 0;   // zac = delka na zacatku segmentu seg
            double dl = Geo.Vzd(loop[0], loop[1 % n]);
            for (int i = 0; i < N; i++)
            {
                poz = i * krok;
                while (zac + dl < poz && seg < n - 1) { zac += dl; seg++; dl = Geo.Vzd(loop[seg], loop[(seg + 1) % n]); }
                double t = dl > 1e-12 ? (poz - zac) / dl : 0;
                t = Math.Max(0, Math.Min(1, t));
                var a = loop[seg]; var b = loop[(seg + 1) % n];
                res[i] = new P(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }
            return res;
        }

        static bool FitPrimka(Kontext k, int od, int @do, Kus u)
        {
            int c = @do - od + 1;
            if (c < 2) return false;
            double sx = 0, sy = 0;
            for (int i = od; i <= @do; i++) { var q = k.Q[i % k.N]; sx += q.X; sy += q.Y; }
            double mx = sx / c, my = sy / c, xx = 0, yy = 0, xy = 0;
            for (int i = od; i <= @do; i++) { var q = k.Q[i % k.N]; double dx = q.X - mx, dy = q.Y - my; xx += dx * dx; yy += dy * dy; xy += dx * dy; }
            double uh = 0.5 * Math.Atan2(2 * xy, xx - yy);
            double ux = Math.Cos(uh), uy = Math.Sin(uh);
            var a = k.Q[od % k.N]; var b = k.Q[@do % k.N];
            if ((b.X - a.X) * ux + (b.Y - a.Y) * uy < 0) { ux = -ux; uy = -uy; }
            double m = 0, s = 0;
            for (int i = od; i <= @do; i++)
            {
                var q = k.Q[i % k.N];
                double d = Math.Abs((q.X - mx) * uy - (q.Y - my) * ux);
                m = Math.Max(m, d); s += d * d;
            }
            u.Od = od; u.Do = @do; u.Obl = false; u.Cx = mx; u.Cy = my; u.Ux = ux; u.Uy = uy; u.Mx = m; u.Sweep = 0; u.Rms = Math.Sqrt(s / c);
            return m <= k.TolMax && Math.Sqrt(s / c) <= k.TolRms;
        }

        static bool FitKruh(Kontext k, int od, int @do, Kus u, double maxSweep = Math.PI + 0.05)
        {
            int c = @do - od + 1;
            if (c < 4) return false;
            double mx = 0, my = 0;
            for (int i = od; i <= @do; i++) { mx += k.Q[i % k.N].X; my += k.Q[i % k.N].Y; }
            mx /= c; my /= c;
            double suu = 0, suv = 0, svv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
            for (int i = od; i <= @do; i++)
            {
                double uu = k.Q[i % k.N].X - mx, vv = k.Q[i % k.N].Y - my;
                suu += uu * uu; suv += uu * vv; svv += vv * vv;
                suuu += uu * uu * uu; svvv += vv * vv * vv; suvv += uu * vv * vv; svuu += vv * uu * uu;
            }
            double det = suu * svv - suv * suv;
            if (Math.Abs(det) < 1e-12) return false;
            double b1 = 0.5 * (suuu + suvv), b2 = 0.5 * (svvv + svuu);
            double uc = (b1 * svv - b2 * suv) / det, vc = (suu * b2 - suv * b1) / det;
            double cx = mx + uc, cy = my + vc;
            double r = Math.Sqrt(uc * uc + vc * vc + (suu + svv) / c);
            if (double.IsNaN(r) || r > 1e5) return false;
            // geometricke upresneni (Gauss-Newton)
            for (int it = 0; it < 4; it++)
            {
                double a11 = 0, a12 = 0, a13 = 0, a22 = 0, a23 = 0, a33 = 0, g1 = 0, g2 = 0, g3 = 0;
                for (int i = od; i <= @do; i++)
                {
                    var q = k.Q[i % k.N];
                    double dx = q.X - cx, dy = q.Y - cy, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 1e-9) continue;
                    double j1 = -dx / d, j2 = -dy / d, j3 = -1, res = d - r;
                    a11 += j1 * j1; a12 += j1 * j2; a13 += j1 * j3; a22 += j2 * j2; a23 += j2 * j3; a33 += j3 * j3;
                    g1 += j1 * res; g2 += j2 * res; g3 += j3 * res;
                }
                double[] sol;
                if (!Res3(a11, a12, a13, a22, a23, a33, -g1, -g2, -g3, out sol)) break;
                cx += sol[0]; cy += sol[1]; r += sol[2];
                if (Math.Abs(sol[0]) + Math.Abs(sol[1]) + Math.Abs(sol[2]) < 1e-6) break;
            }
            if (double.IsNaN(r) || r <= 0.3 || r > 1e5) return false;
            double m = 0, s = 0, sweep = 0;
            double ap = Math.Atan2(k.Q[od % k.N].Y - cy, k.Q[od % k.N].X - cx);
            for (int i = od; i <= @do; i++)
            {
                var q = k.Q[i % k.N];
                double d = Math.Abs(Math.Sqrt((q.X - cx) * (q.X - cx) + (q.Y - cy) * (q.Y - cy)) - r);
                m = Math.Max(m, d); s += d * d;
                if (i > od)
                {
                    double an = Math.Atan2(q.Y - cy, q.X - cx);
                    sweep += Geo.Norm(an - ap);
                    ap = an;
                }
            }
            // body musi jit po kruznici jednim smerem
            double sg = Math.Sign(sweep);
            ap = Math.Atan2(k.Q[od % k.N].Y - cy, k.Q[od % k.N].X - cx);
            double proti = 0;
            for (int i = od + 1; i <= @do; i++)
            {
                var q = k.Q[i % k.N];
                double an = Math.Atan2(q.Y - cy, q.X - cx);
                double da = Geo.Norm(an - ap);
                if (da * sg < 0) proti += -da * sg;
                ap = an;
            }
            u.Od = od; u.Do = @do; u.Obl = true; u.Cx = cx; u.Cy = cy; u.R = r; u.Sweep = sweep; u.Mx = m; u.Rms = Math.Sqrt(s / c);
            return m <= k.TolMax && Math.Sqrt(s / c) <= k.TolRms && Math.Abs(sweep) <= maxSweep && proti < 0.15 + 0.1 * Math.Abs(sweep);
        }

        // Je usek hrany rovny? Sum obrazku skace nahodne (kruznice ho nevysvetli o moc lip nez primka),
        // krivka je soustavne vyboulená (kruznice sedi vyrazne lip) a oblouk na ni ma znatelnou sipku.
        static bool JeRovna(Kontext k, int od, int @do)
        {
            var p = new Kus(); var c = new Kus();
            FitPrimka(k, od, @do, p);
            if (p.Rms < 0.06) return true;
            if (!FitKruh(k, od, @do, c, 2 * Math.PI) && c.Rms == 0) return true;
            if (c.R <= 0 || double.IsNaN(c.Rms)) return true;
            double tet = Geo.Vzd(k.Q[od % k.N], k.Q[@do % k.N]);
            double sipka = c.R - Math.Sqrt(Math.Max(0, c.R * c.R - tet * tet / 4));
            return c.Rms > 0.6 * p.Rms || sipka < 0.25;
        }

        static bool Res3(double a11, double a12, double a13, double a22, double a23, double a33, double b1, double b2, double b3, out double[] x)
        {
            x = null;
            double det = a11 * (a22 * a33 - a23 * a23) - a12 * (a12 * a33 - a23 * a13) + a13 * (a12 * a23 - a22 * a13);
            if (Math.Abs(det) < 1e-18) return false;
            double d1 = b1 * (a22 * a33 - a23 * a23) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a23 - a22 * b3);
            double d2 = a11 * (b2 * a33 - a23 * b3) - b1 * (a12 * a33 - a23 * a13) + a13 * (a12 * b3 - b2 * a13);
            double d3 = a11 * (a22 * b3 - b2 * a23) - a12 * (a12 * b3 - b2 * a13) + b1 * (a12 * a23 - a22 * a13);
            x = new[] { d1 / det, d2 / det, d3 / det };
            return true;
        }

        // nejdelsi platny usek od "od" (pocet vzorku), nejmene minS
        static int Dosah(Kontext k, int od, int max, int minS, bool obl, Kus vys)
        {
            var u = new Kus();
            // oblouky pri segmentaci nejvys ~100 st.: dva oblouky po 180 st. tecne spojene do smycky jsou vzdy kruznice,
            // elipsa by se pak nedala vystihnout (cele kruznice se slouci az pri optimalizaci)
            Func<int, bool> plati = L => obl ? FitKruh(k, od, od + L, u, MaxSweepSeg) : FitPrimka(k, od, od + L, u);
            int ok = Math.Min(minS, max);
            if (!plati(ok)) { if (obl) FitKruh(k, od, od + ok, vys, MaxSweepSeg); else FitPrimka(k, od, od + ok, vys); return ok; }
            int spatne = -1;
            while (ok < max)
            {
                int L2 = Math.Min(max, ok * 2);
                if (plati(L2)) ok = L2; else { spatne = L2; break; }
            }
            if (spatne > 0)
                while (spatne - ok > 1)
                {
                    int s = (ok + spatne) / 2;
                    if (plati(s)) ok = s; else spatne = s;
                }
            if (obl) FitKruh(k, od, od + ok, vys, MaxSweepSeg); else FitPrimka(k, od, od + ok, vys);
            return ok;
        }

        static double TetivovyUhel(Kontext k, int i, int s)
        {
            int N = k.N;
            var a = k.Q[((i - s) % N + N) % N]; var b = k.Q[i % N]; var c = k.Q[(i + s) % N];
            return Math.Abs(Geo.Norm(Math.Atan2(c.Y - b.Y, c.X - b.X) - Math.Atan2(b.Y - a.Y, b.X - a.X)));
        }

        static List<Kus> Segmentuj(Kontext k)
        {
            int N = k.N;
            int s3 = Math.Max(2, (int)Math.Round(3 / k.H));
            int st = 0; double maxU = -1, minU = double.MaxValue; int stMin = 0;
            for (int i = 0; i < N; i++)
            {
                double uh = TetivovyUhel(k, i, s3);
                if (uh > maxU) { maxU = uh; st = i; }
                if (uh < minU) { minU = uh; stMin = i; }
            }
            if (maxU < k.UhelRohu) st = stMin;     // bez rohu: zacit na nejrovnejsim miste
            int minS = Math.Max(3, (int)Math.Round(2.5 / k.H));
            var kusy = new List<Kus>();
            int pos = st, konec = st + N;
            while (pos < konec)
            {
                int zbyva = konec - pos;
                var kl = new Kus(); var ka = new Kus();
                int ll = Dosah(k, pos, zbyva, minS, false, kl);
                int la = Dosah(k, pos, zbyva, minS, true, ka);
                bool obl = la > 1.25 * ll + 2 / k.H;
                // primka jen kdyz je usek opravdu rovny; soustavne vyboulený usek je oblouk
                if (!obl && la >= ll && ll * k.H >= 6 && !JeRovna(k, pos, pos + ll)) obl = true;
                var vys = obl ? ka : kl;
                int L = obl ? la : ll;
                if (zbyva - L < minS) { L = zbyva; if (obl) FitKruh(k, pos, pos + L, vys); else FitPrimka(k, pos, pos + L, vys); }
                vys.Od = pos; vys.Do = pos + L;
                kusy.Add(vys);
                pos += L;
                if (kusy.Count > N) return null;
            }
            return kusy;
        }

        // tecna a prumet bodu na kus
        static double Tecna(Kus u, P q)
        {
            if (!u.Obl) return Math.Atan2(u.Uy, u.Ux);
            return Math.Atan2(q.Y - u.Cy, q.X - u.Cx) + Math.Sign(u.Sweep == 0 ? 1 : u.Sweep) * Math.PI / 2;
        }
        static P Prumet(Kus u, P q)
        {
            if (!u.Obl) { double t = (q.X - u.Cx) * u.Ux + (q.Y - u.Cy) * u.Uy; return new P(u.Cx + t * u.Ux, u.Cy + t * u.Uy); }
            double dx = q.X - u.Cx, dy = q.Y - u.Cy, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-9) return q;
            return new P(u.Cx + u.R * dx / d, u.Cy + u.R * dy / d);
        }

        // prusecik dvou kusu nejblize bodu q (null = neni)
        static P? Prusecik(Kus a, Kus b, P q)
        {
            var kand = new List<P>();
            if (!a.Obl && !b.Obl)
            {
                double det = a.Ux * b.Uy - a.Uy * b.Ux;
                if (Math.Abs(det) < 1e-9) return null;
                double t = ((b.Cx - a.Cx) * b.Uy - (b.Cy - a.Cy) * b.Ux) / det;
                kand.Add(new P(a.Cx + t * a.Ux, a.Cy + t * a.Uy));
            }
            else if (a.Obl && b.Obl) KruhKruh(a.Cx, a.Cy, a.R, b.Cx, b.Cy, b.R, kand);
            else
            {
                var l = a.Obl ? b : a; var c = a.Obl ? a : b;
                double fx = l.Cx - c.Cx, fy = l.Cy - c.Cy;
                double bb = fx * l.Ux + fy * l.Uy, cc = fx * fx + fy * fy - c.R * c.R;
                double disc = bb * bb - cc;
                if (disc >= 0)
                {
                    double sq = Math.Sqrt(disc);
                    foreach (double t in new[] { -bb - sq, -bb + sq }) kand.Add(new P(l.Cx + t * l.Ux, l.Cy + t * l.Uy));
                }
            }
            P? best = null; double bd = double.MaxValue;
            foreach (var p in kand) { double d = Geo.Vzd(p, q); if (d < bd) { bd = d; best = p; } }
            return bd < 6 ? best : null;
        }

        static void KruhKruh(double x0, double y0, double r0, double x1, double y1, double r1, List<P> res)
        {
            double dx = x1 - x0, dy = y1 - y0, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-9 || d > r0 + r1 || d < Math.Abs(r0 - r1)) return;
            double a = (r0 * r0 - r1 * r1 + d * d) / (2 * d), h = Math.Sqrt(Math.Max(0, r0 * r0 - a * a));
            double mx = x0 + a * dx / d, my = y0 + a * dy / d;
            res.Add(new P(mx + h * dy / d, my - h * dx / d));
            res.Add(new P(mx - h * dy / d, my + h * dx / d));
        }

        static Model PostavModel(Kontext k, List<Kus> kusy)
        {
            int N = k.N;
            // male zakulacene kusy (rozmazany ostry roh) pryc -> roh
            var roh = new List<bool>();          // roh na zacatku kusu i
            for (int i = 0; i < kusy.Count; i++) roh.Add(false);
            for (int i = 0; i < kusy.Count && kusy.Count > 2; i++)
            {
                var u = kusy[i];
                if (!u.Obl || u.R >= k.RMin || Math.Abs(u.Sweep) < 30 * Math.PI / 180) continue;
                int stred = (u.Od + u.Do) / 2;
                int pi = (i - 1 + kusy.Count) % kusy.Count, ni = (i + 1) % kusy.Count;
                // predchozi konci a dalsi zacina uprostred maleho oblouku
                kusy[pi].Do = i == 0 ? stred + N : stred;
                kusy[ni].Od = ni == 0 ? stred - N : stred;
                kusy.RemoveAt(i);
                roh.RemoveAt(i);
                int nidx = i % kusy.Count;
                roh[nidx] = true;
                i--;
            }
            // znovu prolozit kusy, jejichz rozsah se zmenil, a urcit rohy podle zlomu tecen
            for (int i = 0; i < kusy.Count; i++)
            {
                var u = kusy[i];
                int od = ((u.Od % N) + N) % N, @do = od + (u.Do - u.Od);
                if (u.Obl) FitKruh(k, od, @do, u); else FitPrimka(k, od, @do, u);
                u.Od = od; u.Do = @do;
            }
            int m = kusy.Count;
            if (m < 2)
            {
                // jediny kus (napr. kruh): rozdelit na dva
                var u = kusy[0];
                int pul = (u.Do - u.Od) / 2;
                var a = new Kus(); var b = new Kus();
                if (!FitKruh(k, u.Od, u.Od + pul, a)) FitPrimka(k, u.Od, u.Od + pul, a);
                if (!FitKruh(k, u.Od + pul, u.Do, b)) FitPrimka(k, u.Od + pul, u.Do, b);
                a.Od = u.Od; a.Do = u.Od + pul; b.Od = u.Od + pul; b.Do = u.Do;
                kusy = new List<Kus> { a, b }; roh = new List<bool> { false, false };
                m = 2;
            }
            var mo = new Model();
            for (int i = 0; i < m; i++)
            {
                var a = kusy[(i - 1 + m) % m]; var b = kusy[i];
                var q = k.Q[b.Od % N];
                double ta = Tecna(a, Prumet(a, q)), tb = Tecna(b, Prumet(b, q));
                double zlom = Geo.Norm(tb - ta);
                bool jeRoh = roh[i] || Math.Abs(zlom) >= k.UhelRohu;
                var uz = new Uzel { B = b.Od % N, Roh = jeRoh };
                if (jeRoh)
                {
                    var x = Prusecik(a, b, q);
                    var pa = Prumet(a, q); var pb = Prumet(b, q);
                    var p = x.HasValue ? x.Value : new P((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2);
                    uz.X = p.X; uz.Y = p.Y;
                    uz.Fi = Tecna(a, p); uz.Fo = Tecna(b, p);
                }
                else
                {
                    var pa = Prumet(a, q); var pb = Prumet(b, q);
                    uz.X = (pa.X + pb.X) / 2; uz.Y = (pa.Y + pb.Y) / 2;
                    uz.Fi = uz.Fo = ta + zlom / 2;
                }
                mo.U.Add(uz);
                mo.Obl.Add(b.Obl);
            }
            // hladky uzel mezi dvema primkami: primky by musely byt v zakrytu -> vlozit zaobleni
            for (int j = 0; j < mo.M; j++)
            {
                int pj = (j - 1 + mo.M) % mo.M;
                if (mo.U[j].Roh || mo.Obl[pj] || mo.Obl[j]) continue;
                VlozZaobleni(k, mo, j);
                j++;
            }
            return mo;
        }

        // uzel j (hladky, mezi primkami) rozdeli na dva uzly s kratkym obloukem mezi nimi
        static void VlozZaobleni(Kontext k, Model mo, int j)
        {
            int m = mo.M, N = k.N;
            var u = mo.U[j];
            int pocP = PocetVzorku(k, mo, (j - 1 + m) % m), pocD = PocetVzorku(k, mo, j);
            int t = Math.Max(1, Math.Min((int)Math.Ceiling(2 / k.H), Math.Min(pocP, pocD) / 4));
            var A = mo.U[(j - 1 + m) % m]; var B = mo.U[(j + 1) % m];
            double fa = Math.Atan2(u.Y - A.Y, u.X - A.X), fb = Math.Atan2(B.Y - u.Y, B.X - u.X);
            double d = t * k.H;
            var u1 = new Uzel { X = u.X - d * Math.Cos(fa), Y = u.Y - d * Math.Sin(fa), Fi = fa, Fo = fa, B = ((u.B - t) % N + N) % N };
            var u2 = new Uzel { X = u.X + d * Math.Cos(fb), Y = u.Y + d * Math.Sin(fb), Fi = fb, Fo = fb, B = (u.B + t) % N };
            mo.U[j] = u1;
            mo.U.Insert(j + 1, u2);
            mo.Obl.Insert(j, true);      // prvek j (z u1 do u2) = oblouk; puvodni prvek j je ted j+1
            mo.Cil = null;
        }

        // ---------- upravy modelu ----------
        static bool[] Okno(Model mo, int stred, int r)
        {
            int m = mo.M;
            var v = new bool[m];
            if (m <= 2 * r + 2) { for (int i = 0; i < m; i++) v[i] = true; return v; }
            for (int t = -r; t <= r + 1; t++) v[((stred + t) % m + m) % m] = true;
            return v;
        }

        static double MaxChyba(Kontext k, Model mo, int j) { double mx, rms; Chyba(k, mo, j, out mx, out rms); return mx; }
        static bool Ok(Kontext k, Model mo, int j) { double mx, rms; Chyba(k, mo, j, out mx, out rms); return mx <= k.TolMax && rms <= k.TolRms; }

        static void Prevezmi(Model cil, Model z)
        {
            cil.U = z.U; cil.Obl = z.Obl; cil.Cil = z.Cil;
        }

        // Usek hrany, ktery se pri uprave v okne (Okno(z, stred, r)) mohl zmenit: od pevneho uzlu pred oknem
        // po pevny uzel za nim. Pevne uzly maji v puvodnim i upravenem modelu stejny vzorek B.
        static void RozsahOkna(Model z, int stred, int r, out int bOd, out int bDo, out bool cele)
        {
            int m = z.M;
            cele = m <= 2 * r + 4;
            bOd = z.U[((stred - r - 1) % m + m) % m].B;
            bDo = z.U[((stred + r + 2) % m + m) % m].B;
        }

        // nejvetsi odchylka vzorku hrany v useku <bOd, bDo) od modelu (vzorky u ostrych rohu se nepocitaji)
        static double ChybaVzorku(Kontext k, Model mo, int bOd, int bDo, bool cele)
        {
            double rms;
            return ChybaVzorku(k, mo, bOd, bDo, cele, out rms);
        }

        static double ChybaVzorku(Kontext k, Model mo, int bOd, int bDo, bool cele, out double rms)
        {
            ObnovVahy(k, mo);
            int m = mo.M, N = k.N;
            int delka = ((bDo - bOd) % N + N) % N;
            double mx = 0, sum = 0; int pocet = 0;
            for (int j = 0; j < m; j++)
            {
                var A = mo.U[j]; var B = mo.U[(j + 1) % m];
                int od = A.B, @do = B.B;
                if (@do <= od) @do += N;
                double s = double.NaN;
                for (int i = od; i < @do; i++)
                {
                    int ii = i % N;
                    if (!cele && ((ii - bOd) % N + N) % N >= delka) continue;
                    if (k.W[ii] <= 0) continue;
                    if (double.IsNaN(s)) s = mo.Obl[j] ? Sweep(A, B) : 0;
                    double d = Math.Abs(SD(k.Q[ii].X, k.Q[ii].Y, A.X, A.Y, B.X, B.Y, s));
                    if (mo.Obl[j] && !NaOblouku(k.Q[ii], A, B, s)) d = Math.Min(Geo.Vzd(k.Q[ii], new P(A.X, A.Y)), Geo.Vzd(k.Q[ii], new P(B.X, B.Y)));
                    if (d > mx) mx = d;
                    sum += d * d; pocet++;
                }
            }
            rms = pocet > 0 ? Math.Sqrt(sum / pocet) : 0;
            return mx;
        }

        // porovna puvodni a upraveny model na stejnem useku hrany; vahy v k.W pak odpovidaji puvodnimu modelu
        static void Porovnej(Kontext k, Model mo, Model z, int stred, out double pred, out double po)
        {
            int bOd, bDo; bool cele;
            RozsahOkna(z, stred, 2, out bOd, out bDo, out cele);
            po = ChybaVzorku(k, z, bOd, bDo, cele);
            pred = ChybaVzorku(k, mo, bOd, bDo, cele);
        }

        // uprava smi zhorsit odchylku jen v ramci tolerance
        // (tolerance se nesmi prekrocit; kde uz prekrocena je, nesmi se zhorsit)
        static bool Snese(Kontext k, double pred, double po) { return po <= Math.Max(k.TolMax, pred + 1e-3); }

        // pootoci cyklicky model tak, aby uzel s byl na indexu 0 (upravy pak nepresahuji pres konec seznamu)
        static void Otoc(Model z, int s)
        {
            int m = z.M;
            s = ((s % m) + m) % m;
            if (s == 0) return;
            z.U = z.U.Skip(s).Concat(z.U.Take(s)).ToList();
            z.Obl = z.Obl.Skip(s).Concat(z.Obl.Take(s)).ToList();
            z.Cil = null;
        }

        // Ostry roh, nebo male zaobleni? Vzorky u rohu (pri prokladu vynechane) se porovnaji se zaoblenim
        // polomeru r tecnym k obema hranam. Rozmazany ostry roh vypada jako zaobleni R < rMin; vetsi polomer
        // = skutecne zaobleni -> vlozit oblouk.
        static bool RohyNaZaobleni(Kontext k, Model mo, ref int limit)
        {
            bool zmena = false;
            for (int j = 0; j < mo.M && limit > 0; j++)
            {
                var u = mo.U[j];
                if (!u.Roh) continue;
                long klic = Klic(2, u.B, 0);
                if (k.Zamitnute.Contains(klic)) continue;
                double th = Geo.Norm(u.Fo - u.Fi);
                if (Math.Abs(th) > 150 * Math.PI / 180 || Math.Abs(th) < 5 * Math.PI / 180) continue;
                int m = mo.M;
                double lp = Delka(mo, (j - 1 + m) % m), ln = Delka(mo, j);
                double tan2 = Math.Tan(Math.Abs(th) / 2);
                double tMax = 0.8 * Math.Min(lp, ln);
                // jen mala zaobleni (vetsi najde uz segmentace jako oblouk) a okno jen kolem rohu: sousedni prvky
                // se tu nahrazuji tecnami a u zakrivenych hran by delsi okno nutilo vetsi polomer
                double zona = ZonaRohu(k.ZonaRohu, k.Sigma, Math.Abs(th));
                double tOkno = Math.Min(Math.Min(tMax, 4 * k.RMin * tan2), 2.5 * zona);
                double rMax = tOkno / tan2;
                if (rMax < k.RMin) continue;
                tOkno += 2;
                int w = (int)Math.Ceiling(tOkno / k.H);
                var vz = new List<P>();
                for (int t = -w; t <= w; t++) vz.Add(k.Q[((u.B + t) % k.N + k.N) % k.N]);
                var X = new P(u.X, u.Y);
                var di = new P(Math.Cos(u.Fi), Math.Sin(u.Fi)); var dz = new P(Math.Cos(u.Fo), Math.Sin(u.Fo));
                Func<double, double> cena = r =>
                {
                    double tt = r * tan2;
                    var T1 = new P(X.X - di.X * tt, X.Y - di.Y * tt);
                    var T2 = new P(X.X + dz.X * tt, X.Y + dz.Y * tt);
                    var Z1 = new P(X.X - di.X * (tt + 1e3), X.Y - di.Y * (tt + 1e3));
                    var Z2 = new P(X.X + dz.X * (tt + 1e3), X.Y + dz.Y * (tt + 1e3));
                    double sg = th > 0 ? 1 : -1, s = 0;
                    double ccx = T1.X - di.Y * sg * r, ccy = T1.Y + di.X * sg * r;     // stred zaobleni
                    double a1 = Math.Atan2(T1.Y - ccy, T1.X - ccx);
                    foreach (var q in vz)
                    {
                        double d = Math.Min(Geo.VzdalenostKSegmentu(q, Z1, T1, 0), Geo.VzdalenostKSegmentu(q, T2, Z2, 0));
                        if (tt > 1e-9)
                        {
                            double vx = q.X - ccx, vy = q.Y - ccy;
                            double rel = sg > 0 ? Geo.Mod2Pi(Math.Atan2(vy, vx) - a1) : Geo.Mod2Pi(a1 - Math.Atan2(vy, vx));
                            if (rel <= Math.Abs(th)) d = Math.Min(d, Math.Abs(Math.Sqrt(vx * vx + vy * vy) - r));
                        }
                        s += d * d;
                    }
                    return s;
                };
                // hrube vzorkovani + zjemneni
                int G = 48; double rb = 0, cb = double.MaxValue;
                for (int i = 0; i <= G; i++) { double r = rMax * i / G, c = cena(r); if (c < cb) { cb = c; rb = r; } }
                double a = Math.Max(0, rb - rMax / G), b = Math.Min(rMax, rb + rMax / G);
                for (int it = 0; it < 30; it++)
                {
                    double m1 = a + (b - a) / 3, m2 = b - (b - a) / 3;
                    if (cena(m1) < cena(m2)) b = m2; else a = m1;
                }
                double rOpt = (a + b) / 2;
                // ostry roh, ktery by presahoval obrazek vic nez tolerance, se zkusi zaoblit vzdy (tolerance ma prednost)
                bool vynutit = PresahRohu(k, mo, j) > k.TolMax;
                if (vynutit) rOpt = Math.Max(rOpt, 0.5 * k.RMin);
                if (Ladeni != null && rOpt < k.RMin) Ladeni(string.Format(CultureInfo.InvariantCulture, "ROH {0:0.0},{1:0.0} rOpt {2:0.00} < RMin, rMax {3:0.00}", X.X, X.Y, rOpt, rMax));
                if (rOpt < k.RMin && !vynutit) { k.Zamitnute.Add(klic); continue; }
                // Rozmazani obrazku zakulati i ostry roh. Kdyz se data od drahy ostreho rohu neodchyluji vic,
                // nez vysvetli rozmazani (+ pul tolerance), je to rozmazany ostry roh, ne zaobleni.
                {
                    double ustup = UstupRohu(k), mxOstry = 0;
                    // ostry roh = sousedni prvky modelu (oblouky se zakrivuji, tecny by roh zkreslily)
                    var Up = mo.U[(j - 1 + m) % m]; var Un = mo.U[(j + 1) % m];
                    var Pp = new P(Up.X, Up.Y); var Pn = new P(Un.X, Un.Y);
                    double bp = mo.Obl[(j - 1 + m) % m] ? Math.Tan(Sweep(Up, u) / 4) : 0, bn = mo.Obl[j] ? Math.Tan(Sweep(u, Un) / 4) : 0;
                    foreach (var q in vz) mxOstry = Math.Max(mxOstry, Math.Min(Geo.VzdalenostKSegmentu(q, Pp, X, bp), Geo.VzdalenostKSegmentu(q, X, Pn, bn)));
                    if (Ladeni != null) Ladeni(string.Format(CultureInfo.InvariantCulture, "ROH->ZAOBLENI {0:0.0},{1:0.0} th {2:0} rOpt {3:0.00} rMax {4:0.00} mxOstry {5:0.00} ustup {6:0.00}", X.X, X.Y, th * 180 / Math.PI, rOpt, rMax, mxOstry, ustup));
                    if (mxOstry <= ustup && !vynutit) { k.Zamitnute.Add(klic); continue; }
                }
                limit--;
                var z = ZaoblenyRoh(k, mo, j, rOpt);
                if (z == null) continue;
                Prolozit(k, z, Okno(z, 1, 2), 2);
                double pred, po;
                Porovnej(k, mo, z, 1, out pred, out po);
                if (Konecne(z) && po <= k.TolMax) { Prevezmi(mo, z); ObnovVahy(k, mo); zmena = true; }
                else k.Zamitnute.Add(klic);
            }
            return zmena;
        }

        // Jak daleko od drahy ostreho rohu (hran) smi lezet data, aby to porad byl ostry roh (px): rozmazani obrazku
        // zakulati spicku asi o 0,6 sigma (vzdalenost od hran temer nezavisi na uhlu) + polovina tolerance.
        // Vetsi odchylka = skutecne zaobleni (napr. R4 pri sigma 0,8 lezi 1,2 px od hran).
        static double UstupRohu(Kontext k) { return 0.6 * k.Sigma + 0.45 * k.TolMax; }

        // kopie modelu, v niz je roh j nahrazen tecnym zaoblenim polomeru r (roh = uzel 1 po pootoceni);
        // null, kdyz se zaobleni na sousedni prvky nevejde
        static Model ZaoblenyRoh(Kontext k, Model mo, int j, double r)
        {
            var z = mo.Kopie();
            Otoc(z, j - 1);
            var v = z.U[1];
            double th = Geo.Norm(v.Fo - v.Fi);
            double tt = r * Math.Tan(Math.Abs(th) / 2);
            int dt = Math.Max(1, (int)Math.Round(tt / k.H));
            int pp = PocetVzorku(k, z, 0), pn = PocetVzorku(k, z, 1);
            dt = Math.Min(dt, Math.Min(pp, pn) / 2 - 1);
            if (dt < 1 || tt > 0.45 * Math.Min(Delka(z, 0), Delka(z, 1))) return null;
            double ci = Math.Cos(v.Fi), si = Math.Sin(v.Fi), co = Math.Cos(v.Fo), so = Math.Sin(v.Fo);
            var u1 = new Uzel { X = v.X - ci * tt, Y = v.Y - si * tt, Fi = v.Fi, Fo = v.Fi, B = ((v.B - dt) % k.N + k.N) % k.N };
            var u2 = new Uzel { X = v.X + co * tt, Y = v.Y + so * tt, Fi = v.Fo, Fo = v.Fo, B = (v.B + dt) % k.N };
            z.U[1] = u1;
            z.U.Insert(2, u2);
            z.Obl.Insert(1, true);
            return z;
        }

        // malé oblouky (R < rMin, velky uhel) = rozmazany ostry roh
        static bool MaleObloukyNaRohy(Kontext k, Model mo, ref int limit)
        {
            bool zmena = false;
            for (int j = 0; j < mo.M && mo.M > 2 && limit > 0; j++)
            {
                if (!mo.Obl[j]) continue;
                var A = mo.U[j]; var B = mo.U[(j + 1) % mo.M];
                double s = Sweep(A, B), c = Geo.Vzd(new P(A.X, A.Y), new P(B.X, B.Y));
                if (Math.Abs(s) < 1e-6) continue;
                double R = c / (2 * Math.Abs(Math.Sin(s / 2)));
                if (Math.Abs(s) < 30 * Math.PI / 180) continue;
                // maly polomer, nebo oblouk, ktery se od drahy ostreho rohu (tecen) odchyluje jen o tolik, kolik
                // vysvetli rozmazani obrazku (0,9 x mez z RohyNaZaobleni = hystereze)
                double cs = Math.Cos(Math.Abs(s) / 2);
                bool rozmazany = Math.Abs(s) <= 150 * Math.PI / 180 && R < 4 * k.RMin &&
                                 R * (1 - cs) <= 0.9 * UstupRohu(k);
                if (R >= 0.85 * k.RMin && !rozmazany) continue;    // 0,85: hystereze proti RohyNaZaobleni
                long klic = Klic(3, A.B, B.B);
                if (k.Zamitnute.Contains(klic)) continue;
                limit--;
                var z = mo.Kopie();
                // oblouk j nahradit rohem: uzly j a j+1 splynou (po pootoceni je oblouk prvek 1)
                Otoc(z, j - 1);
                var a = z.U[1]; var b = z.U[2];
                var r = new Uzel { Roh = true, Fi = a.Fi, Fo = b.Fo, B = PulB(k, a.B, b.B) };
                // roh v pruseciku tecen
                double det = Math.Cos(a.Fi) * Math.Sin(b.Fo) - Math.Sin(a.Fi) * Math.Cos(b.Fo);
                if (Math.Abs(det) > 1e-6)
                {
                    double t = ((b.X - a.X) * Math.Sin(b.Fo) - (b.Y - a.Y) * Math.Cos(b.Fo)) / det;
                    r.X = a.X + t * Math.Cos(a.Fi); r.Y = a.Y + t * Math.Sin(a.Fi);
                }
                else { r.X = (a.X + b.X) / 2; r.Y = (a.Y + b.Y) / 2; }
                z.U[1] = r;
                z.U.RemoveAt(2);
                z.Obl.RemoveAt(1);
                Prolozit(k, z, Okno(z, 1, 2), 2);
                double pred, po;
                Porovnej(k, mo, z, 1, out pred, out po);
                if (Konecne(z) && Snese(k, pred, po) && PresahRohu(k, z, 1) <= k.TolMax) { Prevezmi(mo, z); ObnovVahy(k, mo); zmena = true; j = -1; }
                else k.Zamitnute.Add(klic);
            }
            return zmena;
        }

        static int PulB(Kontext k, int a, int b)
        {
            if (b < a) b += k.N;
            return ((a + b) / 2) % k.N;
        }

        // roh s malym zlomem: zkusit hladke napojeni (nebo sloucit primky)
        static bool SlabeRohy(Kontext k, Model mo, ref int limit)
        {
            bool zmena = false;
            for (int j = 0; j < mo.M && limit > 0; j++)
            {
                var u = mo.U[j];
                if (!u.Roh || Math.Abs(Geo.Norm(u.Fo - u.Fi)) >= 0.6 * k.UhelRohu) continue;
                if (mo.M < 3) continue;
                long klic = Klic(4, u.B, 0);
                if (k.Zamitnute.Contains(klic)) continue;
                limit--;
                var z = mo.Kopie();
                Otoc(z, j - 1);                       // roh je uzel 1, prvky 0 a 1 kolem nej
                var v = z.U[1];
                v.Roh = false;
                v.Fi = v.Fo = v.Fi + Geo.Norm(v.Fo - v.Fi) / 2;
                if (!z.Obl[0] && !z.Obl[1]) { z.U.RemoveAt(1); z.Obl.RemoveAt(1); }   // dve primky -> jedna
                Prolozit(k, z, Okno(z, 1, 4), 3);
                double pred, po;
                Porovnej(k, mo, z, 1, out pred, out po);
                if (Konecne(z) && Snese(k, pred, po)) { Prevezmi(mo, z); ObnovVahy(k, mo); zmena = true; continue; }
                // hladce to nejde -> maly zlom zaoblit drobnym tecnym obloukem (oblouk ~2,5 px, odchylka R*th^2/8)
                double th = Math.Abs(Geo.Norm(u.Fo - u.Fi));
                double r = Math.Max(k.RMin, 2.5 / Math.Max(th, 0.01));
                var z2 = ZaoblenyRoh(k, mo, j, r);
                if (z2 == null) z2 = ZaoblenyRoh(k, mo, j, r / 3);
                if (z2 != null)
                {
                    Prolozit(k, z2, Okno(z2, 1, 4), 3);
                    Porovnej(k, mo, z2, 1, out pred, out po);
                    // drobne zaobleni malicko zmeni jen spicku zlomu (R*th^2/8), odstrani ale netecne napojeni
                    if (Konecne(z2) && po <= Math.Max(k.TolMax, pred + 0.1)) { Prevezmi(mo, z2); ObnovVahy(k, mo); zmena = true; continue; }
                }
                k.Zamitnute.Add(klic);
            }
            return zmena;
        }

        // prvky za toleranci rozdelit v miste nejvetsi odchylky
        static bool Rozdel(Kontext k, Model mo, ref int limit)
        {
            bool zmena = false;
            for (int j = 0; j < mo.M && limit > 0; j++)
            {
                double mx, rms;
                Chyba(k, mo, j, out mx, out rms);
                if (mx <= k.TolMax && rms <= k.TolRms) continue;
                int n = PocetVzorku(k, mo, j);
                if (n < 8) continue;
                var A = mo.U[j]; var B = mo.U[(j + 1) % mo.M];
                long klic = Klic(1, A.B, B.B);
                if (k.Zamitnute.Contains(klic)) continue;
                limit--;
                // misto nejvetsi odchylky (ne u kraju)
                double s = mo.Obl[j] ? Sweep(A, B) : 0;
                // rez ve stredni casti prvku (20-80 %), kde je odchylka nejvetsi; u kraje by vznikl jen
                // kratky zbytek, ktery se smrskne na nulu a nic nevyresi
                int best = A.B + n / 2; double bd = -1;
                for (int t = Math.Max(3, n / 5); t < Math.Min(n - 3, n - n / 5); t++)
                {
                    int ii = (A.B + t) % k.N;
                    if (k.W[ii] <= 0) continue;
                    double d = Math.Abs(SD(k.Q[ii].X, k.Q[ii].Y, A.X, A.Y, B.X, B.Y, s));
                    if (d > bd) { bd = d; best = A.B + t; }
                }
                // pulky prolozit nezavisle -> typ a tecna noveho uzlu; pulka oblouku zustane obloukem,
                // pokud neni opravdu rovna
                var k1 = new Kus(); var k2 = new Kus();
                int od = A.B, @do = A.B + n;
                bool p1 = FitPrimka(k, od, best, k1), p2 = FitPrimka(k, best, @do, k2);
                if (mo.Obl[j])
                {
                    p1 = p1 && (best - od) * k.H >= 6 && JeRovna(k, od, best);
                    p2 = p2 && (@do - best) * k.H >= 6 && JeRovna(k, best, @do);
                }
                var o1 = new Kus(); var o2 = new Kus();
                bool c1 = FitKruh(k, od, best, o1), c2 = FitKruh(k, best, @do, o2);
                bool obl1 = !p1 && (c1 || o1.Mx < k1.Mx || mo.Obl[j]), obl2 = !p2 && (c2 || o2.Mx < k2.Mx || mo.Obl[j]);
                var u1 = obl1 ? o1 : k1; var u2 = obl2 ? o2 : k2;
                var q = k.Q[best % k.N];
                double ta = Tecna(u1, Prumet(u1, q)), tb = Tecna(u2, Prumet(u2, q));
                double zl = Geo.Norm(tb - ta);
                var nu = new Uzel { X = q.X, Y = q.Y, B = best % k.N };
                nu.Roh = Math.Abs(zl) >= k.UhelRohu;
                if (nu.Roh) { nu.Fi = ta; nu.Fo = tb; }
                else
                {
                    nu.Fi = nu.Fo = ta + zl / 2;
                    if (!obl1 && !obl2) { if (k1.Mx > k2.Mx) obl1 = true; else obl2 = true; }   // dve primky hladce nejdou
                }
                var z = mo.Kopie();
                z.U.Insert(j + 1, nu);
                z.Obl[j] = obl1;
                z.Obl.Insert(j + 1, obl2);
                z.Cil = null;
                Prolozit(k, z, Okno(z, j + 1, 2), 2);
                // zlepseni se meri na vzorcich rozdeleneho prvku (max i RMS vuci toleranci), okoli se nesmi zhorsit
                double pred, po, rmsPo;
                Porovnej(k, mo, z, j + 1, out pred, out po);
                double mxPo = ChybaVzorku(k, z, A.B, B.B, false, out rmsPo);
                ObnovVahy(k, mo);
                double skorePred = Math.Max(mx / k.TolMax, rms / k.TolRms), skorePo = Math.Max(mxPo / k.TolMax, rmsPo / k.TolRms);
                if (Ladeni != null) Ladeni(string.Format(CultureInfo.InvariantCulture, "ROZDEL j{0} B{1}-{2} typ {3}/{4} roh {5} skore {6:0.00}->{7:0.00} okno {8:0.00}->{9:0.00}", j, A.B, B.B, obl1, obl2, nu.Roh, skorePred, skorePo, pred, po));
                // deleni zpresnuje: okoli se smi docasne mirne zhorsit (dalsi deleni ho dorovna, tolerance se kontroluje na konci)
                if (Konecne(z) && skorePo < 0.85 * skorePred && po <= Math.Max(k.TolMax, pred * 1.1 + 0.05)) { Prevezmi(mo, z); ObnovVahy(k, mo); zmena = true; j++; }
                else k.Zamitnute.Add(klic);
            }
            return zmena;
        }

        // sousedni prvky nahradit jednim, kdyz to jde v toleranci
        static bool Sluc(Kontext k, Model mo, ref int limit)
        {
            bool zmena = false;
            bool dalsi = true;
            var zamitnute = k.Zamitnute;
            while (dalsi && limit > 0 && mo.M > 2)
            {
                dalsi = false;
                int m = mo.M;
                // kandidati: hladke uzly, serazene podle chyby spolecneho prokladu
                var kand = new List<KeyValuePair<double, int>>();
                for (int j = 0; j < m; j++)
                {
                    var u = mo.U[j];
                    if (u.Roh) continue;
                    int pj = (j - 1 + m) % m;
                    var A = mo.U[pj]; var B = mo.U[(j + 1) % m];
                    long klic = Klic(5, A.B, B.B);
                    if (zamitnute.Contains(klic)) continue;
                    int od = A.B, @do = B.B;
                    if (@do <= od) @do += k.N;
                    var kp = new Kus(); var ko = new Kus();
                    bool lp = FitPrimka(k, od, @do, kp);
                    bool lo = FitKruh(k, od, @do, ko, MaxSweepSluc);
                    double e = Math.Min(lp ? kp.Mx : 1e9, lo ? ko.Mx : 1e9);
                    if (e > k.TolMax) continue;
                    kand.Add(new KeyValuePair<double, int>(e, j));
                }
                foreach (var kv in kand.OrderBy(x => x.Key))
                {
                    if (limit <= 0) break;
                    int j = kv.Value; m = mo.M;
                    int pj = (j - 1 + m) % m;
                    var A = mo.U[pj]; var B = mo.U[(j + 1) % m];
                    long klic = Klic(5, A.B, B.B);
                    int od = A.B, @do = B.B;
                    if (@do <= od) @do += k.N;
                    var kp = new Kus(); var ko = new Kus();
                    bool lp = FitPrimka(k, od, @do, kp);
                    bool lo = FitKruh(k, od, @do, ko, MaxSweepSluc);
                    // dlouha primka se nesmi ohnout do oblouku
                    bool obl;
                    if (lp) obl = false;
                    else
                    {
                        if (!lo) continue;
                        bool dlouhaPrimka = (!mo.Obl[pj] && Delka(mo, pj) >= k.PevnaPrimka && RovnyPrvek(k, mo, pj)) ||
                                            (!mo.Obl[j] && Delka(mo, j) >= k.PevnaPrimka && RovnyPrvek(k, mo, j));
                        if (dlouhaPrimka) { zamitnute.Add(klic); continue; }
                        obl = true;
                    }
                    limit--;
                    var z = mo.Kopie();
                    Otoc(z, j - 1);                   // slucuji se prvky 0 a 1, odpada uzel 1
                    z.Obl[0] = obl;
                    z.U.RemoveAt(1);
                    z.Obl.RemoveAt(1);
                    Prolozit(k, z, Okno(z, 0, 2), 2);
                    int nove = 0;
                    double pred, po;
                    Porovnej(k, mo, z, 0, out pred, out po);
                    ObnovVahy(k, z);
                    bool ok = Konecne(z) && Ok(k, z, nove) && Snese(k, pred, po);
                    ObnovVahy(k, mo);
                    if (ok && obl)
                    {
                        // oblouk nejvys 300 st. (na vystupu se oblouk pres 180 st. rozdeli na poloviny)
                        double s = Sweep(z.U[nove], z.U[(nove + 1) % z.M]);
                        if (Math.Abs(s) > MaxSweepSluc) ok = false;
                    }
                    if (ok)
                    {
                        Prevezmi(mo, z); zmena = true; dalsi = true;
                        break;
                    }
                    zamitnute.Add(klic);
                }
            }
            return zmena;
        }

        static bool RovnyPrvek(Kontext k, Model mo, int j)
        {
            int od = mo.U[j].B, @do = mo.U[(j + 1) % mo.M].B;
            if (@do <= od) @do += k.N;
            int okraj = Math.Min((@do - od) / 6, (int)Math.Ceiling(k.ZonaRohu / k.H));   // bez kraju (navaznost na sousedy)
            return JeRovna(k, od + okraj, @do - okraj);
        }

        static double Delka(Model mo, int j)
        {
            var A = mo.U[j]; var B = mo.U[(j + 1) % mo.M];
            return Geo.Vzd(new P(A.X, A.Y), new P(B.X, B.Y));
        }

        // klic zamitnute upravy (druh upravy + vzorky uzlu)
        static long Klic(int op, int a, int b) { return ((long)op << 56) | ((long)a << 28) | (uint)b; }

        // skoro rovny oblouk (mala sipka, nebo body lezi na primce) = primka; navazujici hladka primka se s ni slouci
        static bool ObloukyNaPrimky(Kontext k, Model mo, ref int limit)
        {
            bool zmena = false;
            for (int j = 0; j < mo.M && limit > 0 && mo.M > 2; j++)
            {
                if (!mo.Obl[j]) continue;
                var A = mo.U[j]; var B = mo.U[(j + 1) % mo.M];
                long klic = Klic(6, A.B, B.B);
                if (k.Zamitnute.Contains(klic)) continue;
                double s = Sweep(A, B), c = Delka(mo, j);
                double sipka = c / 2 * Math.Abs(Math.Tan(s / 4));
                // velmi plochy oblouk (pod ~5 st., R > 12x tetiva) je v kresbe rovna hrana: smi se odchylit o 1,5x tolerance
                bool plochy = Math.Abs(s) < 0.08 && c >= k.PevnaPrimka;
                bool kandidat = sipka <= 0.3 * k.TolMax || plochy;
                if (!kandidat)
                {
                    int od = A.B, @do = B.B;
                    if (@do <= od) @do += k.N;
                    int z0 = A.Roh ? (int)Math.Ceiling(k.ZonaRohu / k.H) : 0, z1 = B.Roh ? (int)Math.Ceiling(k.ZonaRohu / k.H) : 0;
                    var kp = new Kus();
                    kandidat = @do - z1 - (od + z0) >= 6 && FitPrimka(k, od + z0, @do - z1, kp) && kp.Mx <= 0.6 * k.TolMax && JeRovna(k, od + z0, @do - z1);
                }
                if (!kandidat) continue;
                limit--;
                var z = mo.Kopie();
                Otoc(z, j - 1);                       // oblouk je prvek 1 (uzly 1 a 2)
                z.Obl[1] = false;
                // hladce navazujici primky musi byt v zakrytu -> jedna primka
                if (z.M > 3 && !z.U[2].Roh && !z.Obl[2]) { z.U.RemoveAt(2); z.Obl.RemoveAt(2); }
                if (z.M > 3 && !z.U[1].Roh && !z.Obl[0]) { z.U.RemoveAt(1); z.Obl.RemoveAt(1); }
                Prolozit(k, z, Okno(z, 1, 2), 2);
                double pred, po;
                Porovnej(k, mo, z, 1, out pred, out po);
                if (Konecne(z) && (Snese(k, pred, po) || (plochy && po <= 1.5 * k.TolMax))) { Prevezmi(mo, z); ObnovVahy(k, mo); zmena = true; }
                else k.Zamitnute.Add(klic);
            }
            return zmena;
        }

        // useky kratsi nez 1 px pryc: jejich dva uzly splynou do jednoho (roh, nebo hladke napojeni)
        static bool OdstranKratke(Kontext k, Model mo, ref int limit)
        {
            bool zmena = false;
            for (int j = 0; j < mo.M && limit > 0 && mo.M > 2; j++)
            {
                if (Delka(mo, j) >= 1.0) continue;
                var A = mo.U[j]; var B = mo.U[(j + 1) % mo.M];
                long klic = Klic(7, A.B, B.B);
                if (k.Zamitnute.Contains(klic)) continue;
                limit--;
                var z = mo.Kopie();
                Otoc(z, j - 1);                       // kratky prvek 1 mezi uzly 1 a 2
                var a = z.U[1]; var b = z.U[2];
                double zlom = Geo.Norm(b.Fo - a.Fi);
                var r = new Uzel { X = (a.X + b.X) / 2, Y = (a.Y + b.Y) / 2, B = PulB(k, a.B, b.B), Fi = a.Fi, Fo = b.Fo };
                r.Roh = a.Roh || b.Roh || Math.Abs(zlom) >= 0.6 * k.UhelRohu || (!z.Obl[0] && !z.Obl[2] && Math.Abs(zlom) > 2 * Math.PI / 180);
                if (!r.Roh) r.Fi = r.Fo = a.Fi + zlom / 2;
                z.U[1] = r;
                z.U.RemoveAt(2);
                z.Obl.RemoveAt(1);
                if (!r.Roh && !z.Obl[0] && !z.Obl[1] && z.M > 3) { z.U.RemoveAt(1); z.Obl.RemoveAt(1); }   // dve primky v zakrytu
                Prolozit(k, z, Okno(z, 1, 2), 2);
                double pred, po;
                Porovnej(k, mo, z, 1, out pred, out po);
                if (Konecne(z) && po <= Math.Max(k.TolMax, pred * 1.1 + 0.05)) { Prevezmi(mo, z); ObnovVahy(k, mo); zmena = true; j = -1; }   // uklid, ne zjednoduseni tvaru
                else k.Zamitnute.Add(klic);
            }
            return zmena;
        }
    }
}
