using System;
using System.Collections.Generic;
using System.Linq;
using ObrysCNC;

static class TestRastr
{
    static Obrys Obdelnik(double x0, double y0, double x1, double y1)
    {
        var o = new Obrys { Zavreny = true, Vrstva = "T" };
        o.Pts.AddRange(new[] { new P(x0, y0), new P(x1, y0), new P(x1, y1), new P(x0, y1) });
        o.B.AddRange(new double[] { 0, 0, 0, 0 });
        return o;
    }

    static void Vypis(string co, List<Obrys> r, double ocek)
    {
        double s = r.Sum(o => Geo.Plocha(o));
        double a, b, c, d; Geo.Rozsah(r, out a, out b, out c, out d);
        Console.WriteLine("{0,-28} obrysu {1}, plocha {2,9:0.0} (ocekavano {3,9:0.0}, odchylka {4:0.00} %), rozsah X {5:0.00}..{6:0.00} Y {7:0.00}..{8:0.00}, segmentu {9}",
            co, r.Count, s, ocek, 100 * (s - ocek) / ocek, a, c, b, d, r.Sum(o => o.B.Count));
    }

    static void Main()
    {
        var obd = new List<Obrys> { Obdelnik(0, 0, 100, 50) };
        Vypis("odsazeni +5", Rastr.Uprav(obd, 5, 0, 1.0), 5000 + 2 * 150 * 5 + Math.PI * 25);
        Vypis("odsazeni -5", Rastr.Uprav(obd, -5, 0, 1.0), 90 * 40);
        Vypis("zaobleni r=10", Rastr.Uprav(obd, 0, 10, 1.0), 5000 - (4 - Math.PI) * 100);
        var dva = new List<Obrys> { Obdelnik(0, 0, 10, 10), Obdelnik(5, 5, 15, 15) };
        Vypis("slouceni 2 ctvercu", Rastr.Uprav(dva, 0, 0, 1.0), 175);
        var dira = new List<Obrys> { Obdelnik(0, 0, 100, 100), Obdelnik(30, 30, 70, 70) };   // dira jinak orientovana se opravi
        Vypis("ctverec s dirou +2", Rastr.Uprav(dira, 2, 0, 1.0), (104 * 104 - (4 - Math.PI) * 4) - 36 * 36);
        var dxf = Dxf.Spoj(Dxf.Nacti(@"C:\Users\tmark\Desktop\Lisplast\CNC\logo_lisplast.dxf").Prvky, 0.02);
        var s0 = DateTime.Now;
        var r = Rastr.Uprav(dxf, 3, 0, 1.0);
        Console.WriteLine("logo odsazeni +3 mm: {0} obrysu, {1:0} ms", r.Count, (DateTime.Now - s0).TotalMilliseconds);
    }
}
