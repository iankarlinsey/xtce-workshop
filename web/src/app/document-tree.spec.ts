import {
  getNodeAtPath,
  updateNodeAtPath,
  deleteNodeAtPath,
  pathsEqual,
  selectionsEqual,
  getItemAtSelection,
  updateItemAtSelection,
  addItemToSystem,
  deleteItemAtSelection,
  collectParameterTypeNames,
  SpaceSystemDocument,
  ParameterTypeDoc,
} from './document-tree';

describe('document-tree', () => {
  function sampleDoc(): SpaceSystemDocument {
    return {
      name: 'Mission',
      children: [
        {
          name: 'Bus',
          children: [
            { name: 'Power', children: [] },
            { name: 'Thermal', children: [] },
          ],
        },
        { name: 'Payload', children: [] },
      ],
    };
  }

  describe('pathsEqual', () => {
    it('treats two nulls as equal', () => {
      expect(pathsEqual(null, null)).toBe(true);
    });

    it('treats null and a path as unequal', () => {
      expect(pathsEqual(null, [0])).toBe(false);
      expect(pathsEqual([0], null)).toBe(false);
    });

    it('compares paths element-wise', () => {
      expect(pathsEqual([0, 1], [0, 1])).toBe(true);
      expect(pathsEqual([0, 1], [0, 2])).toBe(false);
      expect(pathsEqual([0], [0, 0])).toBe(false);
    });
  });

  describe('getNodeAtPath', () => {
    it('returns the root for an empty path', () => {
      expect(getNodeAtPath(sampleDoc(), [])?.name).toBe('Mission');
    });

    it('returns a nested node for a multi-segment path', () => {
      expect(getNodeAtPath(sampleDoc(), [0, 0])?.name).toBe('Power');
    });

    it('returns null for an out-of-range path', () => {
      expect(getNodeAtPath(sampleDoc(), [5])).toBeNull();
      expect(getNodeAtPath(sampleDoc(), [0, 5])).toBeNull();
    });
  });

  describe('updateNodeAtPath', () => {
    it('updates the root without touching siblings', () => {
      const result = updateNodeAtPath(sampleDoc(), [], (n) => ({ ...n, name: 'Renamed' }));
      expect(result.name).toBe('Renamed');
      expect(result.children.length).toBe(2);
    });

    it('updates a nested node, leaving the rest of the tree untouched', () => {
      const original = sampleDoc();
      const result = updateNodeAtPath(original, [0, 1], (n) => ({ ...n, name: 'Renamed Thermal' }));

      expect(getNodeAtPath(result, [0, 1])?.name).toBe('Renamed Thermal');
      expect(getNodeAtPath(result, [0, 0])?.name).toBe('Power');
      expect(getNodeAtPath(result, [1])?.name).toBe('Payload');
      // original is untouched (immutable update)
      expect(getNodeAtPath(original, [0, 1])?.name).toBe('Thermal');
    });

    it('can append a child to a node', () => {
      const result = updateNodeAtPath(sampleDoc(), [1], (n) => ({
        ...n,
        children: [...n.children, { name: 'NewChild', children: [] }],
      }));

      expect(getNodeAtPath(result, [1])?.children.length).toBe(1);
      expect(getNodeAtPath(result, [1, 0])?.name).toBe('NewChild');
    });
  });

  describe('deleteNodeAtPath', () => {
    it('removes a leaf node from its parent', () => {
      const result = deleteNodeAtPath(sampleDoc(), [0, 0]);

      expect(getNodeAtPath(result, [0])?.children.length).toBe(1);
      expect(getNodeAtPath(result, [0, 0])?.name).toBe('Thermal');
    });

    it('removes a node with its own children (subtree goes with it)', () => {
      const result = deleteNodeAtPath(sampleDoc(), [0]);

      expect(result.children.length).toBe(1);
      expect(result.children[0].name).toBe('Payload');
    });

    it('leaves siblings and the rest of the tree untouched', () => {
      const result = deleteNodeAtPath(sampleDoc(), [1]);

      expect(result.name).toBe('Mission');
      expect(getNodeAtPath(result, [0, 0])?.name).toBe('Power');
      expect(getNodeAtPath(result, [0, 1])?.name).toBe('Thermal');
    });
  });

  function telemetryDoc(): SpaceSystemDocument {
    return {
      name: 'Sat',
      children: [{ name: 'Bus', children: [] }],
      // extra unknown property to verify spread passthrough (stand-in for preserved XML)
      preserved: [{ elementName: 'Header', outerXml: '<Header/>' }],
      telemetryMetaData: {
        parameterTypeSet: [
          { name: 'Volt_Type', kind: 'Float', preserved: [{ elementName: 'UnitSet', outerXml: '<UnitSet/>' }] },
          { name: 'Mode_Type', kind: 'Enumerated', enumerations: [{ value: 0, label: 'IDLE' }] },
        ],
        parameterSet: [{ name: 'BusVoltage', parameterTypeRef: 'Volt_Type' }],
        containerSet: [{ name: 'Frame', entryList: [] }],
      },
    };
  }

  describe('selectionsEqual', () => {
    it('compares system selections by path', () => {
      expect(selectionsEqual({ systemPath: [0] }, { systemPath: [0] })).toBe(true);
      expect(selectionsEqual({ systemPath: [0] }, { systemPath: [1] })).toBe(false);
    });

    it('distinguishes system selections from item selections', () => {
      expect(selectionsEqual({ systemPath: [] }, { systemPath: [], item: { kind: 'parameter', index: 0 } })).toBe(false);
    });

    it('compares item selections by kind and index', () => {
      const a = { systemPath: [], item: { kind: 'parameter' as const, index: 0 } };
      expect(selectionsEqual(a, { systemPath: [], item: { kind: 'parameter', index: 0 } })).toBe(true);
      expect(selectionsEqual(a, { systemPath: [], item: { kind: 'parameter', index: 1 } })).toBe(false);
      expect(selectionsEqual(a, { systemPath: [], item: { kind: 'container', index: 0 } })).toBe(false);
    });
  });

  describe('getItemAtSelection', () => {
    it('returns the addressed telemetry item', () => {
      const type = getItemAtSelection(telemetryDoc(), { systemPath: [], item: { kind: 'parameterType', index: 1 } });
      expect((type as ParameterTypeDoc).name).toBe('Mode_Type');
    });

    it('returns null for a system selection or a stale index', () => {
      expect(getItemAtSelection(telemetryDoc(), { systemPath: [] })).toBeNull();
      expect(getItemAtSelection(telemetryDoc(), { systemPath: [], item: { kind: 'parameter', index: 9 } })).toBeNull();
      expect(getItemAtSelection(telemetryDoc(), { systemPath: [0], item: { kind: 'parameter', index: 0 } })).toBeNull();
    });
  });

  describe('updateItemAtSelection', () => {
    it('updates the addressed item immutably, preserving unknown properties', () => {
      const original = telemetryDoc();
      const result = updateItemAtSelection(
        original,
        { systemPath: [], item: { kind: 'parameterType', index: 0 } },
        (type) => ({ ...(type as ParameterTypeDoc), name: 'Renamed_Type' })
      );

      const updated = result.telemetryMetaData!.parameterTypeSet[0];
      expect(updated.name).toBe('Renamed_Type');
      // preserved passthrough survives on the item, the telemetry object, and the doc
      expect(updated['preserved']).toEqual([{ elementName: 'UnitSet', outerXml: '<UnitSet/>' }]);
      expect(result['preserved']).toEqual([{ elementName: 'Header', outerXml: '<Header/>' }]);
      // untouched siblings and the original stay intact
      expect(result.telemetryMetaData!.parameterTypeSet[1].name).toBe('Mode_Type');
      expect(original.telemetryMetaData!.parameterTypeSet[0].name).toBe('Volt_Type');
    });
  });

  describe('addItemToSystem', () => {
    it('appends to the right list', () => {
      const result = addItemToSystem(telemetryDoc(), [], 'parameter', { name: 'NewParam', parameterTypeRef: 'Volt_Type' });
      expect(result.telemetryMetaData!.parameterSet.map((p) => p.name)).toEqual(['BusVoltage', 'NewParam']);
    });

    it('creates telemetryMetaData when the system has none', () => {
      const result = addItemToSystem(telemetryDoc(), [0], 'parameterType', { name: 'T', kind: 'Integer' });
      expect(result.children[0].telemetryMetaData!.parameterTypeSet[0].name).toBe('T');
      expect(result.children[0].telemetryMetaData!.parameterSet).toEqual([]);
    });
  });

  describe('deleteItemAtSelection', () => {
    it('removes only the addressed item', () => {
      const result = deleteItemAtSelection(telemetryDoc(), { systemPath: [], item: { kind: 'parameterType', index: 0 } });
      expect(result.telemetryMetaData!.parameterTypeSet.map((t) => t.name)).toEqual(['Mode_Type']);
      expect(result.telemetryMetaData!.parameterSet.length).toBe(1);
    });
  });

  describe('collectParameterTypeNames', () => {
    it('collects, dedupes, and sorts names across the whole tree', () => {
      const doc = telemetryDoc();
      doc.children[0].telemetryMetaData = {
        parameterTypeSet: [{ name: 'Volt_Type', kind: 'Float' }, { name: 'Amp_Type', kind: 'Float' }],
        parameterSet: [],
      };

      expect(collectParameterTypeNames(doc)).toEqual(['Amp_Type', 'Mode_Type', 'Volt_Type']);
    });
  });
});
