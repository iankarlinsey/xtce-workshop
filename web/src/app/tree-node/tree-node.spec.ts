import { TestBed } from '@angular/core/testing';
import { TreeNodeComponent, TreeNode } from './tree-node';

describe('TreeNodeComponent', () => {
  function render(node: TreeNode) {
    const fixture = TestBed.createComponent(TreeNodeComponent);
    fixture.componentRef.setInput('node', node);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the node label', () => {
    const fixture = render({ label: 'Minimal', nodeType: 'SpaceSystem', children: [] });

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Minimal');
  });

  it('renders a single node with no children with no toggle button', () => {
    const fixture = render({ label: 'Minimal', nodeType: 'SpaceSystem', children: [] });

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.toggle')).toBeNull();
    expect(compiled.querySelector('.toggle-spacer')).toBeTruthy();
  });

  it('renders nested children recursively, expanded by default', () => {
    const fixture = render({
      label: 'Mission',
      nodeType: 'SpaceSystem',
      children: [
        { label: 'Bus', nodeType: 'SpaceSystem', children: [
          { label: 'Power', nodeType: 'SpaceSystem', children: [] },
        ] },
        { label: 'Payload', nodeType: 'SpaceSystem', children: [] },
      ],
    });

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Mission');
    expect(compiled.textContent).toContain('Bus');
    expect(compiled.textContent).toContain('Power');
    expect(compiled.textContent).toContain('Payload');
    expect(compiled.querySelectorAll('app-tree-node').length).toBe(3);
  });

  it('filters out a node whose label does not match the search term', () => {
    const fixture = render({ label: 'Payload', nodeType: 'SpaceSystem', children: [] });
    fixture.componentRef.setInput('searchTerm', 'bus');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent?.trim()).toBe('');
  });

  it('keeps a node visible when its own label matches the search term', () => {
    const fixture = render({ label: 'Bus', nodeType: 'SpaceSystem', children: [] });
    fixture.componentRef.setInput('searchTerm', 'bus');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Bus');
  });

  it('keeps an ancestor visible when a descendant matches, even collapsed', () => {
    const fixture = render({
      label: 'Mission',
      nodeType: 'SpaceSystem',
      children: [
        { label: 'Bus', nodeType: 'SpaceSystem', children: [
          { label: 'Power', nodeType: 'SpaceSystem', children: [] },
        ] },
        { label: 'Payload', nodeType: 'SpaceSystem', children: [] },
      ],
    });

    // Collapse "Mission" before searching — the search should force it back open so the
    // match underneath isn't hidden behind a manual collapse from before the search.
    const compiled = fixture.nativeElement as HTMLElement;
    (compiled.querySelector('.toggle') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(compiled.textContent).not.toContain('Power');

    fixture.componentRef.setInput('searchTerm', 'power');
    fixture.detectChanges();

    expect(compiled.textContent).toContain('Mission');
    expect(compiled.textContent).toContain('Bus');
    expect(compiled.textContent).toContain('Power');
    expect(compiled.textContent).not.toContain('Payload');
  });

  it('restores the full tree when the search term is cleared', () => {
    const fixture = render({
      label: 'Mission',
      nodeType: 'SpaceSystem',
      children: [{ label: 'Bus', nodeType: 'SpaceSystem', children: [] }],
    });

    fixture.componentRef.setInput('searchTerm', 'bus');
    fixture.detectChanges();
    fixture.componentRef.setInput('searchTerm', '');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Mission');
    expect(compiled.textContent).toContain('Bus');
  });

  it('collapses and re-expands children when the toggle is clicked', () => {
    const fixture = render({
      label: 'Mission',
      nodeType: 'SpaceSystem',
      children: [{ label: 'Bus', nodeType: 'SpaceSystem', children: [] }],
    });

    const compiled = fixture.nativeElement as HTMLElement;
    const toggle = compiled.querySelector('.toggle') as HTMLButtonElement;
    expect(compiled.textContent).toContain('Bus');

    toggle.click();
    fixture.detectChanges();
    expect(compiled.textContent).not.toContain('Bus');

    toggle.click();
    fixture.detectChanges();
    expect(compiled.textContent).toContain('Bus');
  });
});
