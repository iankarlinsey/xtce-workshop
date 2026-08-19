import { Component, computed, input, output, signal } from '@angular/core';

export interface SpaceSystemDocument {
  name: string;
  children: SpaceSystemDocument[];
}

/**
 * Renders one SpaceSystemDocument node with Add child / Rename / Delete actions.
 * Unlike TreeNodeComponent (generic, read-only), this is deliberately
 * SpaceSystem-specific — editing is a domain operation, not a generic tree concern.
 * TreeNodeComponent stays in the codebase for future read-only content (e.g. construct
 * types that don't support editing yet); this component is what's actually wired into
 * the app now that a "current document" is always the editable source of truth.
 *
 * Mutations flow up via nodeChange: each level updates its own copy immutably and emits
 * it to its parent, which folds the update into its own copy and re-emits — so the root
 * (App) always receives one coherent updated document, not a path to mutate in place.
 */
@Component({
  selector: 'app-editable-tree-node',
  imports: [EditableTreeNodeComponent],
  templateUrl: './editable-tree-node.html',
  styleUrl: './editable-tree-node.css',
})
export class EditableTreeNodeComponent {
  readonly node = input.required<SpaceSystemDocument>();
  readonly isRoot = input<boolean>(false);
  readonly searchTerm = input<string>('');

  readonly nodeChange = output<SpaceSystemDocument>();
  readonly deleteRequested = output<void>();

  protected readonly expanded = signal(true);

  protected readonly isVisible = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    return !term || matchesOrHasMatch(this.node(), term);
  });

  protected readonly effectiveExpanded = computed(() =>
    this.searchTerm().trim() ? true : this.expanded()
  );

  protected toggle(): void {
    this.expanded.set(!this.expanded());
  }

  protected onAddChild(): void {
    const name = window.prompt('Name for the new child SpaceSystem:');
    if (!name) {
      return;
    }

    this.nodeChange.emit({
      ...this.node(),
      children: [...this.node().children, { name, children: [] }],
    });
  }

  protected onRename(): void {
    const name = window.prompt('New name:', this.node().name);
    if (!name) {
      return;
    }

    this.nodeChange.emit({ ...this.node(), name });
  }

  protected onChildChange(index: number, updatedChild: SpaceSystemDocument): void {
    const children = [...this.node().children];
    children[index] = updatedChild;
    this.nodeChange.emit({ ...this.node(), children });
  }

  protected onChildDelete(index: number): void {
    const children = this.node().children.filter((_, i) => i !== index);
    this.nodeChange.emit({ ...this.node(), children });
  }
}

function matchesOrHasMatch(node: SpaceSystemDocument, lowerCaseTerm: string): boolean {
  if (node.name.toLowerCase().includes(lowerCaseTerm)) {
    return true;
  }
  return node.children.some((child) => matchesOrHasMatch(child, lowerCaseTerm));
}
