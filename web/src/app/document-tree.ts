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
  raw?: { elementName: string; outerXml: string; anchor?: string | null } | null;
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

export interface UnitDoc {
  value: string;
  description?: string | null;
  power?: string | null;
  factor?: string | null;
  form?: string | null;
  [key: string]: unknown;
}

export interface TimeEncodingDoc {
  units?: string | null;
  scale?: string | null;
  offset?: string | null;
  dataEncoding?: DataEncodingDoc | null;
  [key: string]: unknown;
}

export interface ParameterPropertiesDoc {
  dataSource?: string | null;
  readOnly?: boolean | null;
  persistence?: boolean | null;
  [key: string]: unknown;
}

export interface PolynomialTermDoc {
  coefficient: string;
  exponent: string;
  [key: string]: unknown;
}

export interface SplinePointDoc {
  raw: string;
  calibrated: string;
  order?: string | null;
  [key: string]: unknown;
}

export interface CalibratorDoc {
  kind: 'Polynomial' | 'Spline';
  terms?: PolynomialTermDoc[] | null;
  points?: SplinePointDoc[] | null;
  splineOrder?: number | null;
  extrapolate?: boolean | null;
  [key: string]: unknown;
}

export interface DataEncodingDoc {
  kind: 'Integer' | 'Float' | 'String' | 'Binary';
  encoding?: string | null;
  sizeInBits?: number | null;
  changeThreshold?: string | null;
  bitOrder?: string | null;
  byteOrder?: string | null;
  defaultCalibrator?: CalibratorDoc | null;
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
  dataEncoding?: DataEncodingDoc | null;
  timeEncoding?: TimeEncodingDoc | null;
  unitSet?: UnitDoc[] | null;
  [key: string]: unknown;
}

export interface ParameterDoc {
  name: string;
  parameterTypeRef: string;
  initialValue?: string | null;
  properties?: ParameterPropertiesDoc | null;
  [key: string]: unknown;
}

export interface SequenceEntryDoc {
  kind: 'ParameterRef' | 'ContainerRef' | 'Raw' | 'ArgumentRef' | 'FixedValue';
  ref?: string | null;
  rawXml?: { elementName: string; outerXml: string; anchor?: string | null } | null;
  binaryValue?: string | null;
  sizeInBits?: number | null;
  name?: string | null;
  [key: string]: unknown;
}

export interface ComparisonDoc {
  parameterRef: string;
  value: string;
  comparisonOperator?: string | null;
  [key: string]: unknown;
}

export interface RestrictionCriteriaDoc {
  comparison?: ComparisonDoc | null;
  comparisonList?: ComparisonDoc[] | null;
  nextContainerRef?: string | null;
  raw?: { elementName: string; outerXml: string; anchor?: string | null } | null;
  [key: string]: unknown;
}

export interface BaseContainerDoc {
  containerRef: string;
  restrictionCriteria?: RestrictionCriteriaDoc | null;
  [key: string]: unknown;
}

export interface SequenceContainerDoc {
  name: string;
  entryList: SequenceEntryDoc[];
  abstract?: boolean | null;
  baseContainer?: BaseContainerDoc | null;
  [key: string]: unknown;
}

export interface MessageDoc {
  name: string;
  containerRef: string;
  [key: string]: unknown;
}

export interface MessageSetDoc {
  messages: MessageDoc[];
  [key: string]: unknown;
}

export interface AlgorithmParameterRefDoc {
  parameterRef: string;
  name?: string | null;
  [key: string]: unknown;
}

export interface AlgorithmDoc {
  name: string;
  kind: 'Custom' | 'Math';
  algorithmText?: string | null;
  language?: string | null;
  inputs?: AlgorithmParameterRefDoc[] | null;
  outputs?: AlgorithmParameterRefDoc[] | null;
  thread?: boolean | null;
  triggerContainer?: string | null;
  priority?: number | null;
  [key: string]: unknown;
}

