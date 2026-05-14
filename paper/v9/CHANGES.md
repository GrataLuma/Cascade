# Paper v9 — CHANGES (relative to v8)

**Date:** 2026-05-06
**Type:** scope correction (no technical changes; framing only)

## TL;DR

Paper v9 reframuje Grata Cascade z *Continuous Key Agreement (CKA)* primitivy
na *one-shot multi-round symmetric key agreement* primitivu. Důvodem je
honesty mismatch v paperu v8: protokol CKA hru ve smyslu Alwen-Coretti-Dodis
(ACD19) nedefinoval ani neimplementoval (jeden K* per session, žádná
sekvence {k_i}, žádné formální FS/PCS), ale paper ho jako CKA prezentoval.
v9 to napravuje a zároveň pozicuje GC do férového srovnání s jednorázovými
KE primitivami (ECDH, ML-KEM, X-Wing, Merkle puzzles), kde obstojí lépe
než v CKA srovnání. CKA se stává primárním otevřeným směrem (`prob:cka-extension`).

## Motivation

Při strategickém review paper v8 jsme identifikovali, že:

1. **CKA framing v v8 je aspirační, ne deskriptivní.** Protokol nedefinuje
   sekvenci klíčů, nemá state-continuity mechanism, neformalizuje
   FS/PCS hry. Reviewer cryptography venue by toto napadl jako overclaim.
2. **Srovnání s Triple Ratchet/SPQR/MLS je strukturálně asymetrické.**
   Tyto konstrukce mají formální CKA důkazy (game-based v ROM,
   peer-reviewed); GC má jen strukturální argumenty + empiriku.
   Comparison v8 §8 staví GC do nevýhodného světla.
3. **Jednorázová KE primitiva je férová kategorie.** Comparison s ECDH,
   ML-KEM, X-Wing, Merkle puzzles ukazuje GC jako kandidáta třetí
   nezávislé asumce v hybridní kompozici, kde má smysluplnou hodnotu.

Reframing nemění technický obsah — měníme jen tvrzení o tom, co protokol
dělá. Recyklace ≥85 %.

## Changes by section

### Title (přepsán)
- Před: "Grata Cascade: Průběžná domluva klíče prostřednictvím
  statistického driftu sdíleného stavu"
- Po: "Grata Cascade: Hashová jednorázová domluva klíče prostřednictvím
  statistického driftu, jako kandidát pro hybridní postkvantovou
  domluvu klíče"

### Abstract (kompletně přepsán)
- Drop CKA + Triple Ratchet hook
- Add: one-shot KE primitivu, X-Wing as concrete deployment template
- Three contributions: structural argument, empirical validation,
  comparison with one-shot KE primitives
- CKA extension as open direction (prob:cka-extension)

### §1 Introduction (kompletně přepsán)
Nové podsekce:
- §1.1 Hybridní postkvantová domluva klíče (X-Wing, PQXDH, ECDH⊕ML-KEM)
- §1.2 Otevřená otázka: třetí nezávislý předpoklad (Merkle puzzles,
  Impagliazzo-Rudich, SPHINCS+)
- §1.3 Grata Cascade jako kandidát + "Vymezení vůči CKA primitivám"
  disclaimer
- §1.4 Příspěvky (zformulované jako one-shot)
- §1.5 Rozsah platnosti (4 kategorie: dokázané/argumentované/změřené/otevřené)
  + threat model
- §1.6 Osnova
- Drop "Intuice 3-of-10" odstavec (zachovaný v pop verzi)
- Drop ACD19/Triple Ratchet jako primary motivation

### §2 Související práce (NOVÁ sekce)
4 podsekce s existujícími bib citations:
- §2.1 Jednorázová KA + hybridní kompozice
- §2.2 Hashová domluva klíče
- §2.3 Tree Parity Machine a jeho lekce
- §2.4 CKA primitivy a jejich vztah k této práci

### §3-§7 (technické jádro)
**Beze změny obsahu.** Drobné textové úpravy:
- "transformace na CKA konstrukci" → "transformace na jednorázovou KE primitivu"
- "ve standardní CKA distinguishing hře" → "ve standardní
  key-indistinguishability hře pro jednorázovou KE primitivu (analogická
  CKA hře z ACD19 omezené na jeden klíč)"
- "formální redukce z CKA distinguishing výhody" → "z
  key-indistinguishability výhody"

### §8 Srovnání (přepsán)
Tabulka kompletně nová:
- Před: Signal Double Ratchet, PQXDH, Triple Ratchet (SPQR), GC ref, GC Mobile
- Po: ECDH (X25519), ML-KEM-768, X-Wing, Merkle puzzles, GC ref v2

