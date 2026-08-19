import { Component, computed, input, output, signal } from '@angular/core';
import {
  SpaceSystemDocument,
  NodePath,
  Selection,
  ItemKind,
  selectionsEqual,
} from '../document-tree';

export type { SpaceSystemDocument } from '../document-tree';

/**
 * Renders one SpaceSystemDocument node as a pure navigation/selection tree — a row for the
 * SpaceSystem itself, grouped rows for its telemetry items (parameter types, parameters,
 * containers), and recursive child SpaceSystems. Clicking any row emits a Selection;
 * editing happens in the main panel for whichever selection is active, never here.
 */
@Component({
  selector: 'app-editable-tree-node',
  imports: [EditableTreeNodeComponent],
  templateUrl: './editable-tree-node.html',
  styleUrl: './editable-tree-node.css',
})
export class EditableTreeNodeComponent {
  readonly node = input.required<SpaceSystemDocument>();
  readonly path = input<NodePath>([]);
  readonly selection = input<Selection | null>(null);
  readonly searchTerm = input<string>('');

  readonly select = output<Selection>();

  protected readonly expanded = signal(true);

  protected readonly isVisible = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    return !term || matchesOrHasMatch(this.node(), term);
  });

  protected readonly effectiveExpanded = computed(() =>
    this.searchTerm().trim() ? true : this.expanded()
  );

  protected readonly isSelected = computed(() =>
    selectionsEqual({ systemPath: this.path() }, this.selection())
  );

  protected readonly hasExpandableContent = computed(() => {
    const node = this.node();
    return node.children.length > 0 || this.visibleItems().length > 0;
  });

  /** Flattened telemetry item rows, each tagged with its kind and set index. */
  protected readonly visibleItems = computed(() => {
    const telemetry = this.node().telemetryMetaData;
    if (!telemetry) {
      return [];
    }
    const term = this.searchTerm().trim().toLowerCase();
    const rows: { kind: ItemKind; index: number; name: string }[] = [];
    telemetry.parameterTypeSet.forEach((type, index) =>
      rows.push({ kind: 'parameterType', index, name: type.name }));
    telemetry.parameterSet.forEach((parameter, index) =>
      rows.push({ kind: 'parameter', index, name: parameter.name }));
    (telemetry.containerSet ?? []).forEach((container, index) =>
      rows.push({ kind: 'container', index, name: container.name }));
    return term ? rows.filter((row) => row.name.toLowerCase().includes(term)) : rows;
  });

  protected toggle(event: Event): void {
    event.stopPropagation();
    this.expanded.set(!this.expanded());
  }

  protected onRowClick(): void {
    this.select.emit({ systemPath: this.path() });
  }

  protected onItemClick(kind: ItemKind, index: number, event: Event): void {
    event.stopPropagation();
    this.select.emit({ systemPath: this.path(), item: { kind, index } });
  }

  protected isItemSelected(kind: ItemKind, index: number): boolean {
    return selectionsEqual({ systemPath: this.path(), item: { kind, index } }, this.selection());
  }

  protected onChildSelect(selection: Selection): void {
    this.select.emit(selection);
  }

  protected childPath(index: number): NodePath {
    return [...this.path(), index];
  }
}

function matchesOrHasMatch(node: SpaceSystemDocument, lowerCaseTerm: string): boolean {
  if (node.name.toLowerCase().includes(lowerCaseTerm)) {
    return true;
  }
  const telemetry = node.telemetryMetaData;
  if (telemetry) {
    const itemNames = [
      ...telemetry.parameterTypeSet.map((t) => t.name),
      ...telemetry.parameterSet.map((p) => p.name),
      ...(telemetry.containerSet ?? []).map((c) => c.name),
    ];
    if (itemNames.some((name) => name.toLowerCase().includes(lowerCaseTerm))) {
      return true;
    }
  }
  return node.children.some((child) => matchesOrHasMatch(child, lowerCaseTerm));
}
