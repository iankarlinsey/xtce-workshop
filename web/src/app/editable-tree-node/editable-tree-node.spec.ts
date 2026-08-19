import { TestBed } from '@angular/core/testing';
import { EditableTreeNodeComponent, SpaceSystemDocument } from './editable-tree-node';

describe('EditableTreeNodeComponent', () => {
  function render(node: SpaceSystemDocument, isRoot = false) {
    const fixture = TestBed.createComponent(EditableTreeNodeComponent);
    fixture.componentRef.setInput('node', node);
    fixture.componentRef.setInput('isRoot', isRoot);
    fixture.detectChanges();
    return fixture;
  }

  it('renders the node name', () => {
    const fixture = render({ name: 'Minimal', children: [] }, true);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Minimal');
  });

  it('has no Delete action on the root node', () => {
    const fixture = render({ name: 'Minimal', children: [] }, true);

    const compiled = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(compiled.querySelectorAll('button')).map((b) => b.textContent?.trim());
    expect(buttons).not.toContain('Delete');
  });

  it('has a Delete action on a non-root node', () => {
    const fixture = render({ name: 'Bus', children: [] }, false);

    const compiled = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(compiled.querySelectorAll('button')).map((b) => b.textContent?.trim());
    expect(buttons).toContain('Delete');
  });

  it('emits an updated node with the new child when Add child is used', () => {
    const fixture = render({ name: 'Mission', children: [] }, true);
    spyOn(window, 'prompt').and.returnValue('Bus');
    let emitted: SpaceSystemDocument | undefined;
    fixture.componentInstance.nodeChange.subscribe((n) => (emitted = n));

    (fixture.nativeElement.querySelector('.action') as HTMLButtonElement).click();

    expect(emitted).toEqual({ name: 'Mission', children: [{ name: 'Bus', children: [] }] });
  });

  it('does not emit when Add child prompt is cancelled', () => {
    const fixture = render({ name: 'Mission', children: [] }, true);
    spyOn(window, 'prompt').and.returnValue(null);
    let emitted: SpaceSystemDocument | undefined;
    fixture.componentInstance.nodeChange.subscribe((n) => (emitted = n));

    (fixture.nativeElement.querySelector('.action') as HTMLButtonElement).click();

    expect(emitted).toBeUndefined();
  });

  it('emits an updated node with the new name when Rename is used', () => {
    const fixture = render({ name: 'Mission', children: [] }, true);
    spyOn(window, 'prompt').and.returnValue('Renamed');
    let emitted: SpaceSystemDocument | undefined;
    fixture.componentInstance.nodeChange.subscribe((n) => (emitted = n));

    const renameButton = Array.from(fixture.nativeElement.querySelectorAll('button')).find(
      (b) => (b as HTMLButtonElement).textContent?.trim() === 'Rename'
    ) as HTMLButtonElement;
    renameButton.click();

    expect(emitted).toEqual({ name: 'Renamed', children: [] });
  });

  it('removes a child and emits the parent with it deleted', () => {
    const fixture = render({
      name: 'Mission',
      children: [
        { name: 'Bus', children: [] },
        { name: 'Payload', children: [] },
      ],
    }, true);
    let emitted: SpaceSystemDocument | undefined;
    fixture.componentInstance.nodeChange.subscribe((n) => (emitted = n));

    const deleteButtons = Array.from(fixture.nativeElement.querySelectorAll('button')).filter(
      (b) => (b as HTMLButtonElement).textContent?.trim() === 'Delete'
    ) as HTMLButtonElement[];
    deleteButtons[0].click(); // delete "Bus"

    expect(emitted).toEqual({ name: 'Mission', children: [{ name: 'Payload', children: [] }] });
  });

  it('filters out a node whose name does not match the search term', () => {
    const fixture = render({ name: 'Payload', children: [] }, true);
    fixture.componentRef.setInput('searchTerm', 'bus');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent?.trim()).toBe('');
  });

  it('keeps an ancestor visible when a descendant matches the search term', () => {
    const fixture = render({
      name: 'Mission',
      children: [{ name: 'Bus', children: [{ name: 'Power', children: [] }] }],
    }, true);

    fixture.componentRef.setInput('searchTerm', 'power');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Mission');
    expect(compiled.textContent).toContain('Bus');
    expect(compiled.textContent).toContain('Power');
  });
});
