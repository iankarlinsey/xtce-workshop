import { resolveLocation } from './validation';

describe('resolveLocation', () => {
  const positions = {
    'Sat': { line: 2, column: 1 },
    'Sat/ContainerSet/Frame': { line: 90, column: 5 },
    'Sat/CommandMetaData/MetaCommandSet/Cmd': { line: 200, column: 7 },
  };

  it('returns the exact position for a recorded location', () => {
    expect(resolveLocation('Sat/ContainerSet/Frame', positions)?.line).toBe(90);
  });

  it('falls back to the longest recorded ancestor for deeper citations', () => {
    expect(resolveLocation('Sat/CommandMetaData/MetaCommandSet/Cmd/CommandContainer', positions)?.line).toBe(200);
    expect(resolveLocation('Sat/ContainerSet/Frame/EntryList/Entry[3]', positions)?.line).toBe(90);
    expect(resolveLocation('Sat/TelemetryMetaData/Anything', positions)?.line).toBe(2);
  });

  it('returns null when nothing on the path is recorded', () => {
    expect(resolveLocation('Elsewhere/Unknown', positions)).toBeNull();
    expect(resolveLocation('Sat/ParameterSet/P', null)).toBeNull();
  });
});
