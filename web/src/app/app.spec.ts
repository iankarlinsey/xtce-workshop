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

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.componentInstance).toBeTruthy();
    httpMock.expectOne('/api/health').flush({ status: 'ok' });
  });

  it('shows Backend: OK after a successful health check', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    httpMock.expectOne('/api/health').flush({ status: 'ok' });
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

  describe('Load', () => {
    function createAppAndFlushHealth() {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne('/api/health').flush({ status: 'ok' });
      return fixture;
    }

    function selectFile(fixture: ReturnType<typeof createAppAndFlushHealth>, name: string) {
      const file = new File(['<xml/>'], name, { type: 'application/xml' });
      const event = { target: { files: [file] } } as unknown as Event;
      fixture.componentInstance.onFileSelected(event);
    }

    it('renders a childless tree as a single root node', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'minimal.xml');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Minimal',
        tree: { label: 'Minimal', nodeType: 'SpaceSystem', children: [] },
        document: { name: 'Minimal', children: [] },
      });
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelectorAll('app-tree-node').length).toBe(1);
      expect(compiled.textContent).toContain('Minimal');
    });

    it('renders a nested tree as an expandable hierarchy', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Mission',
        tree: {
          label: 'Mission',
          nodeType: 'SpaceSystem',
          children: [
            { label: 'Bus', nodeType: 'SpaceSystem', children: [] },
            { label: 'Payload', nodeType: 'SpaceSystem', children: [] },
          ],
        },
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
      expect(compiled.querySelectorAll('app-tree-node').length).toBe(3);
      expect(compiled.textContent).toContain('Mission');
      expect(compiled.textContent).toContain('Bus');
      expect(compiled.textContent).toContain('Payload');
    });

    it('makes a loaded document immediately saveable, identically to New', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');
      const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Mission',
        tree: { label: 'Mission', nodeType: 'SpaceSystem', children: [] },
        document: { name: 'Mission', children: [] },
      });
      fixture.detectChanges();

      fixture.componentInstance.onSaveDocument();

      const req = httpMock.expectOne('/api/xtce/save');
      expect(req.request.body).toEqual({ name: 'Mission', children: [] });
      req.flush('<SpaceSystem name="Mission"/>');

      expect(clickSpy).toHaveBeenCalled();
    });

    it('filters the tree via the search box', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'nested.xml');

      httpMock.expectOne('/api/xtce/load').flush({
        name: 'Mission',
        tree: {
          label: 'Mission',
          nodeType: 'SpaceSystem',
          children: [
            { label: 'Bus', nodeType: 'SpaceSystem', children: [] },
            { label: 'Payload', nodeType: 'SpaceSystem', children: [] },
          ],
        },
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

    it('shows an error and no tree when loading fails', () => {
      const fixture = createAppAndFlushHealth();
      selectFile(fixture, 'broken.xml');

      httpMock.expectOne('/api/xtce/load').flush(
        { error: 'The document is not well-formed XML.' },
        { status: 400, statusText: 'Bad Request' }
      );
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.querySelector('app-tree-node')).toBeNull();
      expect(compiled.textContent).toContain('The document is not well-formed XML.');
    });
  });

  describe('New / Save', () => {
    function createAppAndFlushHealth() {
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      httpMock.expectOne('/api/health').flush({ status: 'ok' });
      return fixture;
    }

    it('creates a blank document with the entered name', () => {
      const fixture = createAppAndFlushHealth();
      spyOn(window, 'prompt').and.returnValue('Mission');

      fixture.componentInstance.onNewDocument();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).toContain('Mission');
    });

    it('does nothing when the name prompt is cancelled', () => {
      const fixture = createAppAndFlushHealth();
      spyOn(window, 'prompt').and.returnValue(null);

      fixture.componentInstance.onNewDocument();
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      expect(compiled.textContent).not.toContain('Current document');
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
});
