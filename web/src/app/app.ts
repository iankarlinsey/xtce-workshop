import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { TreeNode, TreeNodeComponent } from './tree-node/tree-node';

type HealthStatus = 'checking' | 'ok' | 'unreachable';

interface LoadResult {
  name: string;
  tree: TreeNode;
}

interface SpaceSystemDocument {
  name: string;
  children: SpaceSystemDocument[];
}

@Component({
  selector: 'app-root',
  imports: [TreeNodeComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private readonly http = inject(HttpClient);

  protected readonly healthStatus = signal<HealthStatus>('checking');
  protected readonly selectedFileName = signal<string | null>(null);
  protected readonly loadedTree = signal<TreeNode | null>(null);
  protected readonly loadError = signal<string | null>(null);

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
    this.loadedTree.set(null);
    this.loadError.set(null);

    const formData = new FormData();
    formData.append('file', file);

    this.http.post<LoadResult>('/api/xtce/load', formData).subscribe({
      next: (result) => this.loadedTree.set(result.tree),
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
