import { getNodeAtPath, updateNodeAtPath, deleteNodeAtPath, pathsEqual, SpaceSystemDocument } from './document-tree';

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
});
