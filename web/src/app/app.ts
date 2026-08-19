import { Component, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { EditableTreeNodeComponent } from './editable-tree-node/editable-tree-node';
import {
  SpaceSystemDocument,
  NodePath,
  getNodeAtPath,
  updateNodeAtPath,
  deleteNodeAtPath,
} from './document-tree';

type HealthStatus = 'checking' | 'ok' | 'unreachable';

interface LoadResult {
  name: string;
  document: SpaceSystemDocument;
}

@Component({
  selector: 'app-root',
  imports: [EditableTreeNodeComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private readonly http = inject(HttpClient);

  protected readonly healthStatus = signal<HealthStatus>('checking');
  protected readonly selectedFileName = signal<string | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly treeSearchTerm = signal('');

  protected readonly currentDocument = signal<SpaceSystemDocument | null>(null);
  protected readonly selectedPath = signal<NodePath | null>(null);
  protected readonly saveError = signal<string | null>(null);

  protected readonly selectedNode = computed(() => {
    const doc = this.currentDocument();
    const path = this.selectedPath();
    if (!doc || !path) {
      return null;
    }
    return getNodeAtPath(doc, path);
  });

  protected readonly isRootSelected = computed(() => this.selectedPath()?.length === 0);

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
    this.treeSearchTerm.set('');

    const formData = new FormData();
    formData.append('file', file);

    this.http.post<LoadResult>('/api/xtce/load', formData).subscribe({
      // A loaded file becomes the current editable/saveable document immediately — the
      // same state New already populates, so Save and the tree UI work identically
      // either way. The backend's generic `tree` field (TreeNode) is intentionally
      // unused here — see summary.md's Architecture Decisions: it stays available for
      // future read-only content, but this app now always edits, so the editable tree
      // is the one and only tree UI.
      next: (result) => {
        this.currentDocument.set(result.document);
        this.selectedPath.set([]); // select the root by default so the form isn't empty
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
    this.selectedPath.set([]);
    this.saveError.set(null);
  }

  onNodeSelected(path: NodePath): void {
    this.selectedPath.set(path);
  }

  onSelectedNameInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    const doc = this.currentDocument();
    const path = this.selectedPath();
    if (!doc || !path) {
      return;
    }

    this.currentDocument.set(updateNodeAtPath(doc, path, (node) => ({ ...node, name: input.value })));
  }

  onAddChildToSelected(): void {
    const name = window.prompt('Name for the new child SpaceSystem:');
    if (!name) {
      return;
    }

    const doc = this.currentDocument();
    const path = this.selectedPath();
    if (!doc || !path) {
      return;
    }

    this.currentDocument.set(
      updateNodeAtPath(doc, path, (node) => ({
        ...node,
        children: [...node.children, { name, children: [] }],
      }))
    );
  }

  onDeleteSelected(): void {
    const doc = this.currentDocument();
    const path = this.selectedPath();
    if (!doc || !path || path.length === 0) {
      return; // can't delete the root
    }

    this.currentDocument.set(deleteNodeAtPath(doc, path));
    this.selectedPath.set(path.slice(0, -1)); // select the parent after deleting
  }

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
