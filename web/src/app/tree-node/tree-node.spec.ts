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
