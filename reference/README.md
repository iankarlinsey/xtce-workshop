# XTCE Specification Reference

Pointers to the formal XTCE specification documents from the OMG spec catalog
(https://www.omg.org/spec/XTCE), organized by version, plus the XTCE XML
Schema files themselves.

## Redistribution note (issue #56)

The specification PDFs and their text-extracted `.md` sidecars are **not
redistributed in this repository** and were purged from its git history
before the repository was made public: the OMG specification license permits
copying for informational purposes only — it explicitly prohibits posting on
a network computer and prohibits modified versions (which text extractions
are) — and the CCSDS books carry no explicit redistribution grant. Each
removed document leaves a `<name>.pdf.md` placeholder identifying it and
linking to the official source.

The XSDs (`SpaceSystem.xsd`, `xml.xsd`) **are** included: they are required
to build, test, and run xtce-workshop, and are used under the specification's
implementation grant ("to use this specification to create and distribute
software ... based upon this specification"), consistent with other
open-source XTCE implementations that bundle them (other open-source XTCE implementations).
`xml.xsd` is W3C material under the permissive W3C Software License.

## Versions

| Version | Adopted | Spec | XSD | Notes |
|---|---|---|---|---|
| 1.0 | Aug 2005 | `1.0/xtce-1.0-spec.pdf.md` (pointer) | — | Initial release |
| 1.1 | Mar 2008 | `1.1/xtce-1.1-spec.pdf.md` (pointer; + changebar) | `1.1/SpaceSystem.xsd` | Adopted as CCSDS blue book |
| 1.2 | Oct 2018 | `1.2/xtce-1.2-spec.pdf.md` (pointer; + changebar) | `1.2/SpaceSystem.xsd` | Current target for this project (see ../summary.md) |
| 1.3 | Jul 2025 | not published as PDF by OMG | `1.3/SpaceSystem.xsd` | Latest formal version; only the XSD is available |

## CCSDS companion documents (v1.2 only)

XTCE 1.2 was also adopted by CCSDS as a formal document suite, coordinated
with OMG. These are additional to the OMG documents above, not
replacements — the CCSDS Element Description Green Book in particular is
far denser in per-element rule text than the OMG spec PDF or the XSD's own
`<documentation>` annotations, and served as the primary source for the
semantic validation rules the XSD can't express (see
`../research/ccsds-660.1-g2-mining.md` for the project's own mining notes,
which remain in-repo).

| Document | Type | Pages | Role |
|---|---|---|---|
| `1.2/ccsds-660.0-b2-blue-book.pdf.md` (pointer) | CCSDS 660.0-B-2, Blue Book (Feb 2020) | 40 | Formal/normative anchor — authoritative SHALL-language |
| `1.2/ccsds-660.1-g2-element-description.pdf.md` (pointer) | CCSDS 660.1-G-2, Green Book (Aug 2021) | 286 | Primary corpus for rule extraction — numbered per-element walkthrough with syntax rules and examples |
| `1.2/ccsds-660.2-g2-overview.pdf.md` (pointer) | CCSDS 660.2-G-2, Green Book (Feb 2021) | 42 | High-level introductory companion report |

## Sources

- OMG spec catalog: https://www.omg.org/spec/XTCE
- v1.0: https://www.omg.org/spec/XTCE/1.0
- v1.1: https://www.omg.org/spec/XTCE/1.1
- v1.2: https://www.omg.org/spec/XTCE/1.2
- v1.3: https://www.omg.org/spec/XTCE/1.3
- CCSDS 660.0-B-2: https://public.ccsds.org/Pubs/660x0b2.pdf
- CCSDS 660.1-G-2: https://public.ccsds.org/Pubs/660x1g2.pdf
- CCSDS 660.2-G-2: https://public.ccsds.org/Pubs/660x2g2.pdf

## Working locally with the full documents

Download the PDFs from the sources above into the matching `reference/<ver>/`
directory (git-ignore or just don't commit them) and regenerate sidecars with
`pdftotext -layout <file>.pdf <file>.md` if agent-readable text is useful.
