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

  function createDocumentInline(fixture: ReturnType<typeof TestBed.createComponent<App>>, name: string | null) {
    const compiled = fixture.nativeElement as HTMLElement;
    (Array.from(compiled.querySelectorAll('button')).find(
      (b) => b.textContent?.includes('New')
    ) as HTMLButtonElement).click();
    fixture.detectChanges();
    if (name === null) {
      (Array.from(compiled.querySelectorAll('.creator-row button')).find(
        (b) => b.textContent?.trim() === 'Cancel'
      ) as HTMLButtonElement).click();
    } else {
      const input = compiled.querySelector('input[aria-label="New document name"]') as HTMLInputElement;
      input.value = name;
      (Array.from(compiled.querySelectorAll('.creator-row button')).find(
        (b) => b.textContent?.trim() === 'Create'
      ) as HTMLButtonElement).click();
    }
    fixture.detectChanges();
  }

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
    it('creates a blank document via the inline creator, selects its root', () => {
      const fixture = createAppAndFlushHealth();
      fixture.detectChanges();

      createDocumentInline(fixture, 'Mission');

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')?.textContent).toContain('Mission');
      expect(compiled.querySelector('.creator-row')).toBeNull(); // creator closes on create
    });

    it('cancelling the inline creator leaves no document', () => {
      const fixture = createAppAndFlushHealth();
      fixture.detectChanges();

      createDocumentInline(fixture, null);

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')).toBeNull();
      expect(compiled.querySelector('.creator-row')).toBeNull();
    });

    it('posts the current document and triggers a download on save', () => {
      const fixture = createAppAndFlushHealth();
      fixture.detectChanges();
      const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

      createDocumentInline(fixture, 'Mission');
      fixture.componentInstance.onSaveDocument();

      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body).toEqual({ name: 'Mission', children: [] });
      req.flush('<SpaceSystem name="Mission"/>');

      expect(clickSpy).toHaveBeenCalled();
    });

    it('shows an error if saving fails', () => {
      const fixture = createAppAndFlushHealth();
      fixture.detectChanges();

      createDocumentInline(fixture, 'Mission');
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

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add child'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      const nameInput = compiled.querySelector('input[aria-label="New child name"]') as HTMLInputElement;
      nameInput.value = 'NewSub';
      (Array.from(compiled.querySelectorAll('.creator-row button')).find(
        (b) => b.textContent?.trim() === 'Create'
      ) as HTMLButtonElement).click();
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

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add parameter type'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      (compiled.querySelector('input[aria-label="New type name"]') as HTMLInputElement).value = 'Flag_Type';
      (compiled.querySelector('select[aria-label="New parameter type kind"]') as HTMLSelectElement).value = 'Boolean';
      (Array.from(compiled.querySelectorAll('.creator-row button')).find(
        (b) => b.textContent?.trim() === 'Create'
      ) as HTMLButtonElement).click();
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

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add parameter type'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      (compiled.querySelector('input[aria-label="New type name"]') as HTMLInputElement).value = 'NewArray';
      (compiled.querySelector('select[aria-label="New parameter type kind"]') as HTMLSelectElement).value = 'Array';
      (compiled.querySelector('input[aria-label="New type element or member ref"]') as HTMLInputElement).value = 'Volt_Type';
      (Array.from(compiled.querySelectorAll('.creator-row button')).find(
        (b) => b.textContent?.trim() === 'Create'
      ) as HTMLButtonElement).click();
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

    function loadMessagingDocument(fixture: ReturnType<typeof createAppAndFlushHealth>) {
      const file = new File(['<xml/>'], 'msg.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          telemetryMetaData: {
            parameterTypeSet: [],
            parameterSet: [],
            containerSet: [{ name: 'Packet', entryList: [] }],
            messageSet: {
              messages: [{ name: 'OpsMsg', containerRef: 'Packet', preserved: [{ elementName: 'MatchCriteria', outerXml: '<MatchCriteria/>' }] }],
            },
          },
          commandMetaData: {
            metaCommands: [
              { name: 'BaseCmd', abstract: true },
              { name: 'Reboot', baseMetaCommandRef: 'BaseCmd', completeVerifiers: [{ elementName: 'CompleteVerifier', outerXml: '<CompleteVerifier/>' }] },
            ],
          },
        },
      });
      fixture.detectChanges();
    }

    it('messages and commands render as tree rows and open their forms', () => {
      const fixture = createAppAndFlushHealth();
      loadMessagingDocument(fixture);
      const compiled = fixture.nativeElement as HTMLElement;

      const itemLabels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
      expect(itemLabels).toEqual(['Packet', 'OpsMsg', 'BaseCmd', 'Reboot']);

      clickTreeRowByText(fixture, 'OpsMsg');
      expect((compiled.querySelector('#message-containerref') as HTMLInputElement).value).toBe('Packet');

      clickTreeRowByText(fixture, 'Reboot');
      expect((compiled.querySelector('#command-baseref') as HTMLInputElement).value).toBe('BaseCmd');
      expect(compiled.textContent).toContain('1 complete');
    });

    it('editing a message containerRef flows into Save with MatchCriteria preserved', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadMessagingDocument(fixture);
      clickTreeRowByText(fixture, 'OpsMsg');
      const compiled = fixture.nativeElement as HTMLElement;

      const refInput = compiled.querySelector('#message-containerref') as HTMLInputElement;
      refInput.value = 'OtherPacket';
      refInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const message = req.request.body.telemetryMetaData.messageSet.messages[0];
      expect(message.containerRef).toBe('OtherPacket');
      expect(message.preserved).toEqual([{ elementName: 'MatchCriteria', outerXml: '<MatchCriteria/>' }]);
      req.flush('<SpaceSystem/>');
    }));

    it('adding a command creates it under commandMetaData in Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture); // no commandMetaData yet; root selected

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add command'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      const nameInput = compiled.querySelector('input[aria-label="New command name"]') as HTMLInputElement;
      nameInput.value = 'NewCmd';
      (Array.from(compiled.querySelectorAll('.creator-row button')).find(
        (b) => b.textContent?.trim() === 'Create'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.commandMetaData.metaCommands).toEqual([{ name: 'NewCmd' }]);
      req.flush('<SpaceSystem/>');
    }));

    it('Compute layout fetches and renders the packet layout for the selected container', () => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Frame');
      const compiled = fixture.nativeElement as HTMLElement;

      const computeButton = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Compute layout'
      ) as HTMLButtonElement;
      computeButton.click();

      const req = httpMock.expectOne('/api/xtce/layout');
      expect(req.request.body.containerName).toBe('Frame');
      req.flush({
        rows: [
          { name: 'BusVoltage', kind: 'parameter', sourceContainer: 'Frame', offsetInBits: 0, sizeInBits: 32, isVariable: false, note: null },
          { name: 'Mystery', kind: 'parameter', sourceContainer: 'Frame', offsetInBits: 32, sizeInBits: null, isVariable: false, note: 'no statically-known encoding' },
        ],
        totalSizeInBits: null,
      });
      fixture.detectChanges();

      expect(compiled.querySelectorAll('.bit-cell').length).toBe(2);
      expect(compiled.querySelector('.bit-cell.bit-unknown')).toBeTruthy();
      expect(compiled.querySelector('.layout-table')?.textContent).toContain('BusVoltage');
      expect(compiled.textContent).toContain('not statically known');
    });

    it('editing the document invalidates a computed layout', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Frame');
      const compiled = fixture.nativeElement as HTMLElement;

      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Compute layout'
      ) as HTMLButtonElement).click();
      httpMock.expectOne('/api/xtce/layout').flush({ rows: [
        { name: 'BusVoltage', kind: 'parameter', sourceContainer: 'Frame', offsetInBits: 0, sizeInBits: 32, isVariable: false, note: null },
      ], totalSizeInBits: 32 });
      fixture.detectChanges();
      expect(compiled.querySelector('.layout-table')).toBeTruthy();

      const nameInput = compiled.querySelector('#container-name') as HTMLInputElement;
      nameInput.value = 'RenamedFrame';
      nameInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();
      fixture.detectChanges();

      expect(compiled.querySelector('.layout-table')).toBeNull();
      expect(compiled.textContent).toContain('Compute layout');
    }));

    it('editing restriction criteria flows into Save with single/list normalization', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Frame');
      const compiled = fixture.nativeElement as HTMLElement;

      // Give the container a base first.
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add base container'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      // One comparison -> serialized as the single-Comparison shape.
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add comparison'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      const paramInput = compiled.querySelector('input[aria-label="Comparison 0 parameter"]') as HTMLInputElement;
      paramInput.value = 'BusVoltage';
      paramInput.dispatchEvent(new Event('input'));
      const valueInput = compiled.querySelector('input[aria-label="Comparison 0 value"]') as HTMLInputElement;
      valueInput.value = '28.5';
      valueInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      // A second comparison with an explicit operator -> ComparisonList shape.
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add comparison'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      const param2 = compiled.querySelector('input[aria-label="Comparison 1 parameter"]') as HTMLInputElement;
      param2.value = 'BusVoltage';
      param2.dispatchEvent(new Event('input'));
      const op2 = compiled.querySelector('select[aria-label="Comparison 1 operator"]') as HTMLSelectElement;
      op2.value = '>=';
      op2.dispatchEvent(new Event('change'));
      const value2 = compiled.querySelector('input[aria-label="Comparison 1 value"]') as HTMLInputElement;
      value2.value = '1';
      value2.dispatchEvent(new Event('input'));
      const nextInput = compiled.querySelector('input[aria-label="Next container reference"]') as HTMLInputElement;
      nextInput.value = 'Frame';
      nextInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const criteria = req.request.body.telemetryMetaData.containerSet[0].baseContainer.restrictionCriteria;
      expect(criteria.comparison).toBeNull();
      expect(criteria.comparisonList).toEqual([
        { parameterRef: 'BusVoltage', value: '28.5' },
        { parameterRef: 'BusVoltage', value: '1', comparisonOperator: '>=' },
      ]);
      expect(criteria.nextContainerRef).toBe('Frame');
      req.flush('<SpaceSystem/>');
    }));

    it('raw restriction criteria display as preserved XML, not editable rows', () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<xml/>'], 'raw.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          telemetryMetaData: {
            parameterTypeSet: [], parameterSet: [],
            containerSet: [{
              name: 'Frame', entryList: [],
              baseContainer: {
                containerRef: 'Base',
                restrictionCriteria: { raw: { elementName: 'BooleanExpression', outerXml: '<BooleanExpression/>' } },
              },
            }],
          },
        },
      });
      fixture.detectChanges();
      clickTreeRowByText(fixture, 'Frame');

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).toContain('BooleanExpression criteria preserved as XML');
      expect(compiled.querySelector('input[aria-label="Comparison 0 parameter"]')).toBeNull();
    });

    it('shows preserved XML transparently for items carrying unmodeled content', () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<xml/>'], 'pres.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          telemetryMetaData: {
            parameterTypeSet: [{
              name: 'Enc_Type', kind: 'Integer',
              preserved: [{ elementName: 'IntegerDataEncoding', outerXml: '<IntegerDataEncoding sizeInBits="16"/>' }],
              preservedAttributes: [{ name: 'baseType', value: 'Base_Type' }],
            }],
            parameterSet: [{ name: 'Plain', parameterTypeRef: 'Enc_Type' }],
          },
        },
      });
      fixture.detectChanges();
      const compiled = fixture.nativeElement as HTMLElement;

      clickTreeRowByText(fixture, 'Enc_Type');
      expect(compiled.querySelector('.preserved-panel')).toBeTruthy();
      expect(compiled.textContent).toContain('1 element(s), 1 attribute(s)');
      expect(compiled.querySelector('.preserved-fragment-name')?.textContent).toContain('IntegerDataEncoding');
      expect(compiled.querySelector('.preserved-attr')?.textContent).toContain('baseType="Base_Type"');

      // An item with nothing preserved shows no panel at all.
      clickTreeRowByText(fixture, 'Plain');
      expect(compiled.querySelector('.preserved-panel')).toBeNull();
    });

    it('shows the XSD reference sheet for the selected construct', () => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      const compiled = fixture.nativeElement as HTMLElement;

      // Root SpaceSystem selected by default.
      expect(compiled.querySelector('.reference-source')?.textContent).toContain('SpaceSystemType');

      clickTreeRowByText(fixture, 'Volt_Type');
      expect(compiled.querySelector('.reference-source')?.textContent).toContain('FloatParameterType');

      clickTreeRowByText(fixture, 'Frame');
      expect(compiled.querySelector('.reference-source')?.textContent).toContain('SequenceContainerType');
      expect(compiled.querySelector('.reference-text')?.textContent).toContain('binary layout');
    });

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

  describe('Conformance report', () => {
    function sampleReport(status: string, findings: unknown[] = []) {
      return {
        schemaValid: true,
        schemaErrors: [],
        candidates: [
          {
            candidateNumber: 63,
            ownerPath: 'EnumeratedDataType/initialValue',
            disposition: 'SEMANTIC',
            ruleId: 'XTCE-1.2-R07-enum-initial-value-must-be-valid-label',
            status,
            findings,
            notes: 'Check executed; no findings at this site.',
          },
        ],
        rules: [{ ruleId: 'XTCE-1.2-R07-enum-initial-value-must-be-valid-label', executed: true, findingCount: findings.length }],
        summary: { [status]: 1 },
      };
    }

    it('posts the current document to /api/xtce/report and renders the rows', () => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Report'
      ) as HTMLButtonElement).click();

      const req = httpMock.expectOne('/api/xtce/report');
      expect(req.request.body.name).toBe('Sat');
      req.flush(sampleReport('Pass'));
      fixture.detectChanges();

      expect(compiled.querySelector('.report-panel')).toBeTruthy();
      expect(compiled.textContent).toContain('Schema: VALID');
      expect(compiled.textContent).toContain('PASS: 1');
      const row = compiled.querySelector('.report-table tbody tr') as HTMLElement;
      expect(row.textContent).toContain('63');
      expect(row.textContent).toContain('EnumeratedDataType/initialValue');
    });

    it('renders failing rows with their tagged findings', () => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Report'
      ) as HTMLButtonElement).click();
      httpMock.expectOne('/api/xtce/report').flush(sampleReport('Fail', [
        {
          ruleId: 'XTCE-1.2-R07-enum-initial-value-must-be-valid-label',
          severity: 'Error',
          location: 'Sat/ParameterTypeSet/Mode',
          message: "initialValue 'BAD' is not a valid label in Mode's EnumerationList.",
          candidateNumber: 63,
        },
      ]));
      fixture.detectChanges();

      expect(compiled.textContent).toContain('FAIL: 1');
      const findingItem = compiled.querySelector('.report-findings li') as HTMLElement;
      expect(findingItem.textContent).toContain('Sat/ParameterTypeSet/Mode');
      expect(findingItem.textContent).toContain('not a valid label');
    });

    it('closes the report panel, and any document edit clears a stale report', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Report'
      ) as HTMLButtonElement).click();
      httpMock.expectOne('/api/xtce/report').flush(sampleReport('Pass'));
      fixture.detectChanges();
      expect(compiled.querySelector('.report-panel')).toBeTruthy();

      (Array.from(compiled.querySelectorAll('.report-header button')).find(
        (b) => b.textContent?.trim() === 'Close'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(compiled.querySelector('.report-panel')).toBeNull();

      // Reopen, then edit the document — the stale report must vanish.
      fixture.componentInstance.onRunReport();
      httpMock.expectOne('/api/xtce/report').flush(sampleReport('Pass'));
      fixture.detectChanges();
      expect(compiled.querySelector('.report-panel')).toBeTruthy();

      const nameInput = compiled.querySelector('#node-name') as HTMLInputElement;
      nameInput.value = 'RenamedSat';
      nameInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(compiled.querySelector('.report-panel')).toBeNull();
      flushRevalidate();
    }));

    it('shows an error when the report request fails', () => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');

      fixture.componentInstance.onRunReport();
      httpMock.expectOne('/api/xtce/report').flush('boom', { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).toContain('Failed to build the conformance report.');
    });
  });
});
