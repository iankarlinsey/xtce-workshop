import { Component, CUSTOM_ELEMENTS_SCHEMA, computed, inject, signal, viewChild } from '@angular/core';
import { HttpClient, HttpEventType, HttpEvent } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { EditableTreeNodeComponent } from './editable-tree-node/editable-tree-node';
import { PreservedXmlComponent } from './preserved-xml/preserved-xml';
import { SourceViewComponent } from './source-view/source-view';
import {
  SpaceSystemDocument,
  ParameterTypeDoc,
  DataEncodingDoc,
  DescriptionDoc,
  CalibratorDoc,
  NumericAlarmDoc,
  AlarmRangeDoc,
  ParameterDoc,
  SequenceContainerDoc,
  ParameterTypeKind,
  NodePath,
  Selection,
  ItemKind,
  SequenceEntryDoc,
  ComparisonDoc,
  RestrictionCriteriaDoc,
  MessageDoc,
  MetaCommandDoc,
  BlockMetaCommandDoc,
  MetaCommandStepDoc,
  ContextCalibratorDoc,
  ContextNumericAlarmDoc,
  NonNumericAlarmDoc,
  ContextNonNumericAlarmDoc,
  ContextSignificanceDoc,
  ReferenceTimeDoc,
  MatchCriteriaDoc,
  BooleanExpressionNodeDoc,
  MathOperationTermDoc,
  CommandContainerDoc,
  StreamDoc,
  ServiceDoc,
  CommandVerifierDoc,
  TransmissionConstraintDoc,
  AlgorithmDoc,
  TelemetryItem,
  getNodeAtPath,
  updateNodeAtPath,
  deleteNodeAtPath,
  getItemAtSelection,
  updateItemAtSelection,
  addItemToSystem,
  deleteItemAtSelection,
  collectParameterTypeNames,
  collectParameterNames,
  collectContainerNames,
  collectMetaCommandNames,
  collectArgumentTypeNames,
  moveEntry,
  selectionForLocation,
} from './document-tree';
import {
  ValidationIssue, PacketLayout, ConformanceReport, CandidateStatus, DocumentMetrics,
  SearchMatch, UsageMatch, LoadDiagnostic, SchemaError, LoadPosition, SourceMarker,
  resolveLocation,
} from './validation';
import { XTCE_REFERENCE, ReferenceEntry } from './xtce-reference';

type HealthStatus = 'checking' | 'ok' | 'unreachable';

/** Clean = nothing to triage: no load diagnostics, schema errors, or rule findings. */
function isCleanResult(result: {
  diagnostics?: unknown[];
  schemaErrors?: unknown[];
  validationIssues?: unknown[];
}): boolean {
  return (result.diagnostics ?? []).length === 0 &&
    (result.schemaErrors ?? []).length === 0 &&
    (result.validationIssues ?? []).length === 0;
}

type LoadingStageId = 'read' | 'upload' | 'analyze' | 'download' | 'render';

interface LoadingStage {
  id: LoadingStageId;
  label: string;
  state: 'pending' | 'active' | 'done';
  percent?: number;
  elapsedSeconds?: number;
  detail?: string;
}

interface LoadingState {
  title: string;
  sizeMb: number | null;
  large: boolean;
  stages: LoadingStage[];
}

interface LoadJobSnapshot {
  state: 'running' | 'done' | 'failed' | 'cancelled';
  stage: string;
  percent: number;
  ruleIndex: number;
  ruleCount: number;
  ruleId: string | null;
  error: string | null;
}

interface LoadResult {
  name: string;
  document: SpaceSystemDocument;
  validationIssues: ValidationIssue[];
  diagnostics?: LoadDiagnostic[];
  schemaErrors?: SchemaError[];
  rootNamespace?: string | null;
  detectedVersion?: string | null;
  positions?: Record<string, LoadPosition> | null;
}

