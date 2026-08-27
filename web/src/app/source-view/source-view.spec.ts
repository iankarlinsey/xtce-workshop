import { TestBed } from '@angular/core/testing';
import { Text } from '@codemirror/state';
import { SourceViewComponent, mapMarkersToDiagnostics } from './source-view';
import { SourceMarker } from '../validation';

describe('mapMarkersToDiagnostics', () => {
  const doc = Text.of(['<SpaceSystem name="Sat">', '  <Unclosed>', '</SpaceSystem>']);

  it('maps a line/column marker to the exact document position with its severity', () => {
    const markers = mapMarkersToDiagnostics(doc, [
      { line: 2, column: 3, message: '(document): boom', severity: 'error' },
      { line: 1, column: 1, message: 'Sat: advisory', severity: 'warning' },
    ]);

    expect(markers.length).toBe(2);
    expect(markers[0].from).toBe(doc.line(2).from + 2);
    expect(markers[0].severity).toBe('error');
    expect(markers[0].message).toContain('boom');
    expect(markers[1].severity).toBe('warning');
  });

  it('drops unpositioned markers and clamps positions past the document end', () => {
    const markers = mapMarkersToDiagnostics(doc, [
      { line: null, column: null, message: 'no position', severity: 'error' },
      { line: 99, column: 500, message: 'past the end', severity: 'error' },
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

  it('marks finding positions in the lint gutter', () => {
    const fixture = TestBed.createComponent(SourceViewComponent);
    fixture.componentRef.setInput('text', '<SpaceSystem name="Sat">\n  <Unclosed>');
    const markers: SourceMarker[] = [
      { line: 2, column: 3, message: '(document): unexpected end of file', severity: 'error' },
    ];
    fixture.componentRef.setInput('markers', markers);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.cm-gutter-lint .cm-lint-marker')).toBeTruthy();
  });

  it('scrolls to the reveal target line and moves the cursor there', () => {
    const fixture = TestBed.createComponent(SourceViewComponent);
    fixture.componentRef.setInput('text', 'line one\nline two\nline three');
    fixture.detectChanges();

    fixture.componentRef.setInput('revealTarget', { line: 3, column: null, nonce: 1 });
    fixture.detectChanges();

    const view = (fixture.componentInstance as unknown as { view: { state: { selection: { main: { head: number } }, doc: Text } } }).view;
    expect(view.state.doc.lineAt(view.state.selection.main.head).number).toBe(3);
  });
});
