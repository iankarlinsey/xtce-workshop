import { Component, computed, input } from '@angular/core';

interface RawFragment {
  elementName: string;
  outerXml: string;
  /** Comment fragments ("#comment") record where they sat; see backend issue #51. */
  anchor?: string | null;
}

interface RawAttribute {
  name: string;
  value: string;
}

/**
 * Read-only transparency panel for the lossless-round-trip machinery: shows the raw-XML
 * fragments and attributes the editor is faithfully carrying for an item without modeling
 * them (backend issue #23). Deliberately NOT an editing surface — the whole point of the
 * project is that users don't write raw XML; this just proves nothing is being lost.
 */
@Component({
  selector: 'app-preserved-xml',
  templateUrl: './preserved-xml.html',
  styleUrl: './preserved-xml.css',
})
export class PreservedXmlComponent {
  /** The item object (any modeled construct) whose preserved payload to display. */
  readonly item = input.required<Record<string, unknown>>();

  protected readonly fragments = computed<RawFragment[]>(() => {
    const value = this.item()['preserved'];
    return Array.isArray(value) ? (value as RawFragment[]) : [];
  });

  protected readonly attributes = computed<RawAttribute[]>(() => {
    const value = this.item()['preservedAttributes'];
    return Array.isArray(value) ? (value as RawAttribute[]) : [];
  });

  protected readonly hasContent = computed(
    () => this.fragments().length > 0 || this.attributes().length > 0
  );
}
