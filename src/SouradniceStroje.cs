using System.Collections.Generic;

namespace ObrysCNC
{
    // Interni geometrie zustava kartezska (Y nahoru, nula dole).
    // Transformace do/ze stroje s Y dolu je stejna a nikdy nemeni vstup.
    public static class SouradniceStroje
    {
        public static P Bod(P p, double vyskaDesky, bool yNahore)
        {
            return yNahore ? new P(p.X, vyskaDesky - p.Y) : p;
        }

        public static List<Obrys> Preved(IEnumerable<Obrys> obrysy, double vyskaDesky, bool yNahore)
        {
            var vysledek = new List<Obrys>();
            foreach (var o in obrysy)
            {
                var n = new Obrys { Zavreny = o.Zavreny, Vrstva = o.Vrstva };
                foreach (var p in o.Pts) n.Pts.Add(Bod(p, vyskaDesky, yNahore));
                foreach (var b in o.B) n.B.Add(yNahore ? -b : b);
                vysledek.Add(n);
            }
            return vysledek;
        }
    }
}
