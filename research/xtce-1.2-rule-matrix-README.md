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

## The three FLAGGED findings

1. **Candidate #40 (`BaseMetaCommandType`) — a real spec-internal
   inconsistency.** The documentation states `ArgumentAssignmentList` is
   "required," but the XSD declares it `minOccurs="0"` (optional). This is
   not implemented as a strict rule: doing so would make this validator
   *stricter than the schema itself* and reject files other XTCE tooling
   accepts. Recorded as a finding, not a rule.
2. **`ContainerSegmentRefEntryType`/`ParameterSegmentRefEntryType` `order` —
   another doc-vs-schema inconsistency, found by Phase E.** The XSD's own
   documentation says "the first segment order='0'", but the attribute's
   type is `PositiveLongType` (minInclusive 1), so `order="0"` does not
   validate. Discovered when Phase E's every-trigger-must-be-schema-valid
   assertion rejected an order="0" fixture. R04's duplicate-order detection
   is unaffected (it compares values, whatever their base).
3. **Candidate #60 (`DimensionType`) — needs deeper schema review.** The
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
  whole layout resolved). #30 modeled `MessageSet` and added R09 (`partial`
  — a demonstrable-non-root heuristic: unresolvable, abstract, or
  included-as-a-sub-piece targets are flagged; proving general rootness
  isn't attempted). #31 completed the ParameterTypeSet model
  (Array/Aggregate) and added R02 (`partial` — statically resolvable
  entry→parameter→type chains), R05 (`partial` — FixedValue bounds;
  exceeds-type and same-size-not-a-subset both flagged; Argument-side
  citations blocked on command modeling), and R06 (`yes` — documented
  interpretation: a fixed StartingIndex greater than its EndingIndex cannot
  ascend), plus R11/R15 reach extensions (arrayTypeRef, member typeRef,
  member initialValue, and ComparisonType/value — a listed R15 citation —
  via the resolver's new parameter-instance + defining-scope results).
  #32 added R13 and R03 (both `yes`) via document-wide fragment
  inspection (`FragmentEnumerator` reaches every preserved fragment,
  including CommandMetaData argument encodings). #33 modeled
  CommandMetaData/MetaCommand (verifiers as fragments, BaseMetaCommand
  refs, a fourth MetaCommand reference namespace) and added R12 (`yes` —
  documented exact-duplicate interpretation: whitespace-normalized XML
  equality across the cycle-guarded inheritance merge). **First-pass
  Phase D is complete: all 15 rules carry `Implemented` status (8 `yes`,
  7 `partial`)** — every `partial` has its citation gap recorded in the
  rule's source docs and closes as more constructs become modeled.
- Phase E: **done** (issue #34, `AdversarialEndToEndTests`): every matrix
  rule has a TRIGGER and a NEAR-MISS document, both loaded through the real
  reader (never hand-constructed records) and both asserted schema-valid
  first — if a trigger were schema-invalid, the "semantic" rule would be
  re-checking what the schema already enforces. A completeness fact keeps
  the suite honest: a matrix rule without a Phase E case fails the build.
  The pass immediately paid for itself by finding FLAGGED item #2 above.
- Mining CCSDS 660.1-G-2 (the identified *primary* corpus): **done**
  (issue #38, see `ccsds-660.1-g2-mining.md` for the completeness log).
  Six new rules promoted (R16–R21: inheritance cycles, string length-spec
  conflicts, type-inheritance override restrictions, changePerSecond span,
  telemetered-without-encoding warning, MetaCommand CommandContainer
  inheritance), several corroborations of existing rules (incl. R06's
  ascending interpretation), and a third occurrence of the `order="0"`
  doc-vs-schema inconsistency. #39 implemented R16 (`yes`), R17 (`yes`),
  R18 (`partial` — modeled parents only), R19 (`partial` — explicit
  attributes only; notably the schema's own defaults
  changeType=changePerSecond + span=0 violate the book's rule, so the
  all-defaults case is both undetectable and arguably invalid), and R20
  (`partial` warning — explicit dataSource only; the "implied" case would
  flag every minimal document). #40 modeled MetaCommand/CommandContainer
  and implemented R21 (`partial` warning — severity refined from the
  provisional row since both green-book statements are conditional-intent
  /"should" language; inheritance-without-wiring and
  wiring-without-inheritance directions). **The matrix is fully
  implemented: all 21 rules (10 `yes`, 11 `partial`), every rule Phase E
  adversarially verified.** Remaining validator depth comes from closing
  the recorded `partial` gaps as modeling deepens, not from unimplemented
  rules.

**Phase D first pass is complete** (see above). What remains for the
validation pipeline: Phase E (a dedicated adversarial-verification pass),
mining CCSDS 660.1-G-2 as the second rule-extraction corpus, and closing
the recorded `partial` gaps as command/encoding constructs get modeled
more deeply. The `partial` rules also each have a
recorded gap that closes as more constructs become modeled (see the per-rule
notes above and in the validator source).
