import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { EditableTreeNodeComponent, SpaceSystemDocument } from './editable-tree-node/editable-tree-node';

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
  protected readonly saveError = signal<string | null>(null);

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
      next: (result) => this.currentDocument.set(result.document),
      error: (err) => this.loadError.set(err?.error?.error ?? 'Failed to load file.'),
    });
  }

  onNewDocument(): void {
    const name = window.prompt('Name for the new SpaceSystem:');
    if (!name) {
      return;
    }

    this.currentDocument.set({ name, children: [] });
    this.saveError.set(null);
  }

  onDocumentChange(updated: SpaceSystemDocument): void {
    this.currentDocument.set(updated);
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
