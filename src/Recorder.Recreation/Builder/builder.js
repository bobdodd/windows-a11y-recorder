// Builds the recorded DOM tree of one document with DOM calls, in place of
// the served document's element, then sets the recorded text control values,
// selection, focus, and scroll offsets. It is the only script the page's
// content security policy allows, by a nonce new for each recreation, and it
// runs once, after the served markup is parsed. See
// docs/architecture/page-recreation.md, "Slice 3b design".
(() => {
  "use strict";
  const HTML = "http://www.w3.org/1999/xhtml";
  const SVG = "http://www.w3.org/2000/svg";
  const MATHML = "http://www.w3.org/1998/Math/MathML";
  const XMLNS = "http://www.w3.org/2000/xmlns/";
  // The recording gives an attribute's namespace and local name, not its
  // prefix. The prefixes are those the HTML parser gives foreign attributes:
  // https://html.spec.whatwg.org/multipage/parsing.html#adjust-foreign-attributes
  const XLINK = "http://www.w3.org/1999/xlink";
  const XML = "http://www.w3.org/XML/1998/namespace";

  const block = document.getElementById("recorder-recreation-tree");
  const data = JSON.parse(block.textContent);
  const notes = [];
  const nodes = new Map();
  const note = (id, property, reason) => notes.push({ nodeId: id, property, reason });

  // An element's name is in capitals only for an element in the HTML
  // namespace in an HTML document (DOM standard, "HTML-uppercased qualified
  // name"). Other elements take the namespace of an svg or math ancestor.
  const namespaceOf = (name, parentNamespace) => {
    if (name !== name.toLowerCase() && name === name.toUpperCase()) {
      return HTML;
    }
    if (name === "svg") {
      return SVG;
    }
    if (name === "math") {
      return MATHML;
    }
    if (parentNamespace === SVG || parentNamespace === MATHML) {
      return parentNamespace;
    }
    return null;
  };

  const setAttribute = (element, node, attribute) => {
    const [namespace, name, value] = attribute;
    if (value === null) {
      note(node.id, `attribute ${name}`, "cut in the recording");
      return;
    }
    try {
      if (namespace === null) {
        element.setAttribute(name, value);
      } else {
        let qualified = name;
        if (namespace === XMLNS && name !== "xmlns" && !name.includes(":")) {
          qualified = "xmlns:" + name;
        } else if (namespace === XML && !name.includes(":")) {
          qualified = "xml:" + name;
        } else if (namespace === XLINK && !name.includes(":")) {
          qualified = "xlink:" + name;
        }
        element.setAttributeNS(namespace, qualified, value);
      }
    } catch (error) {
      note(node.id, `attribute ${name}`, `not set: ${error.message}`);
    }
  };

  const create = (node, parentNamespace) => {
    switch (node.type) {
      case "element": {
        const namespace = namespaceOf(node.name, parentNamespace);
        const local = namespace === HTML ? node.name.toLowerCase() : node.name;
        let element;
        try {
          element = document.createElementNS(namespace, local);
        } catch (error) {
          note(node.id, "element", `not created: ${error.message}`);
          return null;
        }
        nodes.set(node.id, element);
        for (const attribute of node.attributes) {
          setAttribute(element, node, attribute);
        }
        if (node.shadowRoot) {
          attachShadow(element, node.shadowRoot, namespace);
        }
        append(element, node.children, namespace);
        return element;
      }
      case "text":
      case "comment":
      case "cdata-section":
      case "processing-instruction": {
        if (node.data === null) {
          note(node.id, "data", "cut in the recording");
        }
        const text = node.data ?? "";
        const made = node.type === "comment" ? document.createComment(text) : document.createTextNode(text);
        if (node.type !== "text" && node.type !== "comment") {
          note(node.id, "node type", `${node.type} built as text`);
        }
        nodes.set(node.id, made);
        return made;
      }
      default:
        note(node.id, "node type", `${node.type} not built`);
        return null;
    }
  };

  const append = (parent, children, namespace) => {
    for (const child of children) {
      const made = create(child, namespace);
      if (made) {
        parent.appendChild(made);
      }
    }
  };

  const attachShadow = (host, root, namespace) => {
    const options = {
      mode: root.mode,
      delegatesFocus: root.delegatesFocus,
      slotAssignment: root.slotAssignment,
      clonable: root.clonable,
      serializable: root.serializable,
    };
    if (root.referenceTarget !== null) {
      options.referenceTarget = root.referenceTarget;
    }
    let shadow;
    try {
      shadow = host.attachShadow(options);
    } catch (error) {
      note(root.id, "shadow root", `not attached: ${error.message}`);
      return;
    }
    nodes.set(root.id, shadow);
    append(shadow, root.children, namespace);
  };

  // The recorded document's element replaces the served one; the
  // document's other recorded children, such as comments, are placed around
  // it in their recorded order.
  const served = document.documentElement;
  let placedElement = false;
  for (const child of data.document.children) {
    if (child.type === "document-type") {
      continue;
    }
    const made = create(child, null);
    if (!made) {
      continue;
    }
    if (child.type === "element" && !placedElement) {
      document.replaceChild(made, served);
      placedElement = true;
    } else if (!placedElement) {
      document.insertBefore(made, served);
    } else {
      document.appendChild(made);
    }
  }
  if (!placedElement) {
    served.remove();
  }

  for (const [slotId, assigned] of data.manualSlots) {
    const slot = nodes.get(slotId);
    const targets = assigned.map((id) => nodes.get(id)).filter((node) => node);
    try {
      slot.assign(...targets);
    } catch (error) {
      note(slotId, "slot assignment", `not set: ${error.message}`);
    }
  }

  for (const control of data.textControls) {
    const element = nodes.get(control.nodeId);
    if (!element) {
      continue;
    }
    if (control.value === null) {
      note(control.nodeId, "value", "cut in the recording");
      continue;
    }
    element.value = control.value;
    if (control.selectionStart !== null && control.selectionEnd !== null) {
      try {
        element.setSelectionRange(control.selectionStart, control.selectionEnd, control.selectionDirection ?? "none");
      } catch (error) {
        note(control.nodeId, "selection", `not set: ${error.message}`);
      }
    }
  }

  for (const scroll of data.scrollOffsets) {
    const target = scroll.nodeId === data.document.id ? null : nodes.get(scroll.nodeId);
    if (scroll.nodeId === data.document.id) {
      window.scrollTo(scroll.x, scroll.y);
    } else if (target && target.scrollTo) {
      target.scrollTo(scroll.x, scroll.y);
    }
  }

  if (data.selection) {
    const anchor = nodes.get(data.selection.anchorNodeId);
    const focus = nodes.get(data.selection.focusNodeId);
    if (anchor && focus) {
      try {
        document.getSelection().setBaseAndExtent(anchor, data.selection.anchorOffset, focus, data.selection.focusOffset);
      } catch (error) {
        note(data.selection.anchorNodeId, "selection", `not set: ${error.message}`);
      }
    }
  }

  if (data.focusedNodeId !== null) {
    const focused = nodes.get(data.focusedNodeId);
    if (focused && focused.focus) {
      focused.focus({ preventScroll: true });
    } else {
      note(data.focusedNodeId, "focus", "the focused node was not built");
    }
  }

  // Read by the recorder over its protocol connection, and by the panel.
  Object.defineProperty(window, "__recorderRecreation", {
    value: Object.freeze({ built: true, notes: Object.freeze(notes) }),
  });
})();
