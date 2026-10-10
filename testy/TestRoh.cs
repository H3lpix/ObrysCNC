using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ObrysCNC;

// test rohu s radiusem a srovnani osy na obrysech se znamym vysledkem
static class TestRoh
{
    static HashSet<long> Vyber(Editor ed) { return (HashSet<long>)typeof(Editor).GetField("vyber", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ed); }
    static long K(int o, int v) { return ((long)o << 32) | (uint)v; }

    static void Vypis(string co, Editor ed)
    {
        var o = ed.Obrysy[0];
        Console.WriteLine("{0}: bodu {1}, plocha {2:0.000}", co, o.Pts.Count, Geo.Plocha(o));
        for (int i = 0; i < o.Pts.Count; i++) Console.WriteLine("   {0,8:0.000} {1,8:0.000}  bulge {2:0.0000}", o.Pts[i].X, o.Pts[i].Y, o.B[i]);
    }

    static void Main()
    {
        var ed = new Editor();
        // ctverec 100x100, jehoz pravy horni roh je "rozsypany": zkoseni + par bodu (jako po obkresleni)
        var o = new Obrys { Zavreny = true, Vrstva = "T" };
        o.Pts.AddRange(new[] { new P(0, 0), new P(100, 0), new P(100.4, 92), new P(99, 97), new P(96, 99.5), new P(91, 100.3), new P(0, 100) });
        o.B.AddRange(new double[] { 0, 0, 0, 0, 0, 0, 0 });
        ed.NastavObrysy(new List<Obrys> { o }, null);
        var v = Vyber(ed);
        foreach (int i in new[] { 2, 3, 4, 5 }) v.Add(K(0, i));
        Console.WriteLine("chyba: " + (ed.RohSRadiusem(0, true) ?? "-"));
        Vypis("ostry pravy uhel", ed);
        ed.Zpet();
        v = Vyber(ed);
        foreach (int i in new[] { 2, 3, 4, 5 }) v.Add(K(0, i));
        Console.WriteLine("chyba: " + (ed.RohSRadiusem(10, true) ?? "-"));
        Vypis("pravy uhel + R10", ed);
        Console.WriteLine("   (ocekavana plocha ~ ctverec s jednim rohem R10: {0:0.000})", 100 * 100 - (4 - Math.PI) * 100 / 4);
        // jeden ostry vrchol primo zaoblit
        var c = new Obrys { Zavreny = true, Vrstva = "T" };
        c.Pts.AddRange(new[] { new P(0, 0), new P(50, 0), new P(50, 30), new P(0, 30) });
        c.B.AddRange(new double[] { 0, 0, 0, 0 });
        ed.NastavObrysy(new List<Obrys> { c }, null);
        v = Vyber(ed); v.Add(K(0, 2));
        Console.WriteLine("chyba: " + (ed.RohSRadiusem(5, false) ?? "-"));
        Vypis("jeden vrchol R5", ed);
        Console.WriteLine("   (ocekavano {0:0.000})", 1500 - (4 - Math.PI) * 25 / 4);
        // prilis velky radius
        ed.Zpet(); v = Vyber(ed); v.Add(K(0, 2));
        Console.WriteLine("R40: " + (ed.RohSRadiusem(40, false) ?? "-"));
        // srovnani X: bod 1 a 2 podle bodu 0
        ed.Zpet(); v = Vyber(ed); v.Clear(); v.Add(K(0, 3));
        typeof(Editor).GetMethod("ZacniSrovnani").Invoke(ed, new object[] { true });
        typeof(Editor).GetMethod("DokonciSrovnani", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ed, new object[] { new P(12.5, 999) });
        Console.WriteLine("srovnani X: bod 3 = {0:0.0}, {1:0.0} (ocekavano 12.5, 30.0)", ed.Obrysy[0].Pts[3].X, ed.Obrysy[0].Pts[3].Y);
    }
}
