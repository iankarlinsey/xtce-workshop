import { TestBed } from '@angular/core/testing';
import { EditableTreeNodeComponent } from './editable-tree-node';
import { SpaceSystemDocument, Selection } from '../document-tree';

describe('EditableTreeNodeComponent', () => {
  function render(node: SpaceSystemDocument, path: number[] = [], selection: Selection | null = null) {
    const fixture = TestBed.createComponent(EditableTreeNodeComponent);
    fixture.componentRef.setInput('node', node);
    fixture.componentRef.setInput('path', path);
    fixture.componentRef.setInput('selection', selection);
    fixture.detectChanges();
    return fixture;
  }

  const withTelemetry = (): SpaceSystemDocument => ({
    name: 'Sat',
    children: [{ name: 'Bus', children: [] }],
    telemetryMetaData: {
      parameterTypeSet: [
        { name: 'Volt_Type', kind: 'Float' },
        { name: 'Mode_Type', kind: 'Enumerated', enumerations: [] },
      ],
      parameterSet: [{ name: 'BusVoltage', parameterTypeRef: 'Volt_Type' }],
      containerSet: [{ name: 'Frame', entryList: [] }],
    },
  });

  it('renders the node name', () => {
    const fixture = render({ name: 'Minimal', children: [] });

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Minimal');
  });

  it('emits a system selection when the system row is clicked', () => {
    const fixture = render({ name: 'Bus', children: [] }, [1]);
    let emitted: Selection | undefined;
    fixture.componentInstance.select.subscribe((s) => (emitted = s));

    (fixture.nativeElement.querySelector('.tree-node-row') as HTMLElement).click();

    expect(emitted).toEqual({ systemPath: [1] });
  });

  /** Groups default collapsed; open every one so item rows are reachable. */
  function expandGroups(fixture: ReturnType<typeof render>) {
    const compiled = fixture.nativeElement as HTMLElement;
    for (let i = 0; i < 10; i++) {
      const collapsedToggle = compiled.querySelector('.group-row .toggle[aria-expanded="false"]');
      if (!collapsedToggle) {
        return;
      }
      (collapsedToggle.closest('.group-row') as HTMLElement).click();
      fixture.detectChanges();
    }
  }

  it('renders per-kind group headers with counts, omitting empty kinds, items hidden', () => {
    const fixture = render(withTelemetry());

    const compiled = fixture.nativeElement as HTMLElement;
    const headers = Array.from(compiled.querySelectorAll('.group-row')).map((row) =>
      `${row.querySelector('.group-label')?.textContent?.trim()}:${row.querySelector('.group-count')?.textContent?.trim()}`);
    expect(headers).toEqual(['Parameter Types:2', 'Parameters:1', 'Containers:1']);
    expect(compiled.querySelector('.item-row')).toBeNull(); // collapsed by default
  });

  it('renders telemetry item rows with their names once their groups are expanded', () => {
    const fixture = render(withTelemetry());
    expandGroups(fixture);

    const compiled = fixture.nativeElement as HTMLElement;
    const labels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
    expect(labels).toEqual(['Volt_Type', 'Mode_Type', 'BusVoltage', 'Frame']);
  });

  it('toggling a group emits no selection and hides its items again', () => {
    const fixture = render(withTelemetry());
    let emitted: Selection | undefined;
    fixture.componentInstance.select.subscribe((s) => (emitted = s));
    expandGroups(fixture);

    const compiled = fixture.nativeElement as HTMLElement;
    (compiled.querySelector('.group-row') as HTMLElement).click(); // collapse Parameter Types
    fixture.detectChanges();

    expect(emitted).toBeUndefined();
    const labels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
    expect(labels).toEqual(['BusVoltage', 'Frame']);
  });

  it('always shows the group holding the current selection, collapsed or not', () => {
    const fixture = render(withTelemetry(), [], { systemPath: [], item: { kind: 'container', index: 0 } });

    const compiled = fixture.nativeElement as HTMLElement;
    const labels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
    expect(labels).toEqual(['Frame']); // only the selected item's group is open
  });

  it('emits an item selection when an item row is clicked', () => {
    const fixture = render(withTelemetry());
    let emitted: Selection | undefined;
    fixture.componentInstance.select.subscribe((s) => (emitted = s));
    expandGroups(fixture);

    const rows = fixture.nativeElement.querySelectorAll('.item-row');
    (rows[2] as HTMLElement).click(); // BusVoltage — the parameter

    expect(emitted).toEqual({ systemPath: [], item: { kind: 'parameter', index: 0 } });
  });

  it('marks the matching item row as selected', () => {
    const fixture = render(withTelemetry(), [], { systemPath: [], item: { kind: 'parameterType', index: 1 } });

    const rows = fixture.nativeElement.querySelectorAll('.item-row');
    expect((rows[0] as HTMLElement).classList).not.toContain('selected');
    expect((rows[1] as HTMLElement).classList).toContain('selected');
  });

  it('does not mark the system row selected when an item within it is selected', () => {
    const fixture = render(withTelemetry(), [], { systemPath: [], item: { kind: 'parameter', index: 0 } });

    const systemRow = fixture.nativeElement.querySelector('.tree-node-row:not(.item-row)') as HTMLElement;
    expect(systemRow.classList).not.toContain('selected');
  });

  it('bubbles a child system selection up unchanged', () => {
    const fixture = render({
      name: 'Mission',
      children: [{ name: 'Bus', children: [] }],
    });
    let emitted: Selection | undefined;
    fixture.componentInstance.select.subscribe((s) => (emitted = s));

    const childRow = fixture.nativeElement.querySelectorAll('.tree-node-row')[1] as HTMLElement;
    childRow.click();

    expect(emitted).toEqual({ systemPath: [0] });
  });

  it('renders a single node with no children and no items with no toggle button', () => {
    const fixture = render({ name: 'Minimal', children: [] });

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.toggle')).toBeNull();
    expect(compiled.querySelector('.toggle-spacer')).toBeTruthy();
  });

  it('shows a toggle when the node has telemetry items even without child systems', () => {
    const node = withTelemetry();
    node.children = [];
    const fixture = render(node);

    expect((fixture.nativeElement as HTMLElement).querySelector('.toggle')).toBeTruthy();
  });

  it('collapsing does not trigger a selection (toggle click does not bubble to row)', () => {
    const fixture = render({
      name: 'Mission',
      children: [{ name: 'Bus', children: [] }],
    });
    let emitted: Selection | undefined;
    fixture.componentInstance.select.subscribe((s) => (emitted = s));

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

    expect((fixture.nativeElement as HTMLElement).textContent?.trim()).toBe('');
  });

  it('keeps the system visible when only a telemetry item matches, and filters item rows', () => {
    const fixture = render(withTelemetry());
    fixture.componentRef.setInput('searchTerm', 'busvolt');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Sat');
    const labels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
    expect(labels).toEqual(['BusVoltage']);
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
