import { Component, computed, input, signal } from '@angular/core';

export interface TreeNode {
  label: string;
  nodeType: string;
  children: TreeNode[];
}

/**
 * Renders a TreeNode recursively. Deliberately generic — this component only knows
 * about {label, nodeType, children}, nothing SpaceSystem-specific, so it doesn't need
 * new code as the backend starts projecting more XTCE construct types into the tree.
 */
@Component({
  selector: 'app-tree-node',
  imports: [TreeNodeComponent],
  templateUrl: './tree-node.html',
  styleUrl: './tree-node.css',
})
export class TreeNodeComponent {
  readonly node = input.required<TreeNode>();
  readonly searchTerm = input<string>('');

  protected readonly expanded = signal(true);

  protected toggle(): void {
    this.expanded.set(!this.expanded());
  }

  // A node is visible if it matches the search itself, or any descendant does — so a
  // matching node's ancestors stay visible even when their own labels don't match.
  protected readonly isVisible = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    return !term || matchesOrHasMatch(this.node(), term);
  });

  // While searching, force every visible branch open so matches aren't hidden behind a
  // manual collapse from before the search started — this ignores the user's own
  // expanded/collapsed toggle state only for the duration of an active search term.
  protected readonly effectiveExpanded = computed(() =>
    this.searchTerm().trim() ? true : this.expanded()
  );
}

function matchesOrHasMatch(node: TreeNode, lowerCaseTerm: string): boolean {
  if (node.label.toLowerCase().includes(lowerCaseTerm)) {
    return true;
  }
  return node.children.some((child) => matchesOrHasMatch(child, lowerCaseTerm));
}
