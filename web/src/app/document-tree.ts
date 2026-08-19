/**
 * Domain-document interfaces mirror the backend's JSON (camelCase, enum names as strings).
 * Every interface carries an index signature so properties the UI doesn't model — the
 * preserved raw-XML fragments and attributes that make round-trips lossless (backend issue
 * #23) — pass through object spreads untouched. Any helper here that rebuilds a node MUST
 * spread the original first; rebuilding field-by-field would silently break losslessness.
 */

export interface EnumerationEntryDoc {
  value: number;
  label: string;
  maxValue?: number | null;
  shortDescription?: string | null;
  [key: string]: unknown;
}

export type ParameterTypeKind =
  | 'Integer' | 'Float' | 'String' | 'Boolean' | 'Enumerated'
  | 'Binary' | 'RelativeTime' | 'AbsoluteTime' | 'Array' | 'Aggregate';

export interface DimensionIndexDoc {
  fixedValue?: number | null;
  raw?: { elementName: string; outerXml: string } | null;
  [key: string]: unknown;
}

export interface DimensionDoc {
  startingIndex: DimensionIndexDoc;
  endingIndex: DimensionIndexDoc;
  [key: string]: unknown;
}

export interface MemberDoc {
  name: string;
  typeRef: string;
  initialValue?: string | null;
  [key: string]: unknown;
}

export interface ParameterTypeDoc {
  name: string;
  kind: ParameterTypeKind;
  initialValue?: string | null;
  signed?: boolean | null;
  sizeInBits?: number | null;
  oneStringValue?: string | null;
  zeroStringValue?: string | null;
  enumerations?: EnumerationEntryDoc[] | null;
  arrayTypeRef?: string | null;
  dimensions?: DimensionDoc[] | null;
  members?: MemberDoc[] | null;
  [key: string]: unknown;
}

export interface ParameterDoc {
  name: string;
  parameterTypeRef: string;
  initialValue?: string | null;
  [key: string]: unknown;
}

export interface SequenceEntryDoc {
  kind: 'ParameterRef' | 'ContainerRef' | 'Raw';
  ref?: string | null;
  rawXml?: { elementName: string; outerXml: string } | null;
  [key: string]: unknown;
}

export interface BaseContainerDoc {
  containerRef: string;
  restrictionCriteria?: unknown | null;
  [key: string]: unknown;
}

export interface SequenceContainerDoc {
  name: string;
  entryList: SequenceEntryDoc[];
  abstract?: boolean | null;
  baseContainer?: BaseContainerDoc | null;
  [key: string]: unknown;
}

export interface TelemetryMetaDataDoc {
  parameterTypeSet: ParameterTypeDoc[];
  parameterSet: ParameterDoc[];
  containerSet?: SequenceContainerDoc[] | null;
  [key: string]: unknown;
}

export interface SpaceSystemDocument {
  name: string;
  children: SpaceSystemDocument[];
  telemetryMetaData?: TelemetryMetaDataDoc | null;
  [key: string]: unknown;
}

/** A path is a list of child indices from the root; [] is the root itself. */
export type NodePath = number[];

export type ItemKind = 'parameterType' | 'parameter' | 'container';

/**
 * What the user has selected: a SpaceSystem (item undefined), or one telemetry item
 * (parameter type / parameter / container) inside the SpaceSystem at systemPath.
 */
export interface Selection {
  systemPath: NodePath;
  item?: { kind: ItemKind; index: number } | null;
}

export function pathsEqual(a: NodePath | null, b: NodePath | null): boolean {
  if (a === null || b === null) {
    return a === b;
  }
  return a.length === b.length && a.every((v, i) => v === b[i]);
}

