import { Component, ElementRef, OnDestroy, ViewEncapsulation, effect, input, viewChild } from '@angular/core';
import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/languages/definitions/xml/register';
import { SourceMarker } from '../validation';

// XML colorization is main-thread Monarch; no language workers are needed. Monaco still
// insists on an environment, so hand it an inert worker.
declare global {
  interface Window { MonacoEnvironment?: monaco.Environment; }
}
if (!window.MonacoEnvironment) {
  window.MonacoEnvironment = {
    getWorker: () =>
      new Worker(URL.createObjectURL(new Blob(['self.onmessage=()=>{}'], { type: 'text/javascript' }))),
  };
}

/** Monaco's 350KB stylesheet ships as a static asset, linked once on first use — it
 *  stays out of the initial bundle and out of the component-style budget. */
function ensureMonacoStyles(): void {
  if (!document.getElementById('monaco-editor-styles')) {
    const link = document.createElement('link');
    link.id = 'monaco-editor-styles';
    link.rel = 'stylesheet';
    link.href = 'monaco/editor.main.css';
    document.head.appendChild(link);
  }
}

/**
 * Maps positioned findings onto Monaco model markers. Lines/columns are clamped to the
 * document — a finding can reference a position past the end when the text was edited
 * after the findings were produced. Unpositioned findings produce no marker.
 */
export function mapMarkersToMonaco(
  model: Pick<monaco.editor.ITextModel, 'getLineCount' | 'getLineMaxColumn'>,
  markers: SourceMarker[]
): monaco.editor.IMarkerData[] {
  return markers
    .filter((marker) => marker.line !== null && marker.line !== undefined)
    .map((marker) => {
      const lineNumber = Math.max(1, Math.min(marker.line!, model.getLineCount()));
      const maxColumn = model.getLineMaxColumn(lineNumber);
      const startColumn = Math.max(1, Math.min(marker.column ?? 1, maxColumn));
      return {
        severity: marker.severity === 'warning'
          ? monaco.MarkerSeverity.Warning
          : monaco.MarkerSeverity.Error,
        message: marker.message,
        startLineNumber: lineNumber,
        startColumn,
        endLineNumber: lineNumber,
        endColumn: Math.min(startColumn + 1, maxColumn),
      };
    });
}

monaco.editor.defineTheme('xtce-workshop-dark', {
  base: 'vs-dark',
  inherit: true,
  rules: [
    { token: 'tag', foreground: '7ee787' },
    { token: 'attribute.name', foreground: 'e6edf5' },
    { token: 'attribute.value', foreground: 'ff7b72' },
    { token: 'comment', foreground: '6d8093' },
    { token: 'delimiter', foreground: 'aebfce' },
  ],
  colors: {
    'editor.background': '#111a24',
    'editor.foreground': '#e6edf5',
    'editorLineNumber.foreground': '#4d5c6b',
    'editorLineNumber.activeForeground': '#aebfce',
    'editor.selectionBackground': '#58b6ff59',
    'editor.inactiveSelectionBackground': '#58b6ff33',
    'editor.selectionHighlightBackground': '#56f0001f',
    'editor.lineHighlightBackground': '#58b6ff0d',
    'editorGutter.background': '#15202e',
    'editorCursor.foreground': '#58b6ff',
    'editorError.foreground': '#ff3838',
    'editorWarning.foreground': '#fce83a',
  },
});

@Component({
  selector: 'app-source-view',
  template: '<div class="editor-host" #host></div>',
  styleUrl: './source-view.css',
  // Monaco's DOM lives outside Angular's emulated scoping; its stylesheet (imported in
  // source-view.css) must apply globally, and it ships with this deferred chunk.
  encapsulation: ViewEncapsulation.None,
})
export class SourceViewComponent implements OnDestroy {
  /** Document text to show; replacing it resets the editor contents. */
  readonly text = input<string>('');
  /** Positioned findings of every class, rendered as gutter + underline markers. */
  readonly markers = input<SourceMarker[]>([]);
  /** Line to scroll to; the nonce lets the same line be requested again. */
  readonly revealTarget = input<{ line: number; column: number | null; nonce: number } | null>(null);

  private readonly host = viewChild<ElementRef<HTMLDivElement>>('host');
  private editor: monaco.editor.IStandaloneCodeEditor | null = null;
  private lastAppliedText: string | null = null;
  private lastRevealNonce = 0;

  constructor() {
    effect(() => {
      const text = this.text();
      const markers = this.markers();
      const host = this.host();
      if (!host) {
        return;
      }

      if (!this.editor) {
        ensureMonacoStyles();
        this.editor = monaco.editor.create(host.nativeElement, {
          value: text,
          language: 'xml',
          theme: 'xtce-workshop-dark',
          automaticLayout: true,
          minimap: { enabled: false },
          scrollBeyondLastLine: false,
          fixedOverflowWidgets: true,
          fontSize: 13,
        });
        this.lastAppliedText = text;
      } else if (text !== this.lastAppliedText) {
        this.editor.getModel()!.setValue(text);
        this.lastAppliedText = text;
      }

      const model = this.editor.getModel()!;
      monaco.editor.setModelMarkers(model, 'xtce-workshop', mapMarkersToMonaco(model, markers));

      const reveal = this.revealTarget();
      if (reveal && reveal.nonce !== this.lastRevealNonce) {
        this.lastRevealNonce = reveal.nonce;
        const lineNumber = Math.max(1, Math.min(reveal.line, model.getLineCount()));
        const column = Math.max(1, Math.min(reveal.column ?? 1, model.getLineMaxColumn(lineNumber)));
        this.editor.setPosition({ lineNumber, column });
        this.editor.revealPositionInCenter({ lineNumber, column });
      }
    });
  }

  /** The editor's live contents — what "switch to tree" submits for re-parsing. */
  currentText(): string {
    return this.editor?.getModel()?.getValue() ?? this.text();
  }

  ngOnDestroy(): void {
    this.editor?.dispose();
    this.editor = null;
  }
}
