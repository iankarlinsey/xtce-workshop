import { Component, input, signal } from '@angular/core';

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
  protected readonly expanded = signal(true);

  protected toggle(): void {
    this.expanded.set(!this.expanded());
  }
}
