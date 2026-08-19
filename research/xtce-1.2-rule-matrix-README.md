# XTCE 1.2 Rule Matrix — Phase B Triage

This is Phase B of the validation-completeness pipeline scoped early in this
project's planning: mechanical extraction (Phase A, `Xtce.SpecTools`) produced
109 candidate rule statements — XSD `<documentation>` blocks containing
RFC2119-style normative language (shall/must/should/required/may not). Phase
B is the triage: read each candidate and classify it.

## Files

- **`xtce-1.2-triage-log.csv`** — all 109 candidates, one row each, with a
  disposition and a reason. This is the completeness proof: every candidate
  is accounted for, including the ones discarded — a candidate silently
  vanishing is indistinguishable from one properly rejected unless rejection
  is itself a recorded row.
- **`xtce-1.2-rule-matrix.csv`** — the 15 distinct semantic rules promoted
  from those 109 candidates, deduped (the same rule often applies to
  multiple element types — e.g. "value must be valid for its type" cites 7
  different owners). This is the actual backlog for Phase D (implementation):
  each row tracks `Implemented`/`Tested` status, currently all `no`.

## Disposition categories

- **SEMANTIC** (25 candidates → 15 deduped rules) — a genuine constraint the
  XSD can't express, worth implementing as a validator.
- **REDUNDANT** (6 candidates) — already enforced by the XSD itself (pattern
  restrictions, declared `default=` values) — verified against the actual
  schema, not assumed from the prose alone.
- **NON_NORMATIVE** (76 candidates) — descriptive text, implementation hints,
  display-formatting guidance, or runtime/decode-time behavioral semantics
  (out of scope for *static file* validation, which is what this matrix is
  for) rather than a constraint on what makes an XTCE document valid.
- **FLAGGED** (2 candidates) — neither of the above; a genuine finding worth
  recording on its own. See below.

## The two FLAGGED findings

1. **Candidate #40 (`BaseMetaCommandType`) — a real spec-internal
   inconsistency.** The documentation states `ArgumentAssignmentList` is
   "required," but the XSD declares it `minOccurs="0"` (optional). This is
   not implemented as a strict rule: doing so would make this validator
   *stricter than the schema itself* and reject files other XTCE tooling
   accepts. Recorded as a finding, not a rule.
2. **Candidate #60 (`DimensionType`) — needs deeper schema review.** The
   documentation describes an OR/choice between `{StartingIndex,EndingIndex}`
   and a "Size" alternative, but `DimensionType`'s own definition only shows
   the index pair. If the Size alternative is real, it likely lives in a
   different type or a higher-level `xs:choice` not visible from inspecting
   `DimensionType` alone — undetermined without further investigation, not
   guessed at here.

## A methodology shortcut, stated honestly

Candidates #93–#103 (`NumberFormatType`'s various display-formatting
attributes) were classified `NON_NORMATIVE` as a group without individually
re-verifying each one's XSD `default=` value against the schema, unlike
candidates #69/#72/#76/#84/#89/#104 which *were* individually checked. This
doesn't change any candidate's disposition — display-formatting guidance is
out of scope either way, whether or not the specific default happens to also
be schema-declared — so the shortcut costs nothing here, but it's the kind of
thing worth being honest about rather than implying every single row got the
same depth of scrutiny.

## What's next (Phase C / D / E)

- Phase C: this matrix already doubles as the backlog — no separate step
  needed beyond keeping it current as rules get implemented.
- Phase D: implement each rule as a validator against `Xtce.Workshop.Model`,
  citing its `RuleId` in code, with a positive and negative fixture per rule
  (per the OSS test-idea harvest's finding that round-trip/negative-case
  testing catches real bugs single-direction tests miss). **Started** — see
  issues #21 (object-model expansion: `TelemetryMetaData`/`ParameterTypeSet`/
  `ParameterSet`) and #22 (`Xtce.Workshop.Validation`: R07 fully, R15
  partially — 1 of its 7 cited owner locations, the rest blocked on
  MetaCommand/Argument/Container modeling). #24 modeled `ContainerSet`, and
  #25 added the NameReferenceType resolver plus R10 (`yes`) and R11
  (`partial` — modeled ref sites only: parameterTypeRef, entry refs,
  BaseContainer, Comparison; references inside preserved raw fragments are
  invisible by construction), and upgraded R15's type resolution to work
  cross-SpaceSystem. #28 modeled the Binary/RelativeTime/AbsoluteTime kinds
  and added R14 (`yes` — Encoding-fragment presence, with baseType-present
  skipping the check since encoding may be inherited and time-type
  inheritance chains aren't resolved) and R01 (`partial` warning — the
  Encoding/units site is checked by fragment inspection; the
  TimeAlarmRangesType/timeUnits site sits inside preserved alarm content
  and is unreachable), and extended R15 to the new kinds (hexBinary /
  xs:duration / xs:dateTime literal checks). #29 added R08 (`yes` —
  LocationInContainerInBits flags via descendant fragment scanning, covering
  both modeled entries' preserved children and raw entry fragments) and R04
  (`partial` — duplicate-`order` detection among same-target segments, the
  conservative no-false-positive reading; true bit-level overlap needs the
  whole layout resolved). 8 of 15 rules now carry `Implemented` status
  (4 `yes`, 4 `partial` — `partial` is deliberate honesty about citation
  coverage).
- Phase E: adversarial verification that "tested" rules actually fire. Not
  started as a distinct pass yet — #22's fixtures include both positive and
  negative cases per rule, but a dedicated adversarial-verification pass
  (per the methodology's original intent) hasn't happened.
- Not yet started: mining CCSDS 660.1-G-2 (Element Description, the
  identified *primary* corpus, 286 pages) the same way — this matrix is
  built entirely from the XSD's own `<documentation>` blocks, which is one
  of at least two intended sources.

**Phase D remains blocked, for the other 13 rules, on the same object-model
gap this note originally flagged.** Every rule not yet `Implemented` applies
to an XTCE construct — `Calibrator`, `Container`, `MetaCommand`, name
references generally — that `Xtce.Workshop.Model` still doesn't parse or
represent. #21 extended the model from "just `SpaceSystem` nesting" to also
cover `TelemetryMetaData`/`ParameterTypeSet`/`ParameterSet`, which is what
unblocked R07 and (partially) R15. Each further construct is its own
vertical slice, not a small follow-on — `ContainerSet` for `R04`/`R09`/`R10`
being the next likely candidate given how many rules cite it.
