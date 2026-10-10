using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ObrysCNC
{
    // export do SVG v mm (oblouky jako prikaz A), pro jine programy a tisk
    public static class Svg
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Zapis(string soubor, List<Obrys> obrysy)
        {
            double minx, miny, maxx, maxy;
            Geo.Rozsah(obrysy, out minx, out miny, out maxx, out maxy);
            if (obrysy.Count == 0) { minx = miny = 0; maxx = maxy = 1; }
            double w = Math.Max(maxx - minx, 0.001), h = Math.Max(maxy - miny, 0.001);
            Func<double, string> F = v => v.ToString("0.###", Inv);
            Func<P, string> B = p => F(p.X - minx) + " " + F(maxy - p.Y);
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{0}mm\" height=\"{1}mm\" viewBox=\"0 0 {0} {1}\">\n", F(w), F(h));
            foreach (var g in obrysy.GroupBy(o => o.Vrstva))
            {
                sb.AppendFormat("<g id=\"{0}\" fill=\"none\" stroke=\"#000\" stroke-width=\"0.2\">\n", System.Security.SecurityElement.Escape(g.Key));
                foreach (var o in g)
                {
                    if (o.Pts.Count < 2) continue;
                    var d = new StringBuilder("M" + B(o.Pts[0]));
                    for (int i = 0; i < o.PocetSegmentu; i++)
                    {
                        P a = o.Pts[i], b = o.KonecSegmentu(i);
                        if (Math.Abs(o.B[i]) < 1e-12) { d.Append(" L" + B(b)); continue; }
                        var ob = Geo.Oblouk(a, b, o.B[i]);
                        // osa Y je v SVG obracena: oblouk proti smeru hodin (bulge > 0) ma sweep-flag 0
                        d.AppendFormat(" A{0} {0} 0 {1} {2} {3}", F(ob.R), Math.Abs(ob.Sw) > Math.PI ? 1 : 0, o.B[i] > 0 ? 0 : 1, B(b));
                    }
                    if (o.Zavreny) d.Append(" Z");
                    sb.AppendFormat("<path d=\"{0}\"/>\n", d);
                }
                sb.Append("</g>\n");
            }
            sb.Append("</svg>\n");
            File.WriteAllText(soubor, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
