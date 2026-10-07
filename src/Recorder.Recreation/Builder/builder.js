// Builds the recorded DOM tree of one document with DOM calls, in place of
// the served document's element, then sets the recorded text control values,
// selection, focus, and scroll offsets. It is the only script the page's
// content security policy allows, by a nonce new for each recreation, and it
// runs once, after the served markup is parsed. It is written into the
// served page (slice 4a), so that nothing is fetched to build the page when
// it is served at its recorded address, and it waits for the markup to be
// parsed, as a deferred script would. See
// docs/architecture/page-recreation.md, "Slice 3b design".
document.addEventListener("DOMContentLoaded", async () => {
  "use strict";
  // Stage 3: the times, from the page's time origin, at which the builder
  // started and finished each part of its work, read by the evidence panel.
  const times = { builderStarted: performance.now() };
  // Slice 5c: when each frame built in place, at any depth, was started and
  // built, on this document's clock, for the build report.
  const inPlaceTimes = [];
  const block = document.getElementById("recorder-recreation-tree");
  const data = JSON.parse(block.textContent);
  times.treeRead = performance.now();
  const notes = [];

  // Slice 5b: builds one recorded document's tree in a document, with that
  // document's window's constructors, so that the same steps build the
  // served document and each frame document built in place, whose window is
  // another realm. The parameters are named document and window so that
  // every step uses the document being built.
  const build = async (document, window, data, notes, times) => {
    const HTML = "http://www.w3.org/1999/xhtml";
    const SVG = "http://www.w3.org/2000/svg";
    const MATHML = "http://www.w3.org/1998/Math/MathML";
    const XMLNS = "http://www.w3.org/2000/xmlns/";
    // The recording gives an attribute's namespace and local name, not its
    // prefix. The prefixes are those the HTML parser gives foreign attributes:
    // https://html.spec.whatwg.org/multipage/parsing.html#adjust-foreign-attributes
    const XLINK = "http://www.w3.org/1999/xlink";
    const XML = "http://www.w3.org/XML/1998/namespace";

    const nodes = new Map();
    const note = (id, property, reason) => notes.push({ nodeId: id, property, reason });

    // Sub-step 3: the faces the document had loaded at the frame are added to
    // its set of faces before the tree is built, each made from its recorded
    // family, descriptors, and font file, which the recorder answers at its
    // own address, so that Blink matches the recorded font-family values to
    // the same faces as when recorded. Each file is read once. A face that
    // does not load is left out, and the Console says so.
    const files = new Map();
    const fontFile = (face) => {
      if (!files.has(face.digest)) {
        files.set(face.digest, window.fetch(face.url).then((response) => {
          if (!response.ok) {
            throw new Error(`the recorder answered ${response.status}`);
          }
          return response.arrayBuffer();
        }));
      }
      return files.get(face.digest);
    };
    const faces = data.fontFaces ?? [];
    const read = await Promise.allSettled(faces.map(fontFile));
    const loading = [];
    // The faces are added in the order the document added them, since of two
    // faces with the same descriptors the later one is used.
    faces.forEach((face, index) => {
      const failed = (error) => {
        note(null, `font face ${face.family}`, `not added: ${error.message}`);
        console.warn(`Windows A11y Recorder: the recorded font face ${face.family} (font file ${face.digest}) was not added: ${error.message}`);
      };
      if (read[index].status !== "fulfilled") {
        failed(read[index].reason);
        return;
      }
      try {
        const made = new window.FontFace(face.family, read[index].value, face.descriptors);
        document.fonts.add(made);
        loading.push(made.load().catch(failed));
      } catch (error) {
        failed(error);
      }
    });
    await Promise.all(loading);
    times.fontsLoaded = performance.now();

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
          // Stage 3: the recorded values the recreation mode imposes, set
          // before the element is inserted, so that its first style and layout
          // use them.
          if (node.recordedStyle !== null && node.recordedStyle !== undefined) {
            element.setAttribute("data-a11y-recorded-style", node.recordedStyle);
          }
          if (node.recordedLayout !== null && node.recordedLayout !== undefined) {
            element.setAttribute("data-a11y-recorded-layout", node.recordedLayout);
          }
          if (node.noLayoutObject !== null && node.noLayoutObject !== undefined) {
            element.setAttribute("data-a11y-recorded-no-layout-object", node.noLayoutObject);
          }
          if (node.recordedCompositor !== null && node.recordedCompositor !== undefined) {
            element.setAttribute("data-a11y-recorded-compositor", node.recordedCompositor);
          }
          if (node.recordedPaintWorklet !== null && node.recordedPaintWorklet !== undefined) {
            element.setAttribute("data-a11y-recorded-paint-worklet", node.recordedPaintWorklet);
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
        if (served) {
          document.replaceChild(made, served);
        } else {
          document.appendChild(made);
        }
        placedElement = true;
      } else if (!placedElement && served) {
        document.insertBefore(made, served);
      } else {
        document.appendChild(made);
      }
    }
    if (!placedElement) {
      served?.remove();
    }

    // Slice 5b: a srcdoc frame starts to load its recorded markup once its
    // owner is in the document, which it now is; its load is waited for, at
    // most 10 seconds, before its recorded tree is built in it.
    const frameLoads = new Map();
    for (const frame of data.frames ?? []) {
      const owner = nodes.get(frame.ownerNodeId);
      if (frame.way === "srcdoc" && owner) {
        frameLoads.set(frame.ownerNodeId, new Promise((resolve) => {
          owner.addEventListener("load", resolve, { once: true });
          setTimeout(resolve, 10000);
        }));
      }
    }

    times.domBuilt = performance.now();

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

    // Slice 4d sub-step 2: each option's recorded selectedness at the frame,
    // which no attribute holds. Unselections are set first, so that setting a
    // selection in a single select, which unselects its other options, ends
    // in the recorded state. An option with no record keeps the selectedness
    // its attributes give.
    const selectedness = data.optionSelectedness ?? [];
    for (const pass of [false, true]) {
      for (const [optionId, selected] of selectedness) {
        if (selected !== pass) {
          continue;
        }
        const option = nodes.get(optionId);
        if (option instanceof window.HTMLOptionElement) {
          option.selected = selected;
        } else {
          note(optionId, "selectedness", "the option was not built");
        }
      }
    }

    // Slice 4e: the page's style sheets as recorded at the frame. The link
    // sheets the built DOM asks for, and the sheets they import, are answered
    // by the recorder with the text they arrived with; the builder waits for
    // them, then gives each sheet script changed its recorded CSSOM text, makes
    // each constructed sheet, and adopts the sheets of each tree scope in the
    // recorded order. A sheet that cannot be given its recorded state is noted,
    // and the Console says so.
    const sheetNote = (sheet, reason) => {
      note(null, `style sheet ${sheet}`, reason);
      console.warn(`Windows A11y Recorder: the recorded style sheet ${sheet} ${reason}.`);
    };
    const linkLoads = [];
    for (const made of nodes.values()) {
      if (made instanceof window.HTMLLinkElement && made.relList.contains("stylesheet") && !made.sheet) {
        linkLoads.push(new Promise((resolve) => {
          made.addEventListener("load", resolve, { once: true });
          made.addEventListener("error", resolve, { once: true });
        }));
      }
    }
    if (linkLoads.length > 0) {
      await Promise.race([
        Promise.all(linkLoads),
        new Promise((resolve) => setTimeout(resolve, 10000)),
      ]);
    }
    times.styleSheetsLoaded = performance.now();
    // A sheet's CSSOM text is each rule's cssText on a line of its own, and a
    // rule may span lines, so it is split into rules at the ends of top-level
    // blocks and statements, outside strings and comments.
    const splitRules = (text) => {
      const rules = [];
      let depth = 0;
      let start = 0;
      let quote = null;
      for (let i = 0; i < text.length; i++) {
        const c = text[i];
        if (quote !== null) {
          if (c === "\\") {
            i++;
          } else if (c === quote) {
            quote = null;
          }
          continue;
        }
        if (c === "\"" || c === "'") {
          quote = c;
        } else if (c === "/" && text[i + 1] === "*") {
          const end = text.indexOf("*/", i + 2);
          i = end < 0 ? text.length : end + 1;
        } else if (c === "{") {
          depth++;
        } else if (c === "}") {
          depth = Math.max(0, depth - 1);
          if (depth === 0) {
            rules.push(text.slice(start, i + 1));
            start = i + 1;
          }
        } else if (c === ";" && depth === 0) {
          rules.push(text.slice(start, i + 1));
          start = i + 1;
        }
      }
      rules.push(text.slice(start));
      return rules.map((rule) => rule.trim()).filter((rule) => rule.length > 0);
    };
    // Leading rules the loaded sheet already has as recorded, such as its
    // @import rules, are kept, so that an imported sheet already loaded is
    // not asked for again.
    const replaceRules = (sheet, recorded) => {
      const rules = splitRules(recorded.text);
      let kept = 0;
      try {
        while (kept < sheet.cssRules.length && kept < rules.length &&
               sheet.cssRules[kept] instanceof window.CSSImportRule &&
               sheet.cssRules[kept].cssText === rules[kept]) {
          kept++;
        }
        while (sheet.cssRules.length > kept) {
          sheet.deleteRule(sheet.cssRules.length - 1);
        }
      } catch (error) {
        sheetNote(recorded.sheet, `kept its loaded rules: ${error.message}`);
        return;
      }
      let refused = 0;
      let firstError = null;
      for (const rule of rules.slice(kept)) {
        try {
          sheet.insertRule(rule, sheet.cssRules.length);
        } catch (error) {
          refused++;
          firstError ??= error.message;
        }
      }
      if (refused > 0) {
        sheetNote(recorded.sheet, `lacks ${refused} recorded rules that insertRule refused, the first with: ${firstError}`);
      }
    };
    const sheetObjects = new Map();
    for (const recorded of data.styleSheets ?? []) {
      let sheet = null;
      try {
        if (recorded.kind === "constructed") {
          sheet = new window.CSSStyleSheet({ media: recorded.media, disabled: recorded.disabled });
          sheet.replaceSync(recorded.text ?? "");
          if (recorded.text === null) {
            sheetNote(recorded.sheet, "was constructed, but its text is not in the recording, so it is made empty");
          }
        } else if (recorded.kind === "import") {
          const parent = sheetObjects.get(recorded.parentSheet);
          sheet = parent?.cssRules[recorded.ruleIndex]?.styleSheet ?? null;
        } else if (recorded.ownerNodeId !== null) {
          sheet = nodes.get(recorded.ownerNodeId)?.sheet ?? null;
        }
      } catch (error) {
        sheetNote(recorded.sheet, `was not reached: ${error.message}`);
        continue;
      }
      if (!sheet) {
        sheetNote(recorded.sheet, `(${recorded.kind}) was not found in the built page`);
        continue;
      }
      sheetObjects.set(recorded.sheet, sheet);
      if (recorded.kind === "constructed") {
        continue;
      }
      if (recorded.text !== null) {
        replaceRules(sheet, recorded);
      } else if (recorded.textExpected) {
        sheetNote(recorded.sheet, "had been changed through the CSSOM, but its CSSOM text is not in the recording, so it keeps the rules it loaded");
      }
      try {
        if (sheet.media.mediaText !== recorded.media) {
          sheet.media.mediaText = recorded.media;
        }
        if (sheet.disabled !== recorded.disabled) {
          sheet.disabled = recorded.disabled;
        }
      } catch (error) {
        sheetNote(recorded.sheet, `media or disabled state not set: ${error.message}`);
      }
    }
    for (const scope of data.adoptedStyleSheets ?? []) {
      const root = scope.scopeNodeId === data.document.id ? document : nodes.get(scope.scopeNodeId);
      if (!root || !("adoptedStyleSheets" in root)) {
        if (scope.sheets.length > 0) {
          note(scope.scopeNodeId, "adopted style sheets", "the tree scope was not built");
        }
        continue;
      }
      const adopted = scope.sheets.map((number) => sheetObjects.get(number)).filter((sheet) => sheet);
      if (adopted.length !== scope.sheets.length) {
        note(scope.scopeNodeId, "adopted style sheets", `${scope.sheets.length - adopted.length} of ${scope.sheets.length} were not made`);
      }
      if (scope.sheets.length > 0 || root.adoptedStyleSheets.length > 0) {
        try {
          root.adoptedStyleSheets = adopted;
        } catch (error) {
          note(scope.scopeNodeId, "adopted style sheets", `not set: ${error.message}`);
        }
      }
    }
    times.styleSheetsApplied = performance.now();

    // The page's first style and layout, forced here so that its time is
    // measured apart from the rest; scrolling would force it in any case.
    document.documentElement?.getBoundingClientRect();
    times.styleAndLayout = performance.now();

    // Slice 5b: each frame built in place, in its own document, by these same
    // steps, before this document's scroll offsets, selection, and focus are
    // set, so that this document's recorded focus is the one that holds. Its
    // notes are kept with its owner's node ID.
    for (const frame of data.frames ?? []) {
      const owner = nodes.get(frame.ownerNodeId);
      if (!owner || !("contentDocument" in owner)) {
        note(frame.ownerNodeId, "frame", "its owner was not built as a frame owner");
        continue;
      }
      if (frame.way === "srcdoc") {
        await frameLoads.get(frame.ownerNodeId);
        if (owner.contentDocument?.URL !== "about:srcdoc") {
          note(frame.ownerNodeId, "frame", "it did not load its recorded srcdoc markup within 10 seconds, so its tree is not built");
          continue;
        }
      }
      const frameDocument = owner.contentDocument;
      const frameWindow = owner.contentWindow;
      if (!frameDocument || !frameWindow) {
        note(frame.ownerNodeId, "frame", "its document cannot be reached from its parent, so its tree is not built");
        continue;
      }
      const frameNotes = [];
      const frameTime = { ownerNodeId: frame.ownerNodeId, started: performance.now() };
      inPlaceTimes.push(frameTime);
      try {
        await build(frameDocument, frameWindow, frame.tree, frameNotes, {});
        frameTime.built = performance.now();
      } catch (error) {
        note(frame.ownerNodeId, "frame", `its tree was not built: ${error.message}`);
      }
      for (const item of frameNotes) {
        notes.push({ ...item, frameOwnerNodeId: frame.ownerNodeId });
      }
    }
    times.framesBuilt = performance.now();

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

    // Slice 4d sub-step 2: each page popup open at the frame, rebuilt by this
    // builder in an iframe the recreation adds, from the popup document's
    // recorded DOM, which it builds as it builds this one. The iframe is shown
    // in the top layer, as a manual popover, at the popup window's place in
    // this document: its window rectangle less the owner's local root origin,
    // plus the root scroll offset, worked out by the recorder. The iframe and
    // its attributes are the recreation's, not the recorded page's.
    for (const popup of data.popups ?? []) {
      const frame = document.createElement("iframe");
      frame.setAttribute("data-a11y-recorder-popup", `${popup.kind} owner ${popup.ownerNodeId}`);
      frame.setAttribute("title", popup.label);
      frame.setAttribute("popover", "manual");
      frame.setAttribute("scrolling", "no");
      frame.style.cssText = [
        "position: absolute",
        "inset: auto",
        `left: ${popup.left}px`,
        `top: ${popup.top}px`,
        `width: ${popup.width}px`,
        `height: ${popup.height}px`,
        "margin: 0",
        "padding: 0",
        "border: 0",
        "background: transparent",
        "overflow: hidden",
        "box-sizing: content-box",
      ].map((item) => `${item} !important`).join("; ");
      frame.srcdoc = popup.markup;
      (document.body ?? document.documentElement).appendChild(frame);
      try {
        frame.showPopover();
      } catch (error) {
        note(popup.ownerNodeId, "popup", `not shown in the top layer: ${error.message}`);
      }
    }

  };

  await build(document, window, data, notes, times);
  times.builderFinished = performance.now();
  // Read by the recorder over its protocol connection, and by the panel.
  // Read by the panel; the frame key is the recorder's for a served frame,
  // and empty for the top document (slice 5c).
  const frameKey = typeof data.frameKey === "string" ? data.frameKey : "";
  Object.defineProperty(window, "__recorderRecreation", {
    value: Object.freeze({ built: true, notes: Object.freeze(notes), times, frameKey }),
  });
  // The first frame after the build: the next animation frame's callback
  // runs before that frame is painted, and a task posted from it runs after.
  requestAnimationFrame(() => {
    setTimeout(() => {
      times.firstPaint = performance.now();
      // Slice 5c: the build report, through the recorder's binding, when
      // the recorder added it.
      if (typeof window.__a11yRecorderBuilt === "function") {
        window.__a11yRecorderBuilt(JSON.stringify({
          frameKey,
          timeOrigin: performance.timeOrigin,
          times,
          frames: inPlaceTimes,
        }));
      }
    }, 0);
  });
}, { once: true });
