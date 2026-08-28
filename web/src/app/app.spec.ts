import { DeferBlockBehavior, DeferBlockState, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient, HttpEventType } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { App } from './app';

describe('App', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
      // The source editor lives behind @defer. Manual keeps CodeMirror OUT of fakeAsync
      // editing tests (which only pass through source mode); tests that assert on the
      // editor render it explicitly via renderSourceEditor.
      deferBlockBehavior: DeferBlockBehavior.Manual,
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    App.pollDelayMs = 0; // job polling runs inline in tests
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createDocumentInline(fixture: ReturnType<typeof TestBed.createComponent<App>>, name: string) {
    const compiled = fixture.nativeElement as HTMLElement;
    (Array.from(compiled.querySelectorAll('rux-button')).find(
      (b) => b.textContent?.trim() === 'New'
    ) as HTMLButtonElement).click();
    fixture.detectChanges();
    // New seeds a skeleton and parses it; a clean result lands in the tree.
    flushTextJob({
      name,
      document: { name, children: [] },
      validationIssues: [],
      diagnostics: [],
      schemaErrors: [],
    });
    fixture.detectChanges();
  }

  function createAppAndFlushHealth() {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne('/api/health').flush({ status: 'ok' });
    return fixture;
  }


  let jobCounter = 0;

  /** Drives one load-job round trip: start -> poll(done) -> result. */
  function flushJob(startUrl: string, payload: Object, resultStatus?: number) {
    const id = `job${++jobCounter}`;
    httpMock.expectOne(startUrl).flush({ jobId: id });
    httpMock.expectOne(`/api/xtce/jobs/${id}`).flush({
      state: 'done', stage: 'done', percent: 100, ruleIndex: 0, ruleCount: 0, error: null,
    });
    if (resultStatus) {
      httpMock.expectOne(`/api/xtce/jobs/${id}/result`).flush(payload, { status: resultStatus, statusText: 'Bad Request' });
    } else {
      httpMock.expectOne(`/api/xtce/jobs/${id}/result`).flush(payload);
    }
  }

  function flushLoadJob(payload: Object, resultStatus?: number) {
    flushJob('/api/xtce/jobs', payload, resultStatus);
  }

  function flushTextJob(payload: Object, resultStatus?: number) {
    flushJob('/api/xtce/jobs/text', payload, resultStatus);
  }

  function flushLoadJob400(payload: Object) {
    flushJob('/api/xtce/jobs', payload, 400);
  }

  function flushTextJob400(payload: Object) {
    flushJob('/api/xtce/jobs/text', payload, 400);
  }

  /** Flushes a clean load job; the app auto-switches into the tree on its own. */
  function flushLoadIntoTree(fixture: ReturnType<typeof createAppAndFlushHealth>, payload: Object) {
    flushLoadJob(payload);
    fixture.detectChanges();
  }

  /** Renders the @defer'd source editor (Manual defer behavior leaves it as placeholder). */
  async function renderSourceEditor(fixture: ReturnType<typeof createAppAndFlushHealth>) {
    const deferBlocks = await fixture.getDeferBlocks();
    for (const block of deferBlocks) {
      await block.render(DeferBlockState.Complete);
    }
    fixture.detectChanges();
  }

  /** Tree item groups default collapsed; expand every group so item rows are reachable. */
  function expandAllGroups(fixture: ReturnType<typeof createAppAndFlushHealth>) {
    const compiled = fixture.nativeElement as HTMLElement;
    for (let i = 0; i < 25; i++) {
      const collapsedToggle = compiled.querySelector('.group-row .toggle[aria-expanded="false"]');
      if (!collapsedToggle) {
        return;
      }
      (collapsedToggle.closest('.group-row') as HTMLElement).click();
      fixture.detectChanges();
    }
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

  it('shows the backend build version when health reports one', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    httpMock.expectOne('/api/health').flush({ status: 'ok', version: 'abc1234 2026-08-21T00:00:00Z' });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.build-version')?.textContent).toContain('abc1234');
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

    it('offers .xtce files first in the file chooser', () => {
      const fixture = createAppAndFlushHealth();
      const input = (fixture.nativeElement as HTMLElement).querySelector('input[type=file]');
      expect(input?.getAttribute('accept')).toBe('.xtce,.xml');
    });

    it('selects the root and shows it in the main panel after loading', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'minimal.xml');

      flushLoadIntoTree(fixture, {
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

      flushLoadJob({
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

      flushLoadJob({
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
      expandAllGroups(fixture);
      const itemLabels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
      expect(itemLabels).toEqual(['Volt_Type', 'BusVoltage', 'Frame']);
    });

    it('filters the tree via the search box', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');

      flushLoadJob({
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

      flushLoadJob400(
        { error: 'The document is not well-formed XML.' });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.node-title')).toBeNull();
      expect(compiled.textContent).toContain('The document is not well-formed XML.');
    });

    it('renders validation issues returned by the load endpoint', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');

      flushLoadJob({
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
      expect(compiled.textContent).toContain('Rule findings (1)');
      expect(compiled.textContent).toContain('Mission/ParameterTypeSet/State_Type');
      expect(compiled.textContent).toContain('is not a valid label');
    });

    it('makes a loaded document immediately saveable, identically to New', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');
      const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

      flushLoadJob({
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

    it('New submits the skeleton for parsing without any name prompt', () => {
      const fixture = createAppAndFlushHealth();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('rux-button')).find(
        (b) => b.textContent?.trim() === 'New'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      const request = httpMock.expectOne('/api/xtce/jobs/text');
      expect(request.request.body.xml).toContain('<SpaceSystem');
      expect(compiled.querySelector('.creator-row')).toBeNull();
      request.flush({ jobId: 'jobNew' });
      httpMock.expectOne('/api/xtce/jobs/jobNew').flush({
        state: 'done', stage: 'done', percent: 100, ruleIndex: 0, ruleCount: 0, error: null,
      });
      httpMock.expectOne('/api/xtce/jobs/jobNew/result')
        .flush({ name: 'NewSystem', document: { name: 'NewSystem', children: [] } });
      fixture.detectChanges();
      expect(compiled.querySelector('.node-title')?.textContent).toContain('NewSystem');
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
      flushLoadIntoTree(fixture, {
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
      flushLoadIntoTree(fixture, {
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          header: { validationStatus: 'Test', version: '1.0' },
          preserved: [{ elementName: 'ServiceSet', outerXml: '<ServiceSet/>' }],
          telemetryMetaData: {
            parameterTypeSet: [
              {
                name: 'Volt_Type', kind: 'Float', sizeInBits: 32,
                dataEncoding: { kind: 'Float', sizeInBits: 32, encoding: 'IEEE754_1985' },
                unitSet: [{ value: 'V', description: 'volts' }],
              },
              { name: 'Mode_Type', kind: 'Enumerated', enumerations: [{ value: 0, label: 'IDLE' }] },
              { name: 'Uptime_Type', kind: 'RelativeTime', timeEncoding: { units: 'seconds', dataEncoding: { kind: 'Integer', sizeInBits: 32 } } },
            ],
            parameterSet: [{
              name: 'BusVoltage', parameterTypeRef: 'Volt_Type', initialValue: '28.5',
              properties: { dataSource: 'telemetered', readOnly: true },
            }],
            algorithmSet: [{
              name: 'Smooth', kind: 'Custom', language: 'python', algorithmText: 'y = x',
              inputs: [{ parameterRef: 'BusVoltage', name: 'x' }],
            }],
            containerSet: [{ name: 'Frame', entryList: [{ kind: 'ParameterRef', ref: 'BusVoltage' }] }],
          },
        },
      });
      fixture.detectChanges();
    }

    function clickTreeRowByText(fixture: ReturnType<typeof createAppAndFlushHealth>, text: string) {
      expandAllGroups(fixture);
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
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add child'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      const nameInput = compiled.querySelector('input[aria-label="New child name"]') as HTMLInputElement;
      nameInput.value = 'NewSub';
      (Array.from(compiled.querySelectorAll('.creator-row rux-button, .creator-row button')).find(
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
      const buttons = Array.from(compiled.querySelectorAll('rux-button, button')).map((b) => b.textContent?.trim());
      expect(buttons).not.toContain('Delete');
    });

    it('Delete on a non-root selected node removes it and selects its parent', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture);
      clickTreeRowByText(fixture, 'Bus');

      const compiled = fixture.nativeElement as HTMLElement;
      const deleteButton = Array.from(compiled.querySelectorAll('rux-button, button')).find(
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

      expect(compiled.textContent).toContain('Rule findings (1)');
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
      const addEnum = Array.from(compiled.querySelectorAll('rux-button, button')).find(
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

    it('the type form shows the modeled data encoding and edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Volt_Type');
      const compiled = fixture.nativeElement as HTMLElement;

      expect((compiled.querySelector('#enc-encoding') as HTMLInputElement).value).toBe('IEEE754_1985');
      expect((compiled.querySelector('#enc-size') as HTMLInputElement).value).toBe('32');

      const sizeInput = compiled.querySelector('#enc-size') as HTMLInputElement;
      sizeInput.value = '64';
      sizeInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.parameterTypeSet[0].dataEncoding)
        .toEqual({ kind: 'Float', sizeInBits: 64, encoding: 'IEEE754_1985' });
      req.flush('<SpaceSystem/>');
    }));

    it('units render on the type form and edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Volt_Type');
      const compiled = fixture.nativeElement as HTMLElement;

      const valueInput = compiled.querySelector('input[aria-label="Unit 0 value"]') as HTMLInputElement;
      expect(valueInput.value).toBe('V');
      valueInput.value = 'mV';
      valueInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.parameterTypeSet[0].unitSet)
        .toEqual([{ value: 'mV', description: 'volts' }]);
      req.flush('<SpaceSystem/>');
    }));

    it('the time-encoding section renders and units edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Uptime_Type');
      const compiled = fixture.nativeElement as HTMLElement;

      expect((compiled.querySelector('#time-units') as HTMLInputElement).value).toBe('seconds');
      expect(compiled.textContent).toContain('Inner encoding: IntegerDataEncoding, 32 bits');

      const unitsInput = compiled.querySelector('#time-units') as HTMLInputElement;
      unitsInput.value = 'picoSeconds';
      unitsInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.parameterTypeSet[2].timeEncoding.units).toBe('picoSeconds');
      req.flush('<SpaceSystem/>');
    }));

    it('parameter properties render as selects and edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'BusVoltage');
      const compiled = fixture.nativeElement as HTMLElement;

      const dataSourceSelect = compiled.querySelector('#param-datasource') as HTMLSelectElement;
      expect(dataSourceSelect.value).toBe('telemetered');
      expect((compiled.querySelector('#param-readonly') as HTMLSelectElement).value).toBe('true');

      dataSourceSelect.value = 'constant';
      dataSourceSelect.dispatchEvent(new Event('change'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.parameterSet[0].properties)
        .toEqual({ dataSource: 'constant', readOnly: true });
      req.flush('<SpaceSystem/>');
    }));

    it('the alarm editor adds ranges and edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Volt_Type');
      const compiled = fixture.nativeElement as HTMLElement;

      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add alarm'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      const setInput = (selector: string, value: string) => {
        const input = compiled.querySelector(selector) as HTMLInputElement;
        input.value = value;
        input.dispatchEvent(new Event('input'));
        fixture.detectChanges();
      };
      setInput('input[aria-label="warningRange minInclusive"]', '10');
      setInput('input[aria-label="warningRange maxInclusive"]', '90');
      setInput('input[aria-label="criticalRange minExclusive"]', '0');
      setInput('input[aria-label="criticalRange maxExclusive"]', '100');
      setInput('#alarm-rangeform', 'outside');
      setInput('#alarm-minviolations', '2');
      // Clearing every bound drops the range back to null.
      setInput('input[aria-label="watchRange minInclusive"]', '5');
      setInput('input[aria-label="watchRange minInclusive"]', '');
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const alarm = req.request.body.telemetryMetaData.parameterTypeSet[0].defaultAlarm;
      expect(alarm.warningRange).toEqual({ minInclusive: '10', maxInclusive: '90' });
      expect(alarm.criticalRange).toEqual({ minExclusive: '0', maxExclusive: '100' });
      expect(alarm.watchRange).toBeNull();
      expect(alarm.rangeForm).toBe('outside');
      expect(alarm.minViolations).toBe(2);
      req.flush('<SpaceSystem/>');
    }));

    it('the calibrator editor adds a polynomial and edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Volt_Type');
      const compiled = fixture.nativeElement as HTMLElement;

      const kindSelect = compiled.querySelector('select[aria-label="New calibrator kind"]') as HTMLSelectElement;
      kindSelect.value = 'Polynomial';
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add calibrator'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      const coefficient = compiled.querySelector('input[aria-label="Term 0 coefficient"]') as HTMLInputElement;
      coefficient.value = '2.5';
      coefficient.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.parameterTypeSet[0].dataEncoding.defaultCalibrator)
        .toEqual({ kind: 'Polynomial', terms: [{ coefficient: '2.5', exponent: '1' }] });
      req.flush('<SpaceSystem/>');
    }));

    it('algorithms render in the tree and text edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Smooth');
      const compiled = fixture.nativeElement as HTMLElement;

      expect(compiled.querySelector('.type-badge')?.textContent?.trim()).toBe('CustomAlgorithm');
      expect((compiled.querySelector('input[aria-label="Algorithm input 0 parameter ref"]') as HTMLInputElement).value)
        .toBe('BusVoltage');

      const textArea = compiled.querySelector('#algo-text') as HTMLTextAreaElement;
      expect(textArea.value).toBe('y = x');
      textArea.value = 'y = 2 * x';
      textArea.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.algorithmSet[0].algorithmText).toBe('y = 2 * x');
      req.flush('<SpaceSystem/>');
    }));

    it('algorithm input/output rows and unit rows add, edit, and remove', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Smooth');
      const compiled = fixture.nativeElement as HTMLElement;
      const clickButton = (label: string) => {
        (Array.from(compiled.querySelectorAll('rux-button, button')).find(
          (b) => b.textContent?.trim() === label
        ) as HTMLButtonElement).click();
        fixture.detectChanges();
      };

      clickButton('+ Add output');
      const outputRef = compiled.querySelector('input[aria-label="Algorithm output 0 parameter ref"]') as HTMLInputElement;
      outputRef.value = 'BusVoltage';
      outputRef.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      const outputName = compiled.querySelector('input[aria-label="Algorithm output 0 name"]') as HTMLInputElement;
      outputName.value = 'y';
      outputName.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (compiled.querySelector('button[aria-label="Remove algorithm input"]') as HTMLButtonElement).click();
      fixture.detectChanges();

      clickTreeRowByText(fixture, 'Volt_Type');
      clickButton('+ Add unit');
      const newUnit = compiled.querySelector('input[aria-label="Unit 1 value"]') as HTMLInputElement;
      newUnit.value = 'mV';
      newUnit.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      (compiled.querySelector('button[aria-label="Remove unit"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const algorithm = req.request.body.telemetryMetaData.algorithmSet[0];
      expect(algorithm.inputs).toEqual([]);
      expect(algorithm.outputs).toEqual([{ parameterRef: 'BusVoltage', name: 'y' }]);
      // First unit removed; the added 'mV' one remains.
      expect(req.request.body.telemetryMetaData.parameterTypeSet[0].unitSet).toEqual([{ value: 'mV' }]);
      req.flush('<SpaceSystem/>');
    }));

    it('a type without an encoding offers the add-encoding picker and creates one', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'Mode_Type');
      const compiled = fixture.nativeElement as HTMLElement;

      const kindSelect = compiled.querySelector('select[aria-label="New data encoding kind"]') as HTMLSelectElement;
      expect(kindSelect).toBeTruthy();
      kindSelect.value = 'Integer';
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add encoding'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.parameterTypeSet[1].dataEncoding).toEqual({ kind: 'Integer' });
      req.flush('<SpaceSystem/>');
    }));

    it('add-header seeds a Working validation status on a headerless root', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture); // no header
      const compiled = fixture.nativeElement as HTMLElement;

      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add header'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      const classificationInput = compiled.querySelector('#header-classification') as HTMLInputElement;
      classificationInput.value = 'Unrestricted';
      classificationInput.dispatchEvent(new Event('input'));
      const dateInput = compiled.querySelector('#header-date') as HTMLInputElement;
      dateInput.value = '2026-08-28';
      dateInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.header).toEqual(
        { validationStatus: 'Working', classification: 'Unrestricted', date: '2026-08-28' });
      req.flush('<SpaceSystem/>');
    }));

    it('the header panel edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture); // root selected
      const compiled = fixture.nativeElement as HTMLElement;

      const versionInput = compiled.querySelector('#header-version') as HTMLInputElement;
      expect(versionInput.value).toBe('1.0');
      versionInput.value = '2.0';
      versionInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.header).toEqual({ validationStatus: 'Test', version: '2.0' });
      req.flush('<SpaceSystem/>');
    }));

    it('adding a parameter type through the kind picker appears in the tree and Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture); // root selected

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add parameter type'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      (compiled.querySelector('input[aria-label="New type name"]') as HTMLInputElement).value = 'Flag_Type';
      (compiled.querySelector('select[aria-label="New parameter type kind"]') as HTMLSelectElement).value = 'Boolean';
      (Array.from(compiled.querySelectorAll('.creator-row rux-button, .creator-row button')).find(
        (b) => b.textContent?.trim() === 'Create'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      flushRevalidate();

      expandAllGroups(fixture);
      const itemLabels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
      expect(itemLabels).toContain('Flag_Type');

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      const added = req.request.body.telemetryMetaData.parameterTypeSet[3];
      expect(added).toEqual({ name: 'Flag_Type', kind: 'Boolean' });
      req.flush('<SpaceSystem/>');
    }));

    it('deleting a telemetry item selects its owning system', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadTelemetryDocument(fixture);
      clickTreeRowByText(fixture, 'BusVoltage');

      const compiled = fixture.nativeElement as HTMLElement;
      const deleteButton = Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === 'Delete'
      ) as HTMLButtonElement;
      deleteButton.click();
      fixture.detectChanges();
      flushRevalidate();

      expect(compiled.querySelector('.node-title')?.textContent).toContain('Sat');
      expandAllGroups(fixture);
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
      const addButton = Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add entry'
      ) as HTMLButtonElement;
      addButton.click();
      fixture.detectChanges();

      // Move the new entry (index 1) up to the front.
      const moveUpButtons = compiled.querySelectorAll('.entry-edit-row rux-button[aria-label="Move entry up"], button[aria-label="Move entry up"]');
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

      const removeButton = compiled.querySelector('.entry-edit-row rux-button[aria-label="Remove entry"], button[aria-label="Remove entry"]') as HTMLButtonElement;
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

      const addBase = Array.from(compiled.querySelectorAll('rux-button, button')).find(
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
      flushLoadIntoTree(fixture, {
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
      const moveDown = compiled.querySelector('.entry-edit-row rux-button[aria-label="Move entry down"], button[aria-label="Move entry down"]') as HTMLButtonElement;
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
      flushLoadIntoTree(fixture, {
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

      const addDim = Array.from(compiled.querySelectorAll('rux-button, button')).find(
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
      flushLoadIntoTree(fixture, {
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

      const removeMember = compiled.querySelector('rux-button[aria-label="Remove member"], button[aria-label="Remove member"]') as HTMLButtonElement;
      expect(removeMember.disabled).toBeTrue();

      const addMember = Array.from(compiled.querySelectorAll('rux-button, button')).find(
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
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add parameter type'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      (compiled.querySelector('input[aria-label="New type name"]') as HTMLInputElement).value = 'NewArray';
      (compiled.querySelector('select[aria-label="New parameter type kind"]') as HTMLSelectElement).value = 'Array';
      (compiled.querySelector('input[aria-label="New type element or member ref"]') as HTMLInputElement).value = 'Volt_Type';
      (Array.from(compiled.querySelectorAll('.creator-row rux-button, .creator-row button')).find(
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
      flushLoadIntoTree(fixture, {
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          telemetryMetaData: {
            parameterTypeSet: [],
            parameterSet: [],
            containerSet: [{ name: 'Packet', entryList: [] }],
            messageSet: {
              messages: [{ name: 'OpsMsg', containerRef: 'Packet', matchCriteria: { comparison: { parameterRef: 'MsgId', value: '7' } } }],
            },
          },
          commandMetaData: {
            commandContainerSet: [{
              name: 'SharedHdr',
              entryList: [
                { kind: 'FixedValue', binaryValue: '1ACF', sizeInBits: 16, repeat: { fixedCount: 2 } },
                { kind: 'Raw', rawXml: { elementName: 'ArrayArgumentRefEntry', outerXml: '<ArrayArgumentRefEntry/>' } },
              ],
            }],
            parameterTypeSet: [{ name: 'CmdApid_Type', kind: 'Integer', dataEncoding: { kind: 'Integer', sizeInBits: 11 } }],
            parameterSet: [{ name: 'CmdApid', parameterTypeRef: 'CmdApid_Type' }],
            argumentTypeSet: [{ name: 'CmdU8', kind: 'Integer', signed: false, sizeInBits: 8 }],
            metaCommands: [
              { name: 'BaseCmd', abstract: true },
              {
                name: 'Reboot',
                baseMetaCommandRef: 'BaseCmd',
                arguments: [{ name: 'delay', argumentTypeRef: 'CmdU8' }],
                commandContainer: {
                  name: 'RebootFrame',
                  entryList: [
                    { kind: 'FixedValue', binaryValue: '5A5A', sizeInBits: 16 },
                    { kind: 'ArgumentRef', ref: 'delay' },
                  ],
                },
                verifiers: [{ kind: 'CompleteVerifier', comparison: { parameterRef: 'Ack', value: '1' }, hasCheckWindow: true, timeToStopChecking: 'PT5S' }],
                transmissionConstraints: [{ timeOut: 'PT10S', comparison: { parameterRef: 'Mode', value: '1' } }],
                defaultSignificance: { consequenceLevel: 'critical', reasonForWarning: 'thruster fire' },
                interlock: { verificationToWaitFor: 'accepted', suspendable: true },
                parameterToSets: [{ parameterRef: 'CmdCount', newValue: '0' }],
              },
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

      expandAllGroups(fixture);
      const itemLabels = Array.from(compiled.querySelectorAll('.item-row .label')).map((el) => el.textContent?.trim());
      expect(itemLabels).toEqual(['Packet', 'OpsMsg', 'CmdApid_Type', 'CmdApid', 'CmdU8', 'BaseCmd', 'Reboot', 'SharedHdr']);

      clickTreeRowByText(fixture, 'OpsMsg');
      expect((compiled.querySelector('#message-containerref') as HTMLInputElement).value).toBe('Packet');

      clickTreeRowByText(fixture, 'Reboot');
      expect((compiled.querySelector('#command-baseref') as HTMLInputElement).value).toBe('BaseCmd');
      expect(compiled.textContent).toContain('CompleteVerifier');
      expect(compiled.textContent).toContain('Ack == 1');
      expect(compiled.textContent).toContain('Mode == 1 — timeout PT10S');
      expect(compiled.textContent).toContain('Significance: critical — thruster fire');
      expect(compiled.textContent).toContain('Interlock: waits for accepted, suspendable');
      expect(compiled.textContent).toContain('CmdCount = 0');
      expect((compiled.querySelector('input[aria-label="Argument 0 type ref"]') as HTMLInputElement).value).toBe('CmdU8');
    });

    it('command-side parameters and types open the shared forms and edits land in commandMetaData', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadMessagingDocument(fixture);
      const compiled = fixture.nativeElement as HTMLElement;

      clickTreeRowByText(fixture, 'CmdApid_Type');
      expect(compiled.querySelector('.type-badge')?.textContent?.trim()).toBe('IntegerParameterType');
      expect((compiled.querySelector('#enc-size') as HTMLInputElement).value).toBe('11');

      // clickTreeRowByText matches substrings and would hit CmdApid_Type again.
      expandAllGroups(fixture);
      (Array.from(compiled.querySelectorAll('.item-row .label'))
        .find((el) => el.textContent?.trim() === 'CmdApid')!
        .closest('.item-row') as HTMLElement).click();
      fixture.detectChanges();
      const refInput = compiled.querySelector('#param-typeref') as HTMLInputElement;
      expect(refInput.value).toBe('CmdApid_Type');
      const initialInput = compiled.querySelector('#param-initial') as HTMLInputElement;
      initialInput.value = '42';
      initialInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.commandMetaData.parameterSet)
        .toEqual([{ name: 'CmdApid', parameterTypeRef: 'CmdApid_Type', initialValue: '42' }]);
      req.flush('<SpaceSystem/>');
    }));

    it('standalone command containers open their form and edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadMessagingDocument(fixture);
      clickTreeRowByText(fixture, 'SharedHdr');
      const compiled = fixture.nativeElement as HTMLElement;

      expect(compiled.querySelector('.type-badge')?.textContent?.trim()).toBe('CommandContainer');
      expect(compiled.textContent).toContain('1ACF');
      expect(compiled.textContent).toContain('×2');
      expect(compiled.textContent).toContain('ArrayArgumentRefEntry');

      const baseRef = compiled.querySelector('#cc-baseref') as HTMLInputElement;
      baseRef.value = 'Packet';
      baseRef.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.commandMetaData.commandContainerSet[0].baseContainerRef).toBe('Packet');
      req.flush('<SpaceSystem/>');
    }));

    it('argument types open the shared type form under their ArgumentType element name', () => {
      const fixture = createAppAndFlushHealth();
      loadMessagingDocument(fixture);
      clickTreeRowByText(fixture, 'CmdU8');
      const compiled = fixture.nativeElement as HTMLElement;

      expect(compiled.querySelector('.type-badge')?.textContent?.trim()).toBe('IntegerArgumentType');
      expect((compiled.querySelector('#type-size') as HTMLInputElement).value).toBe('8');
    });

    it('the command container entry list renders and edits flow into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadMessagingDocument(fixture);
      clickTreeRowByText(fixture, 'Reboot');
      const compiled = fixture.nativeElement as HTMLElement;

      expect((compiled.querySelector('input[aria-label="Command entry 0 binary value"]') as HTMLInputElement).value).toBe('5A5A');
      expect((compiled.querySelector('input[aria-label="Command entry 1 reference"]') as HTMLInputElement).value).toBe('delay');

      const sizeInput = compiled.querySelector('input[aria-label="Command entry 0 size in bits"]') as HTMLInputElement;
      sizeInput.value = '32';
      sizeInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.commandMetaData.metaCommands[1].commandContainer.entryList).toEqual([
        { kind: 'FixedValue', binaryValue: '5A5A', sizeInBits: 32 },
        { kind: 'ArgumentRef', ref: 'delay' },
      ]);
      req.flush('<SpaceSystem/>');
    }));

    it('adding and reordering command container entries flows into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadMessagingDocument(fixture);
      clickTreeRowByText(fixture, 'Reboot');
      const compiled = fixture.nativeElement as HTMLElement;

      const kindSelect = compiled.querySelector('select[aria-label="New command entry kind"]') as HTMLSelectElement;
      const refInput = compiled.querySelector('input[aria-label="New command entry reference"]') as HTMLInputElement;
      kindSelect.value = 'ArgumentRef';
      refInput.value = 'mode';
      (Array.from(compiled.querySelectorAll('.add-entry-row rux-button, .add-entry-row button')).find(
        (b) => b.textContent?.trim() === '+ Add entry'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      fixture.componentInstance.onMoveCommandEntry(2, -1);
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.commandMetaData.metaCommands[1].commandContainer.entryList.map(
        (e: { kind: string; ref?: string }) => e.ref ?? e.kind
      )).toEqual(['FixedValue', 'mode', 'delay']);
      req.flush('<SpaceSystem/>');
    }));

    it('editing a command argument flows into Save under arguments', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadMessagingDocument(fixture);
      clickTreeRowByText(fixture, 'Reboot');
      const compiled = fixture.nativeElement as HTMLElement;

      const initialInput = compiled.querySelector('input[aria-label="Argument 0 initial value"]') as HTMLInputElement;
      initialInput.value = '5';
      initialInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.commandMetaData.metaCommands[1].arguments)
        .toEqual([{ name: 'delay', argumentTypeRef: 'CmdU8', initialValue: '5' }]);
      req.flush('<SpaceSystem/>');
    }));

    it('editing the message match comparison flows into Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadMessagingDocument(fixture);
      clickTreeRowByText(fixture, 'OpsMsg');
      const compiled = fixture.nativeElement as HTMLElement;

      const matchValue = compiled.querySelector('#message-matchvalue') as HTMLInputElement;
      expect(matchValue.value).toBe('7');
      matchValue.value = '9';
      matchValue.dispatchEvent(new Event('input'));
      const matchParam = compiled.querySelector('#message-matchparam') as HTMLInputElement;
      matchParam.value = 'PktId';
      matchParam.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      flushRevalidate();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.telemetryMetaData.messageSet.messages[0].matchCriteria)
        .toEqual({ comparison: { parameterRef: 'PktId', value: '9' } });
      req.flush('<SpaceSystem/>');
    }));

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
      expect(message.matchCriteria).toEqual({ comparison: { parameterRef: 'MsgId', value: '7' } });
      req.flush('<SpaceSystem/>');
    }));

    it('adding a command creates it under commandMetaData in Save', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture); // no commandMetaData yet; root selected

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add command'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      const nameInput = compiled.querySelector('input[aria-label="New command name"]') as HTMLInputElement;
      nameInput.value = 'NewCmd';
      (Array.from(compiled.querySelectorAll('.creator-row rux-button, .creator-row button')).find(
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

      const computeButton = Array.from(compiled.querySelectorAll('rux-button, button')).find(
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

      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
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
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === '+ Add base container'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      // One comparison -> serialized as the single-Comparison shape.
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
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
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
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
      flushLoadIntoTree(fixture, {
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
      flushLoadIntoTree(fixture, {
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
      expect(req.request.body.preserved).toEqual([{ elementName: 'ServiceSet', outerXml: '<ServiceSet/>' }]);
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
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
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
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
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
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === 'Report'
      ) as HTMLButtonElement).click();
      httpMock.expectOne('/api/xtce/report').flush(sampleReport('Pass'));
      fixture.detectChanges();
      expect(compiled.querySelector('.report-panel')).toBeTruthy();

      (Array.from(compiled.querySelectorAll('.report-header rux-button, .report-header button')).find(
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

    it('saves the open report as text via the backend renderer, and as JSON locally', () => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');
      const compiled = fixture.nativeElement as HTMLElement;

      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === 'Report'
      ) as HTMLButtonElement).click();
      httpMock.expectOne('/api/xtce/report').flush(sampleReport('Pass'));
      fixture.detectChanges();

      const objectUrlSpy = spyOn(URL, 'createObjectURL').and.returnValue('blob:test');
      spyOn(URL, 'revokeObjectURL');

      (Array.from(compiled.querySelectorAll('.report-header rux-button, .report-header button')).find(
        (b) => b.textContent?.trim() === 'Save text'
      ) as HTMLButtonElement).click();
      const request = httpMock.expectOne('/api/xtce/report/text');
      expect(request.request.body.name).toBe('Sat');
      request.flush('XTCE 1.2 conformance report: Sat\n');
      expect(objectUrlSpy).toHaveBeenCalledTimes(1);

      (Array.from(compiled.querySelectorAll('.report-header rux-button, .report-header button')).find(
        (b) => b.textContent?.trim() === 'Save JSON'
      ) as HTMLButtonElement).click();
      expect(objectUrlSpy).toHaveBeenCalledTimes(2); // JSON path is client-side, no HTTP
    });

    it('computes and renders document metrics for the root, cleared by any edit', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === 'Document metrics'
      ) as HTMLButtonElement).click();

      const counts = {
        childSystems: 0, parameters: 3, parameterTypes: 2, parameterTypesByKind: { Integer: 2 },
        containers: 1, messages: 0, metaCommands: 0, preservedFragments: 4,
      };
      httpMock.expectOne('/api/xtce/metrics').flush({
        totals: counts,
        systems: [{ systemPath: 'Sat', local: counts, deep: counts }],
      });
      fixture.detectChanges();

      const table = compiled.querySelector('.metrics-table') as HTMLElement;
      expect(table).toBeTruthy();
      expect(table.textContent).toContain('Sat');
      expect(table.textContent).toContain('3');

      const nameInput = compiled.querySelector('#node-name') as HTMLInputElement;
      nameInput.value = 'RenamedSat';
      nameInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(compiled.querySelector('.metrics-table')).toBeNull();
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

  describe('Load diagnostics', () => {
    function postFile(fixture: ReturnType<typeof createAppAndFlushHealth>) {
      const file = new File(['<xml/>'], 'broken.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
    }

    it('a 200 response without a document surfaces an error instead of silence', () => {
      const fixture = createAppAndFlushHealth();
      postFile(fixture);
      // e.g. an intermediary (proxy/auth layer) swallowing the API response shape
      flushLoadJob({ unexpected: 'shape' });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).toContain('response did not contain a document');
      expect(compiled.querySelector('.tree-container')).toBeNull();
    });

    it('renders quarantine diagnostics on a partial load', () => {
      const fixture = createAppAndFlushHealth();
      postFile(fixture);
      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        validationIssues: [],
        diagnostics: [{ kind: 'ModelError', message: "missing 'parameterTypeRef'", path: 'Sat/ParameterSet/Parameter[NoTypeRef]', line: 7, column: 8 }],
        schemaErrors: [{ message: 'The required attribute parameterTypeRef is missing.', line: 7, column: 8 }],
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).toContain('1 quarantined element(s)');
      expect(compiled.textContent).toContain('Parameter[NoTypeRef]');
      expect(compiled.textContent).toContain('7:8');
      expect(compiled.textContent).toContain('required attribute');
    });

    it('renders the full evidence when the load fails outright', () => {
      const fixture = createAppAndFlushHealth();
      postFile(fixture);
      flushLoadJob400(
        {
          error: 'Not well-formed XML: unexpected end of file.',
          diagnostics: [{ kind: 'MalformedXml', message: 'Not well-formed XML: unexpected end of file.', path: '(document)', line: 2, column: 1 }],
          schemaErrors: [],
        });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).toContain('Not well-formed XML');
      expect(compiled.textContent).toContain('XML');
      expect(compiled.textContent).toContain('2:1');
      expect(compiled.querySelector('.load-diagnostics-summary')).toBeNull(); // hard failure, not partial
    });
  });

  describe('Search and usages', () => {
    function loadSearchableDocument(fixture: ReturnType<typeof createAppAndFlushHealth>) {
      const file = new File(['<xml/>'], 'telemetry.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      flushLoadIntoTree(fixture, {
        name: 'Sat',
        document: {
          name: 'Sat',
          children: [],
          telemetryMetaData: {
            parameterTypeSet: [{ name: 'Volt_Type', kind: 'Float' }],
            parameterSet: [{ name: 'BattVoltage', parameterTypeRef: 'Volt_Type' }],
          },
        },
      });
      fixture.detectChanges();
    }

    it('debounces a backend search and selects the clicked match', fakeAsync(() => {
      const fixture = createAppAndFlushHealth();
      loadSearchableDocument(fixture);

      const compiled = fixture.nativeElement as HTMLElement;
      const searchInput = compiled.querySelector('.search-field input') as HTMLInputElement;
      searchInput.value = 'EPS_V';
      searchInput.dispatchEvent(new Event('input'));
      tick(App.revalidateDelayMs);

      const request = httpMock.expectOne('/api/xtce/search');
      expect(request.request.body.query).toBe('EPS_V');
      request.flush({ matches: [{ kind: 'Parameter', systemPath: 'Sat', name: 'BattVoltage', matchedAlias: 'EPS_V_BATT' }] });
      fixture.detectChanges();

      const result = compiled.querySelector('.search-result') as HTMLButtonElement;
      expect(result.textContent).toContain('BattVoltage');
      expect(result.textContent).toContain('alias: EPS_V_BATT');

      result.click();
      fixture.detectChanges();
      expect(compiled.querySelector('#param-name')).toBeTruthy(); // parameter editor opened
      expect((compiled.querySelector('#param-name') as HTMLInputElement).value).toBe('BattVoltage');
    }));

    it('posts the document for CSV export from the root view', () => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');
      const compiled = fixture.nativeElement as HTMLElement;

      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === 'Export parameters CSV'
      ) as HTMLButtonElement).click();

      const request = httpMock.expectOne('/api/xtce/export/parameters');
      expect(request.request.body.name).toBe('Sat');
      request.flush('SystemPath,Name\r\n');
    });

    it('finds usages for the selected parameter and renders them', () => {
      const fixture = createAppAndFlushHealth();
      loadSearchableDocument(fixture);
      const compiled = fixture.nativeElement as HTMLElement;

      fixture.componentInstance.onSelectSearchMatch(
        { kind: 'Parameter', systemPath: 'Sat', name: 'BattVoltage', matchedAlias: null });
      fixture.detectChanges();

      (Array.from(compiled.querySelectorAll('rux-button, button')).find(
        (b) => b.textContent?.trim() === 'Find usages'
      ) as HTMLButtonElement).click();

      const request = httpMock.expectOne('/api/xtce/usages');
      expect(request.request.body.systemPath).toBe('Sat');
      expect(request.request.body.parameterName).toBe('BattVoltage');
      request.flush({ usages: [{ kind: 'ParameterRefEntry', location: 'Sat/ContainerSet/Hk', detail: 'BattVoltage' }] });
      fixture.detectChanges();

      const row = compiled.querySelector('.usage-row') as HTMLElement;
      expect(row.textContent).toContain('ParameterRefEntry');
      expect(row.textContent).toContain('Sat/ContainerSet/Hk');
    });
  });

  describe('Loading indicator', () => {
    function selectFile(fixture: ReturnType<typeof createAppAndFlushHealth>, name: string) {
      const file = new File(['<xml/>'], name, { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
    }

    it('shows a spinner with the filename while the load request is in flight', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'big.xml');
      fixture.detectChanges();

      const row = (fixture.nativeElement as HTMLElement).querySelector('.loading-modal');
      expect(row).toBeTruthy();
      expect(row?.textContent).toContain('Loading big.xml');
      expect(row?.querySelector('rux-indeterminate-progress')).toBeTruthy();

      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
      });
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).querySelector('.loading-modal')).toBeNull();
    });

    it('shows staged progress: upload percent from events, cancel aborts the request', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'big.xml');
      fixture.detectChanges();

      const request = httpMock.expectOne('/api/xtce/jobs');
      request.event({ type: HttpEventType.UploadProgress, loaded: 45, total: 100 });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const stages = Array.from(compiled.querySelectorAll('.loading-stage')).map((s) => s.textContent?.trim());
      expect(stages.some((s) => s?.includes('Upload') && s.includes('45%'))).toBeTrue();

      request.event({ type: HttpEventType.UploadProgress, loaded: 100, total: 100 });
      fixture.detectChanges();
      const analyze = Array.from(compiled.querySelectorAll('.loading-stage'))
        .find((s) => s.textContent?.includes('Analyze')) as HTMLElement;
      expect(analyze.classList).toContain('stage-active');

      (Array.from(compiled.querySelectorAll('.loading-modal rux-button')).find(
        (b) => b.textContent?.trim() === 'Cancel'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(request.cancelled).toBeTrue();
      expect(compiled.querySelector('.loading-modal')).toBeNull();
      expect(compiled.querySelector('.error')?.textContent).toContain('Cancelled');
    });

    it('flags large documents in the modal', () => {
      const fixture = createAppAndFlushHealth();
      const big = new File([new ArrayBuffer(30 * 1048576)], 'huge.xtce', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [big] } } as unknown as Event);
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.loading-hint')?.textContent).toContain('Large document');
      expect(compiled.querySelector('.loading-size')?.textContent).toContain('30 MB');
      flushLoadJob({ name: 'H', document: { name: 'H', children: [] } });
      fixture.detectChanges();
      expect(compiled.querySelector('.loading-modal')).toBeNull();
    });

    it('clears the spinner when the load fails', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'bad.xml');
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).querySelector('.loading-modal')).toBeTruthy();

      flushLoadJob400(
        { error: 'nope' });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.loading-modal')).toBeNull();
      expect(compiled.querySelector('.error')?.textContent).toContain('nope');
    });
  });

  describe('Namespace detection', () => {
    function selectFile(fixture: ReturnType<typeof createAppAndFlushHealth>, name: string) {
      const file = new File(['<xml/>'], name, { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
    }

    it('shows the declared version quietly for an XTCE 1.2 document', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'modern.xml');

      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        rootNamespace: 'http://www.omg.org/spec/XTCE/20180204',
        detectedVersion: '1.2',
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).toContain('XTCE 1.2');
      expect(compiled.querySelector('.namespace-advisory')).toBeNull();
    });

    it('shows an advisory when the document declares the legacy XTCE namespace', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'legacy.xml');

      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        rootNamespace: 'http://www.omg.org/space/xtce',
        detectedVersion: '1.0/1.1',
      });
      fixture.detectChanges();

      const advisory = (fixture.nativeElement as HTMLElement).querySelector('.namespace-advisory');
      expect(advisory?.textContent).toContain('XTCE 1.0/1.1');
      expect(advisory?.textContent).toContain('targets 1.2');
    });

    it('shows an advisory when the namespace is not an XTCE namespace at all', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'other.xml');

      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        rootNamespace: 'http://example.com/other',
        detectedVersion: null,
      });
      fixture.detectChanges();

      const advisory = (fixture.nativeElement as HTMLElement).querySelector('.namespace-advisory');
      expect(advisory?.textContent).toContain('not an XTCE namespace');
    });
  });

  describe('Tree-side findings', () => {
    const payloadWithIssue = {
      name: 'Sat',
      document: {
        name: 'Sat',
        children: [],
        telemetryMetaData: {
          parameterTypeSet: [{ name: 'T', kind: 'Integer' }],
          parameterSet: [{ name: 'P', parameterTypeRef: 'Ghost' }],
        },
      },
      validationIssues: [
        { ruleId: 'R11', severity: 'Error', location: 'Sat/ParameterSet/P', message: 'dangling ref' },
      ],
    };

    async function loadWithIssueAndOpenTree(fixture: ReturnType<typeof createAppAndFlushHealth>) {
      const file = new File(['<x/>'], 'f.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      flushLoadJob(payloadWithIssue);
      await file.text();
      await fixture.whenStable();
      fixture.detectChanges();
      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('.view-toggle rux-button')).find(
        (b) => b.textContent?.trim() === 'Tree'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
      flushTextJob(payloadWithIssue);
      fixture.detectChanges();
    }

    it('clicking a rule finding in tree view selects the offending node', async () => {
      const fixture = createAppAndFlushHealth();
      await loadWithIssueAndOpenTree(fixture);

      const compiled = fixture.nativeElement as HTMLElement;
      (compiled.querySelector('.validation-issue') as HTMLElement).click();
      fixture.detectChanges();

      // The parameter's form opens in the main panel; the group auto-reveals it.
      expect(compiled.querySelector('.node-title')?.textContent).toContain('P');
      const selectedRow = compiled.querySelector('.item-row.selected');
      expect(selectedRow?.textContent).toContain('P');
    });

    it('an unmappable finding falls back to the source position', async () => {
      const fixture = createAppAndFlushHealth();
      await loadWithIssueAndOpenTree(fixture);
      (fixture.componentInstance as unknown as {
        validationIssues: { set: (v: unknown[]) => void };
      }).validationIssues.set([
        { ruleId: 'R11', severity: 'Error', location: 'Sat/ParameterSet/NotInModel', message: 'ghost' },
      ]);
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      (compiled.querySelector('.validation-issue') as HTMLElement).click();
      fixture.detectChanges();

      // Falls back to source view (serialize + pairing re-parse).
      httpMock.expectOne('/api/xtce/save').flush('<SpaceSystem name="Sat"/>');
      fixture.detectChanges();
      httpMock.expectOne('/api/xtce/load-text').flush(payloadWithIssue);
      fixture.detectChanges();
      expect(compiled.querySelector('.main-panel-source')).toBeTruthy();
    });

    it('Save in source view downloads the editor text verbatim without a server call', async () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<SpaceSystem name="Broken"'], 'broken.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      flushLoadJob400(
        { error: 'nope', diagnostics: [], schemaErrors: [] });
      await file.text();
      await fixture.whenStable();
      fixture.detectChanges();

      const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');
      const urlSpy = spyOn(URL, 'createObjectURL').and.returnValue('blob:test');
      fixture.componentInstance.onSaveDocument();

      httpMock.expectNone('/api/xtce/save');
      expect(clickSpy).toHaveBeenCalled();
      expect(urlSpy).toHaveBeenCalled();
      const blob = urlSpy.calls.mostRecent().args[0] as Blob;
      expect(await blob.text()).toBe('<SpaceSystem name="Broken"');
    });
  });

  describe('Source view', () => {
    function clickViewToggle(fixture: ReturnType<typeof createAppAndFlushHealth>, label: string) {
      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('.view-toggle rux-button')).find(
        (b) => b.textContent?.trim() === label
      ) as HTMLButtonElement).click();
      fixture.detectChanges();
    }

    /** Into source view from a created document: save serializes, then the pairing re-parse runs. */
    async function openSourceView(fixture: ReturnType<typeof createAppAndFlushHealth>, xml = '<SpaceSystem name="Sat"/>') {
      clickViewToggle(fixture, 'Source');
      httpMock.expectOne('/api/xtce/save').flush(xml);
      fixture.detectChanges();
      httpMock.expectOne('/api/xtce/load-text').flush({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        validationIssues: [],
        diagnostics: [],
        schemaErrors: [],
      });
      fixture.detectChanges();
      await renderSourceEditor(fixture);
    }

    it('serializes the document, re-parses it for fresh markers, and shows the editor', async () => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');

      clickViewToggle(fixture, 'Source');
      const request = httpMock.expectOne('/api/xtce/save');
      expect(request.request.body.name).toBe('Sat');
      request.flush('<SpaceSystem name="Sat"/>');
      fixture.detectChanges();
      // The pairing rule: markers must describe exactly the text on screen.
      httpMock.expectOne('/api/xtce/load-text').flush({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
      });
      fixture.detectChanges();
      await renderSourceEditor(fixture);

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('app-source-view')).toBeTruthy();
      expect(compiled.querySelector('.node-title')).toBeNull();
      expect(compiled.querySelector('.monaco-editor')).toBeTruthy();
    });

    it('re-parses the source text into the document when switching back to tree', async () => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');
      await openSourceView(fixture);

      clickViewToggle(fixture, 'Tree');
      const request = httpMock.expectOne('/api/xtce/jobs/text');
      expect(request.request.body.xml).toContain('SpaceSystem');
      request.flush({ jobId: 'jobTree' });
      httpMock.expectOne('/api/xtce/jobs/jobTree').flush({
        state: 'done', stage: 'done', percent: 100, ruleIndex: 0, ruleCount: 0, error: null,
      });
      httpMock.expectOne('/api/xtce/jobs/jobTree/result').flush({
        name: 'Renamed',
        document: { name: 'Renamed', children: [] },
        validationIssues: [],
        diagnostics: [],
        schemaErrors: [],
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('app-source-view')).toBeNull();
      expect(compiled.querySelector('.node-title')?.textContent).toContain('Renamed');
    });

    it('stays in source view with the error when the edited text no longer parses', async () => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');
      await openSourceView(fixture);

      clickViewToggle(fixture, 'Tree');
      flushTextJob400(
        {
          error: 'Not well-formed XML: unexpected end of file.',
          diagnostics: [{ kind: 'MalformedXml', message: 'unexpected end of file.', path: '(document)', line: 1, column: 24 }],
          schemaErrors: [],
        });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('app-source-view')).toBeTruthy();
      expect(compiled.querySelector('.error')?.textContent).toContain('Not well-formed XML');
      // The text has no parseable document any more, so the tree is gated off.
      expect(compiled.querySelector('.tree-container')).toBeNull();
    });

    it('shows the re-scanning spinner while switching back to tree', async () => {
      const fixture = createAppAndFlushHealth();
      createDocumentInline(fixture, 'Sat');
      await openSourceView(fixture);

      clickViewToggle(fixture, 'Tree');
      const row = (fixture.nativeElement as HTMLElement).querySelector('.loading-modal');
      expect(row?.textContent).toContain('Re-scanning source');

      flushTextJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
      });
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).querySelector('.loading-modal')).toBeNull();
    });

    it('auto-switches to tree when the initial load is completely clean', async () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<SpaceSystem name="Sat"/>'], 'ok.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);

      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        validationIssues: [],
        diagnostics: [],
        schemaErrors: [],
      });
      await file.text();
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.main-panel-source')).toBeNull();
      expect(compiled.querySelector('.node-title')?.textContent).toContain('Sat');
    });

    it('stays in source with the tree enabled when the load has findings', async () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<SpaceSystem name="Sat"/>'], 'findings.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);

      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        validationIssues: [
          { ruleId: 'R11', severity: 'Error', location: 'Sat/ParameterSet/P', message: 'dangling ref' },
        ],
      });
      await file.text();
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.main-panel-source')).toBeTruthy();
      const treeButton = Array.from(compiled.querySelectorAll('.view-toggle rux-button')).find(
        (b) => b.textContent?.trim() === 'Tree'
      ) as HTMLButtonElement & { disabled: boolean };
      expect(treeButton.disabled).toBeFalsy();
    });

    it('Re-scan re-runs the pipeline and stays in source view with an all-clear', async () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<SpaceSystem name="Sat"'], 'broken.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      flushLoadJob400(
        {
          error: 'Not well-formed XML.',
          diagnostics: [{ kind: 'MalformedXml', message: 'unexpected end of file.', path: '(document)', line: 1, column: 24 }],
          schemaErrors: [],
        });
      await file.text();
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('rux-button')).find(
        (b) => b.textContent?.trim() === 'Re-scan'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      flushTextJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        validationIssues: [],
        diagnostics: [],
        schemaErrors: [],
      });
      fixture.detectChanges();

      // Clean re-scan: stay put, show the all-clear, and enable the Tree toggle.
      expect(compiled.querySelector('.main-panel-source')).toBeTruthy();
      expect(compiled.querySelector('.error')).toBeNull();
      expect(compiled.querySelector('.all-clear')).toBeTruthy();
      const treeButton = Array.from(compiled.querySelectorAll('.view-toggle rux-button')).find(
        (b) => b.textContent?.trim() === 'Tree'
      ) as HTMLButtonElement & { disabled: boolean };
      expect(treeButton.disabled).toBeFalsy();
    });

    it('maps validation issues onto source lines through the position index', () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<x/>'], 'findings.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        validationIssues: [
          { ruleId: 'R11', severity: 'Error', location: 'Sat/ParameterSet/ITEM6', message: 'dangling ref' },
          { ruleId: 'R05', severity: 'Warning', location: 'Sat/ContainerSet/Frame/EntryList/Deep', message: 'subset' },
          { ruleId: 'R99', severity: 'Error', location: 'Elsewhere/Unknown', message: 'unmapped' },
        ],
        positions: {
          'Sat/ParameterSet/ITEM6': { line: 42, column: 7 },
          'Sat/ContainerSet/Frame': { line: 90, column: 5 },
        },
      });
      fixture.detectChanges();

      type Marker = { line: number | null; message: string; severity: string };
      const markers = (fixture.componentInstance as unknown as { sourceMarkers: () => Marker[] }).sourceMarkers();
      const byMessage = (part: string) => markers.find((m) => m.message.includes(part))!;
      expect(byMessage('dangling ref').line).toBe(42);
      expect(byMessage('dangling ref').severity).toBe('error');
      // Deeper citation falls back to the longest recorded ancestor.
      expect(byMessage('subset').line).toBe(90);
      expect(byMessage('subset').severity).toBe('warning');
      expect(byMessage('unmapped').line).toBeNull();
    });

    it('clicking a validation issue reveals its source line', () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<x/>'], 'findings.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        validationIssues: [
          { ruleId: 'R11', severity: 'Error', location: 'Sat/ParameterSet/ITEM6', message: 'dangling ref' },
        ],
        positions: { 'Sat/ParameterSet/ITEM6': { line: 42, column: 7 } },
      });
      fixture.detectChanges();

      ((fixture.nativeElement as HTMLElement).querySelector('.validation-issue') as HTMLElement).click();
      fixture.detectChanges();

      const target = (fixture.componentInstance as unknown as {
        revealTarget: () => { line: number } | null;
      }).revealTarget();
      expect(target?.line).toBe(42);
    });

    it('Format pretty-prints the editor text and automatically re-scans it', async () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<SpaceSystem name="Sat"/>'], 'dense.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);
      flushLoadJob({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        validationIssues: [
          { ruleId: 'R11', severity: 'Error', location: 'Sat/ParameterSet/P', message: 'finding keeps us in source' },
        ],
      });
      await file.text();
      await fixture.whenStable();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      (Array.from(compiled.querySelectorAll('rux-button')).find(
        (b) => b.textContent?.trim() === 'Format'
      ) as HTMLButtonElement).click();
      fixture.detectChanges();

      const formatRequest = httpMock.expectOne('/api/xtce/format');
      expect(formatRequest.request.body.xml).toContain('SpaceSystem');
      formatRequest.flush('<SpaceSystem name="Sat">\n</SpaceSystem>');
      fixture.detectChanges();

      // The automatic re-scan runs against the FORMATTED text.
      const rescanRequest = httpMock.expectOne('/api/xtce/jobs/text');
      expect(rescanRequest.request.body.xml).toBe('<SpaceSystem name="Sat">\n</SpaceSystem>');
      rescanRequest.flush({ jobId: 'jobFmt' });
      httpMock.expectOne('/api/xtce/jobs/jobFmt').flush({
        state: 'done', stage: 'done', percent: 100, ruleIndex: 0, ruleCount: 0, error: null,
      });
      httpMock.expectOne('/api/xtce/jobs/jobFmt/result').flush({
        name: 'Sat',
        document: { name: 'Sat', children: [] },
        validationIssues: [],
        diagnostics: [],
        schemaErrors: [],
      });
      fixture.detectChanges();

      expect(compiled.querySelector('.main-panel-source')).toBeTruthy();
      expect((fixture.componentInstance as unknown as { sourceText: () => string }).sourceText())
        .toBe('<SpaceSystem name="Sat">\n</SpaceSystem>');
    });

    it('opens the original file text in source view when a file fails to load', async () => {
      const fixture = createAppAndFlushHealth();
      const file = new File(['<SpaceSystem name="Broken"'], 'broken.xml', { type: 'application/xml' });
      fixture.componentInstance.onFileSelected({ target: { files: [file] } } as unknown as Event);

      flushLoadJob400(
        {
          error: 'Not well-formed XML.',
          diagnostics: [{ kind: 'MalformedXml', message: 'unexpected end of file.', path: '(document)', line: 1, column: 27 }],
          schemaErrors: [],
        });
      await file.text(); // the component reads the same file; wait out the microtask chain
      await fixture.whenStable();
      fixture.detectChanges();
      await renderSourceEditor(fixture);

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('app-source-view')).toBeTruthy();
      expect((fixture.componentInstance as unknown as { sourceText: () => string }).sourceText())
        .toContain('SpaceSystem name="Broken"');
      expect(compiled.querySelector('.error')?.textContent).toContain('Not well-formed XML');
    });
  });
});
