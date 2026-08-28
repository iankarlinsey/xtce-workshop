import { Component, computed, effect, input, output, signal, untracked } from '@angular/core';
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
      { kind: 'algorithm', label: 'Algorithms', names: (telemetry?.algorithmSet ?? []).map((a) => a.name) },
      { kind: 'commandParameterType', label: 'Command Parameter Types', names: (node.commandMetaData?.parameterTypeSet ?? []).map((t) => t.name) },
      { kind: 'commandParameter', label: 'Command Parameters', names: (node.commandMetaData?.parameterSet ?? []).map((p) => p.name) },
      { kind: 'argumentType', label: 'Argument Types', names: (node.commandMetaData?.argumentTypeSet ?? []).map((t) => t.name) },
      { kind: 'metaCommand', label: 'Commands', names: (node.commandMetaData?.metaCommands ?? []).map((m) => m.name) },
      { kind: 'commandAlgorithm', label: 'Command Algorithms', names: (node.commandMetaData?.algorithmSet ?? []).map((a) => a.name) },
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

  /** The selection this node last reacted to — auto-expansion fires once per change. */
  private lastSeenSelection: Selection | null = null;

  constructor() {
    // A selection ARRIVING in this node (finding/search navigation) must not hide
    // inside a collapsed group, so its group auto-expands — once per selection change.
    // An explicit header click afterwards still collapses it (issue #99): the click
    // isn't fought by a standing pin, because this reacts to selection changes only.
    effect(() => {
      const selection = this.selection();
      untracked(() => {
        if (selectionsEqual(selection, this.lastSeenSelection)) {
          return;
        }
        this.lastSeenSelection = selection;
        if (
          selection?.item &&
          selection.systemPath.length === this.path().length &&
          selection.systemPath.every((index, i) => index === this.path()[i]) &&
          !this.expandedGroups().has(selection.item.kind)
        ) {
          this.expandedGroups.set(new Set([...this.expandedGroups(), selection.item.kind]));
        }
      });
    });
  }

  protected isGroupExpanded(kind: ItemKind): boolean {
    if (this.searchTerm().trim()) {
      return true; // searching shows the matches, not the collapse state
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
    ...(telemetry?.algorithmSet ?? []).map((a) => a.name),
    ...(node.commandMetaData?.algorithmSet ?? []).map((a) => a.name),
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
