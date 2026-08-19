import { TestBed } from '@angular/core/testing';
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

    it('shows no validation panel when there are no issues', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'minimal.xml');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Minimal',
        document: { name: 'Minimal', children: [] },
        validationIssues: [],
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('.validation-panel')).toBeNull();
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

    it('clicking a tree row selects it without editing anything', () => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture);

      const compiled = fixture.nativeElement as HTMLElement;
      const busRow = Array.from(compiled.querySelectorAll('.tree-node-row')).find((r) =>
        r.textContent?.includes('Bus')
      ) as HTMLElement;
      busRow.click();
      fixture.detectChanges();

      expect(compiled.querySelector('.node-title')?.textContent).toContain('Bus');
      // selecting doesn't mutate the document — the tree still shows the full,
      // untouched structure alongside the now-selected Bus row
      expect(compiled.textContent).toContain('Payload');
    });

    it('editing the Name field updates the selected node and is reflected in Save', () => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture);

      const compiled = fixture.nativeElement as HTMLElement;
      const busRow = Array.from(compiled.querySelectorAll('.tree-node-row')).find((r) =>
        r.textContent?.includes('Bus')
      ) as HTMLElement;
      busRow.click();
      fixture.detectChanges();

      const nameInput = compiled.querySelector('#node-name') as HTMLInputElement;
      nameInput.value = 'Renamed Bus';
      nameInput.dispatchEvent(new Event('input'));
      fixture.detectChanges();

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
    });

    it('Add child on the selected node adds a child and is reflected in Save', () => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture); // root (Mission) is selected by default
      spyOn(window, 'prompt').and.returnValue('NewSub');

      const compiled = fixture.nativeElement as HTMLElement;
      const addButton = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === '+ Add child'
      ) as HTMLButtonElement;
      addButton.click();
      fixture.detectChanges();

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body.children.length).toBe(3);
      expect(req.request.body.children[2]).toEqual({ name: 'NewSub', children: [] });
      req.flush('<SpaceSystem/>');
    });

    it('has no Delete action when the root is selected', () => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture); // root selected by default

      const compiled = fixture.nativeElement as HTMLElement;
      const buttons = Array.from(compiled.querySelectorAll('button')).map((b) => b.textContent?.trim());
      expect(buttons).not.toContain('Delete');
    });

    it('Delete on a non-root selected node removes it and selects its parent', () => {
      const fixture = createAppAndFlushHealth();
      loadNestedDocument(fixture);

      const compiled = fixture.nativeElement as HTMLElement;
      const busRow = Array.from(compiled.querySelectorAll('.tree-node-row')).find((r) =>
        r.textContent?.includes('Bus')
      ) as HTMLElement;
      busRow.click();
      fixture.detectChanges();

      const deleteButton = Array.from(compiled.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Delete'
      ) as HTMLButtonElement;
      deleteButton.click();
      fixture.detectChanges();

      // selection moved to the parent (root)
      expect(compiled.querySelector('.node-title')?.textContent).toContain('Mission');
      expect(compiled.textContent).not.toContain('Bus');

      fixture.componentInstance.onSaveDocument();
      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body).toEqual({ name: 'Mission', children: [{ name: 'Payload', children: [] }] });
      req.flush('<SpaceSystem/>');
    });
  });
});
