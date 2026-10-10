using System;
using System.Collections.Generic;
using System.Linq;
using ObrysCNC;

// test narezoveho planu: kuchynska skrinka 600 mm (bok, dno, strop, police, dvirka, zada) x 4
static class TestNarez
{
    static void Main()
    {
        var dily = new List<Dil>
        {
            new Dil { Nazev = "Bok", Delka = 720, Sirka = 560, Ks = 8, Otacet = false, HranaD = 1 },
            new Dil { Nazev = "Dno", Delka = 564, Sirka = 560, Ks = 4, Otacet = true, HranaD = 1 },
            new Dil { Nazev = "Strop", Delka = 564, Sirka = 100, Ks = 8, Otacet = true, HranaD = 1 },
            new Dil { Nazev = "Police", Delka = 562, Sirka = 520, Ks = 4, Otacet = true, HranaD = 1 },
            new Dil { Nazev = "Dvířka", Delka = 716, Sirka = 596, Ks = 4, Otacet = false, HranaD = 2, HranaS = 2 },
            new Dil { Nazev = "Sokl", Delka = 2400, Sirka = 100, Ks = 2, Otacet = false, HranaD = 1 },
            new Dil { Nazev = "Moc velký", Delka = 3000, Sirka = 500, Ks = 1, Otacet = true }
        };
        var n = new NastaveniNarezu();
        var t0 = DateTime.Now;
        var v = Narez.Spocitej(dily, n);
        Console.WriteLine("cas {0:0} ms, desek {1}, kusu {2}, vyuziti {3:0.0} %, hrany {4:0.0} m, nevejde: {5}",
            (DateTime.Now - t0).TotalMilliseconds, v.Desky.Count, v.Kusu, 100 * v.Vyuziti, v.HranyM, string.Join(", ", v.Nevejde));
        // kontrola: zadne prekryvy, vse uvnitr desky po orezu, pocty sedi
        int chyb = 0;
        foreach (var d in v.Desky)
        {
            var k = d.Kusy.ToList();
            foreach (var a in k)
            {
                if (a.X < n.Orez - 1e-6 || a.Y < n.Orez - 1e-6 || a.X + a.W > n.DeskaX - n.Orez + 1e-6 || a.Y + a.H > n.DeskaY - n.Orez + 1e-6) { chyb++; Console.WriteLine("mimo desku: " + a.Nazev); }
                foreach (var b in k)
                    if (!ReferenceEquals(a, b) && a.X < b.X + b.W + n.Prorez - 1e-6 && b.X < a.X + a.W + n.Prorez - 1e-6 && a.Y < b.Y + b.H + n.Prorez - 1e-6 && b.Y < a.Y + a.H + n.Prorez - 1e-6)
                    {
                        // dotyk bez proezu je chyba, jen kdyz se obdelniky opravdu prekryvaji nebo nemaji mezeru na prorez
                        bool prekryv = a.X < b.X + b.W - 1e-6 && b.X < a.X + a.W - 1e-6 && a.Y < b.Y + b.H - 1e-6 && b.Y < a.Y + a.H - 1e-6;
                        if (prekryv) { chyb++; Console.WriteLine("prekryv: " + a.Nazev + " / " + b.Nazev); }
                    }
                var dil = dily[a.CisloDilu];
                if (!dil.Otacet && a.Otoceno) { chyb++; Console.WriteLine("otocen dil s lety: " + a.Nazev); }
            }
        }
        var pocty = v.Desky.SelectMany(d => d.Kusy).GroupBy(x => x.Nazev).ToDictionary(g => g.Key, g => g.Count());
        foreach (var d in dily.Where(x => x.Nazev != "Moc velký"))
            if (!pocty.ContainsKey(d.Nazev) || pocty[d.Nazev] != d.Ks) { chyb++; Console.WriteLine("pocet nesedi: " + d.Nazev); }
        Console.WriteLine("chyb: " + chyb);
        Console.WriteLine("rezny rozmer dvirek: {0} x {1} (ocekavano 712 x 592)", Narez.RezDelka(dily[4], n), Narez.RezSirka(dily[4], n));
        foreach (var r in Narez.Postup(v.Desky[0], n.Orez)) Console.WriteLine(r);
    }
}
