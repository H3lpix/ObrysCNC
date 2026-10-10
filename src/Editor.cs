using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ObrysCNC
{
    // Editor obrysu na imaginarni desce (souradnice v mm, pocatek 0,0 = levy dolni roh desky).
    // Levé tlačítko: bod = výběr/přesun, čára = celý obrys, prázdno = výběr obdélníkem.
    // Pravé/prostřední tlačítko = posun pohledu, kolečko = zoom, dvojklik na čáru = nový bod.
    public class Editor : Panel
    {
        public List<Obrys> Obrysy = new List<Obrys>();
        public Dictionary<string, int> BarvyVrstev = new Dictionary<string, int>();
        public SizeF Deska = new SizeF(1000, 600);
        public bool YNahore;
        public P SouradniceBodu(P p) { return SouradniceStroje.Bod(p, Deska.Height, YNahore); }
        public bool Upraveno;
        public event EventHandler Zmena;

        bool posunZamcen = true;
        public bool PosunZamcen
        {
            get { return posunZamcen; }
            set
            {
                posunZamcen = value;
                if (value && rezim == Rezim.Tah)
                {
                    rezim = Rezim.Nic;
                    tahZaloha = null;
                }
                Invalidate();
            }
        }

        readonly HashSet<long> vyber = new HashSet<long>();
        readonly List<List<Obrys>> zpet = new List<List<Obrys>>();
        readonly List<List<Obrys>> znovu = new List<List<Obrys>>();
        readonly List<string> zpetPopis = new List<string>(), znovuPopis = new List<string>();

        double meritko = 1, ox, oy;
        enum Rezim { Nic, Pohled, Obdelnik, Tah }
        Rezim rezim;
        Point mysPred, obdA, obdB;
        HashSet<long> vyberPred;
        P tahPosledni;
        List<Obrys> tahZaloha;
        P mysMm;
        bool mysUvnitr;

        static readonly Color Drevo = Color.FromArgb(232, 211, 168);
        static readonly Color DrevoOkraj = Color.FromArgb(160, 120, 70);

        public Editor()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(205, 205, 205);
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        static long Klic(int o, int v) { return ((long)o << 32) | (uint)v; }
        static int KlicO(long k) { return (int)(k >> 32); }
        static int KlicV(long k) { return (int)(k & 0xffffffff); }

        float K { get { using (var g = CreateGraphics()) return g.DpiX / 96f; } }

        // ---------- data ----------
        public static List<Obrys> Kopie(IEnumerable<Obrys> src)
        {
            return src.Select(o => new Obrys { Pts = new List<P>(o.Pts), B = new List<double>(o.B), Zavreny = o.Zavreny, Vrstva = o.Vrstva }).ToList();
        }

        public void NastavObrysy(List<Obrys> obrysy, Dictionary<string, int> barvy)
        {
            Obrysy = Kopie(obrysy);
            BarvyVrstev = barvy != null ? new Dictionary<string, int>(barvy) : new Dictionary<string, int>();
            vyber.Clear(); zpet.Clear(); znovu.Clear(); zpetPopis.Clear(); znovuPopis.Clear();
            Upraveno = false;
            Prizpusob();
            Hlas();
        }

        void UlozZpet(List<Obrys> stav, string popis)
        {
            zpet.Add(stav ?? Kopie(Obrysy));
            zpetPopis.Add(popis ?? "úprava");
            if (zpet.Count > 100) { zpet.RemoveAt(0); zpetPopis.RemoveAt(0); }
            znovu.Clear(); znovuPopis.Clear();
            Upraveno = true;
        }

        public bool LzeZpet { get { return zpet.Count > 0; } }
        public bool LzeZnovu { get { return znovu.Count > 0; } }
        public string PopisZpet { get { return zpetPopis.Count > 0 ? zpetPopis[zpetPopis.Count - 1] : null; } }
        public string PopisZnovu { get { return znovuPopis.Count > 0 ? znovuPopis[znovuPopis.Count - 1] : null; } }

        public void Zpet()
        {
            if (zpet.Count == 0) return;
            znovu.Add(Kopie(Obrysy)); znovuPopis.Add(zpetPopis[zpetPopis.Count - 1]);
            Obrysy = zpet[zpet.Count - 1];
            zpet.RemoveAt(zpet.Count - 1); zpetPopis.RemoveAt(zpetPopis.Count - 1);
            vyber.Clear();
            Upraveno = true;
            Hlas();
        }

        public void Znovu()
        {
            if (znovu.Count == 0) return;
            zpet.Add(Kopie(Obrysy)); zpetPopis.Add(znovuPopis[znovuPopis.Count - 1]);
            Obrysy = znovu[znovu.Count - 1];
            znovu.RemoveAt(znovu.Count - 1); znovuPopis.RemoveAt(znovuPopis.Count - 1);
            vyber.Clear();
            Upraveno = true;
            Hlas();
        }

        void Hlas()
        {
            Invalidate();
            if (Zmena != null) Zmena(this, EventArgs.Empty);
        }

        public int PocetVybranych { get { return vyber.Count; } }
        public int PocetBodu { get { return Obrysy.Sum(o => o.Pts.Count); } }

        public bool Rozsah(out double minx, out double miny, out double maxx, out double maxy)
        {
            Geo.Rozsah(Obrysy, out minx, out miny, out maxx, out maxy);
            return Obrysy.Count > 0 && minx <= maxx;
        }

        public bool Presahuje
        {
            get
            {
                double a, b, c, d;
                if (!Rozsah(out a, out b, out c, out d)) return false;
                return a < -0.001 || b < -0.001 || c > Deska.Width + 0.001 || d > Deska.Height + 0.001;
            }
        }

        // ---------- operace ----------
        public void VyberVse()
        {
            vyber.Clear();
            for (int o = 0; o < Obrysy.Count; o++) for (int v = 0; v < Obrysy[o].Pts.Count; v++) vyber.Add(Klic(o, v));
            Hlas();
        }

        public void ZrusVyber() { vyber.Clear(); Hlas(); }

        public void SmazVybrane()
        {
            if (vyber.Count == 0) return;
            UlozZpet(null, "Smazat body");
            var nove = new List<Obrys>();
            for (int oi = 0; oi < Obrysy.Count; oi++)
            {
                var o = Obrysy[oi];
                int n = o.Pts.Count;
                var zbyva = Enumerable.Range(0, n).Where(v => !vyber.Contains(Klic(oi, v))).ToList();
                if (zbyva.Count == n) { nove.Add(o); continue; }
                if (zbyva.Count < (o.Zavreny ? 3 : 2)) continue;          // z obrysu by nic nezbylo
                var r = new Obrys { Zavreny = o.Zavreny, Vrstva = o.Vrstva };
                for (int i = 0; i < zbyva.Count; i++)
                {
                    int a = zbyva[i];
                    r.Pts.Add(o.Pts[a]);
                    bool posledni = i == zbyva.Count - 1;
                    if (posledni && !o.Zavreny) break;
                    int b = posledni ? zbyva[0] : zbyva[i + 1];
                    // oblouk zustane jen mezi puvodne sousednimi body, jinak usecka
                    r.B.Add(b == (a + 1) % n ? o.B[a] : 0);
                }
                nove.Add(r);
            }
            Obrysy = nove;
            vyber.Clear();
            Hlas();
        }

        public void PosunVybrane(double dx, double dy, bool zaznamenat)
        {
            if (vyber.Count == 0) return;
            if (zaznamenat) UlozZpet(null, "Posun bodů");
            foreach (var k in vyber)
            {
                var o = Obrysy[KlicO(k)];
                int v = KlicV(k);
                o.Pts[v] = new P(o.Pts[v].X + dx, o.Pts[v].Y + dy);
            }
            Upraveno = true;
            Hlas();
        }

        public void PosunVse(double dx, double dy)
        {
            if (Obrysy.Count == 0) return;
            UlozZpet(null, "Posun výkresu");
            Obrysy = Obrysy.Select(o => Geo.Posun(o, dx, dy)).ToList();
            Hlas();
        }

        public void Vystredit()
        {
            double a, b, c, d;
            if (!Rozsah(out a, out b, out c, out d)) return;
            PosunVse((Deska.Width - (c - a)) / 2 - a, (Deska.Height - (d - b)) / 2 - b);
        }

        public void DoRohu(double okraj)
        {
            double a, b, c, d;
            if (!Rozsah(out a, out b, out c, out d)) return;
            PosunVse(okraj - a, YNahore ? Deska.Height - okraj - d : okraj - b);
        }

        IEnumerable<int> VybraneObrysy() { return vyber.Select(KlicO).Distinct().OrderBy(x => x); }

        // obrysy, na ktere se uprava pouzije: s vybranym bodem, jinak vsechny
        List<int> Cilove()
        {
            var ids = VybraneObrysy().ToList();
            return ids.Count > 0 ? ids : Enumerable.Range(0, Obrysy.Count).ToList();
        }

        public bool NecoVybrano { get { return vyber.Count > 0; } }

        void Transformuj(Func<P, P> f, bool zrcadlo, string popis)
        {
            var ids = Cilove();
            if (ids.Count == 0) return;
            UlozZpet(null, popis);
            foreach (int i in ids)
            {
                var o = Obrysy[i];
                o.Pts = o.Pts.Select(f).ToList();
                if (zrcadlo) o.B = o.B.Select(b => -b).ToList();
            }
            if (zrcadlo)
            {
                var vse = Obrysy;
                foreach (int i in ids) Obrysy[i] = Geo.UpravSmerAStart(Obrysy[i], vse, 25);
            }
            Hlas();
        }

        P StredCilu()
        {
            double a, b, c, d;
            Geo.Rozsah(Cilove().Select(i => Obrysy[i]), out a, out b, out c, out d);
            return new P((a + c) / 2, (b + d) / 2);
        }

        public void Otoc(double stupne)
        {
            if (Obrysy.Count == 0) return;
            var s = StredCilu();
            double t = stupne * Math.PI / 180, cs = Math.Cos(t), sn = Math.Sin(t);
            Transformuj(p => new P(s.X + (p.X - s.X) * cs - (p.Y - s.Y) * sn, s.Y + (p.X - s.X) * sn + (p.Y - s.Y) * cs), false, "Otočit");
        }

        public void Zrcadli(bool vodorovne)
        {
            if (Obrysy.Count == 0) return;
            var s = StredCilu();
            Transformuj(p => vodorovne ? new P(2 * s.X - p.X, p.Y) : new P(p.X, 2 * s.Y - p.Y), true, "Zrcadlit");
        }

        public void Meritko(double procent)
        {
            if (Obrysy.Count == 0 || procent <= 0) return;
            var s = StredCilu();
            double k = procent / 100;
            Transformuj(p => new P(s.X + (p.X - s.X) * k, s.Y + (p.Y - s.Y) * k), false, "Změnit měřítko");
        }

        // odsazeni (+ ven, - dovnitr), slouceni (odsazeni 0) a zaobleni rohu pres vzdalenostni mapu
        public string UpravTvar(double odsazeniMm, double zaobleniMm, bool zachovat)
        {
            var ids = Cilove().Where(i => Obrysy[i].Zavreny).ToList();
            if (ids.Count == 0) return "Žádný uzavřený obrys k úpravě.";
            var nove = Rastr.Uprav(ids.Select(i => Obrysy[i]).ToList(), odsazeniMm, zaobleniMm, 1.0);
            if (nove.Count == 0) return "Po úpravě nezbyl žádný tvar (odsazení dovnitř je větší než tvar?).";
            string popis = odsazeniMm > 0 ? "Odsadit ven" : odsazeniMm < 0 ? "Odsadit dovnitř" : zaobleniMm > 0 ? "Zaoblit rohy" : "Sloučit překryvy";
            UlozZpet(null, popis);
            var zbyle = zachovat ? Obrysy : Obrysy.Where((o, i) => !ids.Contains(i)).ToList();
            Obrysy = zbyle.Concat(nove).ToList();
            vyber.Clear();
            int od = Obrysy.Count - nove.Count;
            for (int oi = od; oi < Obrysy.Count; oi++) for (int v = 0; v < Obrysy[oi].Pts.Count; v++) vyber.Add(Klic(oi, v));
            Hlas();
            return null;
        }

        // Roh z vybranych bodu: kazdy souvisly usek vybranych bodu (zakulaceni, zkoseni, "rozsypany" roh)
        // se nahradi ostrym rohem v pruseciku navazujicich hran (volitelne presne 90°) a ten se zaobli
        // obloukem polomeru r tecne navazujicim na obe hrany (r = 0 -> ostry roh).
        public string RohSRadiusem(double r, bool pravyUhel)
        {
            if (vyber.Count == 0) return "Vyberte body v rohu (tažením obdélníku přes roh).";
            var nove = Kopie(Obrysy);
            int hotovo = 0;
            string chyba = null;
            foreach (int oi in VybraneObrysy())
            {
                var o = nove[oi];
                int n = o.Pts.Count;
                var vyb = new bool[n];
                for (int v = 0; v < n; v++) vyb[v] = vyber.Contains(Klic(oi, v));
                if (vyb.All(x => x)) { chyba = "Vybraný je celý obrys – vyberte jen body v rohu."; continue; }
                // u uzavreneho obrysu zacit nevybranym bodem, aby zadny usek neprechazel pres zacatek
                if (o.Zavreny)
                {
                    int s0 = Array.IndexOf(vyb, false);
                    o.Pts = o.Pts.Skip(s0).Concat(o.Pts.Take(s0)).ToList();
                    o.B = o.B.Skip(s0).Concat(o.B.Take(s0)).ToList();
                    vyb = vyb.Skip(s0).Concat(vyb.Take(s0)).ToArray();
                }
                // souvisle useky [a..b], od nejvyssiho indexu, aby nahrazeni nerozhodilo ostatni
                var useky = new List<int[]>();
                for (int i = 0; i < n; )
                {
                    if (!vyb[i]) { i++; continue; }
                    int a = i;
                    while (i < n && vyb[i]) i++;
                    useky.Add(new[] { a, i - 1 });
                }
                foreach (var u in useky.OrderByDescending(x => x[0]))
                {
                    string ch = RohVUseku(o, u[0], u[1], r, pravyUhel);
                    if (ch == null) hotovo++; else chyba = ch;
                }
            }
            if (hotovo == 0) return chyba ?? "Roh se nepodařilo vytvořit.";
            UlozZpet(null, "Vytvořit roh");
            Obrysy = nove;
            vyber.Clear();
            Hlas();
            return chyba;   // null = vse v poradku, jinak upozorneni k casti vyberu
        }

        static string RohVUseku(Obrys o, int a, int b, double r, bool pravyUhel)
        {
            int n = o.Pts.Count;
            int pocet = b - a + 1;
            if (!o.Zavreny && (a == 0 || b >= n - 1)) return "U otevřeného obrysu nelze dělat roh v krajním bodě.";
            if (n - pocet < 2) return "Z obrysu by nic nezbylo.";
            int ip = (a - 1 + n) % n, iz = (b + 1) % n, ia = a % n, ib = b % n;
            P pp = o.Pts[ip], pz = o.Pts[iz];
            // smer hrany vstupujici do useku a vystupujici z nej (u oblouku tecna)
            double t1 = Geo.SmerNaZacatku(pp, o.Pts[ia], o.B[ip]);
            double t2 = Geo.SmerNaKonci(o.Pts[ib], pz, o.B[ib]);
            P u1 = new P(Math.Cos(t1), Math.Sin(t1)), u2 = new P(Math.Cos(t2), Math.Sin(t2));
            double otoceni = Geo.Norm(t2 - t1);                         // + = doleva (proti smeru hodin)
            if (Math.Abs(otoceni) < 2 * Math.PI / 180) return "Hrany kolem výběru jsou skoro rovnoběžné – roh tu není.";
            if (pravyUhel && Math.Abs(Math.Abs(otoceni) - Math.PI / 2) < 25 * Math.PI / 180)
            {
                // presne 90°: delsi hrana zustane, kratsi se natoci kolmo
                double s = Math.Sign(otoceni);
                if (Geo.Vzd(pp, o.Pts[ia]) >= Geo.Vzd(o.Pts[ib], pz)) u2 = new P(-s * u1.Y, s * u1.X);
                else u1 = new P(s * u2.Y, -s * u2.X);
                otoceni = s * Math.PI / 2;
            }
            // prusecik primek pp + t*u1 a pz + s*u2
            double det = u1.X * u2.Y - u1.Y * u2.X;
            if (Math.Abs(det) < 1e-9) return "Hrany jsou rovnoběžné.";
            double tt = ((pz.X - pp.X) * u2.Y - (pz.Y - pp.Y) * u2.X) / det;
            if (tt <= 0) return "Roh by vyšel za začátek hrany – vyberte jen body v rohu.";
            var X = new P(pp.X + tt * u1.X, pp.Y + tt * u1.Y);
            if ((pz.X - X.X) * u2.X + (pz.Y - X.Y) * u2.Y <= 0) return "Roh by vyšel za konec hrany – vyberte jen body v rohu.";
            var vloz = new List<P>();
            var vlozB = new List<double>();
            if (r <= 1e-9)
            {
                vloz.Add(X); vlozB.Add(0);
            }
            else
            {
                double d = r * Math.Tan(Math.Abs(otoceni) / 2);          // vzdalenost tecnych bodu od rohu
                if (d >= Geo.Vzd(pp, X) - 1e-6 || d >= Geo.Vzd(X, pz) - 1e-6)
                    return string.Format("Rádius {0:0.##} mm je na tak krátké hrany moc velký (max. ≈ {1:0.##} mm).", r,
                        Math.Min(Geo.Vzd(pp, X), Geo.Vzd(X, pz)) / Math.Tan(Math.Abs(otoceni) / 2));
                var T1 = new P(X.X - d * u1.X, X.Y - d * u1.Y);
                var T2 = new P(X.X + d * u2.X, X.Y + d * u2.Y);
                vloz.Add(T1); vlozB.Add(Math.Tan(otoceni / 4));            // oblouk tecny k obema hranam
                vloz.Add(T2); vlozB.Add(0);
            }
            // nove body: ... pp (usecka k rohu), [roh | T1 oblouk T2] (usecka dal), pz ...
            var P2 = new List<P>();
            var B2 = new List<double>();
            for (int k = 0; k < n; k++)
            {
                int rel = ((k - a) % n + n) % n;                            // poloha vuci zacatku useku
                if (rel < pocet) continue;                                  // body useku pryc
                P2.Add(o.Pts[k]);
                B2.Add(k == ip ? 0 : (k < o.B.Count ? o.B[k] : 0));
                if (k == ip) { P2.AddRange(vloz); B2.AddRange(vlozB); }
            }
            if (!o.Zavreny) B2.RemoveAt(B2.Count - 1);
            o.Pts = P2; o.B = B2;
            return null;
        }

        // srovnani osy: vybrane body dostanou X (nebo Y) bodu, na ktery se pak klikne
        int srovnani;   // 0 = ne, 1 = X, 2 = Y
        public string ZacniSrovnani(bool osaX)
        {
            if (vyber.Count == 0) return "Nejdřív vyberte body, které se mají srovnat.";
            srovnani = osaX ? 1 : 2;
            Cursor = Cursors.Cross;
            Focus();
            Invalidate();
            return null;
        }

        void DokonciSrovnani(P vzor)
        {
            UlozZpet(null, srovnani == 1 ? "Srovnat X" : "Srovnat Y");
            foreach (var k in vyber)
            {
                var o = Obrysy[KlicO(k)];
                int v = KlicV(k);
                o.Pts[v] = srovnani == 1 ? new P(vzor.X, o.Pts[v].Y) : new P(o.Pts[v].X, vzor.Y);
            }
            srovnani = 0;
            Cursor = Cursors.Default;
            Hlas();
        }

        // souradnice jedineho vybraneho bodu (pro informace)
        public bool JedenBod(out P p)
        {
            p = new P();
            if (vyber.Count != 1) return false;
            long k = vyber.First();
            p = Obrysy[KlicO(k)].Pts[KlicV(k)];
            return true;
        }

        // spoji otevrene obrysy, jejichz konce jsou bliz nez tol; uzavrene zustanou
        public string SpojKonce(double tol)
        {
            var ids = Cilove().Where(i => !Obrysy[i].Zavreny).ToList();
            if (ids.Count == 0) return "Žádné otevřené obrysy.";
            var spojene = Dxf.Spoj(ids.Select(i => Obrysy[i]).ToList(), tol);
            UlozZpet(null, "Spojit konce");
            Obrysy = Obrysy.Where((o, i) => !ids.Contains(i)).Concat(spojene).ToList();
            Obrysy = Obrysy.Select(o => o.Zavreny ? Geo.UpravSmerAStart(o, Obrysy, 25) : o).ToList();
            vyber.Clear();
            Hlas();
            return string.Format("Otevřených obrysů: před {0}, po {1}.", ids.Count, Obrysy.Count(o => !o.Zavreny));
        }

        public string UzavriOtevrene()
        {
            var ids = Cilove().Where(i => !Obrysy[i].Zavreny && Obrysy[i].Pts.Count >= 3).ToList();
            if (ids.Count == 0) return "Žádné otevřené obrysy (se 3 a více body).";
            UlozZpet(null, "Uzavřít úsečkou");
            foreach (int i in ids)
            {
                var o = Obrysy[i];
                o.Zavreny = true;
                o.B.Add(0);                                   // uzavira se useckou
                Obrysy[i] = Geo.UpravSmerAStart(o, Obrysy, 25);
            }
            vyber.Clear();
            Hlas();
            return null;
        }

        public void ObratVybrane()
        {
            var ids = VybraneObrysy().ToList();
            if (ids.Count == 0) return;
            UlozZpet(null, "Obrátit směr");
            foreach (int i in ids) Obrysy[i] = Geo.Obrat(Obrysy[i]);
            vyber.Clear();
            Hlas();
        }

        public void SmazObrysy()
        {
            var ids = new HashSet<int>(VybraneObrysy());
            if (ids.Count == 0) return;
            UlozZpet(null, "Smazat obrysy");
            Obrysy = Obrysy.Where((o, i) => !ids.Contains(i)).ToList();
            vyber.Clear();
            Hlas();
        }

        // vybrany bod (prave jeden, v uzavrenem obrysu) bude zacatek frezovani
        public string StartDoBodu()
        {
            if (vyber.Count != 1) return "Vyberte právě jeden bod.";
            long k = vyber.First();
            var o = Obrysy[KlicO(k)];
            if (!o.Zavreny) return "Start lze nastavit jen u uzavřeného obrysu.";
            int s = KlicV(k), n = o.Pts.Count;
            UlozZpet(null, "Start do bodu");
            var r = new Obrys { Zavreny = true, Vrstva = o.Vrstva };
            for (int i = 0; i < n; i++) { r.Pts.Add(o.Pts[(s + i) % n]); r.B.Add(o.B[(s + i) % n]); }
            Obrysy[KlicO(k)] = r;
            vyber.Clear();
            vyber.Add(Klic(KlicO(k), 0));
            Hlas();
            return null;
        }

        void VlozBod(int oi, int seg, P p)
        {
            var o = Obrysy[oi];
            P a = o.Pts[seg], b = o.KonecSegmentu(seg);
            double bulge = o.B[seg], b1 = 0, b2 = 0;
            P q = p;
            if (Math.Abs(bulge) > 1e-12)
            {
                var ob = Geo.Oblouk(a, b, bulge);
                double ap = Math.Atan2(p.Y - ob.Cy, p.X - ob.Cx);
                double rel = ob.Sw > 0 ? Geo.Mod2Pi(ap - ob.A0) : Geo.Mod2Pi(ob.A0 - ap);
                rel = Math.Max(0.01 * Math.Abs(ob.Sw), Math.Min(0.99 * Math.Abs(ob.Sw), rel));
                double sw1 = Math.Sign(ob.Sw) * rel, sw2 = ob.Sw - sw1;
                double t = ob.A0 + sw1;
                q = new P(ob.Cx + ob.R * Math.Cos(t), ob.Cy + ob.R * Math.Sin(t));
                b1 = Math.Tan(sw1 / 4); b2 = Math.Tan(sw2 / 4);
            }
            UlozZpet(null, "Vložit bod");
            o.Pts.Insert(seg + 1, q);
            o.B[seg] = b1;
            o.B.Insert(seg + 1, b2);
            vyber.Clear();
            vyber.Add(Klic(oi, seg + 1));
            Hlas();
        }

        // ---------- zobrazeni ----------
        bool prizpusobeno;

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!prizpusobeno || Obrysy.Count == 0) Prizpusob();
        }

        public void Prizpusob()
        {
            double minx = 0, miny = 0, maxx = Deska.Width, maxy = Deska.Height;
            double a, b, c, d;
            if (Rozsah(out a, out b, out c, out d)) { minx = Math.Min(minx, a); miny = Math.Min(miny, b); maxx = Math.Max(maxx, c); maxy = Math.Max(maxy, d); }
            if (Width < 20 || Height < 20) return;
            double w = Math.Max(maxx - minx, 1), h = Math.Max(maxy - miny, 1), okraj = 50 * K;
            double vpravo = 90 * K, dole = 50 * K;        // misto pro koty a stavovy radek
            meritko = Math.Min((Width - 2 * okraj - vpravo) / w, (Height - 2 * okraj - dole) / h);
            ox = (Width - vpravo - w * meritko) / 2 - minx * meritko;
            oy = (Height - dole + h * meritko) / 2 + miny * meritko;
            prizpusobeno = true;
            Invalidate();
        }

        PointF S(P p) { return new PointF((float)(ox + p.X * meritko), (float)(oy - p.Y * meritko)); }
        P Mm(Point p) { return new P((p.X - ox) / meritko, (oy - p.Y) / meritko); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float k = g.DpiX / 96f;
            // deska
            var d0 = S(new P(0, Deska.Height));
            var deska = new RectangleF(d0.X, d0.Y, (float)(Deska.Width * meritko), (float)(Deska.Height * meritko));
            using (var br = new SolidBrush(Drevo)) g.FillRectangle(br, deska);
            double krok = 100;
            while (krok * meritko < 25 * k) krok *= 2;
            while (krok * meritko > 200 * k && krok > 10) krok /= 2;
            using (var pen = new Pen(Color.FromArgb(70, DrevoOkraj), 1))
            {
                for (double x = krok; x < Deska.Width; x += krok) { var p1 = S(new P(x, 0)); g.DrawLine(pen, p1.X, deska.Top, p1.X, deska.Bottom); }
                for (double y = krok; y < Deska.Height; y += krok) { var p1 = S(new P(0, y)); g.DrawLine(pen, deska.Left, p1.Y, deska.Right, p1.Y); }
            }
            using (var pen = new Pen(DrevoOkraj, 2 * k)) g.DrawRectangle(pen, deska.X, deska.Y, deska.Width, deska.Height);
            TextRenderer.DrawText(g, string.Format("Deska X {0:0.#} × Y {1:0.#} mm  (mřížka {2:0.#} mm, 0,0 = {3})", Deska.Width, Deska.Height, krok, YNahore ? "levý horní roh · Y dolů" : "levý dolní roh · Y nahoru"),
                Font, new Point((int)deska.Left, (int)(deska.Top - 22 * k)), Color.FromArgb(90, 60, 30));
            var o0 = S(new P(0, YNahore ? Deska.Height : 0));
            using (var pen = new Pen(Color.FromArgb(0, 140, 60), 2 * k))
            {
                pen.CustomEndCap = new AdjustableArrowCap(3, 3);
                g.DrawLine(pen, o0.X, o0.Y, o0.X + 40 * k, o0.Y);
                g.DrawLine(pen, o0.X, o0.Y, o0.X, o0.Y + (YNahore ? 40 : -40) * k);
            }

            // obrysy
            double tetiva = Math.Max(0.005, 0.4 / meritko);
            using (var vypln = new GraphicsPath(FillMode.Alternate))
            using (var cary = new GraphicsPath())
            {
                foreach (var o in Obrysy)
                {
                    var body = Geo.RozlozObrys(o, tetiva).Select(p => S(p)).ToArray();
                    if (body.Length < 2) continue;
                    cary.StartFigure();
                    cary.AddLines(body);
                    if (!o.Zavreny) continue;                          // otevrene drahy se nevyplnuji
                    cary.CloseFigure();
                    vypln.StartFigure();
                    vypln.AddLines(body);
                    vypln.CloseFigure();
                }
                using (var br = new SolidBrush(Color.FromArgb(90, 110, 70, 30))) g.FillPath(br, vypln);
                using (var pen = new Pen(Color.FromArgb(0, 87, 200), 1.6f * k)) g.DrawPath(pen, cary);
            }
            float r = 3f * k, rv = 4.5f * k;
            using (var brBod = new SolidBrush(Color.FromArgb(0, 60, 150)))
            using (var brObl = new SolidBrush(Color.FromArgb(0, 160, 220)))
            using (var brVyb = new SolidBrush(Color.FromArgb(230, 20, 20)))
            using (var penVyb = new Pen(Color.White, 1 * k))
            {
                for (int oi = 0; oi < Obrysy.Count; oi++)
                {
                    var o = Obrysy[oi];
                    for (int v = 0; v < o.Pts.Count; v++)
                    {
                        var q = S(o.Pts[v]);
                        if (!ClientRectangle.Contains(Point.Round(q))) continue;
                        if (vyber.Contains(Klic(oi, v)))
                        {
                            g.FillRectangle(brVyb, q.X - rv, q.Y - rv, 2 * rv, 2 * rv);
                            g.DrawRectangle(penVyb, q.X - rv, q.Y - rv, 2 * rv, 2 * rv);
                        }
                        else
                        {
                            bool obl = v < o.B.Count && Math.Abs(o.B[v]) > 1e-12;
                            g.FillRectangle(obl ? brObl : brBod, q.X - r, q.Y - r, 2 * r, 2 * r);
                        }
                    }
                    if (o.Pts.Count >= 2)
                    {
                        var s = S(o.Pts[0]);
                        double t = Geo.SmerNaZacatku(o.Pts[0], o.Pts[1], o.B[0]);
                        using (var pen = new Pen(Color.FromArgb(10, 138, 58), 2.2f * k))
                        {
                            pen.CustomEndCap = new AdjustableArrowCap(4, 4);
                            g.DrawLine(pen, s.X, s.Y, s.X + (float)Math.Cos(t) * 22 * k, s.Y - (float)Math.Sin(t) * 22 * k);
                        }
                    }
                }
            }
            Koty.Kresli(g, Font, k, p => S(p), Obrysy, true, YNahore, Deska.Height);
            if (rezim == Rezim.Obdelnik)
            {
                var rr = Obdelnik(obdA, obdB);
                using (var br = new SolidBrush(Color.FromArgb(40, 0, 120, 255))) g.FillRectangle(br, rr);
                using (var pen = new Pen(Color.FromArgb(0, 90, 220), 1) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, rr);
            }
            KresliSouradniceVyberu(g, k);
            // stavovy radek
            var mysStroje = SouradniceBodu(mysMm);
            string stav = mysUvnitr ? string.Format("X {0:0.0}  Y {1:0.0} mm", mysStroje.X, mysStroje.Y) : "";
            if (vyber.Count > 0) stav += (stav.Length > 0 ? "   ·   " : "") + "vybráno bodů: " + vyber.Count;
            stav += (stav.Length > 0 ? "   ·   " : "") + (PosunZamcen ? "Posun bodů zamčen" : "Posun bodů povolen");
            if (srovnani != 0)
                using (var tucne = new Font(Font, FontStyle.Bold))
                    TextRenderer.DrawText(g, string.Format("Klikněte na bod, podle kterého se srovná {0} vybraných bodů (Esc = zrušit)", srovnani == 1 ? "X" : "Y"),
                        tucne, new Rectangle(0, (int)(8 * k), Width, (int)(24 * k)), Color.FromArgb(0, 90, 200), TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, stav, Font, new Rectangle((int)(8 * k), Height - (int)(26 * k), Width, (int)(22 * k)), Color.FromArgb(40, 40, 40), TextFormatFlags.Left);
            if (Presahuje)
                using (var tucne = new Font(Font, FontStyle.Bold))
                    TextRenderer.DrawText(g, "⚠ Obrys přesahuje desku", tucne, new Rectangle(0, Height - (int)(26 * k), Width - (int)(8 * k), (int)(22 * k)), Color.Firebrick, TextFormatFlags.Right);
            if (Obrysy.Count == 0)
                TextRenderer.DrawText(g, "Otevřete DXF, nebo pošlete obrys z karty Obrázek → DXF / Oprava DXF.", Font, ClientRectangle, Color.FromArgb(90, 60, 30), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void KresliSouradniceVyberu(Graphics g, float k)
        {
            var body = new List<P>();
            foreach (var id in vyber)
            {
                int oi = KlicO(id), vi = KlicV(id);
                if (oi >= 0 && oi < Obrysy.Count && vi >= 0 && vi < Obrysy[oi].Pts.Count)
                    body.Add(SouradniceBodu(Obrysy[oi].Pts[vi]));
            }
            if (body.Count == 0) return;
            string text;
            if (body.Count == 1)
                text = string.Format("Vybraný bod · souřadnice na desce [mm]\nX = {0:0.0000}    Y = {1:0.0000}", body[0].X, body[0].Y);
            else
            {
                double x0 = body.Min(p => p.X), x1 = body.Max(p => p.X);
                double y0 = body.Min(p => p.Y), y1 = body.Max(p => p.Y);
                text = string.Format("Vybráno {0} bodů · souřadnice na desce [mm]\nX = {1:0.0000} až {2:0.0000}    ΔX = {3:G6}{4}\nY = {5:0.0000} až {6:0.0000}    ΔY = {7:G6}{8}",
                    body.Count, x0, x1, x1 - x0, x1 == x0 ? " · stejné X" : "",
                    y0, y1, y1 - y0, y1 == y0 ? " · stejné Y" : "");
            }
            string spoj = InformaceSpojeVyberu();
            if (spoj != null) text += "\n" + spoj;
            int pad = Math.Max(4, (int)(8 * k));
            int width = Math.Max(1, ClientSize.Width - 4 * pad);
            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            Size size = TextRenderer.MeasureText(g, text, Font, new Size(width, int.MaxValue), flags);
            var box = new Rectangle(pad, Math.Max(pad, Height - (int)(34 * k) - size.Height - 2 * pad),
                Math.Min(width + 2 * pad, size.Width + 2 * pad), size.Height + 2 * pad);
            using (var br = new SolidBrush(Color.FromArgb(245, 250, 255))) g.FillRectangle(br, box);
            using (var pen = new Pen(Color.FromArgb(90, 130, 170))) g.DrawRectangle(pen, box);
            var area = new Rectangle(box.X + pad, box.Y + pad, box.Width - 2 * pad, box.Height - 2 * pad);
            TextRenderer.DrawText(g, text, Font, area, Color.FromArgb(25, 55, 85), flags);
        }

        string InformaceSpojeVyberu()
        {
            if (vyber.Count != 2) return null;
            var ids = vyber.OrderBy(id => id).ToArray();
            int oi = KlicO(ids[0]), oj = KlicO(ids[1]);
            if (oi != oj) return "Rádius: body leží na různých obrysech.";
            if (oi < 0 || oi >= Obrysy.Count) return null;
            var o = Obrysy[oi];
            int a = KlicV(ids[0]), b = KlicV(ids[1]);
            if (a < 0 || b < 0 || a >= o.Pts.Count || b >= o.Pts.Count) return null;
            var spoje = new List<string>();
            for (int s = 0; s < o.PocetSegmentu; s++)
            {
                int konec = (s + 1) % o.Pts.Count;
                if (!((s == a && konec == b) || (s == b && konec == a))) continue;
                if (s >= o.B.Count || Geo.Vzd(o.Pts[s], o.KonecSegmentu(s)) <= 1e-9)
                    spoje.Add("rádius nelze určit (neplatný segment)");
                else if (Math.Abs(o.B[s]) <= 1e-12) spoje.Add("přímka · bez rádiusu");
                else
                {
                    double r = Geo.Oblouk(o.Pts[s], o.KonecSegmentu(s), o.B[s]).R;
                    spoje.Add(double.IsNaN(r) || double.IsInfinity(r) || r <= 0
                        ? "rádius nelze určit (neplatný oblouk)"
                        : string.Format("R = {0:0.0000} mm", r));
                }
            }
            if (spoje.Count == 0) return "Rádius: body nesousedí · mezi nimi není jeden segment.";
            if (spoje.Count == 1) return "Spoj mezi body: " + spoje[0];
            // Uzavrena kontura se dvema body ma dva ruzne spoje (napr. dva pulkruhy).
            return string.Join("\n", spoje.Select((text, i) => "Spoj " + (i + 1) + ": " + text));
        }

        static Rectangle Obdelnik(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        // ---------- hledani pod mysi ----------
        bool NajdiBod(Point p, out int oi, out int vi)
        {
            oi = vi = -1;
            double best = 7 * K;
            for (int i = Obrysy.Count - 1; i >= 0; i--)
                for (int v = 0; v < Obrysy[i].Pts.Count; v++)
                {
                    var q = S(Obrysy[i].Pts[v]);
                    double d = Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                    if (d < best) { best = d; oi = i; vi = v; }
                }
            return oi >= 0;
        }

        bool NajdiSegment(Point p, out int oi, out int seg)
        {
            oi = seg = -1;
            var m = Mm(p);
            double best = 6 * K / meritko;
            for (int i = Obrysy.Count - 1; i >= 0; i--)
            {
                var o = Obrysy[i];
                for (int s = 0; s < o.PocetSegmentu; s++)
                {
                    double d = Geo.VzdalenostKSegmentu(m, o.Pts[s], o.KonecSegmentu(s), o.B[s]);
                    if (d < best) { best = d; oi = i; seg = s; }
                }
            }
            return oi >= 0;
        }

        // ---------- mys a klavesnice ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            mysPred = e.Location;
            int oi, vi, seg;
            if (srovnani != 0)
            {
                // ceka se na klik na vzorovy bod; jinde (nebo pravym tlacitkem) se srovnani zrusi
                if (e.Button == MouseButtons.Left && NajdiBod(e.Location, out oi, out vi)) DokonciSrovnani(Obrysy[oi].Pts[vi]);
                else { srovnani = 0; Cursor = Cursors.Default; Invalidate(); }
                return;
            }
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) { rezim = Rezim.Pohled; Cursor = Cursors.SizeAll; return; }
            if (e.Button != MouseButtons.Left) return;
            bool pridat = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
            if (NajdiBod(e.Location, out oi, out vi))
            {
                long k = Klic(oi, vi);
                if (pridat) { if (!vyber.Remove(k)) vyber.Add(k); }
                else if (!vyber.Contains(k)) { vyber.Clear(); vyber.Add(k); }
                if (vyber.Contains(k)) ZacniTah(e.Location);
            }
            else if (NajdiSegment(e.Location, out oi, out seg))
            {
                if (!pridat) vyber.Clear();
                for (int v = 0; v < Obrysy[oi].Pts.Count; v++) vyber.Add(Klic(oi, v));
                ZacniTah(e.Location);
            }
            else
            {
                if (!pridat) vyber.Clear();
                vyberPred = new HashSet<long>(vyber);
                rezim = Rezim.Obdelnik;
                obdA = obdB = e.Location;
            }
            Hlas();
        }

        void ZacniTah(Point p)
        {
            if (PosunZamcen) return;
            rezim = Rezim.Tah;
            tahPosledni = Mm(p);
            tahZaloha = Kopie(Obrysy);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mysMm = Mm(e.Location);
            mysUvnitr = true;
            switch (rezim)
            {
                case Rezim.Pohled:
                    ox += e.X - mysPred.X; oy += e.Y - mysPred.Y;
                    break;
                case Rezim.Tah:
                    var m = Mm(e.Location);
                    double dx = m.X - tahPosledni.X, dy = m.Y - tahPosledni.Y;
                    if (Math.Abs(e.X - mysPred.X) + Math.Abs(e.Y - mysPred.Y) == 0) break;
                    if (tahZaloha != null) { UlozZpet(tahZaloha, "Přesun tažením"); tahZaloha = null; }
                    PosunVybrane(dx, dy, false);
                    tahPosledni = m;
                    break;
                case Rezim.Obdelnik:
                    obdB = e.Location;
                    var rr = Obdelnik(obdA, obdB);
                    vyber.Clear();
                    vyber.UnionWith(vyberPred);
                    for (int oi = 0; oi < Obrysy.Count; oi++)
                        for (int v = 0; v < Obrysy[oi].Pts.Count; v++)
                            if (rr.Contains(Point.Round(S(Obrysy[oi].Pts[v])))) vyber.Add(Klic(oi, v));
                    if (Zmena != null) Zmena(this, EventArgs.Empty);
                    break;
                default:
                    int a, b;
                    Cursor = NajdiBod(e.Location, out a, out b) ? Cursors.Hand : NajdiSegment(e.Location, out a, out b) ? Cursors.Cross : Cursors.Default;
                    break;
            }
            mysPred = e.Location;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            rezim = Rezim.Nic;
            tahZaloha = null;
            Cursor = Cursors.Default;
            Hlas();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            mysUvnitr = false;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            int oi, vi, seg;
            if (NajdiBod(e.Location, out oi, out vi)) return;
            if (NajdiSegment(e.Location, out oi, out seg)) VlozBod(oi, seg, Mm(e.Location));
            else Prizpusob();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            double f = e.Delta > 0 ? 1.25 : 0.8;
            ox = e.X - (e.X - ox) * f;
            oy = e.Y - (e.Y - oy) * f;
            meritko *= f;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            if (k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (PosunZamcen && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            double krok = e.Shift ? 10 : e.Control ? 0.1 : 1;
            switch (e.KeyCode)
            {
                case Keys.Delete: case Keys.Back: SmazVybrane(); break;
                case Keys.Escape:
                    if (srovnani != 0) { srovnani = 0; Cursor = Cursors.Default; Invalidate(); }
                    else ZrusVyber();
                    break;
                case Keys.Left: PosunVybrane(-krok, 0, true); break;
                case Keys.Right: PosunVybrane(krok, 0, true); break;
                case Keys.Up: PosunVybrane(0, krok, true); break;
                case Keys.Down: PosunVybrane(0, -krok, true); break;
                case Keys.A: if (e.Control) VyberVse(); break;
                case Keys.Z: if (e.Control) Zpet(); break;
                case Keys.Y: if (e.Control) Znovu(); break;
                default: return;
            }
            e.Handled = true;
        }
    }
}
