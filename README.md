# ObrysCNC

Windows aplikace pro převod obrázků na CAD obrysy, opravu DXF a úpravu geometrie na desce. Přímky a kruhové oblouky exportuje jako DXF R12 POLYLINE s bulge.

## Na druhém PC

Pro samotné testování stáhněte [kompletní balíček](distribuce/ObrysCNC-testy-2026-10-10.zip), rozbalte jej a otevřete `CTI-ME-NEJDRIV.txt`. Pak spusťte `ObrysCNC.exe`. Potřeba je Windows 10/11 a .NET Framework 4.8.

Pro pokračování ve vývoji naklonujte tento soukromý repozitář pod přihlášeným GitHub účtem:

```text
git clone https://github.com/H3lpix/ObrysCNC.git
cd ObrysCNC
sestavit.cmd
```

Bez Gitu lze použít GitHub **Code → Download ZIP** a rozbalit zdroje. Sestavení nevyžaduje Python ani nové knihovny; používá překladač .NET Framework ve Windows a C# 5.

## Dokumentace a stav

- [Kompletní návod](NÁVOD.txt)
- [Aktuální stav a známé problémy](AKTUALNI-STAV.txt)
- [Postup testování na pracovním PC a v Xilog Plus](POSTUP-TESTU-XILOG.txt)
- [Protokol pro výsledky pracovního PC](PROTOKOL-PRACOVNI-PC.txt)
- [Souhrn testů](testy/SOUHRN-TESTU-2026-10-10.md)
- [Historické porovnání geometrie před/po](testy/OVERENI.md)

Aktuální funkce: tři karty (Obrázek → DXF, Oprava DXF, Editor DXF), optimalizace přímek a oblouků, diagnostika přesnosti, kružnice ze dvou půlkruhů, souřadnice výběru, údaj rádiusu mezi vybranými sousedními body a zámek nechtěného posunu. Nářezový plán je odstraněný; jeho starý kód je pouze archivovaný.

**Import DXF v Xilog Plus zatím není plně ověřený.** U DXF tlapky v srdci je otevřený problém vykreslení mimo desku. Přepočet horního/spodního Y je matematicky otestovaný, ale správná interpretace v cílovém CAM vyžaduje ověření referenční soustavy a importních posunů. Logo a strom mají známá varování kvality, podrobnosti v aktuálním stavu. Nic se automaticky neodesílá na CNC.

## Testy

Se zavřenou aplikací spusťte:

```text
testy\overit.cmd
```

Skript sestaví aplikaci a spustí geometrické, funkční, souřadnicové testy, ochranu bodů a kontroly výběru rádiusu. Výsledky ukládá do `testy/vysledky`. Poslední kompletní běh je v [Balicek-testy.txt](testy/vysledky/Balicek-testy.txt). Úspěšný roundtrip DXF neznamená, že každá předloha splňuje toleranci nebo nemá průsečíky.

## Nastavení

Nastavení je lokální v `%APPDATA%\ObrysCNC\nastaveni.ini`; není součástí Git synchronizace. Před ručním přenosem jej zazálohujte. Referenční kopie v distribučním balíčku se automaticky nepoužije.

## Kde pokračovat

Nejbližší úkol: zaznamenat v Xilog Plus **Edit → General settings** a **Edit → Reference system**, ověřit umístění testovacích DXF a rádiusy skutečných oblouků. Potom prošetřit odchylky a samoprůsečík stromu. Výsledky pracovního PC zapisujte do protokolu a změny zdrojů ukládejte do Gitu.
