using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ObrysCNC
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new HlavniOkno(args.Length > 0 ? args[0] : null));
        }
    }

    // polozka seznamu DXF: zobrazi nazev souboru a slozku, uchova celou cestu
    class PolozkaDxf
    {
        public readonly string Cesta;
        public PolozkaDxf(string cesta) { Cesta = cesta; }
        public override string ToString() { return Path.GetFileName(Cesta) + "   (" + Path.GetDirectoryName(Cesta) + ")"; }
    }

    public class HlavniOkno : Form
    {
        const string Verze = "1.0";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly string[] PriponyObrazku = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".jfif", ".heic", ".ico" };

        TabControl karty;
        // --- obrazek ---
        Obrazek obr;
        VysledekObrazku vys;
        string souborObrazku;
        Label lblSoubor, lblInfo, lblKvalita, lblVarovani;
        ComboBox cbRezim, cbPocatek;
        TextBox txtVrstva;
        NumericUpDown nAlfa;
        ComboBox cbProklad;
        ComboBox cbVystup;
        CheckBox chUzka;
        NumericUpDown nUhel, nMeritko, nOdsazeni, nPolomer, nTolSpoje;
        CheckBox chZachovat;
        NumericUpDown nRadiusRohu;
        CheckBox chPravyUhel;
        NumericUpDown nCitlivost, nPrah, nSirka, nVyska, nPosunX, nPosunY, nSigma, nTol, nTolMm, nNarovnat, nMinPlocha, nFreza;
        CheckBox chInvert, chOblouky, chBezOblouku, chZobrObrazek, chZobrBody;
        Nahled nahled;
        Timer casovac;
        bool nacitam;
        SplitContainer split1, split2;
        // --- oprava DXF ---
        ListBox lbDxf;
        NumericUpDown nTolOblouku, nSpoj, nRoh;
        CheckBox chBezOblouku2, chZobrBody2;
        TextBox txtVystup, txtLog;
        Nahled nahled2;
        // --- editor ---
        Editor editor;
        NumericUpDown nDeskaW1, nDeskaH1, nDeskaW3, nDeskaH3, nOkraj;
        CheckBox chBezOblouku3;
        Label lblEditor;
        Button btnZpet, btnZnovu;
        string editorZdroj;
        bool synchronizujiDesku;
        bool yNahore, synchronizujiStroj;
        readonly List<ComboBox> volbyStroje = new List<ComboBox>();
        SplitContainer split3;

        readonly Dictionary<string, Control> ulozitelne = new Dictionary<string, Control>();

        public HlavniOkno(string soubor)
        {
            SuspendLayout();
            // Automaticke skalovani WinForms je vypnute: u NumericUpDown v TableLayoutPanelu se pri
            // zvetseni nad 100 % okraje skaluji opakovane (u kazdeho dalsiho prvku) a panel narostl na
            // desitky tisic pixelu. Pisma v bodech se zvetsi sama, pevne rozmery prepocitava S()/Pad().
            AutoScaleMode = AutoScaleMode.None;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpiFaktor = g.DpiX / 96f;
            Font = new Font("Segoe UI", 9F);
            Text = "Obrys CNC " + Verze + " – obrázek → DXF pro Xilog";
            var plocha = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(S(1300), plocha.Width - 20), Math.Min(S(860), plocha.Height - 20));
            MinimumSize = new Size(Math.Min(S(900), plocha.Width - 20), Math.Min(S(600), plocha.Height - 20));
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;

            karty = new TabControl { Dock = DockStyle.Fill };
            var k1 = new TabPage("Obrázek → DXF");
            var k2 = new TabPage("Oprava DXF");
            var k3 = new TabPage("Editor DXF");
            karty.TabPages.Add(k1);
            karty.TabPages.Add(k2);
            karty.TabPages.Add(k3);
            Controls.Add(karty);
            PostavKartuObrazek(k1);
            PostavKartuOprava(k2);
            PostavKartuEditor(k3);

            casovac = new Timer { Interval = 350 };
            casovac.Tick += (s, e) => { casovac.Stop(); Prepocitej(false); };

            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => OtevritSoubory((string[])e.Data.GetData(DataFormats.FileDrop));
            Load += (s, e) =>
            {
                split1.SplitterDistance = S(380);
                split2.SplitterDistance = S(380);
                split3.SplitterDistance = S(380);
                NactiNastaveni();
                NastavDesku(nDeskaW3.Value, nDeskaH3.Value);
                if (soubor != null) OtevritSoubory(new[] { soubor });
            };
            FormClosing += (s, e) =>
            {
                if (editor.Upraveno && editor.Obrysy.Count > 0 &&
                    MessageBox.Show(this, "V editoru jsou neuložené změny. Zavřít program bez uložení?", "Obrys CNC",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                { e.Cancel = true; return; }
                UlozNastaveni();
            };
            ResumeLayout(false);
            PerformLayout();
        }

        // ================= pomocne stavebni funkce =================

        static float dpiFaktor = 1f;
        static int S(int px) { return (int)Math.Round(px * dpiFaktor); }
        static Padding Pad(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        static TableLayoutPanel Tabulka()
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Padding = Pad(6, 6, 6, 6) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            return t;
        }

        static void Nadpis(TableLayoutPanel t, string text)
        {
            var l = new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = Color.FromArgb(0, 70, 140), Margin = Pad(0, 12, 0, 3) };
            t.Controls.Add(l);
            t.SetColumnSpan(l, 2);
        }

        static void Radek(TableLayoutPanel t, string popis, Control c, ToolTip tip, string napoveda)
        {
            var l = new Label { Text = popis, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Pad(0, 6, 3, 3) };
            c.Dock = DockStyle.Fill;
            c.Margin = Pad(3, 3, 0, 3);
            t.Controls.Add(l);
            t.Controls.Add(c);
            if (tip != null && napoveda != null) { tip.SetToolTip(l, napoveda); tip.SetToolTip(c, napoveda); }
        }

        static void CelyRadek(TableLayoutPanel t, Control c)
        {
            c.Margin = Pad(0, 3, 0, 3);
            t.Controls.Add(c);
            t.SetColumnSpan(c, 2);
        }

        NumericUpDown Cislo(string klic, decimal min, decimal max, decimal hodnota, int des, decimal krok, bool prepocitat)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = des, Increment = krok, Value = hodnota, TextAlign = HorizontalAlignment.Right };
            if (prepocitat) n.ValueChanged += (s, e) => { if (!nacitam) Naplanuj(); };
            if (klic != null) ulozitelne[klic] = n;
            return n;
        }

        static decimal Omez(NumericUpDown n, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return n.Value;
            return Math.Max(n.Minimum, Math.Min(n.Maximum, (decimal)Math.Round(v, n.DecimalPlaces)));
        }

        // ================= karta Obrazek -> DXF =================

        void PostavKartuObrazek(TabPage k)
        {
            split1 = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            k.Controls.Add(split1);
            var tip = new ToolTip { AutoPopDelay = 15000 };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            split1.Panel1.Controls.Add(scroll);
            var t = Tabulka();
            scroll.Controls.Add(t);

            var btnOtevrit = new Button { Text = "Otevřít obrázek…", Height = S(34), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            btnOtevrit.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "Obrázky|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.ico|Všechny soubory|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK) OtevritObrazek(d.FileName);
            };
            CelyRadek(t, btnOtevrit);
            lblSoubor = new Label { Text = "Obrázek můžete také přetáhnout do okna.", AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(S(340), 0) };
            CelyRadek(t, lblSoubor);

            Nadpis(t, "Rozpoznání");
            cbRezim = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cbRezim.Items.AddRange(new object[] { "Vše kromě pozadí", "Tmavé na světlém", "Kresba / fotka (nerovné světlo)" });
            cbRezim.SelectedIndex = 0;
            cbRezim.SelectedIndexChanged += (s, e) => { AktualizujRezim(); if (!nacitam) Naplanuj(); };
            ulozitelne["rezim2"] = cbRezim;
            Radek(t, "Co je tvar", cbRezim, tip, "Vše kromě pozadí: pozadí se pozná podle okraje obrázku, všechno ostatní je tvar.\nTmavé na světlém: tvar je vše tmavší než práh jasu.\nKresba / fotka: porovnává bod s jeho okolím, takže zvládne i fotku kresby se stínem\nnebo nerovnoměrným světlem (jen čáry, ne velké plochy).");
            cbVystup = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cbVystup.Items.AddRange(new object[] { "Obrys tvaru", "Středová čára (gravírování)" });
            cbVystup.SelectedIndex = 0;
            cbVystup.SelectedIndexChanged += (s, e) => { if (!nacitam) Naplanuj(); };
            ulozitelne["vystup2"] = cbVystup;
            Radek(t, "Výstup", cbVystup, tip, "Obrys tvaru: dráha kolem každého tvaru (vyřezání, vybrání plochy).\nStředová čára: jedna dráha středem každé čáry kresby – pro gravírování\nfrézou o šířce čáry (místo dvou drah kolem čáry).");
            nCitlivost = Cislo("citlivost", 5, 250, 60, 0, 5, true);
            Radek(t, "Min. rozdíl od pozadí", nCitlivost, tip, "Jak moc se musí bod lišit od pozadí (u kresby od okolí), aby patřil do tvaru.\nMenší = zachytí i světlé/slabé části, větší = jen výrazné.");
            nPrah = Cislo("prah", 1, 254, 128, 0, 5, true);
            Radek(t, "Práh jasu (0–255)", nPrah, tip, "Body tmavší než tato hodnota patří do tvaru.");
            chInvert = new CheckBox { Text = "Invertovat (světlé = tvar)", AutoSize = true };
            chInvert.CheckedChanged += (s, e) => { if (!nacitam) Naplanuj(); };
            ulozitelne["invert"] = chInvert;
            CelyRadek(t, chInvert);
            txtVrstva = new TextBox { Text = "OBRYS", CharacterCasing = CharacterCasing.Upper };
            txtVrstva.TextChanged += (s, e) => { if (!nacitam) Naplanuj(); };
            ulozitelne["vrstva"] = txtVrstva;
            Radek(t, "Název vrstvy v DXF", txtVrstva, tip, "Podle názvu vrstvy může Xilog při importu přiřadit nástroj a hloubku.");

            Nadpis(t, "Velikost a umístění");
            nSirka = Cislo("sirka", 1, 10000, 500, 1, 10, true);
            Radek(t, "Šířka obrysu X [mm]", nSirka, tip, "Celková šířka výsledného obrysu ve směru X.");
            nVyska = Cislo(null, 1, 10000, 100, 1, 10, false);
            nVyska.ValueChanged += (s, e) =>
            {
                if (nacitam || vys == null || vys.SirkaMm <= 0 || vys.VyskaMm <= 0) return;
                nacitam = true;
                nSirka.Value = Omez(nSirka, (double)nVyska.Value * vys.SirkaMm / vys.VyskaMm);
                nacitam = false;
                Naplanuj();
            };
            Radek(t, "Výška obrysu Y [mm]", nVyska, tip, "Výška se dopočítá ze šířky (poměr stran zůstává).\nKdyž ji zadáte, upraví se šířka.");
            cbPocatek = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cbPocatek.Items.AddRange(new object[] { "Levý dolní roh", "Střed" });
            cbPocatek.SelectedIndex = 0;
            cbPocatek.SelectedIndexChanged += (s, e) => { if (!nacitam) Naplanuj(); };
            ulozitelne["pocatek"] = cbPocatek;
            Radek(t, "Umístění obrázku", cbPocatek, tip, "Roh: obrázek se umístí od zvolené strany desky s posuny X/Y.\nStřed: střed obrázku má zadané souřadnice X/Y. Souřadnicovou soustavu určuje volba Souřadnice stroje.");
            nPosunX = Cislo("posunx", -10000, 10000, 0, 1, 10, true);
            Radek(t, "Posun X [mm]", nPosunX, tip, "Posune celý obrys, např. na místo na desce.");
            nPosunY = Cislo("posuny", -10000, 10000, 0, 1, 10, true);
            Radek(t, "Posun Y [mm]", nPosunY, tip, "Vzdálenost od spodní strany při Y=0 dole, od horní strany při Y=0 nahoře.\nKladné hodnoty míří dovnitř desky.");

            Nadpis(t, "Deska");
            nDeskaW1 = Cislo(null, 10, 10000, 1000, 0, 10, false);
            nDeskaW1.ValueChanged += (s, e) => NastavDesku(nDeskaW1.Value, nDeskaH1.Value);
            Radek(t, "Šířka desky X [mm]", nDeskaW1, tip, "Rozměr desky, na kterou se bude frézovat (zobrazí se v náhledu i v editoru).");
            nDeskaH1 = Cislo(null, 10, 10000, 600, 0, 10, false);
            nDeskaH1.ValueChanged += (s, e) => NastavDesku(nDeskaW1.Value, nDeskaH1.Value);
            Radek(t, "Výška desky Y [mm]", nDeskaH1, tip, "Rozměr desky, na kterou se bude frézovat (zobrazí se v náhledu i v editoru).");
            VolbaStroje(t, tip);
            var btnStred1 = new Button { Text = "Vystředit obrys na desce", AutoSize = true };
            btnStred1.Click += (s, e) =>
            {
                if (vys == null || vys.Chyba != null) return;
                nacitam = true;
                cbPocatek.SelectedIndex = 0;
                nPosunX.Value = Omez(nPosunX, ((double)nDeskaW1.Value - vys.SirkaMm) / 2);
                nPosunY.Value = Omez(nPosunY, ((double)nDeskaH1.Value - vys.VyskaMm) / 2);
                nacitam = false;
                Prepocitej(false);
            };
            tip.SetToolTip(btnStred1, "Umístí obrys doprostřed desky v obou souřadnicových soustavách.");
            CelyRadek(t, btnStred1);

            Nadpis(t, "Kvalita obrysu");
            nSigma = Cislo("sigma", 0, 4, 0.8m, 1, 0.1m, true);
            Radek(t, "Vyhlazení [px]", nSigma, tip, "Vyhladí zubaté hrany obrázku. Větší = hladší, ale zaoblí ostré rohy.");
            nTol = Cislo("tol", 0.1m, 5, 0.6m, 2, 0.1m, true);
            Radek(t, "Zjednodušení [px]", nTol, tip, "Jak moc se smí obrys odchýlit od obrázku (v pixelech obrázku).\nMenší = přesnější kopie (víc oblouků), větší = méně bodů a rovnější hrany.\nU věrného prokladu mají oblouky přesnost 0,4 × tato hodnota.");
            nTolMm = Cislo("tolmm", 0, 50, 0, 2, 0.05m, true);
            Radek(t, "Max. odchylka [mm] (0 = auto)", nTolMm, tip, "Největší povolená odchylka geometrie od obrázku v milimetrech na desce.\nPrávě tolik smí optimalizace přímek a oblouků odbočit od obrázku – a nic přes to.\n0 = automaticky 1,5 × Zjednodušení (v px), přepočteno na mm podle šířky obrysu.\nMenší hodnota = věrnější tvar a víc prvků, větší = méně prvků (např. kruh 2 půlkruhy).\nPřesnost nejde zvýšit pod rozlišení obrázku (asi 0,3 px) – program pak upozorní.");
            cbProklad = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cbProklad.Items.AddRange(new object[] { "Věrný (podle obrázku)", "Hladký (Potrace)" });
            cbProklad.SelectedIndex = 0;
            cbProklad.SelectedIndexChanged += (s, e) => { if (!nacitam) Naplanuj(); };
            ulozitelne["proklad"] = cbProklad;
            Radek(t, "Způsob prokladu", cbProklad, tip, "Věrný: křivky se prokládají přímo na hranu obrázku (s přesností na zlomky pixelu),\nzaoblení kopírují obrázek. Doporučeno.\nHladký: jako Potrace/Inkscape – velmi hladké křivky, méně přesné na zaobleních.");
            nAlfa = Cislo("alfa", 0, 1.34m, 1.0m, 2, 0.05m, true);
            Radek(t, "Zaoblení rohů (0–1,33)", nAlfa, tip, "Které zlomy zůstanou ostrými rohy a které se zaoblí (jako „alphamax“ v Potrace/Inkscape).\n0 = všechny rohy ostré, 1 = doporučeno, 1,33 = vše zaoblené.\nZaoblení navazuje na rovné hrany plynule, bez zlomu.");
            nNarovnat = Cislo("narovnat", 0, 10, 3, 1, 0.5m, true);
            Radek(t, "Narovnat hrany do [°]", nNarovnat, tip, "Skoro vodorovné/svislé hrany srovná přesně do vodorovna/svislice. 0 = vypnuto.");
            nMinPlocha = Cislo("minplocha", 0, 10000, 1, 1, 1, true);
            Radek(t, "Vynechat menší než [mm²]", nMinPlocha, tip, "Drobné skvrnky (nečistoty v obrázku) menší než tato plocha se vynechají.");
            chOblouky = new CheckBox { Text = "Křivky jako oblouky (plynulejší frézování)", AutoSize = true, Checked = true };
            chOblouky.CheckedChanged += (s, e) => { if (!nacitam) Naplanuj(); };
            ulozitelne["oblouky"] = chOblouky;
            tip.SetToolTip(chOblouky, "Zaoblené části se uloží jako oblouky místo mnoha krátkých úseček.");
            CelyRadek(t, chOblouky);

            Nadpis(t, "Kontrola nástroje");
            nFreza = Cislo("freza", 0.1m, 50, 6, 1, 0.5m, false);
            nFreza.ValueChanged += (s, e) => { AktualizujInfo(); NastavZvyrazneni(); };
            Radek(t, "Průměr frézy [mm]", nFreza, tip, "Jen pro kontrolu: upozorní, když je fréza širší než nejtenčí místo.");
            chUzka = new CheckBox { Text = "Zvýraznit místa užší než fréza (červeně)", AutoSize = true, Checked = true };
            chUzka.CheckedChanged += (s, e) => NastavZvyrazneni();
            ulozitelne["uzka"] = chUzka;
            tip.SetToolTip(chUzka, "V náhledu se červeně označí tahy a mezery, kam se fréza nevejde.");
            CelyRadek(t, chUzka);

            lblInfo = new Label { AutoSize = true, MaximumSize = new Size(S(350), 0), Margin = Pad(0, 10, 0, 0) };
            CelyRadek(t, lblInfo);
            lblKvalita = new Label { AutoSize = true, MaximumSize = new Size(S(350), 0), Margin = Pad(0, 6, 0, 4) };
            CelyRadek(t, lblKvalita);
            lblVarovani = new Label { AutoSize = true, MaximumSize = new Size(S(350), 0), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            CelyRadek(t, lblVarovani);

            chBezOblouku = new CheckBox { Text = "Uložit i verzi bez oblouků (jen úsečky)", AutoSize = true };
            ulozitelne["bezoblouku"] = chBezOblouku;
            CelyRadek(t, chBezOblouku);
            var btnUlozit = new Button { Text = "Uložit DXF…", Height = S(40), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.FromArgb(225, 240, 255) };
            btnUlozit.Click += (s, e) => UlozDxf();
            CelyRadek(t, btnUlozit);
            var btnDoEditoru1 = new Button { Text = "Upravit v editoru →", Height = S(32), Dock = DockStyle.Fill };
            btnDoEditoru1.Click += (s, e) =>
            {
                if (obr == null || vys == null || vys.Chyba != null) { MessageBox.Show(this, "Nejdřív otevřete obrázek.", "Editor"); return; }
                casovac.Stop();
                Prepocitej(false);
                var obrysy = obr.Vrstvy.Where(v => v.Pouzit).SelectMany(v => v.Obrysy).ToList();
                var barvy = obrysy.Select(o => o.Vrstva).Distinct().ToDictionary(x => x, x => 7);
                PoslatDoEditoru(obrysy, barvy, Path.Combine(Path.GetDirectoryName(souborObrazku), Path.GetFileNameWithoutExtension(souborObrazku) + ".dxf"));
            };
            CelyRadek(t, btnDoEditoru1);

            // nahled
            var horni = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Pad(4, 4, 4, 4) };
            chZobrObrazek = new CheckBox { Text = "Zobrazit obrázek", Checked = true, AutoSize = true };
            chZobrBody = new CheckBox { Text = "Zobrazit body (modré = oblouky)", AutoSize = true };
            chZobrObrazek.CheckedChanged += (s, e) => { nahled.ZobrazitObrazek = chZobrObrazek.Checked; nahled.Invalidate(); };
            chZobrBody.CheckedChanged += (s, e) => { nahled.ZobrazitBody = chZobrBody.Checked; nahled.Invalidate(); };
            btnZpet1 = SipkaTlacitko(true, ZpetNastaveni);
            btnZnovu1 = SipkaTlacitko(false, ZnovuNastaveni);
            horni.Controls.Add(btnZpet1);
            horni.Controls.Add(btnZnovu1);
            horni.Controls.Add(chZobrObrazek);
            horni.Controls.Add(chZobrBody);
            var btnCele = new Button { Text = "Zobrazit celé", AutoSize = true };
            btnCele.Click += (s, e) => nahled.Prizpusob();
            horni.Controls.Add(btnCele);
            nahled = new Nahled { Dock = DockStyle.Fill, Prazdny = "Otevřete obrázek (PNG, JPG, BMP…)\nnebo ho přetáhněte sem." };
            split1.Panel2.Controls.Add(nahled);
            split1.Panel2.Controls.Add(horni);
            AktualizujRezim();
        }

        void AktualizujRezim()
        {
            int r = cbRezim.SelectedIndex;
            nCitlivost.Enabled = r == 0 || r == 2;
            nPrah.Enabled = r == 1;
            chInvert.Enabled = r != 0;
        }

        void OtevritSoubory(string[] soubory)
        {
            var dxf = soubory.Where(f => Path.GetExtension(f).ToLowerInvariant() == ".dxf").ToList();
            var obrazky = soubory.Where(f => PriponyObrazku.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList();
            if (dxf.Count > 0 && karty.SelectedIndex == 2)
            {
                OtevritDoEditoru(dxf[0]);           // na karte Editor se DXF otevre rovnou k uprave
                dxf.Clear();
            }
            if (dxf.Count > 0)
            {
                foreach (var f in dxf) if (!lbDxf.Items.Cast<PolozkaDxf>().Any(p => string.Equals(p.Cesta, f, StringComparison.OrdinalIgnoreCase))) lbDxf.Items.Add(new PolozkaDxf(f));
                karty.SelectedIndex = 1;
                lbDxf.ClearSelected();          // u vicenasobneho vyberu by SelectedIndex jen pridal k vyberu
                lbDxf.SelectedIndex = lbDxf.Items.Count - 1;
            }
            if (obrazky.Count > 0) { karty.SelectedIndex = 0; OtevritObrazek(obrazky[0]); }
        }

        void OtevritObrazek(string soubor)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                var o = new Obrazek(soubor);
                if (obr != null && obr.Nahled != null) obr.Nahled.Dispose();
                obr = o;
                souborObrazku = soubor;
                lblSoubor.Text = Path.GetFileName(soubor) + "  (" + o.W + " × " + o.H + " px" + (o.Zmenseno ? ", zmenšeno" : "") + ")";
                lblSoubor.ForeColor = Color.Black;
                vys = null;
                // nova historie nastaveni pro novy obrazek
                nastZpet.Clear(); nastZpetPopis.Clear(); nastZnovu.Clear(); nastZnovuPopis.Clear(); nastPosledni = null;
                Prepocitej(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Obrázek se nepodařilo otevřít:\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        void Naplanuj()
        {
            if (obr == null) return;
            casovac.Stop();
            casovac.Start();
        }

        void Prepocitej(bool prizpusobit)
        {
            if (obr == null) return;
            try
            {
                Cursor = Cursors.WaitCursor;
                var nr = new NastaveniRozpoznani
                {
                    Rezim = cbRezim.SelectedIndex, Citlivost = (int)nCitlivost.Value, Prah = (int)nPrah.Value, Invertovat = chInvert.Checked
                };
                if (obr.Rozpoznej(nr)) prizpusobit = true;
                string jm = txtVrstva.Text.Trim().Replace(' ', '_');
                foreach (var vr in obr.Vrstvy) vr.Nazev = jm.Length > 0 ? jm : "OBRYS";
                var no = new NastaveniObrysu
                {
                    SirkaMm = (double)nSirka.Value, Pocatek = cbPocatek.SelectedIndex, PosunX = (double)nPosunX.Value, PosunY = (double)nPosunY.Value,
                    YNahore = yNahore, VyskaDeskyMm = (double)nDeskaH1.Value,
                    Sigma = (double)nSigma.Value, Tol = (double)nTol.Value, TolMm = (double)nTolMm.Value, Narovnat = (double)nNarovnat.Value,
                    MinPlochaMm2 = (double)nMinPlocha.Value, Oblouky = chOblouky.Checked, AlfaMax = (double)nAlfa.Value,
                    Stredova = cbVystup.SelectedIndex == 1, Verne = cbProklad.SelectedIndex == 0
                };
                vys = obr.VytvorObrysy(no);
                if (vys.Chyba == null)
                {
                    nacitam = true;
                    nVyska.Value = Omez(nVyska, vys.VyskaMm);
                    nacitam = false;
                }
                nahled.Vrstvy = obr.Vrstvy.Where(v => v.Pouzit).Select(v => new KreslenaVrstva { Barva = Color.FromArgb(0, 87, 200), Obrysy = v.Obrysy, Vyplnit = cbVystup.SelectedIndex == 0 }).ToList();
                nahled.UzkaMista = vys.UzkaMista;
                nahled.UzkaSirky = vys.UzkaSirky;
                NastavZvyrazneni();
                nahled.Obrazek = obr.Nahled;
                nahled.Deska = new SizeF((float)nDeskaW1.Value, (float)nDeskaH1.Value);
                if (vys.Chyba == null) nahled.ObrazekMm = new RectangleF((float)vys.ObrX, (float)vys.ObrY, (float)vys.ObrW, (float)vys.ObrH);
                nahled.Text2 = vys.Chyba == null ? string.Format("Obrys X {0:0.0} × Y {1:0.0} mm", vys.SirkaMm, vys.VyskaMm) : "";
                nahled.NovaData(prizpusobit);
                AktualizujInfo();
                ZaznamenejNastaveni();
            }
            catch (Exception ex)
            {
                lblVarovani.ForeColor = Color.Firebrick;
                lblVarovani.Text = "Chyba výpočtu: " + ex.Message;
            }
            finally { Cursor = Cursors.Default; nacitam = false; }
        }

        void AktualizujInfo()
        {
            lblKvalita.Text = "";
            if (obr == null || vys == null) { lblInfo.Text = ""; lblVarovani.Text = ""; return; }
            if (vys.Chyba != null)
            {
                lblInfo.Text = "";
                lblVarovani.ForeColor = Color.Firebrick;
                lblVarovani.Text = vys.Chyba;
                return;
            }
            var aktivni = obr.Vrstvy.Where(v => v.Pouzit).ToList();
            var vse = aktivni.SelectMany(v => v.Obrysy).ToList();
            int obl = vse.Sum(o => o.B.Count(b => Math.Abs(b) > 1e-12)), use = vse.Sum(o => o.B.Count(b => Math.Abs(b) <= 1e-12));
            bool stredova = cbVystup.SelectedIndex == 1;
            var txt = string.Format("Rozměr: X {0:0.0} × Y {1:0.0} mm\n{2}: {3} ({4} oblouků, {5} úseček)\n", vys.SirkaMm, vys.VyskaMm,
                stredova ? "Drah" : "Obrysů", vse.Count, obl, use);
            var tahy = aktivni.Where(v => !double.IsNaN(v.TahMm)).Select(v => v.TahMm.ToString("0.0") + " mm").ToList();
            if (stredova)
            {
                var typ = aktivni.Where(v => !double.IsNaN(v.TypickaSirkaMm)).Select(v => v.TypickaSirkaMm).ToList();
                if (typ.Count > 0) txt += "Typická šířka čáry ≈ " + typ.Average().ToString("0.0") + " mm (vhodná fréza)\n";
            }
            if (tahy.Count > 0) txt += "Nejtenčí tah ≈ " + string.Join(", ", tahy) + "\n";
            if (!double.IsNaN(vys.MezeraMm)) txt += "Nejužší mezera ≈ " + vys.MezeraMm.ToString("0.0") + " mm\n";
            txt += "Měřítko: 1 px obrázku = " + vys.MmNaPx.ToString("0.000") + " mm";
            lblInfo.Text = txt;
            if (!stredova) PopisKvalitu(vys.Diagnostika, vse.Count);

            double freza = (double)nFreza.Value;
            if (stredova)
            {
                // u stredove cary freza dela sirku cary: porovnat s typickou sirkou
                var typ = aktivni.Where(v => !double.IsNaN(v.TypickaSirkaMm)).Select(v => v.TypickaSirkaMm).ToList();
                if (typ.Count == 0) { lblVarovani.Text = ""; return; }
                double t = typ.Average();
                bool sedi = Math.Abs(freza - t) <= Math.Max(0.5, 0.35 * t);
                lblVarovani.ForeColor = sedi ? Color.FromArgb(0, 120, 60) : Color.FromArgb(190, 110, 0);
                lblVarovani.Text = sedi
                    ? string.Format("✓ Fréza Ø{0:0.0} mm odpovídá šířce čar (≈ {1:0.0} mm).", freza, t)
                    : string.Format("Fréza Ø{0:0.0} mm vyfrézuje čáry {2} než na obrázku (≈ {1:0.0} mm). Pro věrnou kresbu zvolte frézu kolem Ø{1:0.0} mm.", freza, t, freza > t ? "širší" : "užší");
                return;
            }
            var mista = aktivni.Select(v => v.TahMm).Concat(new[] { vys.MezeraMm }).Where(x => !double.IsNaN(x)).ToList();
            if (mista.Count == 0) { lblVarovani.Text = ""; return; }
            double nej = mista.Min();
            if (nej < freza)
            {
                lblVarovani.ForeColor = Color.Firebrick;
                lblVarovani.Text = string.Format("⚠ Fréza Ø{0:0.0} mm je širší než nejtenčí místo (≈ {1:0.0} mm). Zvětšete šířku nebo použijte menší frézu.", freza, nej);
            }
            else
            {
                lblVarovani.ForeColor = Color.FromArgb(0, 120, 60);
                lblVarovani.Text = string.Format("✓ Fréza Ø{0:0.0} mm projde i nejtenčím místem (≈ {1:0.0} mm).", freza, nej);
            }
        }

        // kvalita geometrie: CAD (primky + tecne oblouky), nebo nahradni aproximace - aby to bylo videt
        void PopisKvalitu(Diagnostika d, int obrysu)
        {
            if (d == null) return;
            double mm = vys.MmNaPx;
            string s;
            if (d.ObrysuCad > 0)
            {
                s = string.Format("Geometrie (interní CAD), před → po optimalizaci:\n  přímky {0} → {1}, oblouky {2} → {3}, ostré rohy {4} → {5}",
                    d.PrimekPred, d.Primek, d.ObloukuPred, d.Oblouku, d.RohuPred, d.Rohu);
                if (d.Kruznic > 0) s += string.Format("\n  celé kružnice (2 půlkruhy): {0}", d.Kruznic);
                s += string.Format("\nOdchylka od obrázku: max {0:0.00} mm, průměr {1:0.00} mm\n  (před optimalizací max {2:0.00} mm; rastr 1 px = {3:0.00} mm, šum hrany ±{4:0.00} mm)",
                    d.MaxOdchylkaPx * mm, d.PrumerOdchylkyPx * mm, d.MaxOdchylkaPredPx * mm, mm, d.SumHranyPx * mm);
                s += string.Format("\nNejvětší netečné napojení: {0:0.0000}°", d.MaxZlom);
            }
            else
                s = string.Format("Geometrie: {0} přímek, {1} oblouků, {2} ostrých rohů\nOdchylka od obrázku: max {3:0.00} px ({4:0.00} mm), průměr {5:0.00} px",
                    d.Primek, d.Oblouku, d.Rohu, d.MaxOdchylkaPx, d.MaxOdchylkaPx * mm, d.PrumerOdchylkyPx);
            Color barva;
            if (d.ObrysuHladkych > 0)
            {
                barva = Color.FromArgb(0, 70, 150);
                s += "\nHladký proklad (Potrace): plynulé křivky, nejde o CAD rekonstrukci z přímek a tečných oblouků.";
            }
            else if (d.ObrysuNahradnich > 0)
            {
                barva = Color.FromArgb(190, 90, 0);
                s += string.Format("\n⚠ {0} z {1} obrysů nešlo složit z přímek a tečných oblouků – je u nich použitá NÁHRADNÍ APROXIMACE (krátké oblouky bez tečných napojení).",
                    d.ObrysuNahradnich, d.ObrysuNahradnich + d.ObrysuCad);
                if (d.Duvody.Count > 0) s += "\nDůvod: " + string.Join("; ", d.Duvody.Take(3));
            }
            else
            {
                barva = Color.FromArgb(0, 120, 60);
                s += "\n✓ CAD geometrie: " + (obrysu == 1 ? "obrys je složen" : "všech " + obrysu + " obrysů je složeno") +
                     " z přímek a oblouků, napojení jsou tečná.";
            }
            if (d.Zlomu > 0) { s += string.Format("\n⚠ Netečná napojení (zlom 0,05–15°): {0}, největší {1:0.0}°", d.Zlomu, d.MaxZlom); barva = Color.FromArgb(190, 90, 0); }
            if (d.Kratkych > 0) s += string.Format("\nKrátké úseky (pod 1 px = {0:0.00} mm): {1}", mm, d.Kratkych);
            if (d.ObrysuCad > 0)
            {
                double tolMm = d.TolMaxPx * mm;
                string zdroj = nTolMm.Value > 0 ? "zadaná" : "auto";
                if (d.TolOmezena)
                {
                    s += string.Format("\n⚠ Požadovaná přesnost {0:0.00} mm je pod rozlišením obrázku – dosažitelné je asi {1:0.00} mm (0,3 px). Zvětšete obrázek nebo zmenšete šířku.",
                        (double)nTolMm.Value, Konstrukce.MinTolPx * mm);
                    barva = Color.FromArgb(190, 90, 0);
                }
                if (d.SplnujeToleranci) s += string.Format("\n✓ Tolerance {0:0.00} mm ({1}) je dodržena.", tolMm, zdroj);
                else
                {
                    s += string.Format("\n⚠ Tolerance {0:0.00} mm ({1}) NENÍ všude dodržena: {2} {3} se odchyluje víc (obvykle zúžené špičky a zářezy, kde je obrázek rozmazaný).",
                        tolMm, zdroj, d.PrvkuMimoTol, d.PrvkuMimoTol == 1 ? "prvek" : d.PrvkuMimoTol <= 4 ? "prvky" : "prvků");
                    barva = Color.FromArgb(190, 90, 0);
                }
                if (d.TemerKruznicePx.Count > 0)
                    s += string.Format("\nTéměř kružnice: {0} {1} (od přesné kružnice se odchyluje víc, než dovoluje tolerance). Při toleranci ≥ {2} mm by z {3} byly 2 půlkruhy.",
                        d.TemerKruznicePx.Count, d.TemerKruznicePx.Count == 1 ? "obrys" : d.TemerKruznicePx.Count <= 4 ? "obrysy" : "obrysů",
                        string.Join(" / ", d.TemerKruznicePx.OrderBy(x => x).Select(x => (Math.Ceiling(x * mm * 100) / 100).ToString("0.00"))),
                        d.TemerKruznicePx.Count == 1 ? "něj" : "nich");
            }
            if (d.Samopruseciky + d.PrusecikyObrysu > 0)
            {
                s += string.Format("\n⚠ Obrysy se kříží: {0} samoprůsečíků, {1} průsečíků mezi obrysy – zkontrolujte náhled (úzká místa).", d.Samopruseciky, d.PrusecikyObrysu);
                barva = Color.FromArgb(190, 90, 0);
            }
            if (!chOblouky.Checked)
                s += string.Format("\nDo DXF jdou APROXIMOVANÉ ÚSEČKY ({0} úseček místo oblouků) – volba „Křivky jako oblouky“ je vypnutá.", d.UsecekAprox);
            lblKvalita.ForeColor = barva;
            lblKvalita.Text = s;
        }

        void NastavZvyrazneni()
        {
            if (nahled == null) return;
            nahled.Freza = chUzka.Checked ? (double)nFreza.Value : 0;
            nahled.Invalidate();
        }

        void UlozDxf()
        {
            if (obr == null || vys == null || vys.Chyba != null)
            {
                MessageBox.Show(this, "Nejdřív otevřete obrázek a zkontrolujte náhled.", "Uložit DXF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            casovac.Stop();
            Prepocitej(false);
            var aktivni = obr.Vrstvy.Where(v => v.Pouzit && v.Obrysy.Count > 0).ToList();
            if (aktivni.Count == 0) { MessageBox.Show(this, "Žádná vrstva s obrysy.", "Uložit DXF"); return; }
            using (var d = new SaveFileDialog
            {
                Filter = "DXF výkres|*.dxf|SVG obrázek|*.svg",
                FileName = Path.GetFileNameWithoutExtension(souborObrazku) + ".dxf",
                InitialDirectory = Path.GetDirectoryName(souborObrazku)
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var obrysy = aktivni.SelectMany(v => v.Obrysy).ToList();
                    var barvy = aktivni.GroupBy(v => v.Nazev).ToDictionary(gr => gr.Key, gr => Dxf.Aci(gr.First().Barva));
                    if (Path.GetExtension(d.FileName).ToLowerInvariant() == ".svg")
                    {
                        Svg.Zapis(d.FileName, obrysy);
                        MessageBox.Show(this, "Uloženo jako SVG:\n" + d.FileName, "Uložit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Dxf.Zapis(d.FileName, ObrysyProStroj(obrysy), barvy, false);
                    string zprava = "Uloženo:\n" + d.FileName + (yNahore ? "\nStroj: Y=0 nahoře, Y dolů" : "\nStroj: Y=0 dole, Y nahoru");
                    // kontrola ulozeneho souboru: co v DXF opravdu je (ne jen to, co je videt v nahledu)
                    var ov = Dxf.Over(d.FileName, ObrysyProStroj(obrysy));
                    zprava += string.Format("\n\nKontrola uloženého DXF (znovu načteno):\n  {0} uzavřených obrysů, {1} oblouků (bulge), {2} úseček\n  souřadnice ±{3:0.0000} mm, poloměry ±{4:0.0000} mm, největší netečnost po zaokrouhlení {5:0.000}°\n  {6}",
                        ov.Zavrenych, ov.Oblouku, ov.Usecek, ov.MaxRozdilBodu, ov.MaxRozdilPolomeru, ov.MaxNetecnost,
                        ov.Ok ? "✓ soubor obsahuje přesně tutéž geometrii jako náhled" : "⚠ SOUBOR SE LIŠÍ OD NÁHLEDU");
                    if (!chOblouky.Checked) zprava += "\n  (oblouky jsou aproximované úsečkami – volba „Křivky jako oblouky“ je vypnutá)";
                    var dg = vys.Diagnostika;
                    if (dg != null && dg.ObrysuNahradnich > 0)
                        zprava += string.Format("\n\n⚠ {0} obrysů je jen NÁHRADNÍ APROXIMACE (nejde o čisté přímky a tečné oblouky).", dg.ObrysuNahradnich);
                    if (chBezOblouku.Checked)
                    {
                        string f2 = Path.Combine(Path.GetDirectoryName(d.FileName), Path.GetFileNameWithoutExtension(d.FileName) + "_bez_oblouku.dxf");
                        Dxf.Zapis(f2, ObrysyProStroj(obrysy), barvy, true);
                        zprava += "\n" + f2;
                    }
                    zprava += string.Format("\n\nRozměr X {0:0.0} × Y {1:0.0} mm, {2} obrysů ve vrstvách: {3}", vys.SirkaMm, vys.VyskaMm, obrysy.Count, string.Join(", ", barvy.Keys));
                    MessageBox.Show(this, zprava, "Uložit DXF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Uložení se nepodařilo:\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ================= karta Oprava DXF =================

        void PostavKartuOprava(TabPage k)
        {
            split2 = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            k.Controls.Add(split2);
            var tip = new ToolTip { AutoPopDelay = 15000 };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            split2.Panel1.Controls.Add(scroll);
            var t = Tabulka();
            scroll.Controls.Add(t);
            VolbaStroje(t, tip);
            CelyRadek(t, new Label { Text = "Horní počátek používá výšku desky z karty Obrázek / Editor. Vstupní DXF se čte ve zvolených souřadnicích stroje.", AutoSize = true, MaximumSize = new Size(S(350), 0), ForeColor = Color.DimGray });

            var popis = new Label
            {
                AutoSize = true, MaximumSize = new Size(S(350), 0), ForeColor = Color.DimGray,
                Text = "Spojí rozpadlé obrysy do uzavřených, sjednotí směr, start dá do rohu a krátké úsečky nahradí oblouky. Originály se nemění."
            };
            CelyRadek(t, popis);
            var btnPridat = new Button { Text = "Přidat DXF soubory…", Height = S(34), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            btnPridat.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "DXF výkres|*.dxf", Multiselect = true })
                    if (d.ShowDialog(this) == DialogResult.OK) OtevritSoubory(d.FileNames);
            };
            CelyRadek(t, btnPridat);
            lbDxf = new ListBox { Height = S(150), Anchor = AnchorStyles.Left | AnchorStyles.Right, HorizontalScrollbar = true, SelectionMode = SelectionMode.MultiExtended };
            lbDxf.SelectedIndexChanged += (s, e) => ZobrazDxf();
            CelyRadek(t, lbDxf);
            var btnOdebrat = new Button { Text = "Odebrat vybrané", AutoSize = true };
            btnOdebrat.Click += (s, e) =>
            {
                foreach (var it in lbDxf.SelectedItems.Cast<object>().ToList()) lbDxf.Items.Remove(it);
                ZobrazDxf();
            };
            CelyRadek(t, btnOdebrat);

            Nadpis(t, "Nastavení");
            nTolOblouku = Cislo("tolOblouku", 0.001m, 1, 0.03m, 3, 0.01m, false);
            nTolOblouku.ValueChanged += (s, e) => ZobrazDxf();
            Radek(t, "Tolerance oblouků [mm]", nTolOblouku, tip, "Max. odchylka při nahrazení úseček oblouky.");
            nSpoj = Cislo("spoj", 0.001m, 5, 0.02m, 3, 0.01m, false);
            nSpoj.ValueChanged += (s, e) => ZobrazDxf();
            Radek(t, "Spojit mezery do [mm]", nSpoj, tip, "Konce kousků bližší než tato vzdálenost se spojí.");
            nRoh = Cislo("roh", 5, 90, 25, 0, 5, false);
            nRoh.ValueChanged += (s, e) => ZobrazDxf();
            Radek(t, "Roh od [°]", nRoh, tip, "Zlom větší než tento úhel se bere jako roh (zůstane ostrý).");
            chBezOblouku2 = new CheckBox { Text = "Uložit i verzi bez oblouků (podsložka)", AutoSize = true };
            ulozitelne["bezoblouku2"] = chBezOblouku2;
            CelyRadek(t, chBezOblouku2);
            CelyRadek(t, new Label { Text = "Výstupní složka (prázdné = podsložka „upravene“ vedle souboru):", AutoSize = true, MaximumSize = new Size(S(350), 0) });
            txtVystup = new TextBox { Dock = DockStyle.Fill };
            ulozitelne["vystup"] = txtVystup;
            var btnSlozka = new Button { Text = "Vybrat složku…", AutoSize = true };
            btnSlozka.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog()) if (d.ShowDialog(this) == DialogResult.OK) txtVystup.Text = d.SelectedPath;
            };
            CelyRadek(t, txtVystup);
            CelyRadek(t, btnSlozka);
            var btnOpravit = new Button { Text = "Opravit a uložit vše", Height = S(40), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.FromArgb(225, 240, 255) };
            btnOpravit.Click += (s, e) => OpravVse();
            CelyRadek(t, btnOpravit);
            var btnDoEditoru2 = new Button { Text = "Upravit vybraný v editoru →", Height = S(32), Dock = DockStyle.Fill };
            btnDoEditoru2.Click += (s, e) =>
            {
                var pol = lbDxf.SelectedItem as PolozkaDxf;
                if (pol == null) { MessageBox.Show(this, "Vyberte soubor v seznamu.", "Editor"); return; }
                try
                {
                    var v = Dxf.Oprav(pol.Cesta, (double)nTolOblouku.Value, (double)nSpoj.Value, (double)nRoh.Value, (double)nDeskaH3.Value, yNahore);
                    PoslatDoEditoru(v.Obrysy, v.Puvodni.BarvyVrstev, pol.Cesta);
                }
                catch (Exception ex) { MessageBox.Show(this, "Soubor nejde přečíst:\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            CelyRadek(t, btnDoEditoru2);
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = S(220), Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = new Font("Consolas", 8.5F) };
            CelyRadek(t, txtLog);

            var horni = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Pad(4, 4, 4, 4) };
            chZobrBody2 = new CheckBox { Text = "Zobrazit body (modré = oblouky)", AutoSize = true };
            chZobrBody2.CheckedChanged += (s, e) => { nahled2.ZobrazitBody = chZobrBody2.Checked; nahled2.Invalidate(); };
            horni.Controls.Add(chZobrBody2);
            horni.Controls.Add(new Label { Text = "Modře: opravený obrys · červeně čárkovaně: původní kousky · zelená šipka: start a směr", AutoSize = true, Margin = Pad(12, 6, 0, 0), ForeColor = Color.DimGray });
            nahled2 = new Nahled { Dock = DockStyle.Fill, Prazdny = "Přidejte DXF soubory (nebo je přetáhněte do okna)\na vyberte jeden v seznamu pro náhled." };
            split2.Panel2.Controls.Add(nahled2);
            split2.Panel2.Controls.Add(horni);
        }

        void ZobrazDxf()
        {
            if (nahled2 == null) return;
            var pol = lbDxf.SelectedItem as PolozkaDxf;
            string f = pol == null ? null : pol.Cesta;
            if (f == null) { nahled2.Vrstvy = new List<KreslenaVrstva>(); nahled2.Text2 = ""; nahled2.Invalidate(); return; }
            try
            {
                Cursor = Cursors.WaitCursor;
                var v = Dxf.Oprav(f, (double)nTolOblouku.Value, (double)nSpoj.Value, (double)nRoh.Value, (double)nDeskaH3.Value, yNahore);
                nahled2.Vrstvy = new List<KreslenaVrstva>
                {
                    new KreslenaVrstva { Barva = Color.FromArgb(0, 87, 255), Obrysy = v.Obrysy },
                    new KreslenaVrstva { Barva = Color.FromArgb(227, 6, 19), Obrysy = v.Puvodni.Prvky, Vyplnit = false, Prerusovane = true }
                };
                nahled2.Text2 = string.Join("  ·  ", v.Souhrn.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(r => r.Contains("nově") || r.Contains("rozměr") || r.Contains("POZOR")).Select(r => r.Trim()));
                nahled2.NovaData(true);
            }
            catch (Exception ex)
            {
                nahled2.Vrstvy = new List<KreslenaVrstva>();
                nahled2.Text2 = "Soubor nejde přečíst: " + ex.Message;
                nahled2.Invalidate();
            }
            finally { Cursor = Cursors.Default; }
        }

        void OpravVse()
        {
            if (lbDxf.Items.Count == 0) { MessageBox.Show(this, "Nejdřív přidejte DXF soubory.", "Oprava DXF"); return; }
            txtLog.Clear();
            int ok = 0;
            foreach (var f in lbDxf.Items.Cast<PolozkaDxf>().Select(p => p.Cesta).ToList())
            {
                try
                {
                    var v = Dxf.Oprav(f, (double)nTolOblouku.Value, (double)nSpoj.Value, (double)nRoh.Value, (double)nDeskaH3.Value, yNahore);
                    string slozka = txtVystup.Text.Trim().Length > 0 ? txtVystup.Text.Trim() : Path.Combine(Path.GetDirectoryName(f), "upravene");
                    Directory.CreateDirectory(slozka);
                    string jmeno = Path.GetFileName(f);
                    if (string.Equals(Path.GetFullPath(slozka).TrimEnd('\\'), Path.GetDirectoryName(Path.GetFullPath(f)).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        jmeno = Path.GetFileNameWithoutExtension(f) + "_upraveno.dxf";   // nikdy neprepsat original
                    string cil = Path.Combine(slozka, jmeno);
                    Dxf.Zapis(cil, ObrysyProStroj(v.Obrysy), v.Puvodni.BarvyVrstev, false);
                    txtLog.AppendText(v.Souhrn + "  uloženo: " + cil + "\r\n");
                    var kontrola = Dxf.Over(cil, ObrysyProStroj(v.Obrysy));
                    txtLog.AppendText("  souřadnice stroje: " + (yNahore ? "Y=0 nahoře, Y dolů" : "Y=0 dole, Y nahoru") + "; kontrola DXF: " + (kontrola.Ok ? "geometrie ověřena" : "POZOR: geometrie se liší od náhledu") + "\r\n");
                    if (chBezOblouku2.Checked)
                    {
                        string s2 = Path.Combine(slozka, "bez_oblouku");
                        Directory.CreateDirectory(s2);
                        Dxf.Zapis(Path.Combine(s2, jmeno), ObrysyProStroj(v.Obrysy), v.Puvodni.BarvyVrstev, true);
                        txtLog.AppendText("  + verze bez oblouků: " + Path.Combine(s2, jmeno) + "\r\n");
                    }
                    txtLog.AppendText("\r\n");
                    ok++;
                }
                catch (Exception ex)
                {
                    txtLog.AppendText(Path.GetFileName(f) + "\r\n  CHYBA: " + ex.Message + "\r\n\r\n");
                }
            }
            txtLog.AppendText(string.Format("Hotovo: {0} z {1} souborů.\r\n", ok, lbDxf.Items.Count));
        }

        // ================= karta Editor DXF =================

        static void DvaTlacitka(TableLayoutPanel t, Button a, Button b)
        {
            a.Dock = DockStyle.Fill; b.Dock = DockStyle.Fill;
            a.Margin = Pad(0, 3, 3, 3); b.Margin = Pad(3, 3, 0, 3);
            t.Controls.Add(a);
            t.Controls.Add(b);
        }

        Button Tlacitko(string text, ToolTip tip, string napoveda, Action akce)
        {
            var b = new Button { Text = text, Height = S(30) };
            b.Click += (s, e) => { akce(); editor.Focus(); };
            if (napoveda != null) tip.SetToolTip(b, napoveda);
            return b;
        }

        void PostavKartuEditor(TabPage k)
        {
            split3 = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            k.Controls.Add(split3);
            var tip = new ToolTip { AutoPopDelay = 15000 };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            split3.Panel1.Controls.Add(scroll);
            var t = Tabulka();
            scroll.Controls.Add(t);
            editor = new Editor { Dock = DockStyle.Fill };
            editor.Zmena += (s, e) => AktualizujEditorInfo();

            var btnOtevrit = new Button { Text = "Otevřít DXF…", Height = S(34), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            btnOtevrit.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "DXF výkres|*.dxf" })
                    if (d.ShowDialog(this) == DialogResult.OK) OtevritDoEditoru(d.FileName);
            };
            CelyRadek(t, btnOtevrit);
            CelyRadek(t, new Label
            {
                Text = "Nebo pošlete obrys tlačítkem „Upravit v editoru →“ z karty Obrázek → DXF nebo Oprava DXF.",
                AutoSize = true, MaximumSize = new Size(S(350), 0), ForeColor = Color.DimGray
            });

            Nadpis(t, "Deska");
            nDeskaW3 = Cislo("deskaw", 10, 10000, 1000, 0, 10, false);
            nDeskaW3.ValueChanged += (s, e) => NastavDesku(nDeskaW3.Value, nDeskaH3.Value);
            Radek(t, "Šířka desky X [mm]", nDeskaW3, tip, "Rozměr desky. X=0 je vlevo; stranu počátku Y určuje volba Souřadnice stroje.");
            nDeskaH3 = Cislo("deskah", 10, 10000, 600, 0, 10, false);
            nDeskaH3.ValueChanged += (s, e) => NastavDesku(nDeskaW3.Value, nDeskaH3.Value);
            Radek(t, "Výška desky Y [mm]", nDeskaH3, tip, "Skutečná výška desky. Pro horní počátek se použije při převodu Y = výška desky − Y dole.");
            VolbaStroje(t, tip);

            Nadpis(t, "Umístění na desce");
            nOkraj = Cislo("okraj", 0, 5000, 20, 1, 5, false);
            Radek(t, "Okraj od rohu [mm]", nOkraj, tip, "Vzdálenost od levého a zvoleného horního/spodního okraje desky pro tlačítko „Do rohu“.");
            DvaTlacitka(t,
                Tlacitko("Vystředit", tip, "Posune celý výkres doprostřed desky.", () => editor.Vystredit()),
                Tlacitko("Do rohu", tip, "Posune výkres k levému rohu zvolenému pro stroj s nastaveným okrajem.", () => editor.DoRohu((double)nOkraj.Value)));

            Nadpis(t, "Úpravy");
            var chZamekPosunu = new CheckBox { Text = "Zamknout posun bodů (myš a šipky)", AutoSize = true, Checked = true };
            chZamekPosunu.CheckedChanged += (s, e) => { editor.PosunZamcen = chZamekPosunu.Checked; };
            tip.SetToolTip(chZamekPosunu, "Zapnuto: kliknutí pouze vybírá body, tažení ani šipky je neposunou.\nPro přesun vypněte zámek. Při novém spuštění je zámek vždy zapnutý.\nTlačítka pro záměrné úpravy (např. srovnání os nebo vystředění) zůstávají dostupná.");
            CelyRadek(t, chZamekPosunu);
            DvaTlacitka(t,
                Tlacitko("Smazat body (Del)", tip, "Smaže vybrané body; sousední body se spojí úsečkou.", () => editor.SmazVybrane()),
                Tlacitko("Smazat obrysy", tip, "Smaže celé obrysy, ve kterých je vybraný aspoň jeden bod.", () => editor.SmazObrysy()));
            DvaTlacitka(t,
                Tlacitko("Vybrat vše (Ctrl+A)", tip, null, () => editor.VyberVse()),
                Tlacitko("Zrušit výběr (Esc)", tip, null, () => editor.ZrusVyber()));
            btnZpet = Tlacitko("↶ Zpět (Ctrl+Z)", tip, null, () => editor.Zpet());
            btnZnovu = Tlacitko("↷ Znovu (Ctrl+Y)", tip, null, () => editor.Znovu());
            DvaTlacitka(t, btnZpet, btnZnovu);
            DvaTlacitka(t,
                Tlacitko("Obrátit směr", tip, "Obrátí směr frézování u obrysů s vybraným bodem.", () => editor.ObratVybrane()),
                Tlacitko("Start do bodu", tip, "Vybraný bod bude začátkem frézování obrysu (vyberte jeden bod).", () =>
                {
                    string chyba = editor.StartDoBodu();
                    if (chyba != null) MessageBox.Show(this, chyba, "Start obrysu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));

            Nadpis(t, "Roh z vybraných bodů");
            nRadiusRohu = Cislo("radiusrohu", 0, 1000, 5, 2, 0.5m, false);
            Radek(t, "Rádius rohu [mm]", nRadiusRohu, tip, "Poloměr zaoblení vytvořeného rohu. 0 = ostrý roh.");
            chPravyUhel = new CheckBox { Text = "Pravý úhel (srovnat na 90°)", AutoSize = true, Checked = true };
            ulozitelne["pravyuhel"] = chPravyUhel;
            tip.SetToolTip(chPravyUhel, "Když hrany svírají přibližně 90° (±25°), roh se srovná přesně na 90°:\ndelší hrana zůstane, kratší se natočí kolmo.");
            CelyRadek(t, chPravyUhel);
            var btnRoh = Tlacitko("Vytvořit roh s rádiusem", tip,
                "Vyberte tažením obdélníku body v rohu (zakulacení, zkosení, nepřesný roh).\nNahradí se ostrým rohem v průsečíku navazujících hran a ten se zaoblí zadaným rádiusem.",
                () =>
                {
                    string zprava = editor.RohSRadiusem((double)nRadiusRohu.Value, chPravyUhel.Checked);
                    if (zprava != null) MessageBox.Show(this, zprava, "Roh", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            btnRoh.Dock = DockStyle.Fill;
            CelyRadek(t, btnRoh);

            Nadpis(t, "Srovnání osy podle bodu");
            Action<bool> srovnej = osaX =>
            {
                string zprava = editor.ZacniSrovnani(osaX);
                if (zprava != null) MessageBox.Show(this, zprava, "Srovnání", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            var bX = new Button { Text = "Srovnat X podle bodu", Height = S(30) };
            bX.Click += (s, e) => srovnej(true);
            tip.SetToolTip(bX, "Vybrané body dostanou stejnou souřadnici X jako bod, na který potom kliknete (svislá řada).");
            var bY = new Button { Text = "Srovnat Y podle bodu", Height = S(30) };
            bY.Click += (s, e) => srovnej(false);
            tip.SetToolTip(bY, "Vybrané body dostanou stejnou souřadnici Y jako bod, na který potom kliknete (vodorovná řada).");
            DvaTlacitka(t, bX, bY);

            Nadpis(t, "Transformace (vybrané, jinak vše)");
            nUhel = Cislo("uhel", -360, 360, 90, 1, 15, false);
            Radek(t, "Úhel otočení [°]", nUhel, tip, "Kladný úhel = proti směru hodinových ručiček. Otáčí se kolem středu vybraných obrysů.");
            nMeritko = Cislo("meritko", 1, 10000, 100, 1, 5, false);
            Radek(t, "Měřítko [%]", nMeritko, tip, "100 % = beze změny, 50 % = poloviční velikost. Kolem středu vybraných obrysů.");
            DvaTlacitka(t,
                Tlacitko("Otočit", tip, null, () => editor.Otoc((double)nUhel.Value)),
                Tlacitko("Změnit měřítko", tip, null, () => editor.Meritko((double)nMeritko.Value)));
            DvaTlacitka(t,
                Tlacitko("Zrcadlit ↔", tip, "Zrcadlení podle svislé osy (levá a pravá strana se prohodí).", () => editor.Zrcadli(true)),
                Tlacitko("Zrcadlit ↕", tip, "Zrcadlení podle vodorovné osy (horní a dolní strana se prohodí).", () => editor.Zrcadli(false)));

            Nadpis(t, "Tvar a odsazení (uzavřené obrysy)");
            nOdsazeni = Cislo("odsazeni", 0.01m, 500, 3, 2, 0.5m, false);
            Radek(t, "Odsazení [mm]", nOdsazeni, tip, "Vzdálenost nového obrysu od původního – např. poloměr frézy\nnebo okraj kolem nápisu. Vnější rohy se při odsazení ven zaoblí.");
            nPolomer = Cislo("polomer", 0.01m, 500, 3, 2, 0.5m, false);
            Radek(t, "Poloměr zaoblení [mm]", nPolomer, tip, "Zaoblí vnější i vnitřní rohy tímto poloměrem\n(vnitřní rohy pak fréza projede přesně).");
            chZachovat = new CheckBox { Text = "Zachovat původní obrysy", AutoSize = true };
            ulozitelne["zachovat"] = chZachovat;
            CelyRadek(t, chZachovat);
            Action<string> hlas = chyba => { if (chyba != null) MessageBox.Show(this, chyba, "Editor", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            DvaTlacitka(t,
                Tlacitko("Odsadit ven", tip, "Nový obrys vně o zadanou vzdálenost.", () => hlas(editor.UpravTvar((double)nOdsazeni.Value, 0, chZachovat.Checked))),
                Tlacitko("Odsadit dovnitř", tip, "Nový obrys uvnitř o zadanou vzdálenost.", () => hlas(editor.UpravTvar(-(double)nOdsazeni.Value, 0, chZachovat.Checked))));
            DvaTlacitka(t,
                Tlacitko("Zaoblit rohy", tip, "Zaoblí rohy zadaným poloměrem.", () => hlas(editor.UpravTvar(0, (double)nPolomer.Value, chZachovat.Checked))),
                Tlacitko("Sloučit překryvy", tip, "Překrývající se tvary spojí do jednoho obrysu (weld).", () => hlas(editor.UpravTvar(0, 0, false))));

            Nadpis(t, "Otevřené obrysy");
            nTolSpoje = Cislo("tolspoje", 0.01m, 50, 0.5m, 2, 0.1m, false);
            Radek(t, "Spojit konce do [mm]", nTolSpoje, tip, "Konce otevřených obrysů bližší než tato vzdálenost se spojí.");
            DvaTlacitka(t,
                Tlacitko("Spojit konce", tip, "Spojí navazující otevřené obrysy (a uzavře, když se konce sejdou).", () =>
                {
                    string zprava = editor.SpojKonce((double)nTolSpoje.Value);
                    if (zprava != null) MessageBox.Show(this, zprava, "Spojit konce", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }),
                Tlacitko("Uzavřít úsečkou", tip, "Otevřené obrysy uzavře rovnou úsečkou mezi konci.", () => hlas(editor.UzavriOtevrene())));

            lblEditor = new Label { AutoSize = true, MaximumSize = new Size(S(350), 0), Margin = Pad(0, 10, 0, 0) };
            CelyRadek(t, lblEditor);

            Nadpis(t, "Ovládání myší");
            CelyRadek(t, new Label
            {
                AutoSize = true, MaximumSize = new Size(S(350), 0), ForeColor = Color.FromArgb(70, 70, 70),
                Text = "• klik na bod = výběr, tažením bod přesunete\n" +
                       "• klik na čáru = celý obrys (tažením přesunete obrys)\n" +
                       "• tažení na prázdném místě = výběr obdélníkem\n" +
                       "• Ctrl nebo Shift + klik = přidat k výběru\n" +
                       "• dvojklik na čáru = vložit nový bod\n" +
                       "• přesuny tažením a šipkami fungují jen při vypnutém zámku\n" +
                       "• šipky = posun po obrazovce o 1 mm (Shift 10 mm, Ctrl 0,1 mm)\n" +
                       "• pravé tlačítko / kolečko = posun a zoom pohledu\n" +
                       "• dvojklik na prázdné místo = zobrazit celou desku"
            });

            chBezOblouku3 = new CheckBox { Text = "Uložit i verzi bez oblouků (jen úsečky)", AutoSize = true };
            CelyRadek(t, chBezOblouku3);
            var btnUlozit = new Button { Text = "Uložit DXF…", Height = S(40), Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = Color.FromArgb(225, 240, 255) };
            btnUlozit.Click += (s, e) => UlozEditor();
            CelyRadek(t, btnUlozit);

            var listaEd = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = Pad(4, 4, 4, 4) };
            btnZpet3 = SipkaTlacitko(true, () => { editor.Zpet(); editor.Focus(); });
            btnZnovu3 = SipkaTlacitko(false, () => { editor.Znovu(); editor.Focus(); });
            listaEd.Controls.Add(btnZpet3);
            listaEd.Controls.Add(btnZnovu3);
            listaEd.Controls.Add(new Label { Text = "Zpět / Znovu  (Ctrl+Z / Ctrl+Y)", AutoSize = true, Margin = Pad(8, 9, 0, 0), ForeColor = Color.DimGray });
            split3.Panel2.Controls.Add(editor);
            split3.Panel2.Controls.Add(listaEd);
            AktualizujEditorInfo();
        }

        void NastavDesku(decimal w, decimal h)
        {
            if (synchronizujiDesku || editor == null) return;
            synchronizujiDesku = true;
            foreach (var n in new[] { nDeskaW1, nDeskaW3 }) if (n.Value != w) n.Value = w;
            foreach (var n in new[] { nDeskaH1, nDeskaH3 }) if (n.Value != h) n.Value = h;
            synchronizujiDesku = false;
            editor.Deska = new SizeF((float)w, (float)h);
            if (editor.Obrysy.Count == 0) editor.Prizpusob(); else editor.Invalidate();
            nahled.Deska = obr != null ? new SizeF((float)w, (float)h) : SizeF.Empty;
            nahled.Invalidate();
            AktualizujEditorInfo();
            if (yNahore && !nacitam && obr != null) Naplanuj();
        }

        void VolbaStroje(TableLayoutPanel t, ToolTip tip)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cb.Items.AddRange(new object[] { "Y=0 dole · Y nahoru", "Y=0 nahoře · Y dolů" });
            cb.SelectedIndex = yNahore ? 1 : 0;
            volbyStroje.Add(cb);
            if (volbyStroje.Count == 1) ulozitelne["strojY"] = cb;
            cb.SelectedIndexChanged += (s, e) =>
            {
                if (synchronizujiStroj) return;
                yNahore = cb.SelectedIndex == 1;
                synchronizujiStroj = true;
                foreach (var v in volbyStroje) v.SelectedIndex = cb.SelectedIndex;
                synchronizujiStroj = false;
                if (cbPocatek != null) cbPocatek.Items[0] = yNahore ? "Levý horní roh" : "Levý dolní roh";
                if (editor != null) { editor.YNahore = yNahore; editor.Invalidate(); AktualizujEditorInfo(); }
                foreach (var n in new[] { nahled, nahled2 }) if (n != null) { n.YNahore = yNahore; n.Invalidate(); }
                if (!nacitam) { Naplanuj(); ZobrazDxf(); }
            };
            Radek(t, "Souřadnice stroje", cb, tip, "Společná volba pro všechny karty, DXF import a export. X=0 je vlevo.\nDole: kladné Y nahoru. Nahoře: kladné Y dolů.\nPro horní počátek zadejte správnou výšku desky. Posun Y obrázku a tlačítko Do rohu se měří od zvolené strany.\nPřed načtením DXF vyberte souřadnice, ve kterých byl uložen; DXF je automaticky nerozpozná.");
        }

        List<Obrys> ObrysyProStroj(IEnumerable<Obrys> obrysy)
        {
            return SouradniceStroje.Preved(obrysy, (double)nDeskaH3.Value, yNahore);
        }

        void PoslatDoEditoru(List<Obrys> obrysy, Dictionary<string, int> barvy, string zdroj)
        {
            if (editor.Upraveno && editor.Obrysy.Count > 0 &&
                MessageBox.Show(this, "V editoru jsou neuložené změny. Nahradit je?", "Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            editor.NastavObrysy(obrysy, barvy);
            editorZdroj = zdroj;
            karty.SelectedIndex = 2;
            editor.Prizpusob();
            editor.Focus();
        }

        void OtevritDoEditoru(string soubor)
        {
            try
            {
                var data = Dxf.Nacti(soubor);
                var obrysy = Dxf.Spoj(SouradniceStroje.Preved(data.Prvky, (double)nDeskaH3.Value, yNahore), 0.02);
                PoslatDoEditoru(obrysy, data.BarvyVrstev, soubor);
                if (data.Nepodporovane.Count > 0)
                    MessageBox.Show(this, "Některé prvky výkresu editor nezná a vynechal je:\n" +
                        string.Join(", ", data.Nepodporovane.Select(p => p.Key + " (" + p.Value + "×)")), "Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Soubor nejde přečíst:\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void AktualizujEditorInfo()
        {
            if (lblEditor == null) return;
            AktualizujSipky();
            btnZpet.Enabled = editor.LzeZpet;
            btnZnovu.Enabled = editor.LzeZnovu;
            if (editor.Obrysy.Count == 0) { lblEditor.Text = "Žádný výkres."; lblEditor.ForeColor = Color.DimGray; return; }
            double a, b, c, d;
            editor.Rozsah(out a, out b, out c, out d);
            int otevrene = editor.Obrysy.Count(o => !o.Zavreny);
            var txt = string.Format("Obrysů: {0}{1}, bodů: {2}\nVybráno bodů: {3}\nRozměr výkresu: X {4:0.0} × Y {5:0.0} mm\nPoloha od rohu desky: X {6:0.0}, Y {7:0.0} mm",
                editor.Obrysy.Count, otevrene > 0 ? " (" + otevrene + " otevřených)" : "", editor.PocetBodu, editor.PocetVybranych, c - a, d - b, a, yNahore ? (double)nDeskaH3.Value - d : b);
            P bod;
            if (editor.JedenBod(out bod)) { bod = editor.SouradniceBodu(bod); txt += string.Format("\nVybraný bod: X {0:0.00}, Y {1:0.00} mm", bod.X, bod.Y); }
            txt += yNahore ? "\nStroj: Y=0 nahoře, Y dolů" : "\nStroj: Y=0 dole, Y nahoru";
            if (editor.Presahuje) txt += "\n⚠ Výkres přesahuje desku!";
            if (editor.Upraveno) txt += "\n(neuložené změny)";
            lblEditor.Text = txt;
            lblEditor.ForeColor = editor.Presahuje ? Color.Firebrick : Color.Black;
        }

        void UlozEditor()
        {
            if (editor.Obrysy.Count == 0) { MessageBox.Show(this, "V editoru není žádný výkres.", "Uložit DXF"); return; }
            if (editor.Presahuje && MessageBox.Show(this, "Výkres přesahuje desku. Přesto uložit?", "Uložit DXF",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string adresar = null, jmeno = "vykres.dxf";
            if (editorZdroj != null)
            {
                adresar = Path.GetDirectoryName(editorZdroj);
                jmeno = Path.GetFileNameWithoutExtension(editorZdroj) + (Path.GetExtension(editorZdroj).ToLowerInvariant() == ".dxf" ? "_upraveno.dxf" : ".dxf");
            }
            using (var d = new SaveFileDialog { Filter = "DXF výkres|*.dxf|SVG obrázek|*.svg", FileName = jmeno, InitialDirectory = adresar })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (Path.GetExtension(d.FileName).ToLowerInvariant() == ".svg")
                    {
                        Svg.Zapis(d.FileName, editor.Obrysy);
                        MessageBox.Show(this, "Uloženo jako SVG:\n" + d.FileName, "Uložit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Dxf.Zapis(d.FileName, ObrysyProStroj(editor.Obrysy), editor.BarvyVrstev, false);
                    string zprava = "Uloženo:\n" + d.FileName + (yNahore ? "\nStroj: Y=0 nahoře, Y dolů" : "\nStroj: Y=0 dole, Y nahoru");
                    var overeni = Dxf.Over(d.FileName, ObrysyProStroj(editor.Obrysy));
                    zprava += string.Format("\nKontrola DXF: {0} oblouků, {1} úseček; {2}", overeni.Oblouku, overeni.Usecek, overeni.Ok ? "geometrie ověřena" : "⚠ geometrie se liší od náhledu");
                    if (chBezOblouku3.Checked)
                    {
                        string f2 = Path.Combine(Path.GetDirectoryName(d.FileName), Path.GetFileNameWithoutExtension(d.FileName) + "_bez_oblouku.dxf");
                        Dxf.Zapis(f2, ObrysyProStroj(editor.Obrysy), editor.BarvyVrstev, true);
                        zprava += "\n" + f2;
                    }
                    editor.Upraveno = false;
                    AktualizujEditorInfo();
                    MessageBox.Show(this, zprava, "Uložit DXF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Uložení se nepodařilo:\n" + ex.Message, "Chyba", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ================= nastaveni =================

        static string SouborNastaveni
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ObrysCNC", "nastaveni.ini"); }
        }

        void NactiNastaveni()
        {
            try
            {
                if (!File.Exists(SouborNastaveni)) return;
                nacitam = true;
                foreach (var radek in File.ReadAllLines(SouborNastaveni))
                {
                    int i = radek.IndexOf('=');
                    if (i <= 0) continue;
                    string k = radek.Substring(0, i), v = radek.Substring(i + 1);
                    Control c;
                    if (!ulozitelne.TryGetValue(k, out c)) continue;
                    var n = c as NumericUpDown;
                    var cb = c as ComboBox;
                    var ch = c as CheckBox;
                    var tb = c as TextBox;
                    decimal d;
                    int ix;
                    if (n != null && decimal.TryParse(v, NumberStyles.Float, Inv, out d)) n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, d));
                    else if (cb != null && int.TryParse(v, out ix) && ix >= 0 && ix < cb.Items.Count) cb.SelectedIndex = ix;
                    else if (ch != null) ch.Checked = v == "1";
                    else if (tb != null) tb.Text = v;
                }
            }
            catch { }
            finally { nacitam = false; AktualizujRezim(); }
        }

        // ================= zpet / znovu (sipky jako v Malovani) =================

        // nastaveni karty Obrazek -> DXF, ktere se vraci sipkami (meni vysledny obrys)
        static readonly Dictionary<string, string> NastaveniVHistorii = new Dictionary<string, string>
        {
            { "rezim2", "co je tvar" }, { "vystup2", "výstup" }, { "citlivost", "min. rozdíl od pozadí" }, { "prah", "práh jasu" },
            { "invert", "invertování" }, { "vrstva", "název vrstvy" }, { "sirka", "šířka" }, { "pocatek", "počátek" },
            { "posunx", "posun X" }, { "posuny", "posun Y" }, { "strojY", "souřadnice stroje" }, { "sigma", "vyhlazení" }, { "tol", "zjednodušení" }, { "tolmm", "max. odchylka" },
            { "alfa", "zaoblení rohů" }, { "proklad", "způsob prokladu" }, { "narovnat", "narovnání hran" }, { "minplocha", "vynechání skvrn" }, { "oblouky", "oblouky" }
        };
        readonly List<Dictionary<string, string>> nastZpet = new List<Dictionary<string, string>>(), nastZnovu = new List<Dictionary<string, string>>();
        readonly List<string> nastZpetPopis = new List<string>(), nastZnovuPopis = new List<string>();
        Dictionary<string, string> nastPosledni;
        Button btnZpet1, btnZnovu1, btnZpet3, btnZnovu3;
        readonly ToolTip tipSipky = new ToolTip();

        static Bitmap Sipka(bool zpet, int v, Color barva)
        {
            var b = new Bitmap(v, v);
            using (var g = Graphics.FromImage(b))
            using (var pen = new Pen(barva, Math.Max(2f, v * 0.11f)))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(2.6f, 2.6f, true);
                g.DrawArc(pen, new RectangleF(v * 0.22f, v * 0.24f, v * 0.58f, v * 0.52f), 30, -225);   // oblouk s hrotem vlevo
            }
            if (!zpet) b.RotateFlip(RotateFlipType.RotateNoneFlipX);
            return b;
        }

        Button SipkaTlacitko(bool zpet, Action akce)
        {
            var b = new Button { Image = Sipka(zpet, S(20), Color.FromArgb(30, 80, 160)), Size = new Size(S(38), S(32)), Margin = Pad(2, 2, 2, 2), Enabled = false };
            b.Click += (s, e) => akce();
            return b;
        }

        static string Hodnota(Control c)
        {
            var n = c as NumericUpDown; var cb = c as ComboBox; var ch = c as CheckBox;
            if (n != null) return n.Value.ToString(Inv);
            if (cb != null) return cb.SelectedIndex.ToString(Inv);
            if (ch != null) return ch.Checked ? "1" : "0";
            return c.Text;
        }

        static void Nastav(Control c, string v)
        {
            var n = c as NumericUpDown; var cb = c as ComboBox; var ch = c as CheckBox;
            decimal d; int ix;
            if (n != null && decimal.TryParse(v, NumberStyles.Float, Inv, out d)) n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, d));
            else if (cb != null && int.TryParse(v, out ix) && ix >= 0 && ix < cb.Items.Count) cb.SelectedIndex = ix;
            else if (ch != null) ch.Checked = v == "1";
            else if (!(c is NumericUpDown) && !(c is ComboBox) && !(c is CheckBox)) c.Text = v;
        }

        Dictionary<string, string> SnimekNastaveni()
        {
            return NastaveniVHistorii.Keys.Where(k => ulozitelne.ContainsKey(k)).ToDictionary(k => k, k => Hodnota(ulozitelne[k]));
        }

        static string Rozdil(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            return string.Join(", ", a.Keys.Where(k => b.ContainsKey(k) && a[k] != b[k]).Select(k => NastaveniVHistorii[k]));
        }

        // po kazdem prepoctu: zmenilo-li se nastaveni, predchozi stav jde do historie
        void ZaznamenejNastaveni()
        {
            var ted = SnimekNastaveni();
            if (nastPosledni != null)
            {
                string r = Rozdil(nastPosledni, ted);
                if (r.Length > 0)
                {
                    nastZpet.Add(nastPosledni); nastZpetPopis.Add(r);
                    if (nastZpet.Count > 100) { nastZpet.RemoveAt(0); nastZpetPopis.RemoveAt(0); }
                    nastZnovu.Clear(); nastZnovuPopis.Clear();
                }
            }
            nastPosledni = ted;
            AktualizujSipky();
        }

        void ZpetNastaveni()
        {
            if (nastZpet.Count == 0) return;
            nastZnovu.Add(SnimekNastaveni()); nastZnovuPopis.Add(nastZpetPopis[nastZpetPopis.Count - 1]);
            var s = nastZpet[nastZpet.Count - 1];
            nastZpet.RemoveAt(nastZpet.Count - 1); nastZpetPopis.RemoveAt(nastZpetPopis.Count - 1);
            AplikujNastaveni(s);
        }

        void ZnovuNastaveni()
        {
            if (nastZnovu.Count == 0) return;
            nastZpet.Add(SnimekNastaveni()); nastZpetPopis.Add(nastZnovuPopis[nastZnovuPopis.Count - 1]);
            var s = nastZnovu[nastZnovu.Count - 1];
            nastZnovu.RemoveAt(nastZnovu.Count - 1); nastZnovuPopis.RemoveAt(nastZnovuPopis.Count - 1);
            AplikujNastaveni(s);
        }

        void AplikujNastaveni(Dictionary<string, string> s)
        {
            casovac.Stop();
            nacitam = true;
            foreach (var kv in s) Nastav(ulozitelne[kv.Key], kv.Value);
            nacitam = false;
            AktualizujRezim();
            nastPosledni = s;
            Prepocitej(false);
            AktualizujSipky();
        }

        void AktualizujSipky()
        {
            if (btnZpet1 != null)
            {
                btnZpet1.Enabled = nastZpet.Count > 0;
                btnZnovu1.Enabled = nastZnovu.Count > 0;
                tipSipky.SetToolTip(btnZpet1, nastZpet.Count > 0 ? "Zpět: " + nastZpetPopis[nastZpetPopis.Count - 1] + " (Ctrl+Z)" : "Zpět (Ctrl+Z)");
                tipSipky.SetToolTip(btnZnovu1, nastZnovu.Count > 0 ? "Znovu: " + nastZnovuPopis[nastZnovuPopis.Count - 1] + " (Ctrl+Y)" : "Znovu (Ctrl+Y)");
            }
            if (btnZpet3 != null)
            {
                btnZpet3.Enabled = editor.LzeZpet;
                btnZnovu3.Enabled = editor.LzeZnovu;
                tipSipky.SetToolTip(btnZpet3, editor.LzeZpet ? "Zpět: " + editor.PopisZpet + " (Ctrl+Z)" : "Zpět (Ctrl+Z)");
                tipSipky.SetToolTip(btnZnovu3, editor.LzeZnovu ? "Znovu: " + editor.PopisZnovu + " (Ctrl+Y)" : "Znovu (Ctrl+Y)");
            }
        }

        // Ctrl+Z / Ctrl+Y v celem okne podle otevrene karty
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool z = keyData == (Keys.Control | Keys.Z);
            bool y = keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z);
            if ((z || y) && karty.SelectedIndex == 0) { if (z) ZpetNastaveni(); else ZnovuNastaveni(); return true; }
            if ((z || y) && karty.SelectedIndex == 2) { if (z) editor.Zpet(); else editor.Znovu(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void UlozNastaveni()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SouborNastaveni));
                var radky = new List<string>();
                foreach (var kv in ulozitelne)
                {
                    var n = kv.Value as NumericUpDown;
                    var cb = kv.Value as ComboBox;
                    var ch = kv.Value as CheckBox;
                    var tb = kv.Value as TextBox;
                    if (n != null) radky.Add(kv.Key + "=" + n.Value.ToString(Inv));
                    else if (cb != null) radky.Add(kv.Key + "=" + cb.SelectedIndex);
                    else if (ch != null) radky.Add(kv.Key + "=" + (ch.Checked ? "1" : "0"));
                    else if (tb != null) radky.Add(kv.Key + "=" + tb.Text);
                }
                File.WriteAllLines(SouborNastaveni, radky);
            }
            catch { }
        }
    }
}
