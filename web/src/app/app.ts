import { Component, CUSTOM_ELEMENTS_SCHEMA, computed, inject, signal, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { EditableTreeNodeComponent } from './editable-tree-node/editable-tree-node';
import { PreservedXmlComponent } from './preserved-xml/preserved-xml';
import { SourceViewComponent } from './source-view/source-view';
import {
  SpaceSystemDocument,
  ParameterTypeDoc,
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
  moveEntry,
} from './document-tree';
import { ValidationIssue, PacketLayout, ConformanceReport, CandidateStatus, DocumentMetrics, SearchMatch, UsageMatch, LoadDiagnostic } from './validation';
import { XTCE_REFERENCE, ReferenceEntry } from './xtce-reference';

type HealthStatus = 'checking' | 'ok' | 'unreachable';

interface LoadResult {
  name: string;
  document: SpaceSystemDocument;
  validationIssues: ValidationIssue[];
  diagnostics?: LoadDiagnostic[];
  schemaErrors?: string[];
  rootNamespace?: string | null;
  detectedVersion?: string | null;
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
  protected readonly loadError = signal<string | null>(null);
  protected readonly treeSearchTerm = signal('');

  /** Which inline creator row is open (one at a time), or null. */
  protected readonly creating = signal<
    'document' | 'child' | 'parameter' | 'container' | 'message' | 'metaCommand' | 'parameterType' | null
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
  protected readonly loadSchemaErrors = signal<string[]>([]);
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

  protected readonly selectedParameterType = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item?.kind !== 'parameterType') {
      return null;
    }
    return getItemAtSelection(doc, selection) as ParameterTypeDoc | null;
  });

  protected readonly selectedParameter = computed(() => {
    const doc = this.currentDocument();
    const selection = this.selection();
    if (!doc || !selection || selection.item?.kind !== 'parameter') {
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
      case 'parameter':
        return XTCE_REFERENCE['Parameter'] ?? null;
      case 'container':
        return XTCE_REFERENCE['SequenceContainer'] ?? null;
      case 'message':
        return XTCE_REFERENCE['Message'] ?? null;
      case 'metaCommand':
        return XTCE_REFERENCE['MetaCommand'] ?? null;
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
    this.saveError.set(null);
    this.validationIssues.set([]);
    this.treeSearchTerm.set('');

    const formData = new FormData();
    formData.append('file', file);

    this.http.post<LoadResult>('/api/xtce/load', formData).subscribe({
      // A loaded file becomes the current editable/saveable document immediately. The
      // document object is passed through Save wholesale (and mutated only via spreads),
      // which is what carries the backend's preserved raw-XML fields through untouched —
      // see document-tree.ts.
      next: (result) => {
        if (!result?.document) {
          // A 200 whose body isn't our shape (e.g. an intermediary proxy/auth layer
          // answering in the app's place) must never leave the UI silently empty.
          this.loadError.set('The server response did not contain a document — '
            + 'something between the browser and the API may have intercepted the request.');
          return;
        }
        this.applyLoadResult(result);
        this.sourceText.set('');
        this.viewMode.set('tree');
      },
      error: (err) => {
        this.loadError.set(err?.error?.error ?? 'Failed to load file.');
        this.loadDiagnostics.set(err?.error?.diagnostics ?? []);
        this.loadSchemaErrors.set(err?.error?.schemaErrors ?? []);
        this.rootNamespace.set(err?.error?.rootNamespace ?? null);
        this.detectedVersion.set(err?.error?.detectedVersion ?? null);
        if (err?.error?.diagnostics) {
          // The server understood the request and rejected the CONTENT — open the file's
          // text in source view so the problem can be fixed here instead of elsewhere.
          file.text().then((text) => {
            this.sourceText.set(text);
            this.viewMode.set('source');
          });
        }
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
      },
      error: () => this.saveError.set('Failed to serialize the document for source view.'),
    });
  }

  /** Leaving source view IS the re-parse: the editor text becomes the document, or the
   *  view stays put with positioned diagnostics when it can't. */
  onShowTree(): void {
    if (this.viewMode() === 'tree') {
      return;
    }
    const text = this.sourceView()?.currentText() ?? this.sourceText();
    this.loadError.set(null);
    this.http.post<LoadResult>('/api/xtce/load-text', { xml: text }).subscribe({
      next: (result) => {
        if (!result?.document) {
          this.loadError.set('The server response did not contain a document.');
          return;
        }
        this.applyLoadResult(result);
        this.sourceText.set('');
        this.viewMode.set('tree');
      },
      error: (err) => {
        this.loadError.set(err?.error?.error ?? 'The source text could not be parsed.');
        this.loadDiagnostics.set(err?.error?.diagnostics ?? []);
        this.loadSchemaErrors.set(err?.error?.schemaErrors ?? []);
        this.sourceText.set(text);
      },
    });
  }

  onOpenCreator(kind: 'document' | 'child' | 'parameter' | 'container' | 'message' | 'metaCommand' | 'parameterType'): void {
    this.creating.set(this.creating() === kind ? null : kind);
  }

  onCancelCreator(): void {
    this.creating.set(null);
  }

  onCreateDocument(nameInput: HTMLInputElement): void {
    const name = nameInput.value.trim();
    if (!name) {
      return;
    }
    this.currentDocument.set({ name, children: [] });
    this.selection.set({ systemPath: [] });
    this.saveError.set(null);
    this.validationIssues.set([]);
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

  onCreateParameterType(nameInput: HTMLInputElement, kindSelect: HTMLSelectElement, refInput: HTMLInputElement): void {
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
    this.addToSelectedSystem('parameterType', item);
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
      preserved: [{
        elementName: 'MatchCriteria',
        outerXml: `<MatchCriteria xmlns="http://www.omg.org/spec/XTCE/20180204"><Comparison parameterRef="${matchParameter}" value="${matchValue}"/></MatchCriteria>`,
      }],
    });
    this.creating.set(null);
  }

  onCreateMetaCommand(nameInput: HTMLInputElement): void {
    const name = nameInput.value.trim();
    if (!name) {
      return;
    }
    this.addToSelectedSystem('metaCommand', { name });
    this.creating.set(null);
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

  protected entryLabel(entry: SequenceEntryDoc): string {
    switch (entry.kind) {
      case 'ParameterRef':
        return 'param';
      case 'ContainerRef':
        return 'container';
      default:
        return entry.rawXml?.elementName ?? 'other';
    }
  }

  // --- Save / search / revalidation ----------------------------------------------------

  onSaveDocument(): void {
    const doc = this.currentDocument();
    if (!doc) {
      return;
    }

    this.saveError.set(null);

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
