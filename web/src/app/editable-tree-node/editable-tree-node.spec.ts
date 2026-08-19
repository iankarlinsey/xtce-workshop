import { TestBed } from '@angular/core/testing';
import { EditableTreeNodeComponent } from './editable-tree-node';
import { SpaceSystemDocument } from '../document-tree';

describe('EditableTreeNodeComponent', () => {
  function render(node: SpaceSystemDocument, path: number[] = [], selectedPath: number[] | null = null) {
    const fixture = TestBed.createComponent(EditableTreeNodeComponent);
    fixture.componentRef.setInput('node', node);
    fixture.componentRef.setInput('path', path);
    fixture.componentRef.setInput('selectedPath', selectedPath);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the node name', () => {
    const fixture = render({ name: 'Minimal', children: [] });

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Minimal');
  });

  it('emits its own path when the row is clicked', () => {
    const fixture = render({ name: 'Bus', children: [] }, [1]);
    let emitted: number[] | undefined;
    fixture.componentInstance.select.subscribe((p) => (emitted = p));

    (fixture.nativeElement.querySelector('.tree-node-row') as HTMLElement).click();

    expect(emitted).toEqual([1]);
  });

  it('applies the selected class when its path matches selectedPath', () => {
    const fixture = render({ name: 'Bus', children: [] }, [1], [1]);

    const row = fixture.nativeElement.querySelector('.tree-node-row') as HTMLElement;
    expect(row.classList).toContain('selected');
  });

  it('does not apply the selected class when paths differ', () => {
    const fixture = render({ name: 'Bus', children: [] }, [1], [0]);

    const row = fixture.nativeElement.querySelector('.tree-node-row') as HTMLElement;
    expect(row.classList).not.toContain('selected');
  });

  it('bubbles a child selection up unchanged', () => {
    const fixture = render({
      name: 'Mission',
      children: [{ name: 'Bus', children: [] }],
    });
    let emitted: number[] | undefined;
    fixture.componentInstance.select.subscribe((p) => (emitted = p));

    const childRow = fixture.nativeElement.querySelectorAll('.tree-node-row')[1] as HTMLElement;
    childRow.click();

    expect(emitted).toEqual([0]);
  });

  it('passes the correct path to each child', () => {
    const fixture = render({
      name: 'Mission',
      children: [
        { name: 'Bus', children: [] },
        { name: 'Payload', children: [] },
      ],
    });
    let emitted: number[] | undefined;
    fixture.componentInstance.select.subscribe((p) => (emitted = p));

    const rows = fixture.nativeElement.querySelectorAll('.tree-node-row');
    (rows[2] as HTMLElement).click(); // Mission, Bus, Payload -> index 2 is Payload

    expect(emitted).toEqual([1]);
  });

  it('renders a single node with no children with no toggle button', () => {
    const fixture = render({ name: 'Minimal', children: [] });

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.toggle')).toBeNull();
    expect(compiled.querySelector('.toggle-spacer')).toBeTruthy();
  });

  it('renders nested children recursively, expanded by default', () => {
    const fixture = render({
      name: 'Mission',
      children: [
        { name: 'Bus', children: [
          { name: 'Power', children: [] },
        ] },
        { name: 'Payload', children: [] },
      ],
    });

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Mission');
    expect(compiled.textContent).toContain('Bus');
    expect(compiled.textContent).toContain('Power');
    expect(compiled.textContent).toContain('Payload');
    expect(compiled.querySelectorAll('app-editable-tree-node').length).toBe(3);
  });

  it('collapsing does not trigger a selection (toggle click does not bubble to row)', () => {
    const fixture = render({
      name: 'Mission',
      children: [{ name: 'Bus', children: [] }],
    });
    let emitted: number[] | undefined;
    fixture.componentInstance.select.subscribe((p) => (emitted = p));

    const compiled = fixture.nativeElement as HTMLElement;
    const toggle = compiled.querySelector('.toggle') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    expect(emitted).toBeUndefined();
    expect(compiled.textContent).not.toContain('Bus');
  });

  it('filters out a node whose name does not match the search term', () => {
    const fixture = render({ name: 'Payload', children: [] });
    fixture.componentRef.setInput('searchTerm', 'bus');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent?.trim()).toBe('');
  });

  it('keeps an ancestor visible when a descendant matches the search term', () => {
    const fixture = render({
      name: 'Mission',
      children: [{ name: 'Bus', children: [{ name: 'Power', children: [] }] }],
    });

    fixture.componentRef.setInput('searchTerm', 'power');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Mission');
    expect(compiled.textContent).toContain('Bus');
    expect(compiled.textContent).toContain('Power');
  });
});
