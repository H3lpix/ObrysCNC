# Ověření optimalizace ObrysCNC – 10. 10. 2026

Před = uložená verze v2, po = dokončená verze. P/A/R = přímky / kruhové oblouky / ostré rohy. Maximum je nezkorigovaná geometrická odchylka v mm při měřítku použitém testem. Syntetické tvary se měří obousměrně proti známé geometrii; fotografie a logo proti extrahované rastrové hraně. Tyto dvě metriky nelze přímo porovnávat.

| Tvar | P/A/R před | P/A/R po | Max před (mm) | Max po (mm) |
|---|---:|---:|---:|---:|
| horni_rohy_R32 | 4/2/2 | 4/2/2 | 0.017 | 0.017 |
| obdelnik_R32 | 4/4/0 | 4/4/0 | 0.018 | 0.018 |
| kruh_R100 | 0/2/0 | 0/2/0 | 0.003 | 0.003 |
| kruh_sum | 0/2/0 | 0/2/0 | 0.010 | 0.010 |
| elipsa_110x80 | 0/5/0 | 0/5/0 | 0.568 | 0.568 |
| elipsa_103x100 | 1/3/0 | 0/4/0 | 1.548 | 0.097 |
| kruh_nepravidelny | 5/5/0 | 1/10/0 | 2.266 | 0.160 |
| pulkruh_R100 | 1/1/2 | 1/1/2 | 0.004 | 0.004 |
| kapsle_R50 | 2/2/0 | 2/2/0 | 0.011 | 0.011 |
| L_profil_R30 | 6/1/5 | 6/1/5 | 0.017 | 0.017 |
| hvezda | 10/0/10 | 10/0/10 | 1.467 | 0.006 |
| list | 0/6/0 | 0/4/2 | 0.883 | 0.863 |
| maly_roh_R4 | 4/1/3 | 4/1/3 | 0.128 | 0.128 |
| ctverec_ostry | 4/0/4 | 4/0/4 | 0.000 | 0.000 |
| ctverec_pootoceny | 4/0/4 | 4/0/4 | 0.003 | 0.003 |
| mezikruzi | 0/4/0 | 0/4/0 | 0.007 | 0.007 |
| deska_s_otvorem | 8/4/4 | 8/4/4 | 0.027 | 0.027 |
| deska_otvory | 6/8/4 | 6/8/4 | 0.026 | 0.026 |
| motivy | 47/36/31 | 46/37/33 | 0.335 | 0.305 |
| logo_lisplast | 110/47/70 | 111/40/79 | 0.471 | 0.348 |
| strom | 106/360/112 | 127/362/143 | 1.048 | 1.063 |

Všechny automatické kontroly TestGeom prošly, včetně exportu a načtení DXF, poloměrů, tečnosti, kruhů, elips a otvorů. TestRoh a TestNarez jsou výpisové regresní testy; TestRastr ověřil odsazení, zaoblení a sloučení. TestRender byl spuštěn na logu a stromu. Překlad ověřen přepínačem /langversion:5.

Na motivech při šířce 500 mm:

| Zadaná tolerance mm | Naměřené maximum mm (diagnostika) |
|---:|---:|
| 0,2 | 0,184 |
| 0,5 | 0,395 |
| 0,8 | 0,663 |
| 1,5 | 0,728 |

Diagnostika u ostrých rohů odečítá odhad zkrácení způsobeného vyhlazením rastru a vynechává okolí špiček. Není to garantovaná fyzická přesnost vůči originálnímu dílu. Tabulka nahoře uvádí i nezkorigované maximum. Pokud přesnost není dosažena, rozhraní upozorní; zadaná tolerance se automaticky nezvětšuje.

DXF zachovává R12 POLYLINE a bulge. Souřadnice mají 8 desetinných míst, bulge 12. Ověření v Xilog Plus a na SCM Tech Z25 neproběhlo. Nic nebylo odesláno na CNC.

Známé omezení: odsazení loga v editoru v regresním běhu trvalo přibližně 8 sekund. Organický strom se ve všech místech nevejde do automatické tolerance; diagnostika to uvádí.