Discussion:
- Honest acknowledgment: GC ~3 orders worse v bandwidth than ML-KEM
- "GC je třetí coequal vrstva v X-Wing-style hybrid composition"
- "GC NENÍ pokus nahradit ECDH/ML-KEM"
- Konkrétní deployment scenario: X25519 ⊕ ML-KEM-768 ⊕ Grata Cascade
- Section title: "Srovnání se state-of-the-art" → "Srovnání s
  jednorázovými KE primitivami"

### §9 Otevřené problémy (rozšířen)
Nový primary entry **`prob:cka-extension`** (~2-3 stránky) s 3 paths:
- Path A — Repeated-session chaining (PRIMARY follow-up target)
- Path B — Symmetric ratchet on top of single session
- Path C — Native CKA construction

Existing problems retained, prob:dynamic-formal updated to reference
prob:cka-extension as parallel central problem.

### §10 Závěr (reframing)
- "kandidát čtvrtého ratchetu" → "kandidát třetí nezávislé vrstvy
  v X-Wing-style hybrid composition"
- Path forward: dva centrální open problems (prob:dynamic-formal +
  prob:cka-extension)
- Final epigraph: "dynamický systém" → "dynamický jednorázový systém.
  Rozšíření na continuous variant je otevřené"

## Files changed

| File | Status | Lines |
|---|---|---|
| `paper/v9/files/paper_v9_cz.tex` | NEW (copy v8 + reframing) | 1059 → 1182 |
| `paper/v9/files/paper_v9_en.tex` | NEW (copy v8 + reframing) | 1087 → 1211 |
| `paper/v9/files/paper_v9_pop_cz.tex` | NEW (copy v8 pop + reframing) | 1527 → 1555 |
| `paper/v9/files/paper_v9_pop_en.tex` | NEW (copy v8 pop + reframing) | 1458 → 1490 |
| `paper/v9/CHANGES.md` | NEW | this file |

Recyklace:
- Main paper: ~89% (123 řádků diff CZ, 124 EN, většinou §1, §2, §8, §9 přepsání)
- Popular companion: ~98% (28 řádků diff CZ, 32 EN; framing-bearing části zachovány v technickém jádře)

## Pop verze v9 změny

Stejné principy jako main paper, ale v populárním tónu. Klíčové změny:

- **Title subtitle:** "paper v8" → "paper v9"
- **TL;DR opening box:** main paper title aktualizován na v9 framing
- **§1.3 hook (NEW label):** `pop:why-fourth-layer` → `pop:why-third-assumption`
  * Drop "fourth ratchet" framing (Triple Ratchet 3-bullet seznam)
  * Add "third independent assumption" framing (X-Wing 2-bullet seznam: X25519 + ML-KEM-768)
  * Žargon-vsuvka přepsána: "tato práce ratchet nestaví, staví menší primitivu (one-shot KE)"
- **§1.7 outline:** Kapitola Comparison description aktualizován na ECDH/ML-KEM/X-Wing/Merkle
- **§8 Comparison (largely rewritten):**
  * Title: "Srovnání s Signal a Triple Ratchet" → "Srovnání s jednorázovými KE primitivami"
  * Tabulka: Signal DR / PQXDH / Triple Ratchet rows → ECDH / ML-KEM / X-Wing / Merkle / GC
  * Discussion: "kryptografická diverzifikace" → "třetí nezávislá vrstva v hybridní kompozici"
  * Aha box: "fourth layer" → "third coequal layer in X-Wing-style stack"
  * NEW subsection "Vůči Merklovým puzzlům": $O(N^2)$ bound, GC conjectural improvement
- **§9 Open problems:**
  * NEW primary entry `prob:cka-extension` před `prob:dynamic-formal`
  * 3 paths v populárním tónu (Path A: repeated-session, Path B: symmetric ratchet, Path C: native CKA)
  * `prob:dynamic-formal` updated: explicitní cross-ref jako "second central problem"
- **§10 Glossary:** Ratchet entry updated (GC NENÍ ratchet); ML-KEM faktická zmínka zachována
- **§10 "Pokud chcete přispět" priority list:** prob:cka-extension přidán PRIMARY, prob:flood-stress přidán
- **Final epigraph:** "dynamický systém" → "dynamický jednorázový systém. Rozšíření na continuous (CKA) variant je otevřené"

Aplikováno paralelně CZ + EN, plná bilingvní parita.

## Pop verze v9 audit (2026-05-09)

Kompletní audit `paper_v9_pop_{en,cz}.tex` s parity:

- **Cite/bibitem:** pop verze záměrně bez bibliografie (popular companion);
  0 broken citations.
- **v8 → v9 reframing leftovers:** 8 aktivních referencí na "paper v8"
  (4 EN + 4 CZ) v textu opraveno na "paper v9". 1 neplatná section
  reference (`paper v8 §4.2` k Merkle puzzles diskusi — v8 numbering,
  ale §2 Related Work v v9 změnila section čísla) přepsána na "the main
  paper" / "hlavní paper" (drop neplatné §4.2).
