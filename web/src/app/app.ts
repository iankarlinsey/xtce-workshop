import { Component, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { EditableTreeNodeComponent } from './editable-tree-node/editable-tree-node';
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
  moveEntry,
} from './document-tree';
import { ValidationIssue } from './validation';

type HealthStatus = 'checking' | 'ok' | 'unreachable';

interface LoadResult {
  name: string;
  document: SpaceSystemDocument;
  validationIssues: ValidationIssue[];
}

@Component({
  selector: 'app-root',
  imports: [EditableTreeNodeComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  /** Debounce for live revalidation; tests may shorten it. */
  static revalidateDelayMs = 400;

  private readonly http = inject(HttpClient);
  private revalidateTimer: ReturnType<typeof setTimeout> | null = null;

  protected readonly healthStatus = signal<HealthStatus>('checking');
  protected readonly selectedFileName = signal<string | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly treeSearchTerm = signal('');

  protected readonly currentDocument = signal<SpaceSystemDocument | null>(null);
  protected readonly selection = signal<Selection | null>(null);
  protected readonly saveError = signal<string | null>(null);
  protected readonly validationIssues = signal<ValidationIssue[]>([]);

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

  constructor() {
    this.http.get('/api/health').subscribe({
      next: () => this.healthStatus.set('ok'),
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
        this.currentDocument.set(result.document);
        this.selection.set({ systemPath: [] });
        this.validationIssues.set(result.validationIssues ?? []);
      },
      error: (err) => this.loadError.set(err?.error?.error ?? 'Failed to load file.'),
    });
  }

  onNewDocument(): void {
    const name = window.prompt('Name for the new SpaceSystem:');
    if (!name) {
      return;
    }

    this.currentDocument.set({ name, children: [] });
    this.selection.set({ systemPath: [] });
    this.saveError.set(null);
    this.validationIssues.set([]);
  }

  onSelect(selection: Selection): void {
    this.selection.set(selection);
  }

  // --- SpaceSystem editing -------------------------------------------------------------

  onSelectedNameInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.mutateSelectedSystem((system) => ({ ...system, name: input.value }));
  }

  onAddChildToSelected(): void {
    const name = window.prompt('Name for the new child SpaceSystem:');
    if (!name) {
      return;
    }
    this.mutateSelectedSystem((system) => ({
      ...system,
      children: [...system.children, { name, children: [] }],
    }));
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

  onAddParameterType(kindSelect: HTMLSelectElement): void {
    const name = window.prompt('Name for the new parameter type:');
    if (!name) {
      return;
    }
    const kind = kindSelect.value as ParameterTypeKind;
    const item: ParameterTypeDoc = { name, kind };
    if (kind === 'Enumerated') {
      item.enumerations = [];
    } else if (kind === 'Array') {
      // arrayTypeRef is required and an empty one wouldn't validate — prompt for it and
      // seed one 0..0 dimension (DimensionList requires at least one).
      const elementType = window.prompt('Element type ref (arrayTypeRef):');
      if (!elementType) {
        return;
      }
      item.arrayTypeRef = elementType;
      item.dimensions = [{ startingIndex: { fixedValue: 0 }, endingIndex: { fixedValue: 0 } }];
    } else if (kind === 'Aggregate') {
      // MemberList requires at least one Member with a valid typeRef.
      const memberType = window.prompt('Type ref for the first member:');
      if (!memberType) {
        return;
      }
      item.members = [{ name: 'field1', typeRef: memberType }];
    }
    this.addToSelectedSystem('parameterType', item);
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

  onAddParameter(): void {
    const name = window.prompt('Name for the new parameter:');
    if (!name) {
      return;
    }
    const typeRef = window.prompt('parameterTypeRef (name of a parameter type):') ?? '';
    this.addToSelectedSystem('parameter', { name, parameterTypeRef: typeRef });
  }

  onAddContainer(): void {
    const name = window.prompt('Name for the new container:');
    if (!name) {
      return;
    }
    this.addToSelectedSystem('container', { name, entryList: [] });
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

  private addToSelectedSystem(kind: ItemKind, item: ParameterTypeDoc | ParameterDoc | SequenceContainerDoc): void {
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
    const blob = new Blob([xml], { type: 'application/xml' });
    const url = URL.createObjectURL(blob);

    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.click();

    URL.revokeObjectURL(url);
  }
}
