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
