# Počátek Y na různých strojích

Implementovány dvě soustavy: Y=0 dole / kladné Y nahoru a Y=0 nahoře / kladné Y dolů. X=0 vlevo. Dolní soustava zůstává výchozí pro existující nastavení.

Interní souřadnice zůstávají kartézské; export a import horní soustavy používá Y = výška desky − Y a bulge = −bulge. Transformace vytváří kopii, nemění počet segmentů, body editoru ani historii geometrie. Při změně soustavy se v editoru mění pouze údaje, kóty a osy, nikoli fyzická poloha výkresu. U obrázku se umístění přepočítá od zvolené strany se stávajícími posuny.

Společná volba je na všech třech kartách. Nový klíč nastavení `strojY` má hodnotu 0 nebo 1; ostatní klíče se nemění. Testy ověřují registraci klíče a synchronizaci ovládacích prvků, nikoli zápis testovacích hodnot do uživatelova nastavení.

## Ověření

TestSouradniceStroje: **11 scénářů prošlo**. Obsahuje nesymetrický zaoblený obrys, kruhový otvor a otevřený kruhový oblouk.

- Dolní soustava zachovává souřadnice a bulge.
- Horní soustava používá správnou výšku desky; dvojí převod vrátí původní geometrii.
- Středy kružnic se transformují, poloměry a velikosti úhlů se zachovávají. Vzorkované body oblouků odpovídají transformaci, směr úhlu se obrací.
- DXF R12 v obou soustavách obsahuje 5 skutečných oblouků, 2 uzavřené kontury a 1 otevřenou; zpětné načtení a porovnání projde.
- Verze bez oblouků nemá nenulové bulge. Ověřeny výšky 300, 600 a 1220 mm.
- Oprava horního DXF pracuje s normalizovanou geometrií a zachovává fyzický tvar i otevřenou dráhu.
- Souřadnice bodu X=40, Y=40 na desce H=600 jsou v horní soustavě X=40, Y=560. Bod se neposune, zámek zůstává zapnutý.
- „Do rohu“ použije zvolený okraj; zpět vrátí původní data.
- Umístění obrázku od horního i spodního okraje a jeho středové umístění používají zadané souřadnice.
- Všechny tři volby se synchronizují, import horního DXF do formuláře a následná exportní transformace projdou.

Původní sada: **164 geometrických kontrol, 34 funkčních scénářů a 7 kontrol zámku prošlo**. Známá varování u loga a stromu zůstávají — tato změna je neřeší ani nezhoršuje jejich zaznamenané výsledky.

Náhledy editor-Y-dole.png a editor-Y-nahore.png byly vizuálně zkontrolovány: stejný tvar a poloha, změněný počátek, směr osy Y, souřadnice a kóta vzdálenosti od okraje.

Doklady ve složce `vysledky`: TestSouradniceStroje.txt, Kompletni-beh-souradnice.txt, stroj-Y-dole.dxf, stroj-Y-nahore.dxf a oba náhledy editoru. Opakování: `testy\overit.cmd`.

Souřadnicovou soustavu vstupního DXF nelze automaticky určit. Nejprve vyberte soustavu zdrojového stroje, načtěte do editoru a teprve potom přepněte na cílový stroj. Výška desky je součástí převodu. Dávková oprava má stejnou vstupní i výstupní soustavu; uložené DXF znovu ověřuje. SVG je nadále vizuální export s vlastní osou Y dolů a těsným ohraničením výkresu.

Import v CAM jednotlivých strojů a fyzické obrábění nebyly ověřeny. Nebyl odeslán žádný program na CNC.
