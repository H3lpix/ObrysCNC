# Funkční ověření ObrysCNC — 10. 10. 2026

**Výsledek:** sestavení C# 5 prošlo; 164 definovaných geometrických kontrol, 34 funkčních scénářů a 7 kontrol zámku prošlo. To neznamená, že všechny reálné předlohy mají bezchybnou geometrii: logo a strom hlásí problémy kvality. Na CNC nebylo nic odesláno.

## Nově vytvořený obrázek

[test-aplikace.png](vysledky/test-aplikace.png) obsahuje 12 motivů: zaoblené horní rohy, kruh, mezikruží, kapsli, L profil, hvězdu, list, elipsu, malý rádius, ostrý čtverec, desku s otvory a půlkruh. Obrázek 1200 × 900 px vzniká deterministicky v C# z analyticky definovaných testovacích tvarů. Nevyžaduje Python ani další závislosti.

Výsledek při šířce 500 mm: 17 uzavřených kontur, 37 přímek, 30 oblouků, 32 ostrých rohů, 6 kružnic; žádné diagnostikované průsečíky, netečné zlomy ani segmenty kratší než pixel. Před optimalizací: 38 přímek a 48 oblouků.

Měřítko 0,440029 mm/px. Interní diagnostika: maximum 0,278254 mm, tolerance 0,396026 mm — splněna. Nezávislé měření TestRender na vzorkované subpixelové rastrové hraně: průměr 0,034 px, maximum 0,712 px, tedy přibližně 0,313 mm. Metody odchylky se liší, zejména u ostrých rohů; nejde o záruku fyzické přesnosti předlohy.

[test-aplikace.dxf](vysledky/test-aplikace.dxf) byl znovu načten a porovnán s interní geometrií. Skutečně obsahuje oblouky v R12 POLYLINE s nenulovým bulge. [Vykreslená geometrie](vysledky/test-aplikace-vysledek.png) byla vizuálně zkontrolována: modré přímky, fialové oblouky, zelené ostré rohy.

## Rozsah ověření

| Oblast | Ověřeno | Výsledek |
|---|---|---|
| Obrázek → DXF | Pozadí, práh, režim kresby, invertování, prázdná předloha, cache, vypnutí vrstvy | Prošlo |
| Nastavení geometrie | Šířka v mm, posuny X/Y, počátek ve středu, nereálná tolerance bez automatického zvýšení | Prošlo |
| CAD proklad | Přímky, kružnice, půlkruhy, malé rádiusy, tečnost, ostré rohy, elipsy, list, otvory a orientace | Definované kontroly prošly |
| Alternativní výstupy | Hladký proklad označen v diagnostice; středová čára; vypnutí oblouků | Prošlo; hladký režim není garantovaná G1 CAD geometrie |
| Nástroj | Výpočet typické šířky tahu, konzistence seznamu úzkých míst | Prošlo; neověřuje fyzický řez |
| Import obrázků | PNG, JPEG, BMP, GIF, TIFF; WebP na stromu; odmítnutí neplatných dat | Prošlo |
| DXF | R12 export/import, LINE/ARC/CIRCLE, upozornění na SPLINE, spojování, oprava, kontrola bulge a zaokrouhlení | Prošlo |
| SVG | Export používá příkaz A pro oblouky a jednotky mm | Prošlo; nebyl ověřen import v jiném programu |
| Editor | Výběr bodů, souřadnice, vybrat vše/zrušit, přesun, zpět/znovu, mazání | Prošlo |
| Editor — transformace | Vystředění, roh desky, přesah, otočení, obě zrcadlení, změna měřítka | Prošlo |
| Editor — geometrie | Rádius, srovnání X/Y, začátek dráhy, směr, vložení bodu do oblouku, spojení a uzavření | Prošlo |
| Editor — tvar | Odsazení ven/dovnitř, zaoblení, sloučení překryvů | Prošlo; rastrová metoda má malou měřitelnou odchylku |
| Ochrana bodů | Výchozí zámek, tažení bodu i celé kontury, šipky s modifikátory, odemčení, zamčení během tažení | 7/7 prošlo |
| Náhled | Zoom a posun nemění souřadnice; vykreslení souřadnic vybraného bodu | Prošlo, obrázek vizuálně zkontrolován |
| Integrace formuláře | Převod obrázku přes HlavniOkno, načtení DXF do editoru, automatická aktivace editoru | Prošlo v testu skutečných metod formuláře |
| Okno aplikace | Spuštění, přepínání všech tří karet, viditelný zapnutý zámek, rozložení ovládacích prvků | Vizuálně ověřeno přes computer-use |

## Geometrické příklady

U syntetických předloh je měření obousměrné proti známé analytické geometrii v pixelech předlohy. Testy nastavují nominálně 1 px = 1 mm. U reálných předloh je poslední sloupec jednosměrné vzorkované měření vůči rastrové hraně při šířce výsledku 500 mm; není zaměnitelné s interní globální diagnostikou.