- **Historical references zachovány:** "[closed in v8]", "[uzavřeno v v8]"
  status labely + file header comments — legit historical context, nezměněno.
- **Reframing core (CKA, fourth ratchet/layer):** kompletně provedený
  v session 2026-05-06; žádné leftovers v aktivním textu (jen v glossary
  jako popisné slovník hesla a v explicitních disclaimerech).
- **Číselné údaje:** plně konzistentní s main paperem ($99{,}9920\%$ KM,
  $5\times10^4$ runs, $\lambda = 120$ bits, $2^{136}$ indistinguishable
  set, reference parameters $L=32, N=4096, h_{AB}=5, h_{BA}=2, h_P=8, M=8$).
- **Cross-references (`\label`/`\ref`):** 27 labelů, 15 unique referencí
  přes `\ref`, 0 orphan refs. 12 labelů definovaných bez `\ref` invocation
  (LaTeX auto-numbering, legit).
- **Typografické typos:** 0 nálezů (žádné common English/Czech typos,
  no doubled punctuation, no leftover whitespace artifacts).

CZ+EN parita: identické issues opraveny identicky.

## Bibliography audit (2026-05-09, post-eprint-submit)

Audit `\cite{}` vs `\bibitem{}` v obou jazykových verzích odhalil 5 broken
citations + 1 unused bibitem. Všechno opraveno (oba EN+CZ, parita zachována):

- `\cite{xwing}` — chyběl bibitem; přidán: D. Connolly, P. Schwabe, B. Westerbaan,
  *X-Wing: The Hybrid KEM You've Been Looking For*, Cryptology ePrint Archive
  2024/039, 2024
- `\cite{pqxdh}` — case mismatch s `\bibitem{PQXDH}`; cite renamed na PQXDH
- `\cite{SPHINCS+}` — chyběl bibitem; přidán: NIST FIPS 205 (Stateless
  Hash-Based Digital Signature Standard), August 2024
- `\cite{ImpagliazzoRudich1989}` — duplicate s `\bibitem{IR1989}`; cite
  renamed na IR1989 (3 výskyty: §2.2, tab:comparison caption, §8 Merkle
  comparison)
- `\cite{F4Calibration}` — chyběl bibitem; přidán internal report
  reference na `EmpiricalEvaluation/reports/probability_thresholds_calibration.md`
- `\bibitem{Survey2022}` (Meraouche TPM survey, jen v EN, unused) — odstraněn

Po opravě: 14 cite keys, 14 matching bibitemů, 0 orphan, 0 unused. EN+CZ
parita kompletní.

Tyto opravy lze submitovat jako eprint revizi PŘED moderátor approval
(internal ID xxxx/109328). Revision form je na `revise?name=xxxx/109328&...`
(token v emailu). Při revizi paper půjde na konec moderation queue, ale
přijde live už opravený.

## Files NOT changed

- `paper/v8/` — historický artefakt, zachován beze změny
- `GrataCascade.Core/` — implementace beze změny (per-session KE,
  matchuje v9 framing přesně)
- `configs/`, `reports/`, `EmpiricalEvaluation/` — beze změny

## Validation

- LaTeX kompilace: uživatel rebuildne PDF (ne v sandboxu)
- Žádný odstavec netvrdí "CKA primitivu" pro samotnou Grata Cascade
  (ověřeno grep): ne, jen ve future-work kontextu (`prob:cka-extension`,
  §sec:related-cka, "Vymezení vůči CKA" disclaimer)
- §sec:comparison srovnává s ECDH/ML-KEM/X-Wing/Merkle, ne s Triple
  Ratchet (ověřeno)
- §sec:open obsahuje `prob:cka-extension` s 3 paths a konkrétními
  formálními požadavky
- Všechny nové \label{} mají matching counts CZ vs EN
- Žádné nové bib citations → žádné undefined-reference warnings

## Plánované navazující práce

1. ~~**Eprint preprint v9**~~ — **SUBMITTED 2026-05-09**, internal tracking
   ID `xxxx/109328`, awaiting moderator approval (~24h pro first-time
   submitter). Po approval finální `2026/XXXX` URL přijde emailem;
   doplnit sem + do README.md badge + spustit outreach. Submission
   metadata reference: `paper/v9/eprint_submission.md`.
2. **Outreach** — Joël Alwen (AWS Crypto Research, ACD19), Cas Cremers
   (CISPA), SPQR autoři (cílené emaily s preprint linkem). Blokované
   na live eprint URL z bodu 1.
3. **Path A formal reduction** — long-term cíl, ideálně via PhD
   collaboration
4. **prob:flood-stress empirical run** — po obdržení RTX 4060 (cca
   2026-05-13)
5. ~~Pop verze v9~~ — **HOTOVO** v této iteraci (paralelně s main paperem,
   user request 2026-05-06)
