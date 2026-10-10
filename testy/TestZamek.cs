using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using ObrysCNC;

class TestZamek : Editor
{
    Point Bod(P p) { return Point.Round((PointF)typeof(Editor).GetMethod("S", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(this, new object[] { p })); }
    void Dol(Point p) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)); }
    void Pohyb(Point p) { OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, p.X, p.Y, 0)); }
    void Nahoru(Point p) { OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0)); }
    static void Over(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("OK: " + text); }
    [STAThread]
    static void Main()
    {
        using (var e = new TestZamek())
        {
            e.Size = new Size(900, 600);
            var o = new Obrys { Zavreny = true, Vrstva = "T" };
            o.Pts.AddRange(new[] { new P(30,40), new P(230,40), new P(230,140), new P(30,140) });
            o.B.AddRange(new double[4]);
            e.NastavObrysy(new List<Obrys> { o }, null);
            var p = e.Bod(o.Pts[0]); var q = new Point(p.X + 20, p.Y + 10);
            Over(e.PosunZamcen, "Novy editor ma zamknuty posun");
            e.Dol(p); e.Pohyb(q); e.Nahoru(q);
            Over(e.PocetVybranych == 1 && e.Obrysy[0].Pts[0].X == 30 && e.Obrysy[0].Pts[0].Y == 40 && !e.Upraveno, "Vyber a tazeni bodu pod zamkem nemeni geometrii");
            foreach (var k in new[] { Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Control | Keys.Right, Keys.Shift | Keys.Up }) e.OnKeyDown(new KeyEventArgs(k));
            Over(e.Obrysy[0].Pts[0].X == 30 && e.Obrysy[0].Pts[0].Y == 40 && !e.LzeZpet, "Sipky vcetne modifikatoru pod zamkem nic neposunou");
            p = e.Bod(new P(130,40)); q = new Point(p.X + 20, p.Y + 10);
            e.Dol(p); e.Pohyb(q); e.Nahoru(q);
            Over(e.PocetVybranych == 4 && e.Obrysy[0].Pts[1].X == 230 && !e.Upraveno, "Vyber hrany pod zamkem neposune cely obrys");
            e.PosunZamcen = false;
            e.OnKeyDown(new KeyEventArgs(Keys.Right));
            Over(e.Obrysy[0].Pts[0].X == 31 && e.LzeZpet, "Odemknute sipky posouvaji a ukladaji krok zpet");
            p = e.Bod(e.Obrysy[0].Pts[0]); q = new Point(p.X + 20, p.Y + 10);
            e.Dol(p); e.Pohyb(q);
            double x = e.Obrysy[0].Pts[0].X;
            Over(x != 31, "Odemknute tazeni funguje");
            e.PosunZamcen = true; e.Pohyb(new Point(q.X + 20, q.Y)); e.Nahoru(q);
            Over(e.Obrysy[0].Pts[0].X == x, "Zapnuti zamku zastavi i probihajici tazeni");
        }
    }
}
