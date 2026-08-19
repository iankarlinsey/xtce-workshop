export interface SpaceSystemDocument {
  name: string;
  children: SpaceSystemDocument[];
}

/** A path is a list of child indices from the root; [] is the root itself. */
export type NodePath = number[];

export function pathsEqual(a: NodePath | null, b: NodePath | null): boolean {
  if (a === null || b === null) {
    return a === b;
  }
  return a.length === b.length && a.every((v, i) => v === b[i]);
}

export function getNodeAtPath(doc: SpaceSystemDocument, path: NodePath): SpaceSystemDocument | null {
  let node = doc;
  for (const index of path) {
    const child = node.children[index];
    if (!child) {
      return null;
    }
    node = child;
  }
  return node;
}

/** Returns a new document with the node at `path` replaced by `updater(node)`. */
export function updateNodeAtPath(
  doc: SpaceSystemDocument,
  path: NodePath,
  updater: (node: SpaceSystemDocument) => SpaceSystemDocument
): SpaceSystemDocument {
  if (path.length === 0) {
    return updater(doc);
  }

  const [head, ...rest] = path;
  const children = [...doc.children];
  children[head] = updateNodeAtPath(doc.children[head], rest, updater);
  return { ...doc, children };
}

/** Removes the node at `path` from its parent. `path` must be non-empty — the root can't be deleted. */
export function deleteNodeAtPath(doc: SpaceSystemDocument, path: NodePath): SpaceSystemDocument {
  const parentPath = path.slice(0, -1);
  const index = path[path.length - 1];
  return updateNodeAtPath(doc, parentPath, (parent) => ({
    ...parent,
    children: parent.children.filter((_, i) => i !== index),
  }));
}
