import { Component, ElementRef, OnDestroy, effect, input, viewChild } from '@angular/core';
import { EditorView, basicSetup } from 'codemirror';
import { Text, EditorSelection } from '@codemirror/state';
import { xml } from '@codemirror/lang-xml';
import { setDiagnostics, lintGutter, Diagnostic } from '@codemirror/lint';
import { SourceMarker } from '../validation';

/**
 * Maps positioned findings onto CodeMirror lint markers. Lines/columns are clamped to
 * the document — a finding can reference a position past the end when the text was
 * edited after the findings were produced. Unpositioned findings produce no marker.
 */
export function mapMarkersToDiagnostics(doc: Text, markers: SourceMarker[]): Diagnostic[] {
  return markers
    .filter((marker) => marker.line !== null && marker.line !== undefined)
    .map((marker) => {
      const lineNumber = Math.max(1, Math.min(marker.line!, doc.lines));
      const line = doc.line(lineNumber);
      const column = Math.max(0, Math.min((marker.column ?? 1) - 1, line.length));
      const from = line.from + column;
      return {
        from,
        to: Math.min(from + 1, line.to),
        severity: marker.severity,
        message: marker.message,
      };
    });
}

@Component({
  selector: 'app-source-view',
  template: '<div class="editor-host" #host></div>',
  styleUrl: './source-view.css',
})
export class SourceViewComponent implements OnDestroy {
  /** Document text to show; replacing it resets the editor contents. */
  readonly text = input<string>('');
  /** Positioned findings of every class, rendered as gutter + underline markers. */
  readonly markers = input<SourceMarker[]>([]);
  /** Line to scroll to; the nonce lets the same line be requested again. */
  readonly revealTarget = input<{ line: number; column: number | null; nonce: number } | null>(null);

  private readonly host = viewChild<ElementRef<HTMLDivElement>>('host');
  private view: EditorView | null = null;
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

      if (!this.view) {
        this.view = new EditorView({
          doc: text,
          extensions: [basicSetup, xml(), lintGutter(), SourceViewComponent.theme],
          parent: host.nativeElement,
        });
        this.lastAppliedText = text;
      } else if (text !== this.lastAppliedText) {
        this.view.dispatch({
          changes: { from: 0, to: this.view.state.doc.length, insert: text },
        });
        this.lastAppliedText = text;
      }

      this.view.dispatch(
        setDiagnostics(this.view.state, mapMarkersToDiagnostics(this.view.state.doc, markers))
      );

      const reveal = this.revealTarget();
      if (reveal && reveal.nonce !== this.lastRevealNonce) {
        this.lastRevealNonce = reveal.nonce;
        const lineNumber = Math.max(1, Math.min(reveal.line, this.view.state.doc.lines));
        const line = this.view.state.doc.line(lineNumber);
        const column = Math.max(0, Math.min((reveal.column ?? 1) - 1, line.length));
        const position = line.from + column;
        this.view.dispatch({
          selection: EditorSelection.cursor(position),
          effects: EditorView.scrollIntoView(position, { y: 'center' }),
        });
      }
    });
  }

  /** The editor's live contents — what "switch to tree" submits for re-parsing. */
  currentText(): string {
    return this.view?.state.doc.toString() ?? this.text();
  }

  ngOnDestroy(): void {
    this.view?.destroy();
    this.view = null;
  }

  private static readonly theme = EditorView.theme(
    {
      '&': {
        backgroundColor: 'var(--panel)',
        color: 'var(--text)',
        height: '100%',
        fontSize: '0.8125rem',
      },
      '.cm-content': { caretColor: 'var(--accent)' },
      '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--accent)' },
      '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': {
        backgroundColor: 'var(--color-background-surface-selected, #1c3f5e)',
      },
      '.cm-gutters': {
        backgroundColor: 'var(--panel-2)',
        color: 'var(--text-faint)',
        border: 'none',
        borderRight: '1px solid var(--border)',
      },
      '.cm-activeLine': { backgroundColor: 'var(--color-background-base-hover, #1b2d3e)' },
      '.cm-activeLineGutter': { backgroundColor: 'var(--color-background-base-hover, #1b2d3e)' },
      '.cm-lintRange-error': {
        backgroundImage: 'none',
        textDecoration: 'underline wavy var(--error, #ff3838)',
      },
      '.cm-lintRange-warning': {
        backgroundImage: 'none',
        textDecoration: 'underline wavy var(--warning, #fce83a)',
      },
    },
    { dark: true }
  );
}
