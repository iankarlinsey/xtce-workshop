import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

type HealthStatus = 'checking' | 'ok' | 'unreachable';

interface LoadResult {
  name: string;
}

@Component({
  selector: 'app-root',
  imports: [],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private readonly http = inject(HttpClient);

  protected readonly healthStatus = signal<HealthStatus>('checking');
  protected readonly selectedFileName = signal<string | null>(null);
  protected readonly loadedName = signal<string | null>(null);
  protected readonly loadError = signal<string | null>(null);

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
    this.loadedName.set(null);
    this.loadError.set(null);

    const formData = new FormData();
    formData.append('file', file);

    this.http.post<LoadResult>('/api/xtce/load', formData).subscribe({
      next: (result) => this.loadedName.set(result.name),
      error: (err) => this.loadError.set(err?.error?.error ?? 'Failed to load file.'),
    });
  }
}
