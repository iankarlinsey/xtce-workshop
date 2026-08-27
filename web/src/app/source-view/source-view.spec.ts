import { TestBed } from '@angular/core/testing';
import * as monaco from 'monaco-editor/editor/editor.api';
import { SourceViewComponent, mapMarkersToMonaco } from './source-view';
import { SourceMarker } from '../validation';

describe('mapMarkersToMonaco', () => {
  // Three lines: 24, 12, and 14 characters.
  const model = {
    getLineCount: () => 3,
    getLineMaxColumn: (line: number) => [25, 13, 15][line - 1],
  };

  it('maps a line/column marker to the exact position with its severity', () => {
    const markers = mapMarkersToMonaco(model, [
      { line: 2, column: 3, message: '(document): boom', severity: 'error' },
      { line: 1, column: 1, message: 'Sat: advisory', severity: 'warning' },
    ]);

    expect(markers.length).toBe(2);
    expect(markers[0].startLineNumber).toBe(2);
    expect(markers[0].startColumn).toBe(3);
    expect(markers[0].severity).toBe(monaco.MarkerSeverity.Error);
    expect(markers[0].message).toContain('boom');
    expect(markers[1].severity).toBe(monaco.MarkerSeverity.Warning);
  });

  it('drops unpositioned markers and clamps positions past the document end', () => {
    const markers = mapMarkersToMonaco(model, [
      { line: null, column: null, message: 'no position', severity: 'error' },
      { line: 99, column: 500, message: 'past the end', severity: 'error' },
    ]);

    expect(markers.length).toBe(1);
    expect(markers[0].startLineNumber).toBe(3);
    expect(markers[0].startColumn).toBe(15);
    expect(markers[0].endColumn).toBeLessThanOrEqual(15);
  });
});

describe('SourceViewComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SourceViewComponent] }).compileComponents();
  });

  afterEach(() => {
    monaco.editor.getModels().forEach((model) => model.dispose());
  });

  it('renders the text in a Monaco editor and reports live contents', () => {
    const fixture = TestBed.createComponent(SourceViewComponent);
    fixture.componentRef.setInput('text', '<SpaceSystem name="Sat"/>');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.monaco-editor')).toBeTruthy();
    expect(fixture.componentInstance.currentText()).toBe('<SpaceSystem name="Sat"/>');
  });

  it('replaces the editor contents when the text input changes', () => {
    const fixture = TestBed.createComponent(SourceViewComponent);
    fixture.componentRef.setInput('text', '<a/>');
    fixture.detectChanges();
    fixture.componentRef.setInput('text', '<b/>');
    fixture.detectChanges();

    expect(fixture.componentInstance.currentText()).toBe('<b/>');
  });

  it('publishes finding positions as model markers', () => {
    const fixture = TestBed.createComponent(SourceViewComponent);
    fixture.componentRef.setInput('text', '<SpaceSystem name="Sat">\n  <Unclosed>');
    const markers: SourceMarker[] = [
      { line: 2, column: 3, message: '(document): unexpected end of file', severity: 'error' },
    ];
    fixture.componentRef.setInput('markers', markers);
    fixture.detectChanges();

    const published = monaco.editor.getModelMarkers({ owner: 'xtce-workshop' });
    expect(published.length).toBe(1);
    expect(published[0].startLineNumber).toBe(2);
    expect(published[0].severity).toBe(monaco.MarkerSeverity.Error);
  });

  it('moves the cursor to the reveal target line and column', () => {
    const fixture = TestBed.createComponent(SourceViewComponent);
    fixture.componentRef.setInput('text', 'line one\nline two\nline three');
    fixture.detectChanges();

    fixture.componentRef.setInput('revealTarget', { line: 3, column: 6, nonce: 1 });
    fixture.detectChanges();

    const editor = (fixture.componentInstance as unknown as {
      editor: monaco.editor.IStandaloneCodeEditor;
    }).editor;
    expect(editor.getPosition()?.lineNumber).toBe(3);
    expect(editor.getPosition()?.column).toBe(6);
  });
});
