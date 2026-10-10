using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ObrysCNC;

static class TestGui
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var f = new HlavniOkno(args[0]);
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-5000, -5000);       // mimo obrazovku, nekrade fokus
        f.ShowInTaskbar = false;
        var t = new Timer { Interval = 2500 };
        Application.ThreadException += (s, e) => { Console.WriteLine("VYJIMKA: " + e.Exception); Environment.Exit(3); };
        t.Tick += (s, e) =>
        {
            t.Stop();
            ComboBox cbV = null;
            if (args.Length > 2 && args[2] == "stred")
            {
                cbV = VsechnyCb(f).First(c => c.Items.Count > 0 && c.Items[0].ToString() == "Obrys tvaru");
                cbV.SelectedIndex = 1;
                for (int k = 0; k < 40; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
            }
            if (args.Length > 2 && args[2] == "editor") TestEditor(f, args[3]);
            else if (args.Length > 2 && args[2] == "kvalita")
            {
                // posunout levy panel k popisu kvality geometrie (CAD / nahradni proklad)
                var lbl = Vsechny<Label>(f).First(l => l.Text.StartsWith("Geometrie"));
                Control p = lbl.Parent;
                while (p != null && !(p is ScrollableControl && ((ScrollableControl)p).AutoScroll)) p = p.Parent;
                if (p != null) ((ScrollableControl)p).ScrollControlIntoView(lbl);
                Console.WriteLine("KVALITA: " + lbl.Text.Replace("\n", " | "));
            }
            else if (args.Length > 2 && args[2] != "stred") ((TabControl)f.Controls[0]).SelectedIndex = int.Parse(args[2]);
            Application.DoEvents();
            var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            bmp.Save(args[1]);
            Vypis(f, 0);
            if (cbV != null) { cbV.SelectedIndex = 0; Application.DoEvents(); }   // nastaveni uzivatele nechat beze zmeny
            var ed = Najdi<Editor>(f); if (ed != null) ed.Upraveno = false;
            f.Close();
        };
        f.Shown += (s, e) => t.Start();
        Application.Run(f);
    }

    static T Najdi<T>(Control c) where T : Control
    {
        if (c is T) return (T)c;
        foreach (Control d in c.Controls) { var r = Najdi<T>(d); if (r != null) return r; }
        return null;
    }

    static System.Collections.Generic.IEnumerable<T> Vsechny<T>(Control c) where T : Control
    {
        if (c is T) yield return (T)c;
        foreach (Control d in c.Controls) foreach (var x in Vsechny<T>(d)) yield return x;
    }

    static System.Collections.Generic.IEnumerable<ComboBox> VsechnyCb(Control c)
    {
        if (c is ComboBox) yield return (ComboBox)c;
        foreach (Control d in c.Controls) foreach (var x in VsechnyCb(d)) yield return x;
    }

    static void Stav(Editor ed, string co)
    {
        Console.WriteLine("{0}: obrysu {1}, bodu {2}, vybrano {3}, zpet {4}, znovu {5}, presahuje {6}", co, ed.Obrysy.Count, ed.PocetBodu, ed.PocetVybranych, ed.LzeZpet, ed.LzeZnovu, ed.Presahuje);
    }

    static void TestEditor(Form f, string dxf)
    {
        ((TabControl)f.Controls[0]).SelectedIndex = 2;
        Application.DoEvents();
        var ed = Najdi<Editor>(f);
        var data = Dxf.Nacti(dxf);
        ed.NastavObrysy(Dxf.Spoj(data.Prvky, 0.02), data.BarvyVrstev);
        Stav(ed, "nacteno");
        ed.Vystredit();
        double a, b, c, d; ed.Rozsah(out a, out b, out c, out d);
        Console.WriteLine("vystredeno: X {0:0.0}..{1:0.0}, Y {2:0.0}..{3:0.0} (deska {4})", a, c, b, d, ed.Deska);
        var vyber = (System.Collections.Generic.HashSet<long>)typeof(Editor).GetField("vyber", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ed);
        int n1 = ed.Obrysy[1].Pts.Count;
        for (int v = 0; v < 3; v++) vyber.Add(((long)1 << 32) | (uint)v);
        ed.SmazVybrane();
        Stav(ed, "smazany 3 body obrysu 1 (mel " + n1 + ", ma " + ed.Obrysy[1].Pts.Count + ")");
        ed.Zpet(); Stav(ed, "zpet (obrys 1 ma " + ed.Obrysy[1].Pts.Count + ")");
        ed.Znovu(); Stav(ed, "znovu");
        ed.Zpet();
        // smazani skoro celeho obrysu 2 -> obrys zmizi
        if (ed.Obrysy.Count > 3) { for (int v = 0; v < ed.Obrysy[2].Pts.Count - 1; v++) vyber.Add(((long)2 << 32) | (uint)v);
        ed.SmazVybrane(); Stav(ed, "obrys 2 skoro cely smazan");
        ed.Zpet(); }
        // vlozeni bodu do obrysu s obloukem
        int oi = ed.Obrysy.FindIndex(o => o.B.Any(x => x != 0));
        if (oi >= 0)
        {
            var o = ed.Obrysy[oi]; int s = o.B.FindIndex(x => x != 0);
            var ob = Geo.Oblouk(o.Pts[s], o.KonecSegmentu(s), o.B[s]);
            double plochaPred = Geo.Plocha(o);
            var stred = new P(ob.Cx + ob.R * Math.Cos(ob.A0 + ob.Sw / 2), ob.Cy + ob.R * Math.Sin(ob.A0 + ob.Sw / 2));
            typeof(Editor).GetMethod("VlozBod", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(ed, new object[] { oi, s, stred });
            Console.WriteLine("vlozen bod do oblouku: plocha pred {0:0.000}, po {1:0.000}", plochaPred, Geo.Plocha(ed.Obrysy[oi]));
        }
        // ukazka vyberu pro snimek
        vyber.Clear();
        if (ed.Obrysy.Count > 3) for (int v = 0; v < ed.Obrysy[3].Pts.Count; v++) vyber.Add(((long)3 << 32) | (uint)v);
        ed.PosunVybrane(0, 40, true);
        Stav(ed, "obrys 3 posunut o 40 mm nahoru");
        ed.Invalidate();
        Application.DoEvents();
    }

    static void Vypis(Control c, int hl)
    {
        if (hl > 7) return;
        if (c is TableLayoutPanel || c is Panel && hl < 6)
            Console.WriteLine(new string(' ', hl * 2) + c.GetType().Name + " " + c.Bounds + " deti=" + c.Controls.Count);
        if (c.Parent is TableLayoutPanel)
            Console.WriteLine(new string(' ', hl * 2) + "- " + c.GetType().Name + " '" + (c.Text.Length > 25 ? c.Text.Substring(0, 25) : c.Text).Replace("\n", " ") + "' " + c.Bounds + " margin=" + c.Margin + " max=" + c.MaximumSize);
        foreach (Control d in c.Controls) Vypis(d, hl + 1);
    }
}
