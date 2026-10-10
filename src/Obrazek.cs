using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace ObrysCNC
{
    public class NastaveniRozpoznani
    {
        public int Rezim;              // 0 = vse, co se lisi od pozadi; 1 = tmave na svetlem (prah jasu)
        public int Citlivost = 60;     // rezim 0: min. rozdil od barvy pozadi
        public int Prah = 128;         // rezim 1: prah jasu
        public bool Invertovat;        // rezim 1: svetle = tvar

        public string Klic() { return Rezim + "|" + Citlivost + "|" + Prah + "|" + Invertovat; }
    }

    public class NastaveniObrysu
    {
        public double SirkaMm = 500;
        public int Pocatek;            // 0 = levy dolni roh, 1 = stred
        public double PosunX, PosunY;
        public bool YNahore;
        public double VyskaDeskyMm = 600;
        public double Sigma = 0.8;     // vyhlazeni (px)
        public double Tol = 0.6;       // zjednoduseni (px)
        public double TolMm;           // max. povolena odchylka geometrie (mm); 0 = automaticky 1,5 x Tol px
        public double Narovnat = 3;    // stupne
        public double MinPlochaMm2 = 1;
        public bool Oblouky = true;
        public double AlfaMax = 1.0;   // zaobleni rohu jako alphamax v Potrace: 0 = vse ostre, 1.33 = vse oble
        public bool Stredova;          // stredova cara misto obrysu (kresby, gravirovani jednou drahou)
        public bool Verne = true;      // verny proklad (krivky primo na hranu obrazku) misto hladkeho Potrace
        public double Roh = 25;
    }

    public class VrstvaObrazku
    {
        public string Nazev;
        public Color Barva;
        public bool Pouzit = true;
        public int Pixelu;
        public float[] Maska;                       // 0/1 nebo mekka hodnota (rezim jas)
        public List<Obrys> Obrysy = new List<Obrys>();
        public double TahMm = double.NaN;
        public double TypickaSirkaMm = double.NaN;  // median sirky tahu (u stredove cary = doporucena freza)
    }

    public class VysledekObrazku
    {
        public double SirkaMm, VyskaMm, MmNaPx;
        public double ObrX, ObrY, ObrW, ObrH;      // umisteni obrazku v mm (ObrY = horni hrana)
        public double MezeraMm = double.NaN;
        public List<P> UzkaMista = new List<P>();          // stredy tahu a mezer (mm) ...
        public List<double> UzkaSirky = new List<double>(); // ... a jejich sirka (mm) pro zvyrazneni uzkych mist
        public string Chyba;
        public Diagnostika Diagnostika = new Diagnostika(); // jak se obrysy povedly (CAD / nahradni proklad, odchylky)
    }

    public class Obrazek
    {
        public int W, H;
        public byte[] R, G, Bm;                    // slozeno na bile pozadi
        public Bitmap Nahled;                      // pro zobrazeni
        public bool Zmenseno;
        public List<VrstvaObrazku> Vrstvy = new List<VrstvaObrazku>();
        string klicRozpoznani;

        public const int MaxRozmer = 3000;

        public Obrazek(string soubor)
        {
            var src = NactiBitmapu(soubor);
            int w = src.Width, h = src.Height;
            double k = Math.Min(1.0, (double)MaxRozmer / Math.Max(w, h));
            if (k < 1) { w = (int)Math.Round(w * k); h = (int)Math.Round(h * k); Zmenseno = true; }
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }
            src.Dispose();
            W = w; H = h;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var bytes = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bmp.UnlockBits(data);
            R = new byte[w * h]; G = new byte[w * h]; Bm = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int si = y * data.Stride + x * 4, i = y * w + x;
                    double a = bytes[si + 3] / 255.0;
                    Bm[i] = (byte)Math.Round(bytes[si] * a + 255 * (1 - a));
                    G[i] = (byte)Math.Round(bytes[si + 1] * a + 255 * (1 - a));
                    R[i] = (byte)Math.Round(bytes[si + 2] * a + 255 * (1 - a));
                }
            Nahled = bmp;
        }

        // Nejdriv GDI+ (PNG, JPG, BMP, GIF, TIF); co nezna (WebP, HEIC…), zkusi pres WIC = kodeky Windows.
        static Bitmap NactiBitmapu(string soubor)
        {
            try
            {
                using (var fs = System.IO.File.OpenRead(soubor))
                using (var tmp = Image.FromStream(fs)) return new Bitmap(tmp);
            }
            catch (ArgumentException) { }
            catch (OutOfMemoryException) { }   // GDI+ tak hlasi i neznamy format
            try
            {
                return NactiPresWic(soubor);
            }
            catch (Exception ex)
            {
                throw new Exception("Formát obrázku se nepodařilo přečíst (" + ex.Message + ").\n" +
                    "U WebP nainstalujte z Microsoft Store „Rozšíření obrázků WebP“, nebo obrázek otevřete v Malování a uložte jako PNG.");
            }
        }

        static Bitmap NactiPresWic(string soubor)
        {
            using (var fs = System.IO.File.OpenRead(soubor))
            {
                var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(fs,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(dec.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
                var buf = new byte[stride * h];
                conv.CopyPixels(buf, stride, 0);
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < h; y++) Marshal.Copy(buf, y * stride, data.Scan0 + y * data.Stride, stride);
                bmp.UnlockBits(data);
                return bmp;
            }
        }

        // ---------- rozpoznani tvaru ----------
        // Vysledkem je jedna vrstva (frezuje se do dreva, barvy nehraji roli).
        public bool Rozpoznej(NastaveniRozpoznani n)
        {
            if (n.Klic() == klicRozpoznani) return false;
            klicRozpoznani = n.Klic();
            Vrstvy.Clear();
            int N = W * H;
            var m = new float[N];
            int cnt = 0;
            if (n.Rezim == 1)
            {
                for (int i = 0; i < N; i++)
                {
                    double lum = 0.299 * R[i] + 0.587 * G[i] + 0.114 * Bm[i];
                    double v = (n.Prah - lum) / 24.0 + 0.5;
                    if (n.Invertovat) v = 1 - v;
                    m[i] = (float)Math.Max(0, Math.Min(1, v));
                }
            }
            else if (n.Rezim == 2)
            {
                // adaptivni prah (fotka kresby, nerovnomerne svetlo): bod je tvar, kdyz je tmavsi nez
                // prumer sveho okoli o "citlivost/3" (prumer z integralniho obrazu)
                var ii = new double[(W + 1) * (H + 1)];
                for (int y = 0; y < H; y++)
                {
                    double radek = 0;
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        radek += 0.299 * R[i] + 0.587 * G[i] + 0.114 * Bm[i];
                        ii[(y + 1) * (W + 1) + x + 1] = ii[y * (W + 1) + x + 1] + radek;
                    }
                }
                int rr = Math.Max(8, Math.Min(W, H) / 20);
                double c = n.Citlivost / 3.0;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int xa = Math.Max(0, x - rr), xb = Math.Min(W, x + rr + 1), ya = Math.Max(0, y - rr), yb = Math.Min(H, y + rr + 1);
                        double prumer = (ii[yb * (W + 1) + xb] - ii[ya * (W + 1) + xb] - ii[yb * (W + 1) + xa] + ii[ya * (W + 1) + xa]) / ((xb - xa) * (yb - ya));
                        int i = y * W + x;
                        double lum = 0.299 * R[i] + 0.587 * G[i] + 0.114 * Bm[i];
                        double v = (prumer - c - lum) / 12.0 + 0.5;
                        if (n.Invertovat) v = 1 - v;
                        m[i] = (float)Math.Max(0, Math.Min(1, v));
                    }
            }
            else
            {
                // barva pozadi = median pixelu na okraji obrazku; tvar = vse, co se od ni dost lisi
                var okraj = new List<int>();
                for (int x = 0; x < W; x++) { okraj.Add(x); okraj.Add((H - 1) * W + x); }
                for (int y = 0; y < H; y++) { okraj.Add(y * W); okraj.Add(y * W + W - 1); }
                double br = Median(okraj.Select(i => (int)R[i])), bg = Median(okraj.Select(i => (int)G[i])), bb = Median(okraj.Select(i => (int)Bm[i]));
                var dd = new float[N];
                for (int i = 0; i < N; i++)
                    dd[i] = (float)Math.Sqrt((R[i] - br) * (R[i] - br) + (G[i] - bg) * (G[i] - bg) + (Bm[i] - bb) * (Bm[i] - bb));
                // hranovy pixel je smes pozadi a barvy tvaru: podil tvaru = rozdil / plny rozdil barvy tvaru v okoli.
                // Hranice tvaru pak lezi presne v pulce (izokrivka 0,5), ne tam, kde rozdil prekroci citlivost
                // (to tvary zesilovalo o ~0,3 px). Citlivost dal rozhoduje, co vubec je tvar.
                var plny = MaxOkoli(dd, W, H, 2);
                var nejmene = MinOkoli(dd, W, H);
                for (int i = 0; i < N; i++)
                {
                    double d = dd[i], D = Math.Max(plny[i], 1e-3);
                    double jeTvar = Math.Max(0, Math.Min(1, (D - n.Citlivost) / 30.0 + 0.5));   // je v okoli tvar?
                    double v = jeTvar >= 1 ? d / D : Math.Min(d / D, (d - n.Citlivost) / 30.0 + 0.5);
                    if (nejmene[i] >= n.Citlivost + 15) v = 1;      // obklopeny tvarem (svetlejsi misto uvnitr) = tvar
                    m[i] = (float)Math.Max(0, Math.Min(1, v));
                }
            }
            for (int i = 0; i < N; i++) if (m[i] > 0.5f) cnt++;
            Vrstvy.Add(new VrstvaObrazku { Nazev = "OBRYS", Barva = Color.FromArgb(40, 40, 40), Maska = m, Pixelu = cnt });
            return true;
        }

        // maximum v ctverci (2r+1) x (2r+1)
        static float[] MaxOkoli(float[] f, int w, int h, int r)
        {
            var t = new float[w * h];
            var res = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float mx = 0;
                    for (int k = Math.Max(0, x - r); k <= Math.Min(w - 1, x + r); k++) mx = Math.Max(mx, f[y * w + k]);
                    t[y * w + x] = mx;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float mx = 0;
                    for (int k = Math.Max(0, y - r); k <= Math.Min(h - 1, y + r); k++) mx = Math.Max(mx, t[k * w + x]);
                    res[y * w + x] = mx;
                }
            return res;
        }

        // minimum v okoli 3 x 3
        static float[] MinOkoli(float[] f, int w, int h)
        {
            var res = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float mn = float.MaxValue;
                    for (int yy = Math.Max(0, y - 1); yy <= Math.Min(h - 1, y + 1); yy++)
                        for (int xx = Math.Max(0, x - 1); xx <= Math.Min(w - 1, x + 1); xx++) mn = Math.Min(mn, f[yy * w + xx]);
                    res[y * w + x] = mn;
                }
            return res;
        }

        static double Median(IEnumerable<int> hodnoty)
        {
            var s = hodnoty.OrderBy(x => x).ToList();
            return s.Count == 0 ? 255 : s[s.Count / 2];
        }

        // ---------- obrysy ----------
        public VysledekObrazku VytvorObrysy(NastaveniObrysu n)
        {
            var v = new VysledekObrazku();
            var aktivni = Vrstvy.Where(x => x.Pouzit).ToList();
            foreach (var x in Vrstvy) { x.Obrysy = new List<Obrys>(); x.TahMm = double.NaN; }
            if (aktivni.Count == 0) { v.Chyba = "Není vybraná žádná vrstva."; return v; }

            int N = W * H;
            var pole = new Dictionary<VrstvaObrazku, float[]>();
            int bx0 = W, by0 = H, bx1 = -1, by1 = -1;
            foreach (var vr in aktivni)
            {
                var f = Rozmazat(vr.Maska, W, H, n.Sigma);
                pole[vr] = f;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                        if (f[y * W + x] > 0.5f) { bx0 = Math.Min(bx0, x); bx1 = Math.Max(bx1, x); by0 = Math.Min(by0, y); by1 = Math.Max(by1, y); }
            }
            if (bx1 < 0) { v.Chyba = "V obrázku se nenašel žádný tvar. Zkuste jiný režim nebo práh."; return v; }
            double s0 = n.SirkaMm / Math.Max(1, bx1 - bx0 + 1);
            double minPlochaPx = n.MinPlochaMm2 / (s0 * s0);

            // obrysy v pixelech
            var px = new Dictionary<VrstvaObrazku, List<Obrys>>();
            foreach (var vr in aktivni)
            {
                var seznam = new List<Obrys>();
                if (n.Stredova)
                {
                    var maska = new bool[N];
                    for (int i = 0; i < N; i++) maska[i] = pole[vr][i] > 0.5f;
                    double sirkaPx;
                    seznam = Stred.Cary(maska, W, H, n.Tol, Math.Max(4, 2 * Math.Sqrt(minPlochaPx)), out sirkaPx);
                    px[vr] = seznam;
                    continue;
                }
                double zonaRohu = 1.0 + 1.6 * Math.Max(0.6, n.Sigma);
                var smycky = MarchingSquares(pole[vr], W, H)
                    .Where(loop => loop.Count >= 4 && Math.Abs(Geo.PlochaPolygonu(loop)) >= Math.Max(minPlochaPx, 1)).ToList();
                var vysl = new Obrys[smycky.Count];
                var dgs = new Diagnostika[smycky.Count];
                // tolerance v mm -> px podle meritka (sirka obrysu / sirka tvaru v obrazku)
                double tolPx = n.TolMm > 0 ? n.TolMm / s0 : 0;
                // obrysy jsou na sobe nezavisle -> pocitat soubezne na vsech jadrech
                System.Threading.Tasks.Parallel.For(0, smycky.Count, i =>
                {
                    var loop = smycky[i];
                    var dg = new Diagnostika();
                    // CAD: retez primek a oblouku s tecnymi napojenimi, ostre rohy jen kde opravdu jsou.
                    // Kdyz to nejde, nahradni proklad - a v diagnostice se to oznaci.
                    Obrys o = null;
                    if (n.Verne)
                    {
                        var kv = Konstrukce.RekonstruujVse(loop, n.Tol, tolPx, n.AlfaMax, n.Narovnat, n.Sigma);
                        o = kv.Obrys;
                        dg.TolMaxPx = kv.TolMaxPx;
                        dg.TolOmezena = tolPx > 0 && tolPx < Konstrukce.MinTolPx;
                        if (o != null)
                        {
                            dg.ObrysuCad++;
                            dg.PrvkuMimoTol += kv.MimoTol;
                            if (kv.Kruh) dg.Kruznic++;
                            else if (kv.KruhPotrebaPx > 0) dg.TemerKruznicePx.Add(kv.KruhPotrebaPx);
                            dg.SumHranyPx = kv.SumPx;
                            if (kv.ObrysPred != null) dg.ZapoctiPred(kv.ObrysPred, loop, zonaRohu, Math.Max(0.6, n.Sigma));
                            // bez oblouku: jen usecky, odchylka nejvys ctvrtina tolerance; diagnostika popisuje
                            // CAD geometrii, aproximace se pocita zvlast
                            if (!n.Oblouky)
                            {
                                dg.Zapocti(o, loop, zonaRohu, Math.Max(0.6, n.Sigma));
                                o = JenUsecky(o, Math.Max(0.05, 0.25 * kv.TolMaxPx));
                                dg.UsecekAprox += o.PocetSegmentu;
                                vysl[i] = o;
                                dgs[i] = dg;
                                return;
                            }
                        }
                        else
                        {
                            o = Hladke.VyhladVerne(loop, n.Tol, n.AlfaMax, n.Narovnat, !n.Oblouky);
                            if (o != null) { dg.ObrysuNahradnich++; dg.PridejDuvod(kv.Duvod); }
                        }
                    }
                    else
                    {
                        o = Hladke.Vyhlad(loop, n.Tol, n.AlfaMax, n.Narovnat, !n.Oblouky);
                        if (o != null) dg.ObrysuHladkych++;
                    }
                    if (o != null && o.Pts.Count >= 2 && (o.Pts.Count >= 3 || o.B.Any(b => Math.Abs(b) > 1e-12)))
                    {
                        vysl[i] = o;
                        dg.Zapocti(o, loop, zonaRohu, Math.Max(0.6, n.Sigma));
                    }
                    dgs[i] = dg;
                });
                for (int i = 0; i < smycky.Count; i++)
                {
                    if (vysl[i] != null) seznam.Add(vysl[i]);
                    v.Diagnostika.Pricti(dgs[i]);
                }
                // topologie: obrys nesmi krizit sam sebe ani jiny obrys
                int vlastni, vzajemne;
                Geo.Pruseciky(seznam, 0.02, out vlastni, out vzajemne);
                v.Diagnostika.Samopruseciky += vlastni;
                v.Diagnostika.PrusecikyObrysu += vzajemne;
                px[vr] = seznam;
            }

            // prevod do mm: Y nahoru, presna sirka podle skutecneho rozsahu obrysu
            double minx, miny, maxx, maxy;
            Geo.Rozsah(px.Values.SelectMany(l => l), out minx, out miny, out maxx, out maxy);
            if (maxx <= minx) { v.Chyba = "Obrysy jsou příliš malé."; return v; }
            double s = n.SirkaMm / (maxx - minx);
            // Konecne meritko muze byt jine nez odhad z rastru: diagnostika musi hodnotit zadane mm.
            if (n.TolMm > 0) v.Diagnostika.TolMaxPx = n.TolMm / s;
            double vyska = (maxy - miny) * s;
            double dx = n.PosunX, dy = n.PosunY;
            if (n.Pocatek == 1) { dx -= n.SirkaMm / 2; dy -= vyska / 2; }
            if (n.YNahore) dy = n.VyskaDeskyMm - n.PosunY - (n.Pocatek == 1 ? vyska / 2 : vyska);
            Func<int, P> naMm = i => new P((i % W + 0.5 - minx) * s + dx, (maxy - (i / W + 0.5)) * s + dy);
            foreach (var vr in aktivni)
            {
                var mm = px[vr].Select(o => new Obrys
                {
                    Zavreny = o.Zavreny,
                    Vrstva = vr.Nazev,
                    Pts = o.Pts.Select(p => new P((p.X - minx) * s + dx, (maxy - p.Y) * s + dy)).ToList(),
                    B = o.B.Select(b => -b).ToList()   // zrcadleni osy Y otaci smer oblouku
                }).ToList();
                vr.Obrysy = mm.Select(o => Geo.UpravSmerAStart(o, mm, n.Roh)).ToList();
                var idx = new List<int>(); var sir = new List<double>();
                Hrebeny(pole[vr], W, H, 0.5f, false, idx, sir);
                vr.TahMm = Nejtensi(sir) * s;
                if (sir.Count > 0) vr.TypickaSirkaMm = sir.OrderBy(x => x).ElementAt(sir.Count / 2) * s;
                for (int k = 0; k < idx.Count; k++) { v.UzkaMista.Add(naMm(idx[k])); v.UzkaSirky.Add(sir[k] * s); }
            }
            var spojene = new float[N];
            foreach (var f in pole.Values) for (int i = 0; i < N; i++) spojene[i] = Math.Max(spojene[i], f[i]);
            var midx = new List<int>(); var msir = new List<double>();
            Hrebeny(spojene, W, H, 0.5f, true, midx, msir);
            v.MezeraMm = n.Stredova ? double.NaN : Nejtensi(msir) * s;      // u stredove cary mezery nevadi
            if (!n.Stredova)
                for (int k = 0; k < midx.Count; k++) { v.UzkaMista.Add(naMm(midx[k])); v.UzkaSirky.Add(msir[k] * s); }
            v.SirkaMm = n.SirkaMm;
            v.VyskaMm = vyska;
            v.MmNaPx = s;
            v.ObrX = (0 - minx) * s + dx;
            v.ObrY = (maxy - 0) * s + dy;
            v.ObrW = W * s;
            v.ObrH = H * s;
            return v;
        }

        // oblouky rozlozit na usecky (volba "Krivky jako oblouky" vypnuta); sipka = max. odchylka usecky od oblouku
        static Obrys JenUsecky(Obrys o, double sipka)
        {
            var r = new Obrys { Zavreny = o.Zavreny, Vrstva = o.Vrstva };
            for (int i = 0; i < o.PocetSegmentu; i++)
            {
                var body = Geo.Rozloz(o.Pts[i], o.KonecSegmentu(i), o.B[i], sipka);
                for (int k = 0; k < body.Count - 1; k++) { r.Pts.Add(body[k]); r.B.Add(0); }
            }
            if (!o.Zavreny) r.Pts.Add(o.Pts[o.Pts.Count - 1]);
            return r;
        }

        public static float[] Rozmazat(float[] f, int w, int h, double sigma)
        {
            if (sigma <= 0.05) return (float[])f.Clone();
            int r = (int)Math.Ceiling(sigma * 3);
            var k = new float[2 * r + 1];
            float sum = 0;
            for (int i = -r; i <= r; i++) { k[i + r] = (float)Math.Exp(-(i * i) / (2 * sigma * sigma)); sum += k[i + r]; }
            for (int i = 0; i < k.Length; i++) k[i] /= sum;
            var tmp = new float[w * h];
            var res = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float a = 0;
                    for (int j = -r; j <= r; j++) { int xx = x + j; xx = xx < 0 ? 0 : xx >= w ? w - 1 : xx; a += k[j + r] * f[row + xx]; }
                    tmp[row + x] = a;
                }
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a = 0;
                    for (int j = -r; j <= r; j++) { int yy = y + j; yy = yy < 0 ? 0 : yy >= h ? h - 1 : yy; a += k[j + r] * tmp[yy * w + x]; }
                    res[y * w + x] = a;
                }
            return res;
        }

        // marching squares na izolinii 0.5; souradnice v pixelech obrazku (pixel i zabira <i, i+1>)
        public static List<List<P>> MarchingSquares(float[] f, int w, int h)
        {
            int Wp = w + 2, Hp = h + 2;
            const float iso = 0.5f;
            Func<int, int, float> pv = (x, y) => (x <= 0 || y <= 0 || x > w || y > h) ? 0f : f[(y - 1) * w + (x - 1)];
            var segA = new List<long>();
            var segB = new List<long>();
            var s1 = new Dictionary<long, int>();
            var s2 = new Dictionary<long, int>();
            Action<long, long> pridej = (a, b) =>
            {
                int s = segA.Count;
                segA.Add(a); segB.Add(b);
                foreach (var e in new[] { a, b }) { if (!s1.ContainsKey(e)) s1[e] = s; else s2[e] = s; }
            };
            Func<int, int, long> hran = (x, y) => 2L * ((long)y * Wp + x);
            Func<int, int, long> svis = (x, y) => 2L * ((long)y * Wp + x) + 1;
            for (int y = 0; y < Hp - 1; y++)
                for (int x = 0; x < Wp - 1; x++)
                {
                    float tl = pv(x, y), tr = pv(x + 1, y), br = pv(x + 1, y + 1), bl = pv(x, y + 1);
                    int c = (tl > iso ? 8 : 0) | (tr > iso ? 4 : 0) | (br > iso ? 2 : 0) | (bl > iso ? 1 : 0);
                    if (c == 0 || c == 15) continue;
                    long T = hran(x, y), Bo = hran(x, y + 1), L = svis(x, y), Rr = svis(x + 1, y);
                    bool stred = (tl + tr + br + bl) / 4 > iso;
                    switch (c)
                    {
                        case 1: case 14: pridej(L, Bo); break;
                        case 2: case 13: pridej(Bo, Rr); break;
                        case 3: case 12: pridej(L, Rr); break;
                        case 4: case 11: pridej(T, Rr); break;
                        case 6: case 9: pridej(T, Bo); break;
                        case 7: case 8: pridej(T, L); break;
                        case 5: if (stred) { pridej(T, L); pridej(Bo, Rr); } else { pridej(T, Rr); pridej(L, Bo); } break;
                        case 10: if (stred) { pridej(T, Rr); pridej(L, Bo); } else { pridej(T, L); pridej(Bo, Rr); } break;
                    }
                }
            Func<long, P> bod = e =>
            {
                long kk = e >> 1;
                int x = (int)(kk % Wp), y = (int)(kk / Wp);
                if ((e & 1) == 0) { float f0 = pv(x, y), f1 = pv(x + 1, y); return new P(x + (iso - f0) / (f1 - f0) - 0.5, y - 0.5); }
                float g0 = pv(x, y), g1 = pv(x, y + 1);
                return new P(x - 0.5, y + (iso - g0) / (g1 - g0) - 0.5);
            };
            var pouzit = new bool[segA.Count];
            var smycky = new List<List<P>>();
            for (int s0 = 0; s0 < segA.Count; s0++)
            {
                if (pouzit[s0]) continue;
                long start = segA[s0], e = segB[s0];
                var loop = new List<P> { bod(start) };
                int s = s0;
                pouzit[s] = true;
                while (e != start)
                {
                    loop.Add(bod(e));
                    int a1, a2 = -1;
                    s1.TryGetValue(e, out a1);
                    if (!s2.TryGetValue(e, out a2)) a2 = -1;
                    int dalsi = a1 == s ? a2 : a1;
                    if (dalsi < 0 || pouzit[dalsi]) break;
                    s = dalsi;
                    pouzit[s] = true;
                    e = segA[s] == e ? segB[s] : segA[s];
                }
                smycky.Add(loop);
            }
            return smycky;
        }

        // euklidovska vzdalenostni transformace (Felzenszwalb): vzdalenost k nejblizsimu pixelu mimo masku
        public static double[] Edt(bool[] maska, int w, int h)
        {
            const double INF = 1e20;
            var g = new double[w * h];
            for (int i = 0; i < w * h; i++) g[i] = maska[i] ? INF : 0;
            int n = Math.Max(w, h);
            var f = new double[n];
            var d = new double[n];
            var v = new int[n];
            var z = new double[n + 1];
            Action<int> dt1 = len =>
            {
                int k = 0;
                v[0] = 0; z[0] = -INF; z[1] = INF;
                for (int q = 1; q < len; q++)
                {
                    double s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                    while (s <= z[k]) { k--; s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2.0 * q - 2.0 * v[k]); }
                    k++; v[k] = q; z[k] = s; z[k + 1] = INF;
                }
                k = 0;
                for (int q = 0; q < len; q++) { while (z[k + 1] < q) k++; d[q] = (q - v[k]) * (double)(q - v[k]) + f[v[k]]; }
            };
            for (int x = 0; x < w; x++) { for (int y = 0; y < h; y++) f[y] = g[y * w + x]; dt1(h); for (int y = 0; y < h; y++) g[y * w + x] = d[y]; }
            for (int y = 0; y < h; y++) { for (int x = 0; x < w; x++) f[x] = g[y * w + x]; dt1(w); for (int x = 0; x < w; x++) g[y * w + x] = Math.Sqrt(d[x]); }
            return g;
        }

        // hrebeny vzdalenostni mapy = stredy tahu (nebo mezer); vraci indexy pixelu a sirku v px
        static void Hrebeny(float[] f, int w, int h, float prah, bool mezera, List<int> idx, List<double> sirka)
        {
            var m = new bool[w * h];
            for (int i = 0; i < w * h; i++) m[i] = mezera ? f[i] <= prah : f[i] > prah;
            var dt = Edt(m, w, h);
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    double val = dt[i];
                    if (!m[i] || val < 1 || val > 1e9) continue;
                    bool ok = true;
                    for (int dy = -1; dy <= 1 && ok; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (dt[i + dy * w + dx] > val) { ok = false; break; }
                    if (ok) { idx.Add(i); sirka.Add(2 * val - 0.5); }
                }
        }

        // priblizna nejtensi sirka v px (3. percentil hrebenu, spicky se ignoruji)
        static double Nejtensi(List<double> sirky)
        {
            if (sirky.Count == 0) return double.NaN;
            var s = sirky.OrderBy(x => x).ToList();
            return s[(int)(s.Count * 0.03)];
        }
    }
}
