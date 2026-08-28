import { Component, computed, input, output, signal } from '@angular/core';
import {
  SpaceSystemDocument,
  NodePath,
  Selection,
  ItemKind,
  selectionsEqual,
} from '../document-tree';

export type { SpaceSystemDocument } from '../document-tree';

interface ItemGroup {
  kind: ItemKind;
  label: string;
  items: { index: number; name: string }[];
}

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
    return node.children.length > 0 || this.groups().length > 0;
  });

  /** Per-kind collapsible groups of item rows; empty kinds produce no group. */
  protected readonly groups = computed<ItemGroup[]>(() => {
    const node = this.node();
    const telemetry = node.telemetryMetaData;
    const term = this.searchTerm().trim().toLowerCase();
    const sources: { kind: ItemKind; label: string; names: string[] }[] = [
      { kind: 'parameterType', label: 'Parameter Types', names: (telemetry?.parameterTypeSet ?? []).map((t) => t.name) },
      { kind: 'parameter', label: 'Parameters', names: (telemetry?.parameterSet ?? []).map((p) => p.name) },
      { kind: 'container', label: 'Containers', names: (telemetry?.containerSet ?? []).map((c) => c.name) },
      { kind: 'message', label: 'Messages', names: (telemetry?.messageSet?.messages ?? []).map((m) => m.name) },
      { kind: 'commandParameterType', label: 'Command Parameter Types', names: (node.commandMetaData?.parameterTypeSet ?? []).map((t) => t.name) },
      { kind: 'commandParameter', label: 'Command Parameters', names: (node.commandMetaData?.parameterSet ?? []).map((p) => p.name) },
      { kind: 'argumentType', label: 'Argument Types', names: (node.commandMetaData?.argumentTypeSet ?? []).map((t) => t.name) },
      { kind: 'metaCommand', label: 'Commands', names: (node.commandMetaData?.metaCommands ?? []).map((m) => m.name) },
    ];
    return sources
      .map(({ kind, label, names }) => ({
        kind,
        label,
        items: names
          .map((name, index) => ({ index, name }))
          .filter((row) => !term || row.name.toLowerCase().includes(term)),
      }))
      .filter((group) => group.items.length > 0);
  });

  /** Groups the user explicitly opened; everything starts collapsed. */
  private readonly expandedGroups = signal<ReadonlySet<ItemKind>>(new Set());

  protected isGroupExpanded(kind: ItemKind): boolean {
    if (this.searchTerm().trim()) {
      return true; // searching shows the matches, not the collapse state
    }
    const selection = this.selection();
    if (
      selection?.item?.kind === kind &&
      selection.systemPath.length === this.path().length &&
      selection.systemPath.every((index, i) => index === this.path()[i])
    ) {
      return true; // the selected item must never hide inside a collapsed group
    }
    return this.expandedGroups().has(kind);
  }

  protected toggleGroup(kind: ItemKind, event: Event): void {
    event.stopPropagation();
    const next = new Set(this.expandedGroups());
    if (next.has(kind)) {
      next.delete(kind);
    } else {
      next.add(kind);
    }
    this.expandedGroups.set(next);
  }

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
  const itemNames = [
    ...(telemetry?.parameterTypeSet ?? []).map((t) => t.name),
    ...(telemetry?.parameterSet ?? []).map((p) => p.name),
    ...(telemetry?.containerSet ?? []).map((c) => c.name),
    ...(telemetry?.messageSet?.messages ?? []).map((m) => m.name),
    ...(node.commandMetaData?.parameterTypeSet ?? []).map((t) => t.name),
    ...(node.commandMetaData?.parameterSet ?? []).map((p) => p.name),
    ...(node.commandMetaData?.argumentTypeSet ?? []).map((t) => t.name),
    ...(node.commandMetaData?.metaCommands ?? []).map((m) => m.name),
  ];
  if (itemNames.some((name) => name.toLowerCase().includes(lowerCaseTerm))) {
    return true;
  }
  return node.children.some((child) => matchesOrHasMatch(child, lowerCaseTerm));
}