export interface TelemetryMetaDataDoc {
  parameterTypeSet: ParameterTypeDoc[];
  parameterSet: ParameterDoc[];
  containerSet?: SequenceContainerDoc[] | null;
  messageSet?: MessageSetDoc | null;
  algorithmSet?: AlgorithmDoc[] | null;
  [key: string]: unknown;
}

export interface ArgumentDoc {
  name: string;
  argumentTypeRef: string;
  initialValue?: string | null;
  [key: string]: unknown;
}

export interface ArgumentAssignmentDoc {
  argumentName: string;
  argumentValue: string;
  [key: string]: unknown;
}

export interface CommandContainerDoc {
  name: string;
  baseContainerRef?: string | null;
  entryList?: SequenceEntryDoc[] | null;
  [key: string]: unknown;
}

export interface MetaCommandDoc {
  name: string;
  abstract?: boolean | null;
  baseMetaCommandRef?: string | null;
  arguments?: ArgumentDoc[] | null;
  argumentAssignments?: ArgumentAssignmentDoc[] | null;
  commandContainer?: CommandContainerDoc | null;
  executionVerifiers?: unknown[] | null;
  completeVerifiers?: unknown[] | null;
  [key: string]: unknown;
}

export interface CommandMetaDataDoc {
  metaCommands: MetaCommandDoc[];
  argumentTypeSet?: ParameterTypeDoc[] | null;
  parameterTypeSet?: ParameterTypeDoc[] | null;
  parameterSet?: ParameterDoc[] | null;
  algorithmSet?: AlgorithmDoc[] | null;
  [key: string]: unknown;
}

export interface SpaceSystemDocument {
  name: string;
  children: SpaceSystemDocument[];
  telemetryMetaData?: TelemetryMetaDataDoc | null;
  commandMetaData?: CommandMetaDataDoc | null;
  [key: string]: unknown;
}

/** A path is a list of child indices from the root; [] is the root itself. */
export type NodePath = number[];

export type ItemKind =
  | 'parameterType' | 'parameter' | 'container' | 'message' | 'metaCommand'
  | 'argumentType' | 'commandParameterType' | 'commandParameter'
  | 'algorithm' | 'commandAlgorithm';

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

export type TelemetryItem = ParameterTypeDoc | ParameterDoc | SequenceContainerDoc | MessageDoc | MetaCommandDoc;

function itemsOf(system: SpaceSystemDocument, kind: ItemKind): readonly TelemetryItem[] {
  switch (kind) {
    case 'parameterType':
      return system.telemetryMetaData?.parameterTypeSet ?? [];
    case 'parameter':
      return system.telemetryMetaData?.parameterSet ?? [];
    case 'container':
      return system.telemetryMetaData?.containerSet ?? [];
    case 'message':
      return system.telemetryMetaData?.messageSet?.messages ?? [];
    case 'metaCommand':
      return system.commandMetaData?.metaCommands ?? [];
    case 'argumentType':
      return system.commandMetaData?.argumentTypeSet ?? [];
    case 'commandParameterType':
      return system.commandMetaData?.parameterTypeSet ?? [];
    case 'commandParameter':
      return system.commandMetaData?.parameterSet ?? [];
    case 'algorithm':
      return system.telemetryMetaData?.algorithmSet ?? [];
    case 'commandAlgorithm':
      return system.commandMetaData?.algorithmSet ?? [];
  }
}

/** The telemetry item a selection points at, or null for system selections / stale paths. */
export function getItemAtSelection(doc: SpaceSystemDocument, selection: Selection): TelemetryItem | null {
  if (!selection.item) {
    return null;
  }
  const system = getNodeAtPath(doc, selection.systemPath);
  if (!system) {
    return null;
  }
  return itemsOf(system, selection.item.kind)[selection.item.index] ?? null;
}

