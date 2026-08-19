# XTCE Specification Reference

Formal XTCE specification documents pulled from the OMG spec catalog
(https://www.omg.org/spec/XTCE), organized by version. Each PDF has a
`.md` sidecar generated via `pdftotext -layout` so agents can read the
text without a PDF parser — the sidecars are plain-text extractions, not
authoritative; refer to the PDF for exact formatting, tables, and diagrams.

## Versions

| Version | Adopted | Spec PDF | XSD | Notes |
|---|---|---|---|---|
| 1.0 | Aug 2005 | `1.0/xtce-1.0-spec.pdf` | — | Initial release |
| 1.1 | Mar 2008 | `1.1/xtce-1.1-spec.pdf` (+ changebar) | `1.1/SpaceSystem.xsd` | Adopted as CCSDS blue book |
| 1.2 | Oct 2018 | `1.2/xtce-1.2-spec.pdf` (+ changebar) | `1.2/SpaceSystem.xsd` | Current target for this project (see ../summary.md) |
| 1.3 | Jul 2025 | not published as PDF by OMG | `1.3/SpaceSystem.xsd` | Latest formal version; only the XSD is available |

## CCSDS companion documents (v1.2 only)

XTCE 1.2 was also adopted by CCSDS as a formal document suite, coordinated
with OMG. These are additional to the OMG documents above, not
replacements — the CCSDS Element Description Green Book in particular is
far denser in per-element rule text than the OMG spec PDF or the XSD's own
`<documentation>` annotations, and is expected to be the primary source
for extracting semantic validation rules that the XSD can't express (see
project discussion notes for the rule-extraction plan).

| Document | Type | Pages | Role |
|---|---|---|---|
| `1.2/ccsds-660.0-b2-blue-book.pdf` | CCSDS 660.0-B-2, Blue Book (Feb 2020) | 40 | Formal/normative anchor — authoritative SHALL-language |
| `1.2/ccsds-660.1-g2-element-description.pdf` | CCSDS 660.1-G-2, Green Book (Aug 2021) | 286 | Primary corpus for rule extraction — numbered per-element walkthrough with syntax rules and examples |
| `1.2/ccsds-660.2-g2-overview.pdf` | CCSDS 660.2-G-2, Green Book (Feb 2021) | 42 | High-level introductory companion report |

## Sources

- OMG spec catalog: https://www.omg.org/spec/XTCE
- v1.0: https://www.omg.org/spec/XTCE/1.0
- v1.1: https://www.omg.org/spec/XTCE/1.1
- v1.2: https://www.omg.org/spec/XTCE/1.2
- v1.3: https://www.omg.org/spec/XTCE/1.3
- CCSDS 660.0-B-2: https://ccsds.org/Pubs/660x0b2.pdf
- CCSDS 660.1-G-2: https://ccsds.org/Pubs/660x1g2.pdf
- CCSDS 660.2-G-2: https://ccsds.org/Pubs/660x2g2.pdf
