import { Component, ElementRef, OnDestroy, effect, input, viewChild } from '@angular/core';
import { EditorView, basicSetup } from 'codemirror';
import { Text } from '@codemirror/state';
import { xml } from '@codemirror/lang-xml';
import { setDiagnostics, lintGutter, Diagnostic } from '@codemirror/lint';
import { LoadDiagnostic } from '../validation';

/**
 * Maps positioned load diagnostics onto CodeMirror lint markers. Lines/columns are
 * clamped to the document — a diagnostic can reference a position past the end when
 * the text was edited after the diagnostics were produced.
 */
export function mapDiagnosticsToMarkers(doc: Text, diagnostics: LoadDiagnostic[]): Diagnostic[] {
  return diagnostics
    .filter((diagnostic) => diagnostic.line !== null && diagnostic.line !== undefined)
    .map((diagnostic) => {
      const lineNumber = Math.max(1, Math.min(diagnostic.line!, doc.lines));
      const line = doc.line(lineNumber);
      const column = Math.max(0, Math.min((diagnostic.column ?? 1) - 1, line.length));
      const from = line.from + column;
      return {
        from,
        to: Math.min(from + 1, line.to),
        severity: 'error' as const,
        message: diagnostic.path ? `${diagnostic.path}: ${diagnostic.message}` : diagnostic.message,
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
  /** Positioned load diagnostics rendered as error markers in the gutter and text. */
  readonly diagnostics = input<LoadDiagnostic[]>([]);

  private readonly host = viewChild<ElementRef<HTMLDivElement>>('host');
  private view: EditorView | null = null;
  private lastAppliedText: string | null = null;

  constructor() {
    effect(() => {
      const text = this.text();
      const diagnostics = this.diagnostics();
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
        setDiagnostics(this.view.state, mapDiagnosticsToMarkers(this.view.state.doc, diagnostics))
      );
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
    },
    { dark: true }
  );
}