function withUpdatedList(
  system: SpaceSystemDocument,
  kind: ItemKind,
  update: (list: readonly TelemetryItem[]) => unknown[]
): SpaceSystemDocument {
  const telemetry: TelemetryMetaDataDoc = system.telemetryMetaData ?? { parameterTypeSet: [], parameterSet: [] };
  switch (kind) {
    case 'parameterType':
      return { ...system, telemetryMetaData: { ...telemetry, parameterTypeSet: update(telemetry.parameterTypeSet) as ParameterTypeDoc[] } };
    case 'parameter':
      return { ...system, telemetryMetaData: { ...telemetry, parameterSet: update(telemetry.parameterSet) as ParameterDoc[] } };
    case 'container':
      return { ...system, telemetryMetaData: { ...telemetry, containerSet: update(telemetry.containerSet ?? []) as SequenceContainerDoc[] } };
    case 'message': {
      const messageSet: MessageSetDoc = telemetry.messageSet ?? { messages: [] };
      return { ...system, telemetryMetaData: { ...telemetry, messageSet: { ...messageSet, messages: update(messageSet.messages) as MessageDoc[] } } };
    }
    case 'metaCommand': {
      const commandMetaData: CommandMetaDataDoc = system.commandMetaData ?? { metaCommands: [] };
      return { ...system, commandMetaData: { ...commandMetaData, metaCommands: update(commandMetaData.metaCommands) as MetaCommandDoc[] } };
    }
    case 'argumentType': {
      const commandMetaData: CommandMetaDataDoc = system.commandMetaData ?? { metaCommands: [] };
      return { ...system, commandMetaData: { ...commandMetaData, argumentTypeSet: update(commandMetaData.argumentTypeSet ?? []) as ParameterTypeDoc[] } };
    }
    case 'commandParameterType': {
      const commandMetaData: CommandMetaDataDoc = system.commandMetaData ?? { metaCommands: [] };
      return { ...system, commandMetaData: { ...commandMetaData, parameterTypeSet: update(commandMetaData.parameterTypeSet ?? []) as ParameterTypeDoc[] } };
    }
    case 'commandParameter': {
      const commandMetaData: CommandMetaDataDoc = system.commandMetaData ?? { metaCommands: [] };
      return { ...system, commandMetaData: { ...commandMetaData, parameterSet: update(commandMetaData.parameterSet ?? []) as ParameterDoc[] } };
    }
    case 'algorithm':
      return { ...system, telemetryMetaData: { ...telemetry, algorithmSet: update(telemetry.algorithmSet ?? []) as AlgorithmDoc[] } };
    case 'commandAlgorithm': {
      const commandMetaData: CommandMetaDataDoc = system.commandMetaData ?? { metaCommands: [] };
      return { ...system, commandMetaData: { ...commandMetaData, algorithmSet: update(commandMetaData.algorithmSet ?? []) as AlgorithmDoc[] } };
    }
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
  return updateNodeAtPath(doc, selection.systemPath, (system) =>
    withUpdatedList(system, item.kind, (list) =>
      list.map((entry, i) => (i === item.index ? (updater as (x: unknown) => unknown)(entry) : entry))));
}

/** Adds a telemetry item to the system at `systemPath`, creating telemetryMetaData if needed. */
export function addItemToSystem(
  doc: SpaceSystemDocument,
  systemPath: NodePath,
  kind: ItemKind,
  item: TelemetryItem
): SpaceSystemDocument {
  return updateNodeAtPath(doc, systemPath, (system) => withUpdatedList(system, kind, (list) => [...list, item]));
}

/** Removes the selected telemetry item. */
export function deleteItemAtSelection(doc: SpaceSystemDocument, selection: Selection): SpaceSystemDocument {
  const item = selection.item;
  if (!item) {
    return doc;
  }
  return updateNodeAtPath(doc, selection.systemPath, (system) =>
    withUpdatedList(system, item.kind, (list) => list.filter((_, i) => i !== item.index)));
}

/** Every parameter-type name in the document (telemetry and command sides share the namespace). */
export function collectParameterTypeNames(doc: SpaceSystemDocument): string[] {
  return collectNames(doc, (t) => [
    ...(t.telemetryMetaData?.parameterTypeSet ?? []),
    ...(t.commandMetaData?.parameterTypeSet ?? []),
  ]);
}

/** Every parameter name in the document (telemetry and command sides share the namespace). */
export function collectParameterNames(doc: SpaceSystemDocument): string[] {
  return collectNames(doc, (t) => [
    ...(t.telemetryMetaData?.parameterSet ?? []),
    ...(t.commandMetaData?.parameterSet ?? []),
  ]);
}

/** Every container name in the document — datalist fodder for containerRef inputs. */
export function collectContainerNames(doc: SpaceSystemDocument): string[] {
  return collectNames(doc, (t) => t.telemetryMetaData?.containerSet);
}

/** Every MetaCommand name in the document — datalist fodder for metaCommandRef inputs. */
export function collectMetaCommandNames(doc: SpaceSystemDocument): string[] {
  return collectNames(doc, (t) => t.commandMetaData?.metaCommands);
}

/** Every argument-type name in the document — datalist fodder for argumentTypeRef inputs. */
export function collectArgumentTypeNames(doc: SpaceSystemDocument): string[] {
  return collectNames(doc, (t) => t.commandMetaData?.argumentTypeSet);
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

/**
 * Maps a validator location ("Sat/Bus/ParameterSet/Volt") onto a tree Selection, or null
 * when nothing in the modeled tree corresponds to it (e.g. content inside a quarantined
 * fragment). Segments after the item name (like "/CommandContainer") still select the
 * owning item.
 */
export function selectionForLocation(doc: SpaceSystemDocument, location: string): Selection | null {
  const segments = location.split('/');
  if (segments[0] !== doc.name) {
    return null;
  }
  let node = doc;
  const systemPath: number[] = [];
  let i = 1;
  while (i < segments.length) {
    const segment = segments[i];
    const itemLists: Record<string, { kind: ItemKind; items: { name: string }[] }> = {
      ParameterTypeSet: { kind: 'parameterType', items: node.telemetryMetaData?.parameterTypeSet ?? [] },
      ParameterSet: { kind: 'parameter', items: node.telemetryMetaData?.parameterSet ?? [] },
      ContainerSet: { kind: 'container', items: node.telemetryMetaData?.containerSet ?? [] },
      MessageSet: { kind: 'message', items: node.telemetryMetaData?.messageSet?.messages ?? [] },
      AlgorithmSet: { kind: 'algorithm', items: node.telemetryMetaData?.algorithmSet ?? [] },
    };
    let kind: ItemKind | null = null;
    let items: { name: string }[] = [];
    let nameIndex = -1;
    if (itemLists[segment]) {
      kind = itemLists[segment].kind;
      items = itemLists[segment].items;
      nameIndex = i + 1;
    } else if (segment === 'CommandMetaData' && segments[i + 1] === 'MetaCommandSet') {
      kind = 'metaCommand';
      items = node.commandMetaData?.metaCommands ?? [];
      nameIndex = i + 2;
    } else if (segment === 'CommandMetaData' && segments[i + 1] === 'ArgumentTypeSet') {
      kind = 'argumentType';
      items = node.commandMetaData?.argumentTypeSet ?? [];
      nameIndex = i + 2;
    } else if (segment === 'CommandMetaData' && segments[i + 1] === 'ParameterTypeSet') {
      kind = 'commandParameterType';
      items = node.commandMetaData?.parameterTypeSet ?? [];
      nameIndex = i + 2;
    } else if (segment === 'CommandMetaData' && segments[i + 1] === 'ParameterSet') {
      kind = 'commandParameter';
      items = node.commandMetaData?.parameterSet ?? [];
      nameIndex = i + 2;
    } else if (segment === 'CommandMetaData' && segments[i + 1] === 'AlgorithmSet') {
      kind = 'commandAlgorithm';
      items = node.commandMetaData?.algorithmSet ?? [];
      nameIndex = i + 2;
    }
    if (kind !== null) {
      const name = segments[nameIndex];
      if (name === undefined) {
        return { systemPath };
      }
      const index = items.findIndex((item) => item.name === name);
      return index >= 0 ? { systemPath, item: { kind, index } } : null;
    }
    const childIndex = node.children.findIndex((child) => child.name === segment);
    if (childIndex < 0) {
      return null;
    }
    systemPath.push(childIndex);
    node = node.children[childIndex];
    i++;
  }
  return { systemPath };
}
