import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { App } from './app';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createAppAndFlushHealth() {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne('/api/health').flush({ status: 'ok' });
    return fixture;
  }

  /** Runs the revalidation debounce down and answers the resulting validate request. */
  function flushRevalidate(issues: unknown[] = []) {
    tick(App.revalidateDelayMs);
    httpMock.expectOne('/api/xtce/validate').flush({ validationIssues: issues });
  }

  it('should create the app', () => {
    const fixture = createAppAndFlushHealth();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('shows Backend: OK after a successful health check', () => {
    const fixture = createAppAndFlushHealth();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Backend: OK');
  });

  it('shows Backend: unreachable when the health check fails', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    httpMock.expectOne('/api/health').flush('error', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Backend: unreachable');
  });

  it('shows an empty-state message before any document exists', () => {
    const fixture = createAppAndFlushHealth();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Load or create a document to get started.');
  });

  describe('Load', () => {
    function selectFile(fixture: ReturnType<typeof createAppAndFlushHealth>, name: string) {
      const file = new File(['<xml/>'], name, { type: 'application/xml' });
      const event = { target: { files: [file] } } as unknown as Event;
      fixture.componentInstance.onFileSelected(event);
    }

    it('selects the root and shows it in the main panel after loading', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'minimal.xml');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Minimal',
        document: { name: 'Minimal', children: [] },
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')?.textContent).toContain('Minimal');
      expect((compiled.querySelector('#node-name') as HTMLInputElement).value).toBe('Minimal');
    });

    it('renders a loaded nested document as an expandable hierarchy in the sidebar', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Mission',
        document: {
          name: 'Mission',
          children: [
            { name: 'Bus', children: [] },
            { name: 'Payload', children: [] },
          ],
        },
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelectorAll('app-editable-tree-node').length).toBe(3);
      expect(compiled.textContent).toContain('Bus');
      expect(compiled.textContent).toContain('Payload');
    });

    it('renders telemetry items as tree rows', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'telemetry.xml');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          telemetryMetaData: {
            parameterTypeSet: [{ name: 'Volt_Type', kind: 'Float' }],
            parameterSet: [{ name: 'BusVoltage', parameterTypeRef: 'Volt_Type' }],
            containerSet: [{ name: 'Frame', entryList: [] }],
          },
        },
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const itemLabels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
      expect(itemLabels).toEqual(['Volt_Type', 'BusVoltage', 'Frame']);
    });

    it('filters the tree via the search box', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Mission',
        document: {
          name: 'Mission',
          children: [
            { name: 'Bus', children: [] },
            { name: 'Payload', children: [] },
          ],
        },
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const searchInput = compiled.querySelector('input[type="search"]') as HTMLInputElement;
      searchInput.value = 'bus';
      searchInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(compiled.textContent).toContain('Bus');
      expect(compiled.textContent).not.toContain('Payload');
    });

    it('shows an error and no document when loading fails', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'broken.xml');

      httpMock.expectOne('/api/xtce/load').flush(
        { error: 'The document is not well-formed XML.' },
        { status: 400, statusText: 'Bad Request' }
      );
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')).toBeNull();
      expect(compiled.textContent).toContain('The document is not well-formed XML.');
    });

    it('renders validation issues returned by the load endpoint', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Mission',
        document: { name: 'Mission', children: [] },
        validationIssues: [
          {
            ruleId: 'XTCE-1.2-R07-enum-initial-value-must-be-valid-label',
            severity: 'Error',
            location: 'Mission/ParameterTypeSet/State_Type',
            message: "initialValue 'UNKNOWN' is not a valid label in State_Type's EnumerationList.",
          },
        ],
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).toContain('1 validation issue(s)');
      expect(compiled.textContent).toContain('Mission/ParameterTypeSet/State_Type');
      expect(compiled.textContent).toContain('is not a valid label');
    });

    it('makes a loaded document immediately saveable, identically to New', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');
      const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Mission',
        document: { name: 'Mission', children: [] },
      });
      fixture.detectChanges();

      fixture.componentInstance.onSaveDocument();

      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body).toEqual({ name: 'Mission', children: [] });
      req.flush('<SpaceSystem name="Mission"/>');

      expect(clickSpy).toHaveBeenCalled();
    });
  });

  describe('New / Save', () => {
    it('creates a blank document, selects its root, and shows it in the main panel', () => {
      const fixture = createAppAndFlushHealth();
      spyOn(window, 'prompt').and.returnValue('Mission');

      fixture.componentInstance.onNewDocument();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')?.textContent).toContain('Mission');
    });

    it('does nothing when the name prompt is cancelled', () => {
      const fixture = createAppAndFlushHealth();
      spyOn(window, 'prompt').and.returnValue(null);

      fixture.componentInstance.onNewDocument();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')).toBeNull();
    });

    it('posts the current document and triggers a download on save', () => {
      const fixture = createAppAndFlushHealth();
      spyOn(window, 'prompt').and.returnValue('Mission');
      const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

      fixture.componentInstance.onNewDocument();
      fixture.componentInstance.onSaveDocument();

      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body).toEqual({ name: 'Mission', children: [] });
      req.flush('<SpaceSystem name="Mission"/>');

      expect(clickSpy).toHaveBeenCalled();
    });

    it('shows an error if saving fails', () => {
      const fixture = createAppAndFlushHealth();
      spyOn(window, 'prompt').and.returnValue('Mission');

      fixture.componentInstance.onNewDocument();
      fixture.componentInstance.onSaveDocument();

      httpMock.expectOne('/api/xtce/save').flush('error', { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).toContain('Failed to save document.');
    });
  });

  describe('Selection and editing', () => {
    function loadNestedDocument(fixture: ReturnType<typeof createAppAndFlushHealth>) {
      const file = new File(['<xml/>'], 'nested.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Mission',
        document: {
          name: 'Mission',
          children: [
            { name: 'Bus', children: [] },
            { name: 'Payload', children: [] },
          ],
        },
      });
      fixture.detectChanges();
    }

    function loadTelemetryDocument(fixture: ReturnType<typeof createAppAndFlushHealth>) {
      const file = new File(['<xml/>'], 'telemetry.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          preserved: [{ elementName: 'Header', outerXml: '<Header/>' }],
          telemetryMetaData: {
            parameterTypeSet: [
              { name: 'Volt_Type', kind: 'Float', sizeInBits: 32 },
              { name: 'Mode_Type', kind: 'Enumerated', enumerations: [{ value: 0, label: 'IDLE' }] },
            ],
            parameterSet: [{ name: 'BusVoltage', parameterTypeRef: 'Volt_Type', initialValue: '28.5' }],
            containerSet: [{ name: 'Frame', entryList: [{ kind: 'ParameterRef', ref: 'BusVoltage' }] }],
          },
        },
      });
      fixture.detectChanges();
    }

    function clickTreeRowByText(fixture: ReturnType<typeof createAppAndFlushHealth>, text: string) {
      const compiled = fixture.nativeElement as HTMLElement;
      const row = Array.from(compiled.querySelectorAll('.tree-node-row')).find((r) =>
        r.textContent?.includes(text)
      ) as HTMLElement;
      row.click();
      fixture.detectChanges();
    }

    it('clicking a tree row selects it without editing anything', () => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture);

      clickTreeRowByText(fixture, 'Bus');

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')?.textContent).toContain('Bus');
      expect(compiled.textContent).toContain('Payload');
    });

    it('editing the Name field updates the selected node and is reflected in Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture);
      clickTreeRowByText(fixture, 'Bus');

      const compiled = fixture.nativeElement as HTMLElement;
      const nameInput = compiled.querySelector('#node-name') as HTMLInputElement;
      nameInput.value = 'Renamed Bus';
      nameInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body).toEqual({
        name: 'Mission',
        children: [
          { name: 'Renamed Bus', children: [] },
          { name: 'Payload', children: [] },
        ],
      });
      req.flush('<SpaceSystem/>');
    }));

    it('Add child on the selected node adds a child and is reflected in Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture); // root (Mission) is selected by default
      spyOn(window, 'prompt').and.returnValue('NewSub');

      const compiled = fixture.nativeElement as HTMLElement;
      const addButton = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add child'
      ) as HTMLButtonElement;
      addButton.click();
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.children.length).toBe(3);
      expect(req.request.body.children[2]).toEqual({ name: 'NewSub', children: [] });
      req.flush('<SpaceSystem/>');
    }));

    it('has no Delete action when the root is selected', () => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture); // root selected by default

      const compiled = fixture.nativeElement as HTMLElement;
      const buttons = Array.from(compiled.querySelectorAll('button')).map((b) => b.textContent?.trim());
      expect(buttons).not.toContain('Delete');
    });

    it('Delete on a non-root selected node removes it and selects its parent', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture);
      clickTreeRowByText(fixture, 'Bus');

      const compiled = fixture.nativeElement as HTMLElement;
      const deleteButton = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Delete'
      ) as HTMLButtonElement;
      deleteButton.click();
      fixture.detectChanges();
      flushRevalidate();

      expect(compiled.querySelector('.node-title')?.textContent).toContain('Mission');
      expect(compiled.textContent).not.toContain('Bus');

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body).toEqual({ name: 'Mission', children: [{ name: 'Payload', children: [] }] });
      req.flush('<SpaceSystem/>');
    }));

    it('selecting a parameter shows its form with type ref and initial value', () => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);

      clickTreeRowByText(fixture, 'BusVoltage');

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')?.textContent).toContain('BusVoltage');
      expect((compiled.querySelector('#param-typeref') as HTMLInputElement).value).toBe('Volt_Type');
      expect((compiled.querySelector('#param-initial') as HTMLInputElement).value).toBe('28.5');
    });

    it('editing a parameter initial value revalidates and shows returned issues', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'BusVoltage');

      const compiled = fixture.nativeElement as HTMLElement;
      const initialInput = compiled.querySelector('#param-initial') as HTMLInputElement;
      initialInput.value = 'not-a-number';
      initialInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      tick(App.revalidateDelayMs);
      const validateReq = httpMock.expectOne('/api/xtce/validate');
      expect(validateReq.request.body.telemetryMetaData.parameterSet[0].initialValue).toBe('not-a-number');
      validateReq.flush({
        validationIssues: [{
          ruleId: 'XTCE-1.2-R15-typed-value-valid-for-type',
          severity: 'Error',
          location: 'Sat/ParameterSet/BusVoltage',
          message: "initialValue 'not-a-number' is not a valid floating-point number for its Float type.",
        }],
      });
      fixture.detectChanges();

      expect(compiled.textContent).toContain('1 validation issue(s)');
      expect(compiled.textContent).toContain('not a valid floating-point number');
    }));

    it('rapid edits debounce into a single validate request', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'BusVoltage');

      const compiled = fixture.nativeElement as HTMLElement;
      const initialInput = compiled.querySelector('#param-initial') as HTMLInputElement;
      for (const value of ['1', '12', '123']) {
        initialInput.value = value;
        initialInput.dispatchEvent(new Event('input'));
        tick(App.revalidateDelayMs / 2);
      }
      tick(App.revalidateDelayMs);

      const validateReq = httpMock.expectOne('/api/xtce/validate'); // exactly one
      expect(validateReq.request.body.telemetryMetaData.parameterSet[0].initialValue).toBe('123');
      validateReq.flush({ validationIssues: [] });
    }));

    it('editing an enumerated type: add and edit an enumeration row, reflected in Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Mode_Type');

      const compiled = fixture.nativeElement as HTMLElement;
      const addEnum = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add enumeration'
      ) as HTMLButtonElement;
      addEnum.click();
      fixture.detectChanges();

      const labelInputs = compiled.querySelectorAll('.enum-label');
      expect(labelInputs.length).toBe(2);
      const newLabel = labelInputs[1] as HTMLInputElement;
      newLabel.value = 'ACTIVE';
      newLabel.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.parameterTypeSet[1].enumerations).toEqual([
        { value: 0, label: 'IDLE' },
        { value: 1, label: 'ACTIVE' },
      ]);
      req.flush('<SpaceSystem/>');
    }));

    it('adding a parameter type through the kind picker appears in the tree and Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture); // root selected
      spyOn(window, 'prompt').and.returnValue('Flag_Type');

      const compiled = fixture.nativeElement as HTMLElement;
      const kindSelect = compiled.querySelector('.kind-select') as HTMLSelectElement;
      kindSelect.value = 'Boolean';
      const addTypeButton = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add parameter type'
      ) as HTMLButtonElement;
      addTypeButton.click();
      fixture.detectChanges();
      flushRevalidate();

      const itemLabels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
      expect(itemLabels).toContain('Flag_Type');

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const added = req.request.body.telemetryMetaData.parameterTypeSet[2];
      expect(added).toEqual({ name: 'Flag_Type', kind: 'Boolean' });
      req.flush('<SpaceSystem/>');
    }));

    it('deleting a telemetry item selects its owning system', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'BusVoltage');

      const compiled = fixture.nativeElement as HTMLElement;
      const deleteButton = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Delete'
      ) as HTMLButtonElement;
      deleteButton.click();
      fixture.detectChanges();
      flushRevalidate();

      expect(compiled.querySelector('.node-title')?.textContent).toContain('Sat');
      const itemLabels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
      expect(itemLabels).not.toContain('BusVoltage');
    }));

    it('selecting a container shows its entry list with editable refs', () => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);

      clickTreeRowByText(fixture, 'Frame');

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')?.textContent).toContain('Frame');
      const refInput = compiled.querySelector('.entry-editor .entry-ref') as HTMLInputElement;
      expect(refInput.value).toBe('BusVoltage');
    });

    it('adding, editing, moving, and removing entries flows into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Frame');
      const compiled = fixture.nativeElement as HTMLElement;

      // Add a container-ref entry.
      const kindSelect = compiled.querySelector('.add-entry-row .kind-select') as HTMLSelectElement;
      kindSelect.value = 'ContainerRef';
      const newRef = compiled.querySelector('.add-entry-row .entry-ref') as HTMLInputElement;
      newRef.value = 'Frame';
      const addButton = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add entry'
      ) as HTMLButtonElement;
      addButton.click();
      fixture.detectChanges();

      // Move the new entry (index 1) up to the front.
      const moveUpButtons = compiled.querySelectorAll('.entry-edit-row button[aria-label="Move entry up"]');
      (moveUpButtons[1] as HTMLButtonElement).click();
      fixture.detectChanges();

      // Edit the (now second) parameter entry's ref.
      const refInputs = compiled.querySelectorAll('.entry-editor .entry-ref');
      const paramRef = refInputs[1] as HTMLInputElement;
      paramRef.value = 'RenamedRef';
      paramRef.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const entries = req.request.body.telemetryMetaData.containerSet[0].entryList;
      expect(entries).toEqual([
        { kind: 'ContainerRef', ref: 'Frame' },
        { kind: 'ParameterRef', ref: 'RenamedRef' },
      ]);
      req.flush('<SpaceSystem/>');
    }));

    it('removing an entry flows into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Frame');
      const compiled = fixture.nativeElement as HTMLElement;

      const removeButton = compiled.querySelector('.entry-edit-row button[aria-label="Remove entry"]') as HTMLButtonElement;
      removeButton.click();
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.containerSet[0].entryList).toEqual([]);
      req.flush('<SpaceSystem/>');
    }));

    it('adding a base container and editing its ref flows into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Frame');
      const compiled = fixture.nativeElement as HTMLElement;

      const addBase = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add base container'
      ) as HTMLButtonElement;
      addBase.click();
      fixture.detectChanges();

      const baseInput = compiled.querySelector('input[aria-label="Base container reference"]') as HTMLInputElement;
      baseInput.value = 'SomeBase';
      baseInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.containerSet[0].baseContainer).toEqual({ containerRef: 'SomeBase' });
      req.flush('<SpaceSystem/>');
    }));

    it('raw (unmodeled) entries display read-only but keep their payload through Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<xml/>'], 'raw.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          telemetryMetaData: {
            parameterTypeSet: [],
            parameterSet: [],
            containerSet: [{
              name: 'Frame',
              entryList: [
                { kind: 'Raw', rawXml: { elementName: 'ParameterSegmentRefEntry', outerXml: '<ParameterSegmentRefEntry/>' } },
                { kind: 'ParameterRef', ref: 'P' },
              ],
            }],
          },
        },
      });
      fixture.detectChanges();
      clickTreeRowByText(fixture, 'Frame');

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.entry-raw-label')?.textContent).toContain('unmodeled');

      // Move the raw entry down — reorder must carry the raw payload untouched.
      const moveDown = compiled.querySelector('.entry-edit-row button[aria-label="Move entry down"]') as HTMLButtonElement;
      moveDown.click();
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const entries = req.request.body.telemetryMetaData.containerSet[0].entryList;
      expect(entries[0]).toEqual({ kind: 'ParameterRef', ref: 'P' });
      expect(entries[1].rawXml.elementName).toBe('ParameterSegmentRefEntry');
      req.flush('<SpaceSystem/>');
    }));

    it('Array type form edits arrayTypeRef and dimensions, reflected in Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<xml/>'], 'arr.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          telemetryMetaData: {
            parameterTypeSet: [
              { name: 'Elem', kind: 'Integer' },
              {
                name: 'Matrix', kind: 'Array', arrayTypeRef: 'Elem',
                dimensions: [{ startingIndex: { fixedValue: 0 }, endingIndex: { fixedValue: 3 } }],
              },
            ],
            parameterSet: [],
          },
        },
      });
      fixture.detectChanges();
      clickTreeRowByText(fixture, 'Matrix');
      const compiled = fixture.nativeElement as HTMLElement;

      const endInput = compiled.querySelector('input[aria-label="Dimension 0 ending index"]') as HTMLInputElement;
      endInput.value = '7';
      endInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      const addDim = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add dimension'
      ) as HTMLButtonElement;
      addDim.click();
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const matrix = req.request.body.telemetryMetaData.parameterTypeSet[1];
      expect(matrix.dimensions).toEqual([
        { startingIndex: { fixedValue: 0 }, endingIndex: { fixedValue: 7, raw: null } },
        { startingIndex: { fixedValue: 0 }, endingIndex: { fixedValue: 0 } },
      ]);
      req.flush('<SpaceSystem/>');
    }));

    it('Aggregate type form edits members, last member is not removable', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<xml/>'], 'agg.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          telemetryMetaData: {
            parameterTypeSet: [
              { name: 'Elem', kind: 'Integer' },
              { name: 'Struct', kind: 'Aggregate', members: [{ name: 'volt', typeRef: 'Elem' }] },
            ],
            parameterSet: [],
          },
        },
      });
      fixture.detectChanges();
      clickTreeRowByText(fixture, 'Struct');
      const compiled = fixture.nativeElement as HTMLElement;

      const removeMember = compiled.querySelector('button[aria-label="Remove member"]') as HTMLButtonElement;
      expect(removeMember.disabled).toBeTrue();

      const addMember = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add member'
      ) as HTMLButtonElement;
      addMember.click();
      fixture.detectChanges();

      const typeRefInput = compiled.querySelector('input[aria-label="Member 1 type ref"]') as HTMLInputElement;
      typeRefInput.value = 'Elem';
      typeRefInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const struct = req.request.body.telemetryMetaData.parameterTypeSet[1];
      expect(struct.members).toEqual([
        { name: 'volt', typeRef: 'Elem' },
        { name: 'field2', typeRef: 'Elem' },
      ]);
      req.flush('<SpaceSystem/>');
    }));

    it('creating an Array type via the picker prompts for the element type and seeds one dimension', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture); // root selected
      const prompts = ['NewArray', 'Volt_Type'];
      spyOn(window, 'prompt').and.callFake(() => prompts.shift() ?? null);

      const compiled = fixture.nativeElement as HTMLElement;
      const kindSelect = compiled.querySelector('.kind-select') as HTMLSelectElement;
      kindSelect.value = 'Array';
      const addTypeButton = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add parameter type'
      ) as HTMLButtonElement;
      addTypeButton.click();
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const added = req.request.body.telemetryMetaData.parameterTypeSet.at(-1);
      expect(added).toEqual({
        name: 'NewArray',
        kind: 'Array',
        arrayTypeRef: 'Volt_Type',
        dimensions: [{ startingIndex: { fixedValue: 0 }, endingIndex: { fixedValue: 0 } }],
      });
      req.flush('<SpaceSystem/>');
    }));

    it('preserved (unmodeled) document content passes through edits into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);

      const compiled = fixture.nativeElement as HTMLElement;
      const nameInput = compiled.querySelector('#node-name') as HTMLInputElement;
      nameInput.value = 'RenamedSat';
      nameInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.name).toBe('RenamedSat');
      expect(req.request.body.preserved).toEqual([{ elementName: 'Header', outerXml: '<Header/>' }]);
      req.flush('<SpaceSystem/>');
    }));
  });
});
