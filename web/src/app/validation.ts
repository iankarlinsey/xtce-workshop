export interface ValidationIssue {
  ruleId: string;
  severity: 'Warning' | 'Error';
  location: string;
  message: string;
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
