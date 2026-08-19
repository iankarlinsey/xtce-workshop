import { Component, computed, input, output, signal } from '@angular/core';
import { SpaceSystemDocument, NodePath, pathsEqual } from '../document-tree';

export type { SpaceSystemDocument } from '../document-tree';

/**
 * Renders one SpaceSystemDocument node as a pure navigation/selection tree —
 * clicking a row selects it (emits its path); editing happens in the main panel
 * for whichever node is selected, not here. Unlike TreeNodeComponent (generic,
 * read-only, no selection concept), this is deliberately SpaceSystem-specific,
 * since selection needs a path into the real domain document, not a generic
 * display tree. TreeNodeComponent stays in the codebase for future read-only
 * content that doesn't need selection at all.
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
  readonly selectedPath = input<NodePath | null>(null);
  readonly searchTerm = input<string>('');

  readonly select = output<NodePath>();

  protected readonly expanded = signal(true);

  protected readonly isVisible = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    return !term || matchesOrHasMatch(this.node(), term);
  });

  protected readonly effectiveExpanded = computed(() =>
    this.searchTerm().trim() ? true : this.expanded()
  );

  protected readonly isSelected = computed(() => pathsEqual(this.path(), this.selectedPath()));

  protected toggle(event: Event): void {
    event.stopPropagation();
    this.expanded.set(!this.expanded());
  }

  protected onRowClick(): void {
    this.select.emit(this.path());
  }

  protected onChildSelect(path: NodePath): void {
    this.select.emit(path);
  }

  protected childPath(index: number): NodePath {
    return [...this.path(), index];
  }
}

function matchesOrHasMatch(node: SpaceSystemDocument, lowerCaseTerm: string): boolean {
  if (node.name.toLowerCase().includes(lowerCaseTerm)) {
    return true;
  }
  return node.children.some((child) => matchesOrHasMatch(child, lowerCaseTerm));
}