| Předloha | Přímky | Oblouky | Ostré rohy | Max. odchylka |
|---|---:|---:|---:|---:|
| Horní rohy R32 | 4 | 2 | 2 | 0,017 px |
| Obdélník R32 | 4 | 4 | 0 | 0,018 px |
| Kruh R100 | 0 | 2 | 0 | 0,003 px |
| Kruh se šumem | 0 | 2 | 0 | 0,010 px |
| Elipsa 110 × 80 | 0 | 5 | 0 | 0,568 px |
| Elipsa 103 × 100 | 0 | 4 | 0 | 0,097 px |
| Nepravidelný kruh | 1 | 10 | 0 | 0,160 px |
| Půlkruh R100 | 1 | 1 | 2 | 0,004 px |
| Kapsle R50 | 2 | 2 | 0 | 0,011 px |
| L profil R30 | 6 | 1 | 5 | 0,017 px |
| Hvězda | 10 | 0 | 10 | 0,006 px |
| List | 0 | 4 | 2 | 0,863 px |
| Malý roh R4 | 4 | 1 | 3 | 0,128 px |
| Ostrý čtverec | 4 | 0 | 4 | 0,000 px |
| Pootočený čtverec | 4 | 0 | 4 | 0,003 px |
| Mezikruží | 0 | 4 | 0 | 0,007 px |
| Deska s otvorem | 8 | 4 | 4 | 0,027 px |
| Deska s více otvory | 6 | 8 | 4 | 0,026 px |
| Původní motivy | 46 | 37 | 33 | 0,305 mm |
| Logo Lisplast | 111 | 40 | 79 | 0,348 mm* |
| Strom | 127 | 362 | 143 | 1,063 mm* |

## Zjištěné problémy a limity

1. **Logo Lisplast:** interní maximum 0,911 px překračuje automatickou toleranci 0,900 px; jeden prvek je mimo toleranci. DXF roundtrip přesto prošel — pouze potvrzuje věrné uložení, ne dodržení tolerance předlohy.
2. **Strom:** diagnostika hlásí jeden samoprůsečík, 57 prvků mimo toleranci a interní maximum 40,434 px. Toto výrazně větší maximum se liší od jednosměrného vzorkování rastrové hrany (2,722 px / 1,063 mm). Vyžaduje samostatné prošetření měření a problematické kontury. Strom nelze označit za bezchybný CAD výsledek.
3. **Rastrové odsazení:** TestRastr naměřil odchylky plochy přibližně 0,01–0,05 %. Nejde o čistou analytickou offset operaci. TestRoh správně odmítl R40 na příliš krátkých hranách.
4. **Klikací kontrola není úplná:** ovládání nativních souborových dialogů přes automatizaci naráželo na neplatné indexy prvků a nesoulad cílového okna. Import/export byl proto ověřen funkčně, ne dokončeným klikacím průchodem každého dialogu. Neověřeny zůstávají všechny kombinace drag-and-drop, dávkové ukládání přes dialogy, potvrzení neuložené práce a obnova nastavení po restartu. HEIC/ICO a jiné konfigurace DPI také nebyly ověřeny.
5. **Xilog Plus a CNC:** import na cílovém stroji nebyl proveden. DXF je nutné před obráběním ověřit v Xilog Plus.

Zjištění v bodech 1–3 zde nejsou vydávána za opravená. Součástí této práce je testování a transparentní protokol, nikoli změna rekonstrukčního algoritmu.

## Opakování a důkazy

Spusťte `testy\overit.cmd` se zavřenou aplikací. Skript sestaví finální EXE, vygeneruje analytické předlohy i nový složený obrázek a spustí TestGeom, TestFunkce a TestZamek. V testu reálných předloh jsou nyní výslovná varování kvality pro logo a strom. Úspěšné kontroly souboru DXF se již nemusí zaměnit za potvrzení bezchybnosti tvaru.

- [Kompletní běh](vysledky/Kompletni-beh.txt)
- [Geometrie a DXF](vysledky/TestGeom.txt)
- [34 funkčních scénářů](vysledky/TestFunkce.txt)
- [Zámek pohybu](vysledky/TestZamek.txt)
- [Rádiusy a osy](vysledky/TestRoh.txt)
- [Odsazení a sloučení](vysledky/TestRastr.txt)
- [Nezávislé měření renderu](vysledky/TestRender-test.txt)
- [Souřadnice bodu a zámek](vysledky/editor-souradnice.png)

Uživatelské výkresy a seznamy dílů nebyly přepsány. Testy formuláře používají Dispose bez ukládání testovacího nastavení. Aplikace byla při kontrole okna otevřena s existujícím nastavením; hodnoty nastavení nebyly během klikací kontroly měněny.
