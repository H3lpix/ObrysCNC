using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ObrysCNC;

// Funkcni kontroly bez zmen ulozenych uzivatelskych nastaveni.
static class TestFunkce
{
    static int ok, chyby;
    static string dir;
    static BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Over(bool b, string co) { if (!b) throw new Exception(co); }
    static void Test(string co, Action a) { try { a(); ok++; Console.WriteLine("OK | " + co); } catch (Exception e) { chyby++; Console.WriteLine("CHYBA | " + co + " | " + e.GetBaseException().Message); } }
    static void Blizko(double a, double b) { Over(Math.Abs(a-b) < 1e-6, "Hodnota " + a + " misto " + b); }
    static Obrys Obd(double x, double y, double w, double h)
    {
        var o = new Obrys { Zavreny = true, Vrstva = "TEST" };
        o.Pts.AddRange(new[] { new P(x,y), new P(x+w,y), new P(x+w,y+h), new P(x,y+h) }); o.B.AddRange(new double[4]); return o;
    }
    static Editor Ed(params Obrys[] os) { var e = new Editor { Size = new Size(900,600) }; e.NastavObrysy(os.ToList(), null); return e; }
    static void Vyber(Editor e, params int[] vs) { e.ZrusVyber(); var v = (HashSet<long>)typeof(Editor).GetField("vyber", flags).GetValue(e); foreach (int i in vs) v.Add(i); }
    static void Vol(Editor e, string name, params object[] a) { typeof(Editor).GetMethod(name, flags).Invoke(e,a); }
    static double Plocha(Editor e) { return Math.Abs(Geo.Plocha(e.Obrysy[0])); }
    static void Rozmer(Editor e, double w, double h) { double a,b,c,d; e.Rozsah(out a,out b,out c,out d); Blizko(c-a,w); Blizko(d-b,h); }
    static List<Obrys> Prevod(string png, NastaveniRozpoznani r, NastaveniObrysu n)
    {
        var o = new Obrazek(png); try { o.Rozpoznej(r); var v=o.VytvorObrysy(n); Over(v.Chyba==null, v.Chyba); return o.Vrstvy.SelectMany(x=>x.Obrysy).ToList(); } finally { o.Nahled.Dispose(); }
    }
    static void VytvorObrazek()
    {
        string[] tvary={"horni_rohy_R32","kruh_R100","mezikruzi","kapsle_R50","L_profil_R30","hvezda","list","elipsa_110x80","maly_roh_R4","ctverec_ostry","deska_otvory","pulkruh_R100"};
        using(var b=new Bitmap(1200,900)) using(var g=Graphics.FromImage(b))
        {
            g.Clear(Color.White); g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            for(int i=0;i<tvary.Length;i++) using(var s=Image.FromFile(Path.Combine(dir,tvary[i]+".png")))
            { double k=Math.Min(260.0/s.Width,260.0/s.Height); int w=(int)(s.Width*k), h=(int)(s.Height*k); g.DrawImage(s,(i%4)*300+(300-w)/2,(i/4)*300+(300-h)/2,w,h); }
            b.Save(Path.Combine(dir,"test-aplikace.png"));
        }
        using(var b=new Bitmap(300,200)) using(var g=Graphics.FromImage(b)) using(var p=new Pen(Color.Black,12))
        { g.Clear(Color.White); g.DrawLine(p,30,100,260,100); b.Save(Path.Combine(dir,"tah.png")); }
    }
    [STAThread]
    static int Main(string[] args)
    {
        dir=Path.GetFullPath(args[0]); VytvorObrazek(); string png=Path.Combine(dir,"test-aplikace.png");
        Test("Rozpoznani odlisnosti pozadi / prahu / kresby",()=> { foreach(int r in new[]{0,1,2}) Over(Prevod(png,new NastaveniRozpoznani{Rezim=r},new NastaveniObrysu()).Count>0,"Prazdny rezim "+r); });
        Test("Invertovany obrazek a prah",()=> { string p=Path.Combine(dir,"invert.png"); using(var b=new Bitmap(200,160)) using(var g=Graphics.FromImage(b)){g.Clear(Color.Black);g.FillRectangle(Brushes.White,30,30,140,100);b.Save(p);} Over(Prevod(p,new NastaveniRozpoznani{Rezim=1,Invertovat=true},new NastaveniObrysu()).Count==1,"Invertovani"); });
        Test("Stredova cara gravirovani",()=> { var o=Prevod(Path.Combine(dir,"tah.png"),new NastaveniRozpoznani(),new NastaveniObrysu{Stredova=true}); Over(o.Count>0 && o.Any(x=>!x.Zavreny),"Chybi otevrena draha"); });
        Test("Prazdny obrazek vraci chybu",()=> { string p=Path.Combine(dir,"prazdny.png"); using(var b=new Bitmap(120,120))using(var g=Graphics.FromImage(b)){g.Clear(Color.White);b.Save(p);} var o=new Obrazek(p);o.Rozpoznej(new NastaveniRozpoznani());Over(o.VytvorObrysy(new NastaveniObrysu()).Chyba!=null,"Chybi zprava");o.Nahled.Dispose(); });
        Test("Velikost a posun obrysu v mm",()=> { var o=Prevod(Path.Combine(dir,"ctverec_ostry.png"),new NastaveniRozpoznani(),new NastaveniObrysu{SirkaMm=200,PosunX=17,PosunY=23}); double a,b,c,d;Geo.Rozsah(o,out a,out b,out c,out d);Blizko(c-a,200);Blizko(a,17);Blizko(b,23); });
        Test("DXF export, zpetne nacteni a oprava",()=> {var os=Prevod(png,new NastaveniRozpoznani(),new NastaveniObrysu());string p=Path.Combine(dir,"test-aplikace.dxf");Dxf.Zapis(p,os,new Dictionary<string,int>{{"OBRYS",7}},false);Over(Dxf.Over(p,os).Ok,"DXF roundtrip");var r=Dxf.Oprav(p,0.05,0.02,25);Over(r.Obrysy.Count==os.Count,"Zmena poctu kontur");Over(r.MaxOdchylka<0.1,"Odchylka opravy");Console.WriteLine("  nove testovni DXF: "+os.Count+" kontur, "+os.Sum(x=>x.B.Count(b=>b!=0))+" oblouku");});
        Test("DXF bez oblouku",()=> {var os=Prevod(Path.Combine(dir,"kruh_R100.png"),new NastaveniRozpoznani(),new NastaveniObrysu());string p=Path.Combine(dir,"usecky.dxf");Dxf.Zapis(p,os,new Dictionary<string,int>(),true);Over(Dxf.Nacti(p).Prvky.All(x=>x.B.All(b=>b==0)),"Nenulove bulge");});
        Test("Vyber, souradnice a zruseni vyberu",()=> {using(var e=Ed(Obd(30,40,100,50))){Vyber(e,0);P p;Over(e.JedenBod(out p),"Bod");Blizko(p.X,30);e.VyberVse();Over(e.PocetVybranych==4,"Vyber vse");e.ZrusVyber();Over(!e.NecoVybrano,"Zrusit");}});
        Test("Presun, zpet a znovu",()=> {using(var e=Ed(Obd(30,40,100,50))){e.VyberVse();e.PosunVybrane(10,20,true);Blizko(e.Obrysy[0].Pts[0].X,40);e.Zpet();Blizko(e.Obrysy[0].Pts[0].X,30);e.Znovu();Blizko(e.Obrysy[0].Pts[0].Y,60);}});
        Test("Vystredeni, roh desky a presah",()=> {using(var e=Ed(Obd(0,0,100,50))){e.Vystredit();double a,b,c,d;e.Rozsah(out a,out b,out c,out d);Blizko((a+c)/2,((SizeF)e.Deska).Width/2);e.DoRohu(10);e.Rozsah(out a,out b,out c,out d);Blizko(a,10);Blizko(b,10);Over(!e.Presahuje,"Presah");e.PosunVse(2000,0);Over(e.Presahuje,"Chybi presah");}});
        Test("Otoceni 90 stupnu",()=> {using(var e=Ed(Obd(0,0,100,50))){e.Otoc(90);Rozmer(e,50,100);Blizko(Plocha(e),5000);}});
        Test("Zrcadleni X i Y",()=> {using(var e=Ed(Obd(0,0,100,50))){e.Zrcadli(true);e.Zrcadli(false);Rozmer(e,100,50);Blizko(Plocha(e),5000);}});
        Test("Meritko 200 procent",()=> {using(var e=Ed(Obd(0,0,100,50))){e.Meritko(200);Rozmer(e,200,100);Blizko(Plocha(e),20000);}});
        Test("Mazani bodu a obrysu",()=> {using(var e=Ed(Obd(0,0,100,50))){Vyber(e,1);e.SmazVybrane();Over(e.PocetBodu==3,"Bod");e.Zpet();e.VyberVse();e.SmazObrysy();Over(e.Obrysy.Count==0,"Obrys");e.Zpet();Over(e.PocetBodu==4,"Zpet");}});
        Test("Presny radius rohu",()=> {using(var e=Ed(Obd(0,0,100,50))){Vyber(e,2);Over(e.RohSRadiusem(10,false)==null,"Radius");int s=e.Obrysy[0].B.FindIndex(b=>b!=0);Over(s>=0,"Chybi oblouk");Blizko(Geo.Oblouk(e.Obrysy[0].Pts[s],e.Obrysy[0].KonecSegmentu(s),e.Obrysy[0].B[s]).R,10);}});
        Test("Srovnani X a Y podle bodu",()=> {using(var e=Ed(Obd(0,0,100,50))){Vyber(e,0,3);e.ZacniSrovnani(true);Vol(e,"DokonciSrovnani",new P(12,18));Blizko(e.Obrysy[0].Pts[0].X,12);Blizko(e.Obrysy[0].Pts[3].X,12);Vyber(e,0,1);e.ZacniSrovnani(false);Vol(e,"DokonciSrovnani",new P(12,18));Blizko(e.Obrysy[0].Pts[1].Y,18);}});
        Test("Start do bodu a obraceni smeru",()=> {using(var e=Ed(Obd(0,0,100,50))){Vyber(e,2);Over(e.StartDoBodu()==null,"Start");Blizko(e.Obrysy[0].Pts[0].X,100);double a=Geo.Plocha(e.Obrysy[0]);e.VyberVse();e.ObratVybrane();Blizko(Geo.Plocha(e.Obrysy[0]),-a);}});
        Test("Vlozeni bodu do oblouku zachova radius a plochu",()=> {var o=new Obrys{Zavreny=true,Vrstva="TEST"};o.Pts.AddRange(new[]{new P(-10,0),new P(10,0)});o.B.AddRange(new[]{1.0,1.0});using(var e=Ed(o)){double a=Plocha(e);Vol(e,"VlozBod",0,0,new P(0,-10));Over(e.PocetBodu==3,"Pocet");Blizko(Plocha(e),a);foreach(int s in Enumerable.Range(0,e.Obrysy[0].PocetSegmentu))Blizko(Geo.Oblouk(e.Obrysy[0].Pts[s],e.Obrysy[0].KonecSegmentu(s),e.Obrysy[0].B[s]).R,10);}});
        Test("Spojeni koncu otevrenych car",()=> {var a=new Obrys{Vrstva="TEST"};a.Pts.AddRange(new[]{new P(0,0),new P(50,0)});a.B.Add(0);var b=new Obrys{Vrstva="TEST"};b.Pts.AddRange(new[]{new P(50.01,0),new P(100,0)});b.B.Add(0);using(var e=Ed(a,b)){e.SpojKonce(0.02);Over(e.Obrysy.Count==1,"Spojeni");}});
        Test("Uzavreni otevrene kontury",()=> {var o=Obd(0,0,100,50);o.Zavreny=false;o.B.RemoveAt(3);using(var e=Ed(o)){Over(e.UzavriOtevrene()==null,"Uzavreni");Over(e.Obrysy[0].Zavreny&&e.Obrysy[0].B.Count==4,"Uzavrena geometrie");}});
        Test("Odsazeni ven/dovnitr, zaobleni a slouceni",()=> {foreach(double d in new[]{5.0,-5.0})using(var e=Ed(Obd(0,0,100,50))){Over(e.UpravTvar(d,0,false)==null,"Offset");Over(d>0?Plocha(e)>5000:Plocha(e)<5000,"Plocha");}using(var e=Ed(Obd(0,0,100,50))){Over(e.UpravTvar(0,10,false)==null,"Zaobleni");Over(e.Obrysy[0].B.Any(b=>b!=0),"Oblouky");}using(var e=Ed(Obd(0,0,100,50),Obd(50,0,100,50))){Over(e.UpravTvar(0,0,false)==null&&e.Obrysy.Count==1,"Slouceni");}});
        Test("Tri karty a vychozi zamek ve formulari",()=> {using(var f=new HlavniOkno(null)){var t=(TabControl)f.Controls[0];Over(t.TabPages.Count==3,"Pocet karet");Over(t.TabPages[2].Text=="Editor DXF","Editor");var e=(Editor)typeof(HlavniOkno).GetField("editor",flags).GetValue(f);Over(e.PosunZamcen,"Zamek ve formulari");}});
        Test("Vykresleni souradnic vybraneho bodu",()=> {using(var e=Ed(Obd(30,40,100,50))){Vyber(e,0);using(var b=new Bitmap(900,600)){e.DrawToBitmap(b,new Rectangle(0,0,900,600));b.Save(Path.Combine(dir,"editor-souradnice.png"));}}});
        Test("SVG export ma skutecne oblouky a jednotky mm",()=> {var os=Prevod(Path.Combine(dir,"kruh_R100.png"),new NastaveniRozpoznani(),new NastaveniObrysu());string p=Path.Combine(dir,"test.svg");Svg.Zapis(p,os);string s=File.ReadAllText(p);Over(s.Contains("mm")&&s.Contains(" A"),"Chybi SVG oblouk nebo mm");});
        Test("PNG/JPEG/BMP/GIF/TIFF import",()=> {using(var b=new Bitmap(png)) foreach(var f in new[]{System.Drawing.Imaging.ImageFormat.Jpeg,System.Drawing.Imaging.ImageFormat.Bmp,System.Drawing.Imaging.ImageFormat.Gif,System.Drawing.Imaging.ImageFormat.Tiff}){string p=Path.Combine(dir,"format-"+f+".img");b.Save(p,f);var o=new Obrazek(p);Over(o.W==1200&&o.H==900,"Rozmery importu "+f);o.Nahled.Dispose();}});
        Test("Neznamy soubor hlasi chybu importu",()=> {string p=Path.Combine(dir,"neplatny.dxf");File.WriteAllText(p,"toto neni obrazek");bool chyba=false;try{new Obrazek(p);}catch(Exception){chyba=true;}Over(chyba,"Import neodmitl data");});
        Test("Zoom a posun nahledu nemeni CAD souradnice",()=> {using(var e=Ed(Obd(30,40,100,50))){Vol(e,"OnMouseWheel",new MouseEventArgs(MouseButtons.None,0,200,200,120));Vol(e,"OnMouseDown",new MouseEventArgs(MouseButtons.Right,1,200,200,0));Vol(e,"OnMouseMove",new MouseEventArgs(MouseButtons.Right,0,250,220,0));Vol(e,"OnMouseUp",new MouseEventArgs(MouseButtons.Right,1,250,220,0));Blizko(e.Obrysy[0].Pts[0].X,30);Blizko(e.Obrysy[0].Pts[0].Y,40);Over(!e.Upraveno,"Zmena geometrie");}});
        Test("Nerealna tolerance se nezvysi a hlasi nesplneni",()=> {var o=new Obrazek(Path.Combine(dir,"elipsa_110x80.png"));o.Rozpoznej(new NastaveniRozpoznani());var v=o.VytvorObrysy(new NastaveniObrysu{SirkaMm=220,TolMm=0.00001});Over(!v.Diagnostika.SplnujeToleranci,"Falesne splneni");Blizko(v.Diagnostika.TolMaxPx,0.00001/v.MmNaPx);o.Nahled.Dispose();});
        Test("Pocatek ve stredu a posun",()=> {var os=Prevod(Path.Combine(dir,"ctverec_ostry.png"),new NastaveniRozpoznani(),new NastaveniObrysu{SirkaMm=200,Pocatek=1,PosunX=17,PosunY=23});double a,b,c,d;Geo.Rozsah(os,out a,out b,out c,out d);Blizko((a+c)/2,17);Blizko((b+d)/2,23);});
        Test("Vypnuta vrstva a cache rozpoznani",()=> {var o=new Obrazek(png);var n=new NastaveniRozpoznani();Over(o.Rozpoznej(n)&&!o.Rozpoznej(n),"Cache");o.Vrstvy[0].Pouzit=false;var v=o.VytvorObrysy(new NastaveniObrysu());Over(v.Chyba!=null||o.Vrstvy[0].Obrysy.Count==0,"Vypnuta vrstva se pouzila");o.Nahled.Dispose();});
        Test("Kontrola sirky tahu a mezer pro frezu",()=> {var o=new Obrazek(Path.Combine(dir,"tah.png"));o.Rozpoznej(new NastaveniRozpoznani());var v=o.VytvorObrysy(new NastaveniObrysu{SirkaMm=230});Over(!double.IsNaN(o.Vrstvy[0].TypickaSirkaMm)&&o.Vrstvy[0].TypickaSirkaMm>0,"Chybi sirka");Over(v.UzkaMista.Count==v.UzkaSirky.Count,"Nesouhlasi uzka mista");o.Nahled.Dispose();});
        Test("DXF LINE ARC CIRCLE a hlaseni nepodporovane entity",()=> {string p=Path.Combine(dir,"entity.dxf");File.WriteAllText(p,"0\nSECTION\n2\nENTITIES\n0\nLINE\n8\nTEST\n10\n0\n20\n0\n11\n50\n21\n0\n0\nARC\n8\nTEST\n10\n100\n20\n100\n40\n20\n50\n0\n51\n90\n0\nCIRCLE\n8\nTEST\n10\n200\n20\n100\n40\n30\n0\nSPLINE\n8\nTEST\n0\nENDSEC\n0\nEOF\n");var d=Dxf.Nacti(p);Over(d.Typy.ContainsKey("LINE")&&d.Typy.ContainsKey("ARC")&&d.Typy.ContainsKey("CIRCLE"),"Import entit");Over(d.Nepodporovane.ContainsKey("SPLINE"),"Chybi diagnostika SPLINE");Over(d.Prvky.Any(x=>x.Zavreny&&x.B.Count==2),"Kruh neni dva oblouky");});
        Test("Obrys z obrazku a DXF lze nacist do formulare editoru",()=> {using(var f=new HlavniOkno(null)){typeof(HlavniOkno).GetMethod("OtevritObrazek",flags).Invoke(f,new object[]{png});var v=(VysledekObrazku)typeof(HlavniOkno).GetField("vys",flags).GetValue(f);Over(v!=null&&v.Chyba==null,"Prevod ve formulari");typeof(HlavniOkno).GetMethod("OtevritDoEditoru",flags).Invoke(f,new object[]{Path.Combine(dir,"test-aplikace.dxf")});var e=(Editor)typeof(HlavniOkno).GetField("editor",flags).GetValue(f);Over(e.Obrysy.Count==17,"Import do editoru");Over(((TabControl)f.Controls[0]).SelectedIndex==2,"Neaktivoval editor");}});
        Test("Tolerance a topologie noveho testovaciho obrazu",()=> {var o=new Obrazek(png);o.Rozpoznej(new NastaveniRozpoznani());var v=o.VytvorObrysy(new NastaveniObrysu());var d=v.Diagnostika;Console.WriteLine("  MERENI: mm/px="+v.MmNaPx.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+", max="+(d.MaxOdchylkaPx*v.MmNaPx).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+" mm, tolerance="+(d.TolMaxPx*v.MmNaPx).ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+" mm, "+d.ToString());Over(d.SplnujeToleranci&&d.Samopruseciky+d.PrusecikyObrysu==0,"Tolerance nebo pruseciky");o.Nahled.Dispose();});
        Console.WriteLine("SOUHRN: "+ok+" proslo, "+chyby+" selhalo");return chyby==0?0:1;
    }
}