export function selectionsEqual(a: Selection | null, b: Selection | null): boolean {
  if (a === null || b === null) {
    return a === b;
  }
  if (!pathsEqual(a.systemPath, b.systemPath)) {
    return false;
  }
  const aItem = a.item ?? null;
  const bItem = b.item ?? null;
  if (aItem === null || bItem === null) {
    return aItem === bItem;
  }
  return aItem.kind === bItem.kind && aItem.index === bItem.index;
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

function itemsOf(telemetry: TelemetryMetaDataDoc, kind: ItemKind): readonly (ParameterTypeDoc | ParameterDoc | SequenceContainerDoc)[] {
  switch (kind) {
    case 'parameterType':
      return telemetry.parameterTypeSet;
    case 'parameter':
      return telemetry.parameterSet;
    case 'container':
      return telemetry.containerSet ?? [];
  }
}

/** The telemetry item a selection points at, or null for system selections / stale paths. */
export function getItemAtSelection(
  doc: SpaceSystemDocument,
  selection: Selection
): ParameterTypeDoc | ParameterDoc | SequenceContainerDoc | null {
  if (!selection.item) {
    return null;
  }
  const system = getNodeAtPath(doc, selection.systemPath);
  if (!system?.telemetryMetaData) {
    return null;
  }
  return itemsOf(system.telemetryMetaData, selection.item.kind)[selection.item.index] ?? null;
}

function withUpdatedList(
  telemetry: TelemetryMetaDataDoc,
  kind: ItemKind,
  update: (list: readonly (ParameterTypeDoc | ParameterDoc | SequenceContainerDoc)[]) => unknown[]
): TelemetryMetaDataDoc {
  switch (kind) {
    case 'parameterType':
      return { ...telemetry, parameterTypeSet: update(telemetry.parameterTypeSet) as ParameterTypeDoc[] };
    case 'parameter':
      return { ...telemetry, parameterSet: update(telemetry.parameterSet) as ParameterDoc[] };
    case 'container':
      return { ...telemetry, containerSet: update(telemetry.containerSet ?? []) as SequenceContainerDoc[] };
  }
}

/** Returns a new document with the selected item replaced by `updater(item)`. */
export function updateItemAtSelection(
  doc: SpaceSystemDocument,
  selection: Selection,
  updater: (item: never) => unknown
): SpaceSystemDocument {
  const item = selection.item;
  if (!item) {
    return doc;
  }
  return updateNodeAtPath(doc, selection.systemPath, (system) => {
    if (!system.telemetryMetaData) {
      return system;
    }
    return {
      ...system,
      telemetryMetaData: withUpdatedList(system.telemetryMetaData, item.kind, (list) =>
        list.map((entry, i) => (i === item.index ? (updater as (x: unknown) => unknown)(entry) : entry))
      ),
    };
  });
}

/** Adds a telemetry item to the system at `systemPath`, creating telemetryMetaData if needed. */
export function addItemToSystem(
  doc: SpaceSystemDocument,
  systemPath: NodePath,
  kind: ItemKind,
  item: ParameterTypeDoc | ParameterDoc | SequenceContainerDoc
): SpaceSystemDocument {
  return updateNodeAtPath(doc, systemPath, (system) => {
    const telemetry: TelemetryMetaDataDoc = system.telemetryMetaData ?? { parameterTypeSet: [], parameterSet: [] };
    return {
      ...system,
      telemetryMetaData: withUpdatedList(telemetry, kind, (list) => [...list, item]),
    };
  });
}

/** Removes the selected telemetry item. */
export function deleteItemAtSelection(doc: SpaceSystemDocument, selection: Selection): SpaceSystemDocument {
  const item = selection.item;
  if (!item) {
    return doc;
  }
  return updateNodeAtPath(doc, selection.systemPath, (system) => {
    if (!system.telemetryMetaData) {
      return system;
    }
    return {
      ...system,
      telemetryMetaData: withUpdatedList(system.telemetryMetaData, item.kind, (list) =>
        list.filter((_, i) => i !== item.index)
      ),
    };
  });
}

/** Every parameter-type name in the document — datalist fodder for parameterTypeRef inputs. */
export function collectParameterTypeNames(doc: SpaceSystemDocument): string[] {
  return collectNames(doc, (t) => t.telemetryMetaData?.parameterTypeSet);
}

/** Every parameter name in the document — datalist fodder for parameterRef inputs. */
export function collectParameterNames(doc: SpaceSystemDocument): string[] {
  return collectNames(doc, (t) => t.telemetryMetaData?.parameterSet);
}

/** Every container name in the document — datalist fodder for containerRef inputs. */
export function collectContainerNames(doc: SpaceSystemDocument): string[] {
  return collectNames(doc, (t) => t.telemetryMetaData?.containerSet);
}

function collectNames(
  doc: SpaceSystemDocument,
  select: (node: SpaceSystemDocument) => { name: string }[] | null | undefined
): string[] {
  const names: string[] = [];
  const walk = (node: SpaceSystemDocument) => {
    for (const item of select(node) ?? []) {
      names.push(item.name);
    }
    node.children.forEach(walk);
  };
  walk(doc);
  return [...new Set(names)].sort();
}

/** Moves the entry at `index` by `delta` positions within a container's entry list. */
export function moveEntry(container: SequenceContainerDoc, index: number, delta: number): SequenceContainerDoc {
  const target = index + delta;
  if (target < 0 || target >= container.entryList.length) {
    return container;
  }
  const entryList = [...container.entryList];
  const [entry] = entryList.splice(index, 1);
  entryList.splice(target, 0, entry);
  return { ...container, entryList };
}
