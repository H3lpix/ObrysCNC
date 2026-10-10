using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ObrysCNC;

static class TestSouradniceStroje
{
    static int ok, chyby;
    static string dir;
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Over(bool b, string co) { if (!b) throw new Exception(co); }
    static void Blizko(double a, double b) { Over(Math.Abs(a-b)<1e-6,"Hodnota "+a+" misto "+b); }
    static void Test(string co, Action a) {try{a();ok++;Console.WriteLine("OK | "+co);}catch(Exception e){chyby++;Console.WriteLine("CHYBA | "+co+": "+e.GetBaseException().Message);}}
    static List<Obrys> Tvary()
    {
        var o=new Obrys{Zavreny=true,Vrstva="VNEJSI"};
        o.Pts.AddRange(new[]{new P(40,40),new P(240,40),new P(240,140),new P(220,160),new P(60,160),new P(40,140)});
        o.B.AddRange(new[]{0.0,0,Math.Tan(Math.PI/8),0,Math.Tan(Math.PI/8),0});
        var h=new Obrys{Zavreny=true,Vrstva="OTVOR"};h.Pts.AddRange(new[]{new P(70,80),new P(90,80)});h.B.AddRange(new[]{-1.0,-1.0});
        var a=new Obrys{Vrstva="OTEVRENA"};a.Pts.AddRange(new[]{new P(300,30),new P(340,70)});a.B.Add(Math.Tan(Math.PI/8));
        return new List<Obrys>{o,h,a};
    }
    static void Stejne(List<Obrys> a,List<Obrys>b)
    {
        Over(a.Count==b.Count,"Pocet kontur");for(int i=0;i<a.Count;i++){Over(a[i].Zavreny==b[i].Zavreny&&a[i].Vrstva==b[i].Vrstva,"Metadata");Over(a[i].Pts.Count==b[i].Pts.Count&&a[i].B.Count==b[i].B.Count,"Pocet segmentu");for(int j=0;j<a[i].Pts.Count;j++){Blizko(a[i].Pts[j].X,b[i].Pts[j].X);Blizko(a[i].Pts[j].Y,b[i].Pts[j].Y);}for(int j=0;j<a[i].B.Count;j++)Blizko(a[i].B[j],b[i].B[j]);}
    }
    static Editor Ed(){var e=new Editor{Size=new Size(900,600),Deska=new SizeF(1000,600)};e.NastavObrysy(Tvary(),null);return e;}
    static void Snimek(Editor e,string soubor){using(var b=new Bitmap(e.Width,e.Height)){e.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(dir,soubor));}}
    [STAThread]
    static int Main(string[] args)
    {
        dir=Path.GetFullPath(args[0]);Directory.CreateDirectory(dir);var zdroj=Tvary();
        Test("Dolni pocatek zachovava puvodni geometrii",()=>Stejne(zdroj,SouradniceStroje.Preved(zdroj,600,false)));
        Test("Horní pocatek: Y=H-Y, X beze zmeny, bulge opacne",()=>{var r=SouradniceStroje.Preved(zdroj,600,true);Blizko(r[0].Pts[0].Y,560);Blizko(r[0].Pts[0].X,40);Blizko(r[0].B[2],-zdroj[0].B[2]);Blizko(Geo.Plocha(r[0]),-Geo.Plocha(zdroj[0]));Blizko(Geo.Plocha(r[1]),-Geo.Plocha(zdroj[1]));});
        Test("Dvoji prevod je identita a vstup se nemeni",()=>{Stejne(zdroj,SouradniceStroje.Preved(SouradniceStroje.Preved(zdroj,600,true),600,true));Stejne(zdroj,Tvary());});
        Test("Oblouky zachovaji radius, uhel a odrazene stredy",()=>{var r=SouradniceStroje.Preved(zdroj,600,true);for(int i=0;i<zdroj.Count;i++)for(int j=0;j<zdroj[i].PocetSegmentu;j++)if(zdroj[i].B[j]!=0){var a=Geo.Oblouk(zdroj[i].Pts[j],zdroj[i].KonecSegmentu(j),zdroj[i].B[j]);var b=Geo.Oblouk(r[i].Pts[j],r[i].KonecSegmentu(j),r[i].B[j]);Blizko(a.R,b.R);Blizko(a.Cx,b.Cx);Blizko(600-a.Cy,b.Cy);Blizko(a.Sw,-b.Sw);for(int k=0;k<=10;k++){double t=k/10.0;var pa=new P(a.Cx+a.R*Math.Cos(a.A0+t*a.Sw),a.Cy+a.R*Math.Sin(a.A0+t*a.Sw));var pb=new P(b.Cx+b.R*Math.Cos(b.A0+t*b.Sw),b.Cy+b.R*Math.Sin(b.A0+t*b.Sw));Blizko(pa.X,pb.X);Blizko(600-pa.Y,pb.Y);}}});
        Test("DXF obou soustav obsahuje spravne skutecne oblouky",()=>{foreach(bool top in new[]{false,true}){var r=SouradniceStroje.Preved(zdroj,600,top);string p=Path.Combine(dir,top?"stroj-Y-nahore.dxf":"stroj-Y-dole.dxf");Dxf.Zapis(p,r,new Dictionary<string,int>(),false);var ov=Dxf.Over(p,r);Over(ov.Ok&&ov.Oblouku==5&&ov.Zavrenych==2,"Kontrola DXF");Stejne(zdroj,SouradniceStroje.Preved(Dxf.Nacti(p).Prvky,600,top));Over(File.ReadAllText(p).Contains("AC1009"),"Neni R12");}});
        Test("DXF bez oblouku a ruzne vysky desky",()=>{foreach(double h in new[]{300.0,600,1220}){var r=SouradniceStroje.Preved(zdroj,h,true);Blizko(r[0].Pts[0].Y,h-40);string p=Path.Combine(dir,"stroj-usecky.dxf");Dxf.Zapis(p,r,new Dictionary<string,int>(),true);Over(Dxf.Nacti(p).Prvky.All(o=>o.B.All(b=>b==0)),"Nenulovy bulge");}});
        Test("Oprava horniho DXF vraci stejnou fyzickou geometrii",()=>{var v=Dxf.Oprav(Path.Combine(dir,"stroj-Y-nahore.dxf"),0.01,0.02,25,600,true);Over(v.Obrysy.Count==3,"Pocet");foreach(var o in zdroj)foreach(var p in Geo.RozlozObrys(o,0.05))Over(v.Obrysy.Min(x=>Geo.VzdalenostKObrysu(p,x))<1e-5,"Zmena tvaru");Over(v.Obrysy.Count(x=>!x.Zavreny)==1,"Uzavrel otevreny oblouk");});
        Test("Editor zobrazuje souradnice stroje bez pohybu bodu",()=>{using(var e=Ed()){var pred=Editor.Kopie(e.Obrysy);var v=(HashSet<long>)typeof(Editor).GetField("vyber",F).GetValue(e);v.Add(0);e.YNahore=false;Blizko(e.SouradniceBodu(e.Obrysy[0].Pts[0]).Y,40);Snimek(e,"editor-Y-dole.png");e.YNahore=true;Blizko(e.SouradniceBodu(e.Obrysy[0].Pts[0]).Y,560);Stejne(pred,e.Obrysy);Over(!e.Upraveno&&e.PosunZamcen,"Zmena dat nebo zamku");Snimek(e,"editor-Y-nahore.png");}});
        Test("Do rohu pouzije horni okraj a lze vratit zpet",()=>{using(var e=Ed()){var pred=Editor.Kopie(e.Obrysy);e.YNahore=true;e.DoRohu(12);double a,b,c,d;e.Rozsah(out a,out b,out c,out d);Blizko(a,12);Blizko(600-d,12);Over(!e.Presahuje,"Presah");e.Zpet();Stejne(pred,e.Obrysy);e.YNahore=false;e.DoRohu(12);e.Rozsah(out a,out b,out c,out d);Blizko(a,12);Blizko(b,12);}});
        Test("Umisteni obrazku od horniho i spodniho okraje",()=>{foreach(bool top in new[]{false,true})foreach(int poc in new[]{0,1}){var o=new Obrazek(Path.Combine(dir,"ctverec_ostry.png"));o.Rozpoznej(new NastaveniRozpoznani());var v=o.VytvorObrysy(new NastaveniObrysu{SirkaMm=100,PosunX=30,PosunY=20,Pocatek=poc,YNahore=top,VyskaDeskyMm=600});var r=SouradniceStroje.Preved(o.Vrstvy[0].Obrysy,600,top);double a,b,c,d;Geo.Rozsah(r,out a,out b,out c,out d);Blizko(poc==0?b:(b+d)/2,20);Blizko(poc==0?a:(a+c)/2,30);o.Nahled.Dispose();}});
        Test("Spolecna volba vsech tri karet a import horniho DXF",()=>{using(var f=new HlavniOkno(null)){var cbs=(List<ComboBox>)typeof(HlavniOkno).GetField("volbyStroje",F).GetValue(f);Over(cbs.Count==3&&cbs.All(c=>c.SelectedIndex==0),"Vychozi kompatibilita");cbs[2].SelectedIndex=1;Over(cbs.All(c=>c.SelectedIndex==1),"Synchronizace");var e=(Editor)typeof(HlavniOkno).GetField("editor",F).GetValue(f);Over(e.YNahore&&e.PosunZamcen,"Stav editoru");typeof(HlavniOkno).GetMethod("OtevritDoEditoru",F).Invoke(f,new object[]{Path.Combine(dir,"stroj-Y-nahore.dxf")});Stejne(zdroj,e.Obrysy);var vystup=(List<Obrys>)typeof(HlavniOkno).GetMethod("ObrysyProStroj",F).Invoke(f,new object[]{e.Obrysy});Stejne(SouradniceStroje.Preved(zdroj,600,true),vystup);var nast=(Dictionary<string,Control>)typeof(HlavniOkno).GetField("ulozitelne",F).GetValue(f);Over(nast.ContainsKey("strojY")&&nast["strojY"]==cbs[0],"Ukladani nove volby");cbs[0].SelectedIndex=0;Over(!e.YNahore&&cbs.All(c=>c.SelectedIndex==0),"Zpet na dolni");Stejne(zdroj,e.Obrysy);}});
        Console.WriteLine("SOUHRN: "+ok+" proslo, "+chyby+" selhalo");return chyby==0?0:1;
    }
}