@Component({
  selector: 'app-root',
  imports: [EditableTreeNodeComponent, PreservedXmlComponent, SourceViewComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
  // Astro UXDS web components (rux-*) are custom elements, not Angular components.
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
})
export class App {
  /** Debounce for live revalidation; tests may shorten it. */
  static revalidateDelayMs = 400;

  private readonly http = inject(HttpClient);
  private revalidateTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly healthStatus = signal<HealthStatus>('checking');
  protected readonly backendVersion = signal<string | null>(null);
  protected readonly selectedFileName = signal<string | null>(null);

  /** Staged loading modal state; null = no operation in flight. */
  protected readonly loading = signal<LoadingState | null>(null);
  private loadSubscription: Subscription | null = null;
  private analyzeTicker: ReturnType<typeof setInterval> | null = null;
  private activeJobId: string | null = null;
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private lastAnalyzeDetail: string | null = null;
  private lastAnalyzeDetailAt = 0;
  /** Poll cadence for load jobs; tests shorten it. */
  static pollDelayMs = 400;
  protected readonly loadError = signal<string | null>(null);
  protected readonly treeSearchTerm = signal('');

  /** Which inline creator row is open (one at a time), or null. */
  protected readonly creating = signal<
    'child' | 'parameter' | 'container' | 'message' | 'metaCommand' | 'parameterType' | 'argumentType' | null
  >(null);

  protected readonly currentDocument = signal<SpaceSystemDocument | null>(null);
  protected readonly selection = signal<Selection | null>(null);
  protected readonly saveError = signal<string | null>(null);
  protected readonly validationIssues = signal<ValidationIssue[]>([]);
  protected readonly packetLayout = signal<PacketLayout | null>(null);
  protected readonly conformanceReport = signal<ConformanceReport | null>(null);
  protected readonly reportError = signal<string | null>(null);
  protected readonly documentMetrics = signal<DocumentMetrics | null>(null);
  protected readonly searchMatches = signal<SearchMatch[] | null>(null);
  protected readonly loadDiagnostics = signal<LoadDiagnostic[]>([]);
  protected readonly loadSchemaErrors = signal<SchemaError[]>([]);
  /** Reader's element-position index for the loaded text, by validator location. */
  protected readonly loadPositions = signal<Record<string, LoadPosition> | null>(null);
  /** Line the source editor should scroll to; nonce lets the same line re-trigger. */
  protected readonly revealTarget = signal<{ line: number; column: number | null; nonce: number } | null>(null);
  private revealNonce = 0;

  /** Every finding class merged into positioned source markers. */
  protected readonly sourceMarkers = computed<SourceMarker[]>(() => {
    const positions = this.loadPositions();
    const markers: SourceMarker[] = [];
    for (const diagnostic of this.loadDiagnostics()) {
      markers.push({
        line: diagnostic.line, column: diagnostic.column,
        message: `${diagnostic.path}: ${diagnostic.message}`, severity: 'error',
      });
    }
    for (const schemaError of this.loadSchemaErrors()) {
      markers.push({
        line: schemaError.line, column: schemaError.column,
        message: schemaError.message, severity: 'error',
      });
    }
    for (const issue of this.validationIssues()) {
      const position = resolveLocation(issue.location, positions);
      markers.push({
        line: position?.line ?? null, column: position?.column ?? null,
        message: `${issue.location}: ${issue.message}`,
        severity: issue.severity === 'Warning' ? 'warning' : 'error',
      });
    }
    return markers;
  });
  protected readonly parameterUsages = signal<UsageMatch[] | null>(null);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** The loaded file's declared root namespace — an assessment fact the verifier leads with. */
  protected readonly rootNamespace = signal<string | null>(null);
  protected readonly detectedVersion = signal<string | null>(null);

  protected readonly namespaceAdvisory = computed<string | null>(() => {
    const ns = this.rootNamespace();
    const version = this.detectedVersion();
    if (ns === null || version === '1.2') {
      return null;
    }
    if (version) {
      return `Declares the XTCE ${version} namespace — the workshop targets 1.2. `
        + 'Elements are matched by name, so it loads, but review the result with care.';
    }
    return ns === ''
      ? 'Declares no namespace — the workshop targets XTCE 1.2.'
      : `Declares '${ns}', which is not an XTCE namespace — the workshop targets XTCE 1.2.`;
  });

  /** True when a parsed document has no findings of any class — the all-clear state. */
  protected readonly allClear = computed(() =>
    this.currentDocument() !== null &&
    this.loadDiagnostics().length === 0 &&
    this.loadSchemaErrors().length === 0 &&
    this.validationIssues().length === 0);

  /** Tree/Source projection of the same document; source is the current serialization. */
  protected readonly viewMode = signal<'tree' | 'source'>('tree');
  protected readonly sourceText = signal('');
  /** String-keyed so the deferred SourceViewComponent stays out of the initial bundle. */
  private readonly sourceView = viewChild<SourceViewComponent>('sourceView');

  /** Summary entries in severity order for the report header chips. */
  protected readonly reportSummary = computed(() => {
    const report = this.conformanceReport();
    if (!report) {
      return [];
    }
    const order: CandidateStatus[] = ['Fail', 'SchemaFail', 'Pass', 'SchemaPass', 'NotEvaluated', 'Info', 'NotApplicable'];
    return order
      .filter((status) => (report.summary[status] ?? 0) > 0)
      .map((status) => ({ status, label: this.reportStatusLabel(status), count: report.summary[status] }));
  });

  protected readonly selectedSystem = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item) {
      return null;
    }
    return getNodeAtPath(doc, selection.systemPath);
  });

  protected readonly selectedItemKind = computed<ItemKind | null>(
    () => this.selection()?.item?.kind ?? null
  );

  /** Any type-definition selection — telemetry/command parameter types and argument types all share the form. */
  protected readonly selectedParameterType = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    const kind = selection?.item?.kind;
    if (!doc || !selection || (kind !== 'parameterType' && kind !== 'argumentType' && kind !== 'commandParameterType')) {
      return null;
    }
    return getItemAtSelection(doc, selection) as ParameterTypeDoc | null;
  });

  /** The XSD element name for the selected type ("RelativeTimeAgumentType" is the schema's own typo). */
  protected readonly selectedTypeElementName = computed(() => {
    const type = this.selectedParameterType();
    if (!type) {
      return '';
    }
    if (this.selectedItemKind() === 'argumentType') {
      return type.kind === 'RelativeTime' ? 'RelativeTimeAgumentType' : `${type.kind}ArgumentType`;
    }
    return `${type.kind}ParameterType`;
  });

  protected readonly selectedParameter = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    const kind = selection?.item?.kind;
    if (!doc || !selection || (kind !== 'parameter' && kind !== 'commandParameter')) {
      return null;
    }
    return getItemAtSelection(doc, selection) as ParameterDoc | null;
  });

  protected readonly selectedContainer = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item?.kind !== 'container') {
      return null;
    }
    return getItemAtSelection(doc, selection) as SequenceContainerDoc | null;
  });

  protected readonly selectedMessage = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item?.kind !== 'message') {
      return null;
    }
    return getItemAtSelection(doc, selection) as MessageDoc | null;
  });

  protected readonly selectedAlgorithm = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    const kind = selection?.item?.kind;
    if (!doc || !selection || (kind !== 'algorithm' && kind !== 'commandAlgorithm')) {
      return null;
    }
    return getItemAtSelection(doc, selection) as AlgorithmDoc | null;
  });

  protected readonly selectedStream = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item?.kind !== 'stream') {
      return null;
    }
    return getItemAtSelection(doc, selection) as StreamDoc | null;
  });

  protected readonly selectedService = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item?.kind !== 'service') {
      return null;
    }
    return getItemAtSelection(doc, selection) as ServiceDoc | null;
  });

  protected readonly selectedCommandContainer = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item?.kind !== 'commandContainer') {
      return null;
    }
    return getItemAtSelection(doc, selection) as CommandContainerDoc | null;
  });

  protected readonly selectedBlockMetaCommand = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item?.kind !== 'blockMetaCommand') {
      return null;
    }
    return getItemAtSelection(doc, selection) as BlockMetaCommandDoc | null;
  });

  protected readonly selectedMetaCommand = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item?.kind !== 'metaCommand') {
      return null;
    }
    return getItemAtSelection(doc, selection) as MetaCommandDoc | null;
  });

  protected readonly isRootSelected = computed(() => {
    const selection = this.selection();
    return selection !== null && !selection.item && selection.systemPath.length === 0;
  });

  protected readonly knownTypeNames = computed(() => {
    const doc = this.currentDocument();
    return doc ? collectParameterTypeNames(doc) : [];
  });

  protected readonly knownParameterNames = computed(() => {
    const doc = this.currentDocument();
    return doc ? collectParameterNames(doc) : [];
  });

  protected readonly knownContainerNames = computed(() => {
    const doc = this.currentDocument();
    return doc ? collectContainerNames(doc) : [];
  });

  protected readonly knownMetaCommandNames = computed(() => {
    const doc = this.currentDocument();
    return doc ? collectMetaCommandNames(doc) : [];
  });

  protected readonly knownArgumentTypeNames = computed(() => {
    const doc = this.currentDocument();
    return doc ? collectArgumentTypeNames(doc) : [];
  });

  /** XSD documentation for whatever construct is selected — the reference sheet. */
  protected readonly referenceEntry = computed<ReferenceEntry | null>(() => {
    const selection = this.selection();
    if (!selection) {
      return null;
    }
    if (!selection.item) {
      return XTCE_REFERENCE['SpaceSystem'] ?? null;
    }
    switch (selection.item.kind) {
      case 'parameterType': {
        const type = this.selectedParameterType();
        return type ? XTCE_REFERENCE[`${type.kind}ParameterType`] ?? null : null;
      }
      case 'argumentType': {
        const type = this.selectedParameterType();
        return type ? XTCE_REFERENCE[`${type.kind}ArgumentType`] ?? null : null;
      }
      case 'commandParameterType': {
        const type = this.selectedParameterType();
        return type ? XTCE_REFERENCE[`${type.kind}ParameterType`] ?? null : null;
      }
      case 'commandParameter':
        return XTCE_REFERENCE['Parameter'] ?? null;
      case 'commandContainer':
        return XTCE_REFERENCE['SequenceContainer'] ?? null;
      case 'stream':
      case 'service':
        return null;
      case 'algorithm':
      case 'commandAlgorithm': {
        const algorithm = this.selectedAlgorithm();
        return algorithm ? XTCE_REFERENCE[algorithm.kind === 'Math' ? 'MathAlgorithm' : 'CustomAlgorithm'] ?? null : null;
      }
      case 'parameter':
        return XTCE_REFERENCE['Parameter'] ?? null;
      case 'container':
        return XTCE_REFERENCE['SequenceContainer'] ?? null;
      case 'message':
        return XTCE_REFERENCE['Message'] ?? null;
      case 'metaCommand':
        return XTCE_REFERENCE['MetaCommand'] ?? null;
      case 'blockMetaCommand':
        return XTCE_REFERENCE['BlockMetaCommand'] ?? null;
    }
  });

  constructor() {
    this.http.get<{ status: string; version?: string }>('/api/health').subscribe({
      next: (health) => {
        this.healthStatus.set('ok');
        this.backendVersion.set(health?.version ?? null);
      },
      error: () => this.healthStatus.set('unreachable'),
    });
  }

  private startLoading(title: string, sizeBytes: number | null): void {
    const sizeMb = sizeBytes === null ? null : Math.round(sizeBytes / 1048576 * 10) / 10;
    this.loading.set({
      title,
      sizeMb,
      large: (sizeBytes ?? 0) > 25 * 1048576,
      stages: [
        { id: 'read', label: 'Read file', state: 'pending' },
        { id: 'upload', label: 'Upload', state: 'pending' },
        { id: 'analyze', label: 'Analyze (server)', state: 'pending' },
        { id: 'download', label: 'Download results', state: 'pending' },
        { id: 'render', label: 'Open in editor', state: 'pending' },
      ],
    });
  }

  private patchStage(id: LoadingStageId, patch: Partial<LoadingStage>): void {
    const current = this.loading();
    if (!current) {
      return;
    }
    this.loading.set({
      ...current,
      stages: current.stages.map((stage) => (stage.id === id ? { ...stage, ...patch } : stage)),
    });
  }

  /** Marks every stage before `id` done and `id` active — stages only move forward. */
  private advanceTo(id: LoadingStageId): void {
    const current = this.loading();
    if (!current) {
      return;
    }
    const target = current.stages.findIndex((stage) => stage.id === id);
    this.loading.set({
      ...current,
      stages: current.stages.map((stage, index) => ({
        ...stage,
        state: index < target ? 'done' : index === target ? 'active' : stage.state,
      })),
    });
    if (id === 'analyze' && this.analyzeTicker === null) {
      this.patchStage('analyze', { elapsedSeconds: 0 });
      this.analyzeTicker = setInterval(() => {
        const state = this.loading();
        const analyze = state?.stages.find((stage) => stage.id === 'analyze');
        if (analyze?.state === 'active') {
          this.patchStage('analyze', { elapsedSeconds: (analyze.elapsedSeconds ?? 0) + 1 });
        }
      }, 1000);
    }
  }

  private closeLoading(): void {
    if (this.analyzeTicker !== null) {
      clearInterval(this.analyzeTicker);
      this.analyzeTicker = null;
    }
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
    this.loading.set(null);
    this.loadSubscription = null;
    this.activeJobId = null;
  }

  protected onCancelLoading(): void {
    this.loadSubscription?.unsubscribe();
    if (this.activeJobId !== null) {
      // Real server-side cancellation: the pipeline stops chewing, not just the browser.
      this.http.delete(`/api/xtce/jobs/${this.activeJobId}`).subscribe({ error: () => {} });
    }
    this.closeLoading();
    this.loadError.set('Cancelled.');
  }

  /** Polls the job until terminal, painting live analyze sub-progress into the modal. */
  private pollJob(jobId: string, onSuccess: (result: LoadResult) => void, onFailure: (err: unknown) => void): void {
    this.activeJobId = jobId;
    this.advanceTo('analyze');
    const poll = () => {
      // pollDelayMs === 0 (tests) runs inline so the whole job dance stays synchronous.
      const schedule = (work: () => void) =>
        App.pollDelayMs <= 0 ? work() : (this.pollTimer = setTimeout(work, App.pollDelayMs), undefined);
      schedule(() => {
        this.http.get<LoadJobSnapshot>(`/api/xtce/jobs/${jobId}`).subscribe({
          next: (snapshot) => {
            if (this.activeJobId !== jobId) {
              return; // cancelled or superseded
            }
            const shortRule = snapshot.ruleId?.replace(/^XTCE-1\.2-/, '');
            let detail = snapshot.stage === 'parse' ? `Parsing ${snapshot.percent}%`
              : snapshot.stage === 'schema' ? `Schema ${snapshot.percent}%`
              : snapshot.stage === 'rules'
                ? `Rule ${snapshot.ruleIndex}/${snapshot.ruleCount}${shortRule ? ` — ${shortRule}` : ''}`
                : undefined;
            if (detail) {
              // A detail that hasn't moved in a while reads as measured work, not a hang.
              if (detail === this.lastAnalyzeDetail) {
                const stuck = Math.round((Date.now() - this.lastAnalyzeDetailAt) / 1000);
                if (stuck >= 3) {
                  detail = `${detail} (${stuck}s)`;
                }
              } else {
                this.lastAnalyzeDetail = detail;
                this.lastAnalyzeDetailAt = Date.now();
              }
              this.patchStage('analyze', { detail });
            }
            if (snapshot.state === 'done') {
              this.fetchJobResult(jobId, onSuccess, onFailure);
            } else if (snapshot.state === 'failed') {
              this.closeLoading();
              onFailure({ error: { error: snapshot.error ?? 'The load failed.' } });
            } else if (snapshot.state === 'cancelled') {
              this.closeLoading();
            } else {
              poll();
            }
          },
          error: (err) => {
            this.closeLoading();
            onFailure(err);
          },
        });
      });
    };
    poll();
  }

  private fetchJobResult(jobId: string, onSuccess: (result: LoadResult) => void, onFailure: (err: unknown) => void): void {
    this.loadSubscription = this.http.get<LoadResult>(`/api/xtce/jobs/${jobId}/result`, {
      reportProgress: true,
      observe: 'events',
    }).subscribe({
      next: (event) => {
        this.trackLoadEvents(event);
        if (event.type !== HttpEventType.Response) {
          return;
        }
        this.advanceTo('render');
        this.closeLoading();
        onSuccess(event.body as LoadResult);
      },
      error: (err) => {
        this.closeLoading();
        onFailure(err);
      },
    });
  }

  /** Routes HttpClient progress events into the stage checklist. */
  private trackLoadEvents(event: HttpEvent<unknown>): void {
    switch (event.type) {
      case HttpEventType.UploadProgress: {
        this.advanceTo('upload');
        if (event.total) {
          const percent = Math.round((event.loaded / event.total) * 100);
          this.patchStage('upload', { percent });
          if (percent >= 100) {
            this.advanceTo('analyze');
          }
        }
        break;
      }
      case HttpEventType.ResponseHeader:
        this.advanceTo('download');
        break;
      case HttpEventType.DownloadProgress:
        this.advanceTo('download');
        if (event.total) {
          this.patchStage('download', { percent: Math.round((event.loaded / event.total) * 100) });
        }
        break;
    }
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.selectedFileName.set(file.name);
    this.loadError.set(null);
    this.loadDiagnostics.set([]);
    this.loadSchemaErrors.set([]);
    this.rootNamespace.set(null);
    this.detectedVersion.set(null);
    this.loadPositions.set(null);

    // Source-first: the file's own text is visible immediately, before (and regardless
    // of) anything the server says. Markers land on this exact text when the parse
    // response arrives.
    this.viewMode.set('source');
    this.startLoading(`Loading ${file.name}`, file.size);
    this.advanceTo('read');
    file.text().then((fileText) => {
      this.sourceText.set(fileText);
      this.patchStage('read', { state: 'done' });
    });
    this.saveError.set(null);
    this.validationIssues.set([]);
    this.treeSearchTerm.set('');

    const formData = new FormData();
    formData.append('file', file);

    const applyInitial = (result: LoadResult) => {
      if (!result?.document) {
        // A 200 whose body isn't our shape (e.g. an intermediary proxy/auth layer
        // answering in the app's place) must never leave the UI silently empty.
        this.loadError.set('The server response did not contain a document — '
          + 'something between the browser and the API may have intercepted the request.');
        return;
      }
      this.applyLoadResult(result);
      if (isCleanResult(result)) {
        // Clean initial load: land in the tree; anything with findings stays in
        // source, where triage happens, with the tree one (enabled) toggle away.
        this.sourceText.set('');
        this.viewMode.set('tree');
      }
    };
    const failInitial = (err: unknown) => {
      const e = err as { error?: { error?: string; diagnostics?: LoadDiagnostic[]; schemaErrors?: SchemaError[];
        rootNamespace?: string | null; detectedVersion?: string | null; positions?: Record<string, LoadPosition> | null } };
      this.loadError.set(e?.error?.error ?? 'Failed to load file.');
      this.loadDiagnostics.set(e?.error?.diagnostics ?? []);
      this.loadSchemaErrors.set(e?.error?.schemaErrors ?? []);
      this.rootNamespace.set(e?.error?.rootNamespace ?? null);
      this.detectedVersion.set(e?.error?.detectedVersion ?? null);
      this.loadPositions.set(e?.error?.positions ?? null);
    };
    this.loadSubscription = this.http.post<{ jobId: string }>('/api/xtce/jobs', formData, {
      reportProgress: true,
      observe: 'events',
    }).subscribe({
      next: (event) => {
        this.trackLoadEvents(event);
        if (event.type !== HttpEventType.Response) {
          return;
        }
        this.pollJob(event.body!.jobId, applyInitial, failInitial);
      },
      error: (err) => {
        this.closeLoading();
        failInitial(err);
      },
    });
  }

  private applyLoadResult(result: LoadResult): void {
    this.currentDocument.set(result.document);
    this.selection.set({ systemPath: [] });
    this.validationIssues.set(result.validationIssues ?? []);
    this.conformanceReport.set(null);
    this.loadDiagnostics.set(result.diagnostics ?? []);
    this.loadSchemaErrors.set(result.schemaErrors ?? []);
    this.rootNamespace.set(result.rootNamespace ?? null);
    this.detectedVersion.set(result.detectedVersion ?? null);
    this.loadPositions.set(result.positions ?? null);
  }

  /** Scrolls the source editor to a finding's line, entering source view if needed. */
  protected onRevealLine(line: number | null, column: number | null = null): void {
    if (line === null) {
      return;
    }
    if (this.viewMode() !== 'source') {
      this.onShowSource();
    }
    this.revealTarget.set({ line, column, nonce: ++this.revealNonce });
  }

  /** Tree view: a rule finding selects its node; unmappable ones fall back to source. */
  protected onSelectIssueNode(issue: ValidationIssue): void {
    const doc = this.currentDocument();
    const selection = doc ? selectionForLocation(doc, issue.location) : null;
    if (selection) {
      this.onSelect(selection);
      return;
    }
    // Nothing in the modeled tree corresponds (e.g. quarantined content): the source
    // text is the only place this finding exists, positioned or not.
    if (this.viewMode() !== 'source') {
      this.onShowSource();
    }
    const position = resolveLocation(issue.location, this.loadPositions());
    if (position) {
      this.revealTarget.set({ line: position.line, column: position.column, nonce: ++this.revealNonce });
    }
  }

  protected onRevealIssue(issue: ValidationIssue): void {
    const position = resolveLocation(issue.location, this.loadPositions());
    this.onRevealLine(position?.line ?? null, position?.column ?? null);
  }

  // --- Tree/Source view toggle -----------------------------------------------------------

  onShowSource(): void {
    if (this.viewMode() === 'source') {
      return;
    }
    const doc = this.currentDocument();
    if (!doc) {
      if (this.sourceText() !== '') {
        this.viewMode.set('source');
      }
      return;
    }
    this.http.post('/api/xtce/save', doc, { responseType: 'text' }).subscribe({
      next: (xmlText) => {
        this.sourceText.set(xmlText);
        this.viewMode.set('source');
        // Markers must describe exactly the text on screen: re-parse the fresh
        // serialization so positions and findings refer to it, not to the original file.
        this.http.post<LoadResult>('/api/xtce/load-text', { xml: xmlText }).subscribe({
          next: (result) => {
            if (result?.document) {
              this.applyLoadResult(result);
            }
          },
          error: () => {
            // The serialization should always re-parse; if not, the text still shows,
            // just without fresh markers.
          },
        });
      },
      error: () => this.saveError.set('Failed to serialize the document for source view.'),
    });
  }

  /** Re-runs the whole pipeline on the current editor text, staying in source view:
   *  markers refresh, and a successful parse is what unlocks the Tree toggle. */
  onRescan(): void {
    this.rescanSource('stay');
  }

  /** Opt-in pretty-print of the editor text; the automatic re-scan re-maps every marker
   *  onto the formatted text before the user can touch it. */
  onFormat(): void {
    const text = this.sourceView()?.currentText() ?? this.sourceText();
    if (!text) {
      return;
    }
    this.loadError.set(null);
    this.startLoading('Formatting', text.length);
    this.patchStage('read', { state: 'done' });
    this.advanceTo('analyze');
    this.loadSubscription = this.http.post('/api/xtce/format', { xml: text }, { responseType: 'text' }).subscribe({
      next: (formatted) => {
        this.closeLoading();
        this.sourceText.set(formatted);
        // Explicit text: the editor's signal update races the re-scan otherwise, and the
        // re-scan would read (and then restore) the pre-format text.
        this.rescanSource('stay', formatted);
      },
      error: (err) => {
        this.closeLoading();
        this.loadError.set(err?.error?.error ?? 'Failed to format the source text.');
      },
    });
  }

  /** Leaving source view IS the re-parse: the editor text becomes the document, or the
   *  view stays put with positioned diagnostics when it can't. */
  onShowTree(): void {
    // rux-button hosts still emit clicks when [disabled]; the tree needs a parsed document.
    if (this.viewMode() === 'tree' || !this.currentDocument()) {
      return;
    }
    this.rescanSource('switch');
  }

  private rescanSource(behavior: 'stay' | 'switch' | 'switchIfClean', textOverride: string | null = null): void {
    const text = textOverride ?? this.sourceView()?.currentText() ?? this.sourceText();
    this.loadError.set(null);
    this.startLoading('Re-scanning source', text.length);
    this.patchStage('read', { state: 'done' });
    const applyRescan = (result: LoadResult) => {
      if (!result?.document) {
        this.loadError.set('The server response did not contain a document.');
        return;
      }
      this.applyLoadResult(result);
      if (behavior === 'switch' || (behavior === 'switchIfClean' && isCleanResult(result))) {
        this.sourceText.set('');
        this.viewMode.set('tree');
      } else {
        this.sourceText.set(text);
      }
    };
    const failRescan = (err: unknown) => {
      const e = err as { error?: { error?: string; diagnostics?: LoadDiagnostic[]; schemaErrors?: SchemaError[];
        positions?: Record<string, LoadPosition> | null } };
      this.loadError.set(e?.error?.error ?? 'The source text could not be parsed.');
      this.loadDiagnostics.set(e?.error?.diagnostics ?? []);
      this.loadSchemaErrors.set(e?.error?.schemaErrors ?? []);
      this.loadPositions.set(e?.error?.positions ?? null);
      // A failed re-scan means the current text has no parseable document.
      this.currentDocument.set(null);
      this.validationIssues.set([]);
      this.sourceText.set(text);
    };
    this.loadSubscription = this.http.post<{ jobId: string }>('/api/xtce/jobs/text', { xml: text }, {
      reportProgress: true,
      observe: 'events',
    }).subscribe({
      next: (event) => {
        this.trackLoadEvents(event);
        if (event.type !== HttpEventType.Response) {
          return;
        }
        this.pollJob(event.body!.jobId, applyRescan, failRescan);
      },
      error: (err) => {
        this.closeLoading();
        failRescan(err);
      },
    });
  }

  onNewDocument(): void {
    const skeleton = '<?xml version="1.0" encoding="UTF-8"?>\n'
      + '<SpaceSystem xmlns="http://www.omg.org/spec/XTCE/20180204" name="NewSystem">\n'
      + '</SpaceSystem>\n';
    this.selectedFileName.set(null);
    this.loadError.set(null);
    this.loadDiagnostics.set([]);
    this.loadSchemaErrors.set([]);
    this.rootNamespace.set(null);
    this.detectedVersion.set(null);
    this.loadPositions.set(null);
    this.currentDocument.set(null);
    this.validationIssues.set([]);
    this.viewMode.set('source');
    this.sourceText.set(skeleton);
    this.rescanSource('switchIfClean');
  }

  onOpenCreator(kind: 'child' | 'parameter' | 'container' | 'message' | 'metaCommand' | 'parameterType' | 'argumentType'): void {
    this.creating.set(this.creating() === kind ? null : kind);
  }

  onCancelCreator(): void {
    this.creating.set(null);
  }

  onSelect(selection: Selection): void {
    this.selection.set(selection);
    this.packetLayout.set(null); // layouts are per-container and computed on demand
    this.parameterUsages.set(null); // usages are per-parameter and computed on demand
  }

  onComputeLayout(): void {
    const doc = this.currentDocument();
    const selection = this.selection();
    const container = this.selectedContainer();
    if (!doc || !selection || !container) {
      return;
    }
    this.http.post<PacketLayout>('/api/xtce/layout', {
      document: doc,
      containerName: container.name,
      systemPath: selection.systemPath,
    }).subscribe({
      next: (layout) => this.packetLayout.set(layout),
      error: () => this.packetLayout.set({ rows: [], totalSizeInBits: null }),
    });
  }

  // --- Conformance report ----------------------------------------------------------------

  onRunReport(): void {
    const doc = this.currentDocument();
    if (!doc) {
      return;
    }
    this.reportError.set(null);
    this.http.post<ConformanceReport>('/api/xtce/report', doc).subscribe({
      next: (report) => this.conformanceReport.set(report),
      error: () => this.reportError.set('Failed to build the conformance report.'),
    });
  }

  onCloseReport(): void {
    this.conformanceReport.set(null);
  }

  /** Saves the open conformance report to disk: machine-readable JSON or rendered text. */
  onSaveReport(format: 'json' | 'text'): void {
    const doc = this.currentDocument();
    const report = this.conformanceReport();
    if (!doc || !report) {
      return;
    }
    if (format === 'json') {
      const payload = {
        documentName: doc.name,
        generatedAt: new Date().toISOString(),
        report,
      };
      this.downloadBlob(JSON.stringify(payload, null, 2), 'application/json', `${doc.name}-conformance-report.json`);
      return;
    }
    this.http.post('/api/xtce/report/text', doc, { responseType: 'text' }).subscribe({
      next: (text) => this.downloadBlob(text, 'text/plain', `${doc.name}-conformance-report.txt`),
      error: () => this.reportError.set('Failed to render the report as text.'),
    });
  }

  onComputeMetrics(): void {
    const doc = this.currentDocument();
    if (!doc) {
      return;
    }
    this.http.post<DocumentMetrics>('/api/xtce/metrics', doc).subscribe({
      next: (metrics) => this.documentMetrics.set(metrics),
      error: () => this.documentMetrics.set(null),
    });
  }

  protected reportStatusLabel(status: CandidateStatus): string {
    switch (status) {
      case 'Pass': return 'PASS';
      case 'Fail': return 'FAIL';
      case 'SchemaPass': return 'SCHEMA PASS';
      case 'SchemaFail': return 'SCHEMA FAIL';
      case 'NotEvaluated': return 'NOT EVALUATED';
      case 'NotApplicable': return 'N/A';
      case 'Info': return 'INFO';
    }
  }

  /** CSS modifier for a report status chip/row. */
  protected reportStatusClass(status: CandidateStatus): string {
    switch (status) {
      case 'Fail':
      case 'SchemaFail':
        return 'report-status-fail';
      case 'Pass':
      case 'SchemaPass':
        return 'report-status-pass';
      case 'Info':
        return 'report-status-info';
      default:
        return 'report-status-muted';
    }
  }

  // --- SpaceSystem editing -------------------------------------------------------------

  onSelectedNameInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.mutateSelectedSystem((system) => ({ ...system, name: input.value }));
  }

  onCreateChild(nameInput: HTMLInputElement): void {
    const name = nameInput.value.trim();
    if (!name) {
      return;
    }
    this.mutateSelectedSystem((system) => ({
      ...system,
      children: [...system.children, { name, children: [] }],
    }));
    this.creating.set(null);
  }

  onDeleteSelected(): void {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item || selection.systemPath.length === 0) {
      return; // can't delete the root
    }

    this.setDocument(deleteNodeAtPath(doc, selection.systemPath));
    this.selection.set({ systemPath: selection.systemPath.slice(0, -1) });
  }

  onCreateParameterType(
    nameInput: HTMLInputElement,
    kindSelect: HTMLSelectElement,
    refInput: HTMLInputElement,
    itemKind: 'parameterType' | 'argumentType' = 'parameterType'
  ): void {
    const name = nameInput.value.trim();
    if (!name) {
      return;
    }
    const kind = kindSelect.value as ParameterTypeKind;
    const reference = refInput.value.trim();
    const item: ParameterTypeDoc = { name, kind };
    if (kind === 'Enumerated') {
      item.enumerations = [];
    } else if (kind === 'Array') {
      // arrayTypeRef is required and an empty one wouldn't validate; seed one 0..0
      // dimension (DimensionList requires at least one).
      if (!reference) {
        return;
      }
      item.arrayTypeRef = reference;
      item.dimensions = [{ startingIndex: { fixedValue: 0 }, endingIndex: { fixedValue: 0 } }];
    } else if (kind === 'Aggregate') {
      // MemberList requires at least one Member with a valid typeRef.
      if (!reference) {
        return;
      }
      item.members = [{ name: 'field1', typeRef: reference }];
    }
    this.addToSelectedSystem(itemKind, item);
    this.creating.set(null);
  }

  // --- Array dimensions editing ---------------------------------------------------------

  onDimensionBoundInput(index: number, bound: 'startingIndex' | 'endingIndex', event: Event): void {
    const raw = (event.target as HTMLInputElement).value.trim();
    const parsed = Number(raw);
    if (raw === '' || !Number.isFinite(parsed)) {
      return;
    }
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return {
        ...type,
        dimensions: (type.dimensions ?? []).map((dimension, i) =>
          i === index ? { ...dimension, [bound]: { ...dimension[bound], fixedValue: parsed, raw: null } } : dimension),
      };
    });
  }

  onAddDimension(): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return {
        ...type,
        dimensions: [...(type.dimensions ?? []), { startingIndex: { fixedValue: 0 }, endingIndex: { fixedValue: 0 } }],
      };
    });
  }

  onRemoveDimension(index: number): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      const dimensions = type.dimensions ?? [];
      // DimensionList requires at least one Dimension — the button is disabled at one,
      // but guard anyway.
      return dimensions.length <= 1 ? type : { ...type, dimensions: dimensions.filter((_, i) => i !== index) };
    });
  }

  // --- Aggregate members editing --------------------------------------------------------

  onMemberFieldInput(index: number, field: 'name' | 'typeRef' | 'initialValue', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return {
        ...type,
        members: (type.members ?? []).map((member, i) =>
          i === index
            ? { ...member, [field]: field === 'initialValue' && value === '' ? null : value }
            : member),
      };
    });
  }

  onAddMember(): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      const members = type.members ?? [];
      return { ...type, members: [...members, { name: `field${members.length + 1}`, typeRef: '' }] };
    });
  }

  onRemoveMember(index: number): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      const members = type.members ?? [];
      return members.length <= 1 ? type : { ...type, members: members.filter((_, i) => i !== index) };
    });
  }

  onCreateParameter(nameInput: HTMLInputElement, typeRefInput: HTMLInputElement): void {
    const name = nameInput.value.trim();
    if (!name) {
      return;
    }
    this.addToSelectedSystem('parameter', { name, parameterTypeRef: typeRefInput.value.trim() });
    this.creating.set(null);
  }

  onCreateContainer(nameInput: HTMLInputElement): void {
    const name = nameInput.value.trim();
    if (!name) {
      return;
    }
    this.addToSelectedSystem('container', { name, entryList: [] });
    this.creating.set(null);
  }

  onCreateMessage(
    nameInput: HTMLInputElement,
    containerRefInput: HTMLInputElement,
    matchParameterInput: HTMLInputElement,
    matchValueInput: HTMLInputElement
  ): void {
    const name = nameInput.value.trim();
    const matchParameter = matchParameterInput.value.trim();
    if (!name || !matchParameter) {
      return; // MessageType REQUIRES a MatchCriteria — a match parameter is mandatory.
    }
    const matchValue = matchValueInput.value.trim() || '0';
    this.addToSelectedSystem('message', {
      name,
      containerRef: containerRefInput.value.trim(),
      matchCriteria: { comparison: { parameterRef: matchParameter, value: matchValue } },
    });
    this.creating.set(null);
  }

  onBlockStepRefInput(index: number, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const block = item as BlockMetaCommandDoc;
      const steps = [...(block.steps ?? [])];
      steps[index] = { ...steps[index], metaCommandRef: value };
      return { ...block, steps };
    });
  }

  onCreateMetaCommand(nameInput: HTMLInputElement): void {
    const name = nameInput.value.trim();
    if (!name) {
      return;
    }
    this.addToSelectedSystem('metaCommand', { name });
    this.creating.set(null);
  }

  // --- MetaCommand argument editing -----------------------------------------------------

  onArgumentFieldInput(index: number, field: 'name' | 'argumentTypeRef' | 'initialValue', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const metaCommand = item as MetaCommandDoc;
      return {
        ...metaCommand,
        arguments: (metaCommand.arguments ?? []).map((argument, i) =>
          i === index ? { ...argument, [field]: field === 'initialValue' && value === '' ? null : value } : argument),
      };
    });
  }

  onAddArgument(): void {
    this.mutateSelectedItem((item) => {
      const metaCommand = item as MetaCommandDoc;
      const args = metaCommand.arguments ?? [];
      return { ...metaCommand, arguments: [...args, { name: `arg${args.length + 1}`, argumentTypeRef: '' }] };
    });
  }

  onRemoveArgument(index: number): void {
    this.mutateSelectedItem((item) => {
      const metaCommand = item as MetaCommandDoc;
      const args = (metaCommand.arguments ?? []).filter((_, i) => i !== index);
      return { ...metaCommand, arguments: args.length > 0 ? args : null };
    });
  }

  onLongDescriptionInput(event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.mutateSelectedItem((item) => {
      const current = item as { description?: DescriptionDoc | null };
      return {
        ...current,
        description: { ...(current.description ?? {}), longDescription: value === '' ? null : value },
      };
    });
  }

  protected aliasSummary(description: DescriptionDoc | null | undefined): string {
    return (description?.aliases ?? []).map((a) => `${a.nameSpace}: ${a.alias}`).join(', ');
  }

  // --- Header editing --------------------------------------------------------------------

  onHeaderFieldInput(field: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection) {
      return;
    }
    this.setDocument(updateNodeAtPath(doc, selection.systemPath, (system) => ({
      ...system,
      header: { ...(system.header ?? {}), [field]: value === '' ? null : value },
    })));
  }

  onAddHeader(): void {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection) {
      return;
    }
    this.setDocument(updateNodeAtPath(doc, selection.systemPath, (system) =>
      system.header ? system : { ...system, header: { validationStatus: 'Working' } }));
  }

  onMessageMatchInput(field: 'parameterRef' | 'value', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const message = item as MessageDoc;
      if (!message.matchCriteria?.comparison) {
        return message;
      }
      return {
        ...message,
        matchCriteria: {
          ...message.matchCriteria,
          comparison: { ...message.matchCriteria.comparison, [field]: value },
        },
      };
    });
  }

  // --- Telemetry item editing ----------------------------------------------------------

  onItemFieldInput(field: string, event: Event): void {
    const target = event.target as HTMLInputElement;
    const value: string | boolean = target.type === 'checkbox' ? target.checked : target.value;
    this.mutateSelectedItem((item) => ({
      ...item,
      [field]: typeof value === 'string' && value === '' && field !== 'name' ? null : value,
    }));
  }

  onItemNumberFieldInput(field: string, event: Event): void {
    const raw = (event.target as HTMLInputElement).value.trim();
    const parsed = raw === '' ? null : Number(raw);
    this.mutateSelectedItem((item) => ({
      ...item,
      [field]: parsed !== null && Number.isFinite(parsed) ? parsed : null,
    }));
  }

  onDeleteSelectedItem(): void {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection?.item) {
      return;
    }
    this.setDocument(deleteItemAtSelection(doc, selection));
    this.selection.set({ systemPath: selection.systemPath });
  }

  // --- Data encoding editing -----------------------------------------------------------

  /** The XSD gives the six non-time scalar kinds a data-encoding choice. */
  protected canHaveEncoding(kind: ParameterTypeKind): boolean {
    return ['Integer', 'Float', 'String', 'Boolean', 'Enumerated', 'Binary'].includes(kind);
  }

  onEncodingFieldInput(field: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      if (!type.dataEncoding) {
        return type;
      }
      return { ...type, dataEncoding: { ...type.dataEncoding, [field]: value === '' ? null : value } };
    });
  }

  onEncodingNumberFieldInput(field: string, event: Event): void {
    const raw = (event.target as HTMLInputElement).value.trim();
    const parsed = raw === '' ? null : Number(raw);
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      if (!type.dataEncoding) {
        return type;
      }
      return {
        ...type,
        dataEncoding: { ...type.dataEncoding, [field]: parsed !== null && Number.isFinite(parsed) ? parsed : null },
      };
    });
  }

  // --- MathOperation postfix editing (#127) ---------------------------------------------

  private static defaultMathTerm(kind: MathOperationTermDoc['kind']): MathOperationTermDoc {
    switch (kind) {
      case 'ThisParameter':
        return { kind };
      case 'ParameterInstanceRef':
        return { kind, instanceRef: { parameterRef: '' } };
      default:
        return { kind, text: '' };
    }
  }

  private static editMathTerm(
    terms: MathOperationTermDoc[], index: number, value: string
  ): MathOperationTermDoc[] {
    const next = [...terms];
    const term = next[index];
    next[index] = term.kind === 'ParameterInstanceRef'
      ? { ...term, instanceRef: { ...(term.instanceRef ?? { parameterRef: '' }), parameterRef: value } }
      : { ...term, text: value };
    return next;
  }

  private static moveMathTerm(
    terms: MathOperationTermDoc[], index: number, delta: number
  ): MathOperationTermDoc[] {
    const target = index + delta;
    if (target < 0 || target >= terms.length) {
      return terms;
    }
    const next = [...terms];
    const [term] = next.splice(index, 1);
    next.splice(target, 0, term);
    return next;
  }

  private mutateCalibratorMathTerms(update: (terms: MathOperationTermDoc[]) => MathOperationTermDoc[]): void {
    this.mutateCalibrator((calibrator) => ({ ...calibrator, mathTerms: update(calibrator.mathTerms ?? []) }));
  }

  onCalibratorMathTermKind(index: number, event: Event): void {
    const kind = (event.target as HTMLSelectElement).value as MathOperationTermDoc['kind'];
    this.mutateCalibratorMathTerms((terms) =>
      terms.map((term, i) => (i === index ? App.defaultMathTerm(kind) : term)));
  }

  onCalibratorMathTermValue(index: number, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateCalibratorMathTerms((terms) => App.editMathTerm(terms, index, value));
  }

  onAddCalibratorMathTerm(): void {
    this.mutateCalibratorMathTerms((terms) => [...terms, App.defaultMathTerm('Value')]);
  }

  onRemoveCalibratorMathTerm(index: number): void {
    this.mutateCalibratorMathTerms((terms) => terms.filter((_, i) => i !== index));
  }

  onMoveCalibratorMathTerm(index: number, delta: number): void {
    this.mutateCalibratorMathTerms((terms) => App.moveMathTerm(terms, index, delta));
  }

  private mutateAlgorithmMathTerms(update: (terms: MathOperationTermDoc[]) => MathOperationTermDoc[]): void {
    this.mutateSelectedItem((item) => {
      const algorithm = item as AlgorithmDoc;
      if (!algorithm.mathOperation) {
        return algorithm;
      }
      return {
        ...algorithm,
        mathOperation: { ...algorithm.mathOperation, terms: update(algorithm.mathOperation.terms ?? []) },
      };
    });
  }

  onAlgorithmMathTermKind(index: number, event: Event): void {
    const kind = (event.target as HTMLSelectElement).value as MathOperationTermDoc['kind'];
    this.mutateAlgorithmMathTerms((terms) =>
      terms.map((term, i) => (i === index ? App.defaultMathTerm(kind) : term)));
  }

  onAlgorithmMathTermValue(index: number, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateAlgorithmMathTerms((terms) => App.editMathTerm(terms, index, value));
  }

  onAddAlgorithmMathTerm(): void {
    this.mutateAlgorithmMathTerms((terms) => [...terms, App.defaultMathTerm('Value')]);
  }

  onRemoveAlgorithmMathTerm(index: number): void {
    this.mutateAlgorithmMathTerms((terms) => terms.filter((_, i) => i !== index));
  }

  onMoveAlgorithmMathTerm(index: number, delta: number): void {
    this.mutateAlgorithmMathTerms((terms) => App.moveMathTerm(terms, index, delta));
  }

  onAlgorithmMathOutputInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const algorithm = item as AlgorithmDoc;
      if (!algorithm.mathOperation) {
        return algorithm;
      }
      return { ...algorithm, mathOperation: { ...algorithm.mathOperation, outputParameterRef: value } };
    });
  }

  private mutateCalibrator(update: (calibrator: CalibratorDoc) => CalibratorDoc | null): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      if (!type.dataEncoding?.defaultCalibrator) {
        return type;
      }
      return {
        ...type,
        dataEncoding: { ...type.dataEncoding, defaultCalibrator: update(type.dataEncoding.defaultCalibrator) },
      };
    });
  }

  onAddCalibrator(kindSelect: HTMLSelectElement): void {
    const kind = kindSelect.value as CalibratorDoc['kind'];
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      if (!type.dataEncoding || type.dataEncoding.defaultCalibrator) {
        return type;
      }
      const calibrator: CalibratorDoc = kind === 'Polynomial'
        ? { kind, terms: [{ coefficient: '1', exponent: '1' }] }
        : { kind, points: [{ raw: '0', calibrated: '0' }, { raw: '1', calibrated: '1' }] };
      return { ...type, dataEncoding: { ...type.dataEncoding, defaultCalibrator: calibrator } };
    });
  }

  onRemoveCalibrator(): void {
    this.mutateCalibrator(() => null);
  }

  onCalibratorRowInput(list: 'terms' | 'points', index: number, field: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateCalibrator((calibrator) => ({
      ...calibrator,
      [list]: ((calibrator[list] ?? []) as Record<string, unknown>[]).map((row, i) =>
        i === index ? { ...row, [field]: value } : row),
    }));
  }

  onAddCalibratorRow(list: 'terms' | 'points'): void {
    this.mutateCalibrator((calibrator) => ({
      ...calibrator,
      [list]: [
        ...((calibrator[list] ?? []) as Record<string, unknown>[]),
        list === 'terms' ? { coefficient: '0', exponent: '0' } : { raw: '0', calibrated: '0' },
      ],
    }));
  }

  onRemoveCalibratorRow(list: 'terms' | 'points', index: number): void {
    this.mutateCalibrator((calibrator) => ({
      ...calibrator,
      [list]: ((calibrator[list] ?? []) as Record<string, unknown>[]).filter((_, i) => i !== index),
    }));
  }

  onCalibratorFieldInput(field: 'splineOrder' | 'extrapolate', event: Event): void {
    const target = event.target as HTMLInputElement;
    this.mutateCalibrator((calibrator) => {
      if (field === 'extrapolate') {
        return { ...calibrator, extrapolate: target.checked };
      }
      const raw = target.value.trim();
      const parsed = raw === '' ? null : Number(raw);
      return { ...calibrator, splineOrder: parsed !== null && Number.isFinite(parsed) ? parsed : null };
    });
  }

  // --- Numeric alarm editing --------------------------------------------------------------

  protected readonly alarmRangeKeys = ['watchRange', 'warningRange', 'distressRange', 'criticalRange', 'severeRange'] as const;

  protected alarmRangeLabel(key: string): string {
    return key.replace('Range', '');
  }

  protected alarmRange(alarm: NumericAlarmDoc, key: string): AlarmRangeDoc | null {
    return (alarm[key] as AlarmRangeDoc | null) ?? null;
  }

  private mutateAlarm(update: (alarm: NumericAlarmDoc) => NumericAlarmDoc | null): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      if (!type.defaultAlarm) {
        return type;
      }
      return { ...type, defaultAlarm: update(type.defaultAlarm) };
    });
  }

  onAddAlarm(): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return type.defaultAlarm ? type : { ...type, defaultAlarm: { hasStaticRanges: true } };
    });
  }

  onRemoveAlarm(): void {
    this.mutateAlarm(() => null);
  }

  onAlarmRangeInput(rangeKey: string, field: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateAlarm((alarm) => {
      const range = { ...((alarm[rangeKey] as AlarmRangeDoc | null) ?? {}), [field]: value === '' ? null : value };
      const empty = !range.minInclusive && !range.minExclusive && !range.maxInclusive && !range.maxExclusive;
      return { ...alarm, hasStaticRanges: true, [rangeKey]: empty ? null : range };
    });
  }

  onAlarmFieldInput(field: 'rangeForm' | 'minViolations', event: Event): void {
    const raw = (event.target as HTMLInputElement).value.trim();
    this.mutateAlarm((alarm) => field === 'rangeForm'
      ? { ...alarm, rangeForm: raw === '' ? null : raw }
      : { ...alarm, minViolations: raw !== '' && Number.isFinite(Number(raw)) ? Number(raw) : null });
  }

  onAddEncoding(kindSelect: HTMLSelectElement): void {
    const kind = kindSelect.value as DataEncodingDoc['kind'];
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return type.dataEncoding ? type : { ...type, dataEncoding: { kind } };
    });
  }

  onRemoveEncoding(): void {
    this.mutateSelectedItem((item) => ({ ...(item as ParameterTypeDoc), dataEncoding: null }));
  }

  // --- Unit set editing ------------------------------------------------------------------

  onUnitFieldInput(index: number, field: 'value' | 'description', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return {
        ...type,
        unitSet: (type.unitSet ?? []).map((unit, i) =>
          i === index ? { ...unit, [field]: field === 'description' && value === '' ? null : value } : unit),
      };
    });
  }

  onAddUnit(): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return { ...type, unitSet: [...(type.unitSet ?? []), { value: '' }] };
    });
  }

  onRemoveUnit(index: number): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return { ...type, unitSet: (type.unitSet ?? []).filter((_, i) => i !== index) };
    });
  }

  // --- Time encoding editing ---------------------------------------------------------------

  onTimeEncodingFieldInput(field: 'units' | 'scale' | 'offset', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      if (!type.timeEncoding) {
        return type;
      }
      return { ...type, timeEncoding: { ...type.timeEncoding, [field]: value === '' ? null : value } };
    });
  }

  // --- Parameter properties editing ----------------------------------------------------------

  onParameterPropertyChange(field: 'dataSource' | 'readOnly', event: Event): void {
    const raw = (event.target as HTMLSelectElement).value;
    const value: string | boolean | null =
      raw === '' ? null : field === 'readOnly' ? raw === 'true' : raw;
    this.mutateSelectedItem((item) => {
      const parameter = item as ParameterDoc;
      return { ...parameter, properties: { ...(parameter.properties ?? {}), [field]: value } };
    });
  }

  // --- Algorithm input/output editing ----------------------------------------------------

  onAlgorithmRefInput(list: 'inputs' | 'outputs', index: number, field: 'parameterRef' | 'name', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const algorithm = item as AlgorithmDoc;
      return {
        ...algorithm,
        [list]: (algorithm[list] ?? []).map((entry, i) =>
          i === index ? { ...entry, [field]: field === 'name' && value === '' ? null : value } : entry),
      };
    });
  }

  onAddAlgorithmRef(list: 'inputs' | 'outputs'): void {
    this.mutateSelectedItem((item) => {
      const algorithm = item as AlgorithmDoc;
      return { ...algorithm, [list]: [...(algorithm[list] ?? []), { parameterRef: '' }] };
    });
  }

  onRemoveAlgorithmRef(list: 'inputs' | 'outputs', index: number): void {
    this.mutateSelectedItem((item) => {
      const algorithm = item as AlgorithmDoc;
      return { ...algorithm, [list]: (algorithm[list] ?? []).filter((_, i) => i !== index) };
    });
  }

  // --- Enumeration list editing --------------------------------------------------------

  onAddEnumeration(): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      const enumerations = type.enumerations ?? [];
      const nextValue = enumerations.length === 0
        ? 0
        : Math.max(...enumerations.map((e) => e.value)) + 1;
      return { ...type, enumerations: [...enumerations, { value: nextValue, label: `LABEL_${nextValue}` }] };
    });
  }

  onRemoveEnumeration(index: number): void {
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return { ...type, enumerations: (type.enumerations ?? []).filter((_, i) => i !== index) };
    });
  }

  onEnumerationFieldInput(index: number, field: 'value' | 'label', event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const type = item as ParameterTypeDoc;
      return {
        ...type,
        enumerations: (type.enumerations ?? []).map((entry, i) => {
          if (i !== index) {
            return entry;
          }
          if (field === 'value') {
            const parsed = Number(raw);
            return { ...entry, value: Number.isFinite(parsed) ? parsed : entry.value };
          }
          return { ...entry, label: raw };
        }),
      };
    });
  }

  // --- Container entry-list editing -----------------------------------------------------

  onAddEntry(kindSelect: HTMLSelectElement, refInput: HTMLInputElement): void {
    const reference = refInput.value.trim();
    if (!reference) {
      return;
    }
    const kind = kindSelect.value as 'ParameterRef' | 'ContainerRef';
    this.mutateSelectedContainer((container) => ({
      ...container,
      entryList: [...container.entryList, { kind, ref: reference }],
    }));
    refInput.value = '';
  }

  onEntryRefInput(index: number, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedContainer((container) => ({
      ...container,
      entryList: container.entryList.map((entry, i) => (i === index ? { ...entry, ref: value } : entry)),
    }));
  }

  onRemoveEntry(index: number): void {
    this.mutateSelectedContainer((container) => ({
      ...container,
      entryList: container.entryList.filter((_, i) => i !== index),
    }));
  }

  onMoveEntry(index: number, delta: number): void {
    this.mutateSelectedContainer((container) => moveEntry(container, index, delta));
  }

  onBaseContainerRefInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedContainer((container) => ({
      ...container,
      // Spread the existing baseContainer so its RestrictionCriteria (and preserved
      // content) survive a ref edit.
      baseContainer: { ...(container.baseContainer ?? {}), containerRef: value },
    }));
  }

  onAddBaseContainer(): void {
    this.mutateSelectedContainer((container) =>
      container.baseContainer ? container : { ...container, baseContainer: { containerRef: '' } });
  }

  onRemoveBaseContainer(): void {
    this.mutateSelectedContainer((container) => ({ ...container, baseContainer: null }));
  }

  /**
   * Comparisons shown in the criteria editor: the single-Comparison and ComparisonList
   * model shapes flatten to one array here; the editor re-normalizes on write (one row ->
   * Comparison, several -> ComparisonList).
   */
  protected criteriaComparisons(container: SequenceContainerDoc): ComparisonDoc[] {
    const criteria = container.baseContainer?.restrictionCriteria;
    if (!criteria) {
      return [];
    }
    return criteria.comparison ? [criteria.comparison] : (criteria.comparisonList ?? []);
  }

  private mutateCriteriaComparisons(update: (comparisons: ComparisonDoc[]) => ComparisonDoc[]): void {
    this.mutateSelectedContainer((container) => {
      if (!container.baseContainer) {
        return container;
      }
      const criteria: RestrictionCriteriaDoc = container.baseContainer.restrictionCriteria ?? {};
      const updated = update(this.criteriaComparisons(container));
      return {
        ...container,
        baseContainer: {
          ...container.baseContainer,
          restrictionCriteria: {
            ...criteria,
            comparison: updated.length === 1 ? updated[0] : null,
            comparisonList: updated.length > 1 ? updated : null,
          },
        },
      };
    });
  }

  onAddCriteriaComparison(): void {
    this.mutateCriteriaComparisons((comparisons) => [...comparisons, { parameterRef: '', value: '' }]);
  }

  onRemoveCriteriaComparison(index: number): void {
    this.mutateCriteriaComparisons((comparisons) => comparisons.filter((_, i) => i !== index));
  }

  onCriteriaComparisonInput(index: number, field: 'parameterRef' | 'value' | 'comparisonOperator', event: Event): void {
    const raw = (event.target as HTMLInputElement | HTMLSelectElement).value;
    this.mutateCriteriaComparisons((comparisons) =>
      comparisons.map((comparison, i) =>
        i === index
          ? { ...comparison, [field]: field === 'comparisonOperator' && raw === '==' ? null : raw }
          : comparison));
  }

  onNextContainerInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();
    this.mutateSelectedContainer((container) => {
      if (!container.baseContainer) {
        return container;
      }
      const criteria: RestrictionCriteriaDoc = container.baseContainer.restrictionCriteria ?? {};
      return {
        ...container,
        baseContainer: {
          ...container.baseContainer,
          restrictionCriteria: { ...criteria, nextContainerRef: value === '' ? null : value },
        },
      };
    });
  }

  private mutateSelectedContainer(updater: (container: SequenceContainerDoc) => SequenceContainerDoc): void {
    const selection = this.selection();
    if (selection?.item?.kind !== 'container') {
      return;
    }
    this.mutateSelectedItem((item) => updater(item as SequenceContainerDoc));
  }

  protected constraintSummary(constraint: TransmissionConstraintDoc): string {
    const parts: string[] = [];
    if (constraint.comparison) {
      parts.push(`${constraint.comparison.parameterRef} ${constraint.comparison.comparisonOperator ?? '=='} ${constraint.comparison.value}`);
    }
    if (constraint.comparisonList?.length) {
      parts.push(`${constraint.comparisonList.length} comparison(s)`);
    }
    if (constraint.timeOut) {
      parts.push(`timeout ${constraint.timeOut}`);
    }
    return parts.length > 0 ? parts.join(' — ') : 'criteria preserved as XML';
  }

  protected verifierSummary(verifier: CommandVerifierDoc): string {
    const parts: string[] = [];
    if (verifier.comparison) {
      parts.push(`${verifier.comparison.parameterRef} ${verifier.comparison.comparisonOperator ?? '=='} ${verifier.comparison.value}`);
    }
    if (verifier.comparisonList?.length) {
      parts.push(`${verifier.comparisonList.length} comparison(s)`);
    }
    if (verifier.containerRef) {
      parts.push(`container ${verifier.containerRef}`);
    }
    if (verifier.hasCheckWindow && verifier.timeToStopChecking) {
      parts.push(`window ≤ ${verifier.timeToStopChecking}`);
    }
    return parts.length > 0 ? parts.join(' — ') : 'check preserved as XML';
  }

  /** "Channel=3, Mode=SAFE" for a block step's argument assignments; '' when none. */
  protected stepAssignmentSummary(step: MetaCommandStepDoc): string {
    return (step.argumentAssignments ?? [])
      .map((a) => `${a.argumentName}=${a.argumentValue}`)
      .join(', ');
  }


  /** Flattened rows of an expression tree: junction headers and editable Condition leaves. */
  protected expressionRows(root: BooleanExpressionNodeDoc):
    { junction?: string; depth: number; node?: BooleanExpressionNodeDoc; leafIndex: number }[] {
    const rows: { junction?: string; depth: number; node?: BooleanExpressionNodeDoc; leafIndex: number }[] = [];
    let leafIndex = 0;
    const walk = (node: BooleanExpressionNodeDoc, depth: number) => {
      if (node.kind === 'Condition') {
        rows.push({ depth, node, leafIndex: leafIndex++ });
        return;
      }
      rows.push({ junction: node.kind === 'And' ? 'all of (AND):' : 'any of (OR):', depth, leafIndex: -1 });
      for (const child of node.children ?? []) {
        walk(child, depth + 1);
      }
    };
    walk(root, 0);
    return rows;
  }

  /** Replaces the nth Condition leaf of the tree, keeping the structure intact. */
  private static updateExpressionLeaf(
    root: BooleanExpressionNodeDoc, targetLeaf: number,
    update: (leaf: BooleanExpressionNodeDoc) => BooleanExpressionNodeDoc
  ): BooleanExpressionNodeDoc {
    let leafIndex = 0;
    const walk = (node: BooleanExpressionNodeDoc): BooleanExpressionNodeDoc => {
      if (node.kind === 'Condition') {
        return leafIndex++ === targetLeaf ? update(node) : node;
      }
      return { ...node, children: (node.children ?? []).map(walk) };
    };
    return walk(root);
  }

  onMessageExpressionLeafInput(leafIndex: number, field: 'parameterRef' | 'operator' | 'rhs', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateSelectedItem((item) => {
      const message = item as MessageDoc;
      const expression = message.matchCriteria?.booleanExpression;
      if (!expression) {
        return message;
      }
      const updated = App.updateExpressionLeaf(expression, leafIndex, (leaf) => {
        switch (field) {
          case 'parameterRef':
            return { ...leaf, left: { ...(leaf.left ?? { parameterRef: '' }), parameterRef: value } };
          case 'operator':
            return { ...leaf, operator: value };
          default:
            // RHS keeps its form: a parameter-to-parameter condition edits the ref,
            // otherwise the literal Value.
            return leaf.right
              ? { ...leaf, right: { ...leaf.right, parameterRef: value } }
              : { ...leaf, value };
        }
      });
      return {
        ...message,
        matchCriteria: { ...message.matchCriteria, booleanExpression: updated },
      };
    });
  }

  /** "Apid != 101", "(A and B)", "(A or B or C)" for a modeled expression tree. */
  protected expressionText(node: BooleanExpressionNodeDoc): string {
    if (node.kind === 'Condition') {
      const rhs = node.right ? node.right.parameterRef : node.value;
      return `${node.left?.parameterRef} ${node.operator} ${rhs}`;
    }
    const joiner = node.kind === 'And' ? ' and ' : ' or ';
    return '(' + (node.children ?? []).map((child) => this.expressionText(child)).join(joiner) + ')';
  }

  /** The condition half of a context summary: comparison, list, expression, or preserved. */
  private matchConditionText(criteria: MatchCriteriaDoc | null | undefined): string {
    const match = criteria?.comparison;
    if (match) {
      return `${match.parameterRef} ${match.comparisonOperator ?? '=='} ${match.value}`;
    }
    if (criteria?.comparisonList?.length) {
      return `${criteria.comparisonList.length} comparison(s)`;
    }
    if (criteria?.booleanExpression) {
      return this.expressionText(criteria.booleanExpression);
    }
    return 'match preserved as XML';
  }

  /** "when Mode == 1 → PolynomialCalibrator (2 terms)" for one context-calibrator entry. */
  protected contextCalibratorSummary(entry: ContextCalibratorDoc): string {
    if (entry.rawXml) {
      return 'entry preserved as XML';
    }
    const condition = this.matchConditionText(entry.context);
    const calibrator = entry.calibrator
      ? entry.calibrator.kind === 'Polynomial'
        ? `PolynomialCalibrator (${(entry.calibrator.terms ?? []).length} term(s))`
        : `SplineCalibrator (${(entry.calibrator.points ?? []).length} point(s))`
      : '';
    return `when ${condition} → ${calibrator}`;
  }

  /** "when Mode == 1 → warning, critical (min violations 3)" for one context-alarm entry. */
  protected contextAlarmSummary(entry: ContextNumericAlarmDoc): string {
    if (entry.rawXml) {
      return 'entry preserved as XML';
    }
    const condition = this.matchConditionText(entry.context);
    const alarm = entry.alarm;
    const levels = (['watch', 'warning', 'distress', 'critical', 'severe'] as const)
      .filter((level) => alarm?.[`${level}Range`]);
    const ranges = alarm?.hasStaticRanges ? levels.join(', ') : 'ranges preserved as XML';
    const violations = alarm?.minViolations ? ` (min violations ${alarm.minViolations})` : '';
    return `when ${condition} → ${ranges}${violations}`;
  }

  /** Summary lines for a non-numeric DefaultAlarm (#119): rows, conditions, attributes. */
  protected nonNumericAlarmSummary(alarm: NonNumericAlarmDoc): string[] {
    const lines: string[] = [];
    for (const row of alarm.enumerationAlarms ?? []) {
      lines.push(`${row.enumerationLabel} → ${row.alarmLevel}`);
    }
    for (const row of alarm.stringAlarms ?? []) {
      lines.push(`/${row.matchPattern}/ → ${row.alarmLevel}`);
    }
    const conditions = alarm.conditions;
    for (const level of ['watch', 'warning', 'distress', 'critical', 'severe'] as const) {
      const criteria = conditions?.[level];
      if (!criteria) {
        continue;
      }
      const match = criteria.comparison;
      const text = match
        ? `${match.parameterRef} ${match.comparisonOperator ?? '=='} ${match.value}`
        : criteria.comparisonList?.length
          ? `${criteria.comparisonList.length} comparison(s)`
          : 'condition preserved as XML';
      lines.push(`${level} when ${text}`);
    }
    const attributes: string[] = [];
    if (alarm.defaultAlarmLevel) {
      attributes.push(`default level ${alarm.defaultAlarmLevel}`);
    }
    if (alarm.minViolations) {
      attributes.push(`min violations ${alarm.minViolations}`);
    }
    if (attributes.length > 0) {
      lines.push(attributes.join(', '));
    }
    return lines.length > 0 ? lines : ['alarm details preserved as XML'];
  }

  /** "when Mode == 1: FAILED → warning; default level watch" for one non-numeric context alarm. */
  protected nonNumericContextAlarmSummary(entry: ContextNonNumericAlarmDoc): string {
    if (entry.rawXml) {
      return 'entry preserved as XML';
    }
    const condition = this.matchConditionText(entry.context);
    const body = entry.alarm ? this.nonNumericAlarmSummary(entry.alarm).join('; ') : '';
    return `when ${condition}: ${body}`;
  }

  /** "when Mode == 1 → critical — thruster fire" for one context-significance entry. */
  protected contextSignificanceSummary(entry: ContextSignificanceDoc): string {
    if (entry.rawXml) {
      return 'entry preserved as XML';
    }
    const condition = this.matchConditionText(entry.context);
    const significance = entry.significance;
    const level = significance?.consequenceLevel ?? 'normal';
    const reason = significance?.reasonForWarning ? ` — ${significance.reasonForWarning}` : '';
    return `when ${condition} → ${level}${reason}`;
  }

  /** "epoch TAI" or "offset from Seconds (instance -1, raw)" for a time type's ReferenceTime. */
  protected referenceTimeSummary(referenceTime: ReferenceTimeDoc): string {
    if (referenceTime.offsetFromParameterRef) {
      const details: string[] = [];
      if (referenceTime.offsetFromInstance != null) {
        details.push(`instance ${referenceTime.offsetFromInstance}`);
      }
      if (referenceTime.offsetFromUseCalibratedValue === false) {
        details.push('raw');
      }
      const suffix = details.length > 0 ? ` (${details.join(', ')})` : '';
      return `offset from ${referenceTime.offsetFromParameterRef}${suffix}`;
    }
    return `epoch ${referenceTime.epoch}`;
  }

  /** "this Gain * 1.5 +" — the postfix program of a MathOperation term list. */
  protected mathTermsText(terms: MathOperationTermDoc[] | null | undefined): string {
    return (terms ?? []).map((term) => {
      switch (term.kind) {
        case 'ThisParameter':
          return 'this';
        case 'ParameterInstanceRef':
          return term.instanceRef?.parameterRef ?? '?';
        default:
          return term.text ?? '';
      }
    }).join(' ');
  }

  /** Compact annotation for modeled entry mechanics (#109): location / repeat / condition. */
  protected entryMechanics(entry: SequenceEntryDoc): string {
    const parts: string[] = [];
    if (entry.location) {
      parts.push(`@ bit ${entry.location.fixedValue}${entry.location.referenceLocation ? ' from ' + entry.location.referenceLocation : ''}`);
    }
    if (entry.repeat) {
      parts.push(`×${entry.repeat.fixedCount}`);
    }
    if (entry.includeCondition) {
      const match = entry.includeCondition.comparison;
      parts.push(match ? `if ${match.parameterRef} ${match.comparisonOperator ?? '=='} ${match.value}` : 'conditional');
    }
    return parts.join(' ');
  }

  protected entryLabel(entry: SequenceEntryDoc): string {
    switch (entry.kind) {
      case 'ParameterRef':
        return 'param';
      case 'ContainerRef':
        return 'container';
      case 'ArgumentRef':
        return 'argument';
      case 'FixedValue':
        return 'fixed';
      default:
        return entry.rawXml?.elementName ?? 'other';
    }
  }

  // --- Command container entry editing ---------------------------------------------------

  private mutateCommandEntryList(update: (entries: SequenceEntryDoc[]) => SequenceEntryDoc[]): void {
    this.mutateSelectedItem((item) => {
      const metaCommand = item as MetaCommandDoc;
      if (!metaCommand.commandContainer) {
        return metaCommand;
      }
      return {
        ...metaCommand,
        commandContainer: {
          ...metaCommand.commandContainer,
          entryList: update(metaCommand.commandContainer.entryList ?? []),
        },
      };
    });
  }

  onCommandEntryFieldInput(index: number, field: 'ref' | 'binaryValue' | 'name', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.mutateCommandEntryList((entries) => entries.map((entry, i) =>
      i === index ? { ...entry, [field]: field !== 'ref' && value === '' ? null : value } : entry));
  }

  onCommandEntryNumberFieldInput(index: number, field: 'sizeInBits', event: Event): void {
    const raw = (event.target as HTMLInputElement).value.trim();
    const parsed = raw === '' ? null : Number(raw);
    this.mutateCommandEntryList((entries) => entries.map((entry, i) =>
      i === index ? { ...entry, [field]: parsed !== null && Number.isFinite(parsed) ? parsed : null } : entry));
  }

  onAddCommandEntry(kindSelect: HTMLSelectElement, refInput: HTMLInputElement): void {
    const kind = kindSelect.value as SequenceEntryDoc['kind'];
    const reference = refInput.value.trim();
    this.mutateCommandEntryList((entries) => [
      ...entries,
      kind === 'FixedValue'
        ? { kind, binaryValue: reference || '00', sizeInBits: 8 }
        : { kind, ref: reference },
    ]);
    refInput.value = '';
  }

  onRemoveCommandEntry(index: number): void {
    this.mutateCommandEntryList((entries) => entries.filter((_, i) => i !== index));
  }

  onMoveCommandEntry(index: number, delta: number): void {
    this.mutateCommandEntryList((entries) => {
      const target = index + delta;
      if (target < 0 || target >= entries.length) {
        return entries;
      }
      const next = [...entries];
      const [entry] = next.splice(index, 1);
      next.splice(target, 0, entry);
      return next;
    });
  }

  // --- Save / search / revalidation ----------------------------------------------------

  /** Save writes the current TEXT: in source view the editor bytes verbatim (parseable
   *  or not), in tree view the serialization of the edits — the same bytes toggling to
   *  source would show. */
  onSaveDocument(): void {
    this.saveError.set(null);
    if (this.viewMode() === 'source') {
      const text = this.sourceView()?.currentText() ?? this.sourceText();
      if (!text) {
        return;
      }
      const name = this.currentDocument()?.name ?? this.selectedFileName() ?? 'document';
      this.downloadXml(text, /\.(xml|xtce)$/.test(name) ? name : `${name}.xml`);
      return;
    }
    const doc = this.currentDocument();
    if (!doc) {
      return;
    }
    this.http.post(
      '/api/xtce/save',
      doc,
      { responseType: 'text' }
    ).subscribe({
      next: (xml) => this.downloadXml(xml, `${doc.name}.xml`),
      error: () => this.saveError.set('Failed to save document.'),
    });
  }

  onTreeSearchInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.treeSearchTerm.set(input.value);
    this.scheduleSearch(input.value);
  }

  /** Debounced backend search: alias-aware and cross-tree, unlike the client-side tree filter. */
  private scheduleSearch(query: string): void {
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
    if (query.trim() === '') {
      this.searchMatches.set(null);
      return;
    }
    this.searchTimer = setTimeout(() => {
      this.searchTimer = null;
      const doc = this.currentDocument();
      if (!doc) {
        return;
      }
      this.http.post<{ matches: SearchMatch[] }>('/api/xtce/search', { document: doc, query }).subscribe({
        next: (result) => this.searchMatches.set(result.matches ?? []),
        error: () => this.searchMatches.set(null),
      });
    }, App.revalidateDelayMs);
  }

  /** Maps a backend search match (name-based paths) onto a tree Selection and selects it. */
  onSelectSearchMatch(match: SearchMatch): void {
    const doc = this.currentDocument();
    if (!doc) {
      return;
    }
    const path: number[] = [];
    let node = doc;
    for (const segment of match.systemPath.split('/').slice(1)) {
      const index = node.children.findIndex((child) => child.name === segment);
      if (index < 0) {
        return;
      }
      path.push(index);
      node = node.children[index];
    }

    const lists: Record<SearchMatch['kind'], { kind: ItemKind; items: { name: string }[] | undefined }> = {
      Parameter: { kind: 'parameter', items: node.telemetryMetaData?.parameterSet },
      ParameterType: { kind: 'parameterType', items: node.telemetryMetaData?.parameterTypeSet },
      Container: { kind: 'container', items: node.telemetryMetaData?.containerSet ?? undefined },
      Message: { kind: 'message', items: node.telemetryMetaData?.messageSet?.messages },
      MetaCommand: { kind: 'metaCommand', items: node.commandMetaData?.metaCommands },
      BlockMetaCommand: { kind: 'blockMetaCommand', items: node.commandMetaData?.blockMetaCommands ?? undefined },
      ArgumentType: { kind: 'argumentType', items: node.commandMetaData?.argumentTypeSet ?? undefined },
      CommandParameterType: { kind: 'commandParameterType', items: node.commandMetaData?.parameterTypeSet ?? undefined },
      CommandParameter: { kind: 'commandParameter', items: node.commandMetaData?.parameterSet ?? undefined },
      Algorithm: { kind: 'algorithm', items: node.telemetryMetaData?.algorithmSet ?? undefined },
      CommandAlgorithm: { kind: 'commandAlgorithm', items: node.commandMetaData?.algorithmSet ?? undefined },
      CommandContainer: { kind: 'commandContainer', items: node.commandMetaData?.commandContainerSet ?? undefined },
      Stream: { kind: 'stream', items: node.telemetryMetaData?.streamSet ?? undefined },
      Service: { kind: 'service', items: node.serviceSet ?? undefined },
    };
    const target = lists[match.kind];
    const itemIndex = target.items?.findIndex((item) => item.name === match.name) ?? -1;
    this.onSelect(itemIndex >= 0
      ? { systemPath: path, item: { kind: target.kind, index: itemIndex } }
      : { systemPath: path });
  }

  /** The selected system's name path ("Root/Bus") — the backend's SystemPath convention. */
  private selectedSystemNamePath(): string | null {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection) {
      return null;
    }
    const names = [doc.name];
    let node = doc;
    for (const index of selection.systemPath) {
      node = node.children[index];
      if (!node) {
        return null;
      }
      names.push(node.name);
    }
    return names.join('/');
  }

  onExportCsv(what: 'parameters' | 'containers'): void {
    const doc = this.currentDocument();
    if (!doc) {
      return;
    }
    this.http.post(`/api/xtce/export/${what}`, doc, { responseType: 'text' }).subscribe({
      next: (csv) => this.downloadBlob(csv, 'text/csv', `${doc.name}-${what}.csv`),
      error: () => this.saveError.set('Failed to export CSV.'),
    });
  }

  onFindUsages(): void {
    const doc = this.currentDocument();
    const parameter = this.selectedParameter();
    const systemPath = this.selectedSystemNamePath();
    if (!doc || !parameter || !systemPath) {
      return;
    }
    this.http.post<{ usages: UsageMatch[] }>('/api/xtce/usages', {
      document: doc,
      systemPath,
      parameterName: parameter.name,
    }).subscribe({
      next: (result) => this.parameterUsages.set(result.usages ?? []),
      error: () => this.parameterUsages.set(null),
    });
  }

  private mutateSelectedSystem(updater: (system: SpaceSystemDocument) => SpaceSystemDocument): void {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item) {
      return;
    }
    this.setDocument(updateNodeAtPath(doc, selection.systemPath, updater));
  }

  private mutateSelectedItem(updater: (item: object) => object): void {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection?.item) {
      return;
    }
    this.setDocument(updateItemAtSelection(doc, selection, updater as (item: never) => unknown));
  }

  private addToSelectedSystem(kind: ItemKind, item: TelemetryItem): void {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item) {
      return;
    }
    this.setDocument(addItemToSystem(doc, selection.systemPath, kind, item));
  }

  /** Every document mutation flows through here so revalidation can't be forgotten. */
  private setDocument(doc: SpaceSystemDocument): void {
    this.currentDocument.set(doc);
    this.packetLayout.set(null); // any edit invalidates a computed layout
    this.conformanceReport.set(null); // ...and any computed conformance report
    this.documentMetrics.set(null);
    this.parameterUsages.set(null);
    this.searchMatches.set(null);
    this.scheduleRevalidate();
  }

  private scheduleRevalidate(): void {
    if (this.revalidateTimer !== null) {
      clearTimeout(this.revalidateTimer);
    }
    this.revalidateTimer = setTimeout(() => {
      this.revalidateTimer = null;
      const doc = this.currentDocument();
      if (!doc) {
        return;
      }
      try {
        this.http.post<{ validationIssues: ValidationIssue[] }>('/api/xtce/validate', doc).subscribe({
          next: (result) => this.validationIssues.set(result.validationIssues ?? []),
          error: () => {
            // Keep the last known issues; a transient validate failure shouldn't blank the panel.
          },
        });
      } catch {
        // The app (or a test fixture) was torn down before the debounce fired.
      }
    }, App.revalidateDelayMs);
  }

  private downloadXml(xml: string, filename: string): void {
    this.downloadBlob(xml, 'application/xml', filename);
  }

  private downloadBlob(content: string, contentType: string, filename: string): void {
    const blob = new Blob([content], { type: contentType });
    const url = URL.createObjectURL(blob);

    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.click();

    URL.revokeObjectURL(url);
  }
}
