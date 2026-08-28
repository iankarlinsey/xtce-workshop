export interface ValidationIssue {
  ruleId: string;
  severity: 'Warning' | 'Error';
  location: string;
  message: string;
  candidateNumber?: number | null;
}

export type CandidateStatus =
  | 'Pass'
  | 'Fail'
  | 'SchemaPass'
  | 'SchemaFail'
  | 'NotEvaluated'
  | 'NotApplicable'
  | 'Info';

export interface CandidateReportRow {
  candidateNumber: number;
  ownerPath: string;
  disposition: string;
  ruleId: string | null;
  status: CandidateStatus;
  findings: ValidationIssue[];
  notes: string;
}

export interface RuleReportRow {
  ruleId: string;
  executed: boolean;
  findingCount: number;
}

export interface ConformanceReport {
  schemaValid: boolean;
  schemaErrors: string[];
  candidates: CandidateReportRow[];
  rules: RuleReportRow[];
  summary: Record<string, number>;
}

export interface PacketLayoutRow {
  name: string;
  kind: string;
  sourceContainer: string;
  offsetInBits: number | null;
  sizeInBits: number | null;
  isVariable: boolean;
  note: string | null;
}

export interface PacketLayout {
  rows: PacketLayoutRow[];
  totalSizeInBits: number | null;
}

export interface MetricCounts {
  childSystems: number;
  parameters: number;
  parameterTypes: number;
  parameterTypesByKind: Record<string, number>;
  containers: number;
  messages: number;
  metaCommands: number;
  preservedFragments: number;
}

export interface SpaceSystemMetrics {
  systemPath: string;
  local: MetricCounts;
  deep: MetricCounts;
}

export interface DocumentMetrics {
  totals: MetricCounts;
  systems: SpaceSystemMetrics[];
}

export interface SearchMatch {
  kind: 'Parameter' | 'ParameterType' | 'Container' | 'Message' | 'MetaCommand' | 'ArgumentType'
    | 'CommandParameter' | 'CommandParameterType';
  systemPath: string;
  name: string;
  matchedAlias: string | null;
}

export interface UsageMatch {
  kind: string;
  location: string;
  detail: string;
}

export interface LoadDiagnostic {
  kind: 'MalformedXml' | 'ModelError';
  message: string;
  path: string;
  line: number | null;
  column: number | null;
}

export interface SchemaError {
  message: string;
  line: number | null;
  column: number | null;
}

export interface LoadPosition {
  line: number;
  column: number;
}

/** One finding of any class, positioned for display in the source editor. */
export interface SourceMarker {
  line: number | null;
  column: number | null;
  message: string;
  severity: 'error' | 'warning';
}

/**
 * Resolves a validator location (e.g. "Sat/ContainerSet/Frame") to a source position via
 * the reader's position index, falling back to the longest recorded ancestor path so a
 * deeper citation still lands near its owner.
 */
export function resolveLocation(
  location: string,
  positions: Record<string, LoadPosition> | null
): LoadPosition | null {
  if (!positions) {
    return null;
  }
  let candidate = location;
  for (;;) {
    const position = positions[candidate];
    if (position) {
      return position;
    }
    const cut = candidate.lastIndexOf('/');
    if (cut < 0) {
      return null;
    }
    candidate = candidate.slice(0, cut);
  }
}
