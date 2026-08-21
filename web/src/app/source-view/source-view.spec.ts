import { TestBed } from '@angular/core/testing';
import { Text } from '@codemirror/state';
import { SourceViewComponent, mapDiagnosticsToMarkers } from './source-view';
import { LoadDiagnostic } from '../validation';

describe('mapDiagnosticsToMarkers', () => {
  const doc = Text.of(['<SpaceSystem name="Sat">', '  <Unclosed>', '</SpaceSystem>']);

  it('maps a line/column diagnostic to the exact document position', () => {
    const markers = mapDiagnosticsToMarkers(doc, [
      { kind: 'MalformedXml', message: 'boom', path: '(document)', line: 2, column: 3 },
    ]);

    expect(markers.length).toBe(1);
    expect(markers[0].from).toBe(doc.line(2).from + 2);
    expect(markers[0].severity).toBe('error');
    expect(markers[0].message).toContain('(document)');
    expect(markers[0].message).toContain('boom');
  });

  it('drops diagnostics without a line and clamps positions past the document end', () => {
    const markers = mapDiagnosticsToMarkers(doc, [
      { kind: 'ModelError', message: 'no position', path: 'Sat', line: null, column: null },
      { kind: 'MalformedXml', message: 'past the end', path: '(document)', line: 99, column: 500 },
    ]);

    expect(markers.length).toBe(1);
    const lastLine = doc.line(doc.lines);
    expect(markers[0].from).toBe(lastLine.from + lastLine.length);
    expect(markers[0].to).toBeLessThanOrEqual(lastLine.to);
  });
});

describe('SourceViewComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [SourceViewComponent] }).compileComponents();
  });

  it('renders the text in a CodeMirror editor and reports live contents', () => {
    const fixture = TestBed.createComponent(SourceViewComponent);
    fixture.componentRef.setInput('text', '<SpaceSystem name="Sat"/>');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.cm-content')?.textContent).toContain('SpaceSystem');
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

  it('marks diagnostic positions in the lint gutter', () => {
    const fixture = TestBed.createComponent(SourceViewComponent);
    fixture.componentRef.setInput('text', '<SpaceSystem name="Sat">\n  <Unclosed>');
    const diagnostics: LoadDiagnostic[] = [
      { kind: 'MalformedXml', message: 'unexpected end of file', path: '(document)', line: 2, column: 3 },
    ];
    fixture.componentRef.setInput('diagnostics', diagnostics);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.cm-gutter-lint .cm-lint-marker')).toBeTruthy();
  });
});
