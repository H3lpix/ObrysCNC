using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using ObrysCNC;

static class TestRadiusVyberu
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static int pocet;
    static void Over(bool b) { if (!b) throw new Exception("Kontrola radiusu selhala: " + pocet); pocet++; }
    static string Vyber(Editor e, params long[] ids)
    {
        var v = (HashSet<long>)typeof(Editor).GetField("vyber", F).GetValue(e);
        v.Clear(); foreach (long id in ids) v.Add(id);
        return (string)typeof(Editor).GetMethod("InformaceSpojeVyberu", F).Invoke(e, null);
    }
    [STAThread]
    static void Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");
        var o = new Obrys { Zavreny = true };
        o.Pts.AddRange(new[] { new P(40,40), new P(50,50), new P(70,50), new P(70,40) });
        o.B.AddRange(new[] { Math.Tan(Math.PI/8), 0, 0, -0.5 });
        using (var e = new Editor { Size = new Size(1000,600), Deska = new SizeF(120,80) })
        {
            e.NastavObrysy(Editor.Kopie(new List<Obrys> { o, o }), null);
            Over(Vyber(e,0,1).Contains("10,0000 mm"));
            Over(Vyber(e,1,0).Contains("10,0000 mm"));
            Over(Vyber(e,1,2).Contains("přímka"));
            Over(Vyber(e,0,2).Contains("nesousedí"));
            Over(Vyber(e,0,1L<<32).Contains("různých"));
            Over(Vyber(e,0)==null && Vyber(e,0,1,2)==null);
            Over(Vyber(e,0,3).Contains("18,7500 mm"));
            e.YNahore=true; Over(Vyber(e,0,1).Contains("10,0000 mm"));
            e.YNahore=false;
            using (var b=new Bitmap(e.Width,e.Height)) { e.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height)); b.Save(args[0]); }
            Over(e.PosunZamcen && !e.Upraveno && e.Obrysy[0].Pts[0].X==40 && e.Obrysy[0].B[0]==o.B[0]);
            e.Obrysy[0].Zavreny=false; Over(Vyber(e,0,3).Contains("nesousedí"));
            var kruh=new Obrys { Zavreny=true }; kruh.Pts.AddRange(new[]{new P(40,40),new P(60,40)}); kruh.B.AddRange(new[]{1.0,-0.5});
            e.NastavObrysy(new List<Obrys>{kruh},null);
            string s=Vyber(e,0,1); Over(s.Contains("Spoj 1") && s.Contains("Spoj 2") && s.Contains("10,0000 mm") && s.Contains("12,5000 mm"));
        }
        Console.WriteLine("SOUHRN: " + pocet + " kontrol proslo");
    }
}
