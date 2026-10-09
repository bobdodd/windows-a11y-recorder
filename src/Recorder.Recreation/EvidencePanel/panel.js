// The evidence panel: shows the recorded evidence of the recreation at its
// frame, read from the recorder's server. Every recorded value is page content
// and is inserted as text, never as markup. See
// docs/architecture/page-recreation.md, "Slice 3 design".
"use strict";

const content = document.getElementById("content");
const notice = document.getElementById("notice");
const status = document.getElementById("status");
const viewer = document.getElementById("viewer");
// The address of the recorder's resources for this recreation, set by load.
let recorderBase = null;
// Slice 5c: where the per-document sections are written, the document they
// show, and the chain of frames from the top document to it: for each
// frame, its owner's path in its parent, its key, its recorded address, and
// how it was built. The top document's chain is empty.
let section = content;
let documentContent = null;
let shownKey = "";
let shownChain = [];
let frameList = [];

function element(name, text, attributes) {
  const result = document.createElement(name);
  if (text !== undefined && text !== null) {
    result.textContent = String(text);
  }
  for (const [key, value] of Object.entries(attributes || {})) {
    result.setAttribute(key, value);
  }
  return result;
}

function seconds(nanoseconds) {
  return nanoseconds === null || nanoseconds === undefined
    ? "not recorded"
    : `${(nanoseconds / 1e9).toFixed(3)} s`;
}

function milliseconds(value) {
  return `${Number(value).toFixed(1)} ms`;
}

// A cell of long text, such as an address, which may wrap anywhere.
function longCell(text) {
  return element("td", text, { class: "long" });
}

function optionalMilliseconds(value) {
  return value === null || value === undefined ? "not recorded" : milliseconds(value);
}

function describeListener(listener) {
  const flags = [
    listener.capture ? "capture" : null,
    listener.once ? "once" : null,
    listener.passive ? "passive" : null
  ].filter(Boolean);
  let text = listener.eventName;
  if (flags.length > 0) {
    text += ` (${flags.join(", ")})`;
  }
  if (listener.registrationKind) {
    text += `, ${listener.registrationKind}`;
  }
  if (listener.location) {
    text += `, at ${listener.location}`;
  }
  return text;
}

function say(text) {
  status.textContent = "";
  // A new text node, so screen readers announce a repeated message.
  status.appendChild(document.createTextNode(text));
}

// Finds the node of a path in a document. Written as a function so that its
// source can be sent to the inspected page. Each scope after the first starts
// at a shadow root, which XPath cannot take as its context node, so the first
// step of such a scope is matched against the shadow root's children here,
// and the rest of the scope is evaluated from the node it selects.
function findNode(scopes, within) {
  const start = within || document;
  const firstStep = scope => {
    let depth = 0;
    for (let index = 1; index < scope.length; index++) {
      const character = scope[index];
      if (character === "[") { depth++; }
      else if (character === "]") { depth--; }
      else if (character === "/" && depth === 0) { return [scope.slice(1, index), scope.slice(index)]; }
    }
    return [scope.slice(1), ""];
  };
  const matchStep = (root, step) => {
    const match = /^(.*)\[(\d+)\]$/.exec(step);
    if (!match) { return null; }
    const test = match[1];
    const position = Number(match[2]);
    const localName = /^\*\[local-name\(\)='([^']*)'\]$/.exec(test);
    const candidates = Array.from(root.childNodes).filter(node =>
      test === "text()" ? node.nodeType === Node.TEXT_NODE
        : localName ? node.nodeType === Node.ELEMENT_NODE && node.localName === localName[1]
        : node.nodeType === Node.ELEMENT_NODE && node.localName === test);
    return candidates[position - 1] || null;
  };
  let node = start.evaluate(scopes[0], start, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
  if (!node) { return { failure: "not-found", scope: 0 }; }
  for (let index = 1; index < scopes.length; index++) {
    const root = node.shadowRoot;
    if (!root) { return { failure: "no-shadow-root", scope: index }; }
    const [step, rest] = firstStep(scopes[index]);
    node = matchStep(root, step);
    if (node && rest) {
      node = start.evaluate("." + rest, node, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
    }
    if (!node) { return { failure: "not-found", scope: index }; }
  }
  return { node };
}

// Selects the node of a path in the Elements panel.
// Slice 5c: the expression that selects a node of a document reached
// through a chain of frame owners from the document it runs in. It walks
// each owner's contentDocument; where an owner has none, its frame being of
// another origin, it stops and says where, and the panel goes on in that
// frame. Run in a frame chosen by its address, it first checks the frame's
// key, so that it does not select in another frame at the same address.
function selectExpression(owners, scopes, expectedKey) {
  return `(() => {
    const findNode = ${findNode.toString()};
    const expectedKey = ${JSON.stringify(expectedKey)};
    if (expectedKey !== null && (!window.__recorderRecreation || window.__recorderRecreation.frameKey !== expectedKey)) {
      return JSON.stringify({ result: "other-frame" });
    }
    const owners = ${JSON.stringify(owners)};
    let within = document;
    for (let index = 0; index < owners.length; index++) {
      const owner = findNode(owners[index], within);
      if (!owner.node) { return JSON.stringify({ result: owner.failure, owner: index }); }
      if (!owner.node.contentDocument) { return JSON.stringify({ result: "cross", owner: index }); }
      within = owner.node.contentDocument;
    }
    const found = findNode(${JSON.stringify(scopes)}, within);
    if (!found.node) { return JSON.stringify({ result: found.failure }); }
    inspect(found.node);
    return JSON.stringify({ result: "selected" });
  })()`;
}

function withoutFragment(url) {
  const hash = url.indexOf("#");
  return hash >= 0 ? url.slice(0, hash) : url;
}

// Selects the node of a path in a document of the recreation: the top
// document's for an empty chain, or a frame's. An extension reaches a frame
// of another origin only by its address, in the first frame DevTools finds
// at it (ExtensionServer.ts), so the frame's key is checked there.
function select(path, label, chain) {
  const links = chain || [];
  const attempt = (from, expectedKey, frameURL) => {
    const owners = links.slice(from).map(link => link.owner.scopes);
    const expression = selectExpression(owners, path.scopes, expectedKey);
    const options = frameURL ? { frameURL } : {};
    chrome.devtools.inspectedWindow.eval(expression, options, (result, error) => {
      if (error) {
        say(`${label} could not be selected: ${error.value || error.description || error.code}${frameURL ? `, in the frame at ${frameURL}` : ""}.`);
        return;
      }
      let answer;
      try {
        answer = JSON.parse(result);
      } catch (parseError) {
        say(`${label} could not be selected: the recreation gave no answer.`);
        return;
      }
      const owner = typeof answer.owner === "number" ? links[from + answer.owner] : null;
      if (answer.result === "selected") {
        say(`${label} is selected in the Elements panel.`);
      } else if (answer.result === "cross" && owner) {
        if (!owner.url) {
          say(`${label} could not be selected: the frame ${owner.key} is of another origin and has no recorded address to reach it by.`);
          return;
        }
        attempt(from + answer.owner + 1, owner.key, withoutFragment(owner.url));
      } else if (answer.result === "other-frame") {
        say(`${label} could not be selected: another frame of the recreation has the same address, ${frameURL}, and DevTools reaches a frame of another origin for an extension by its address only, in the first frame it finds there, so the nodes of this frame cannot be selected from the panel.`);
      } else if (answer.result === "no-shadow-root") {
        say(`${label} could not be selected: ${owner ? `the owner of frame ${owner.key} is` : "it is"} in a shadow root that is closed or missing, and the panel reaches open shadow roots only.`);
      } else if (owner) {
        say(`${label} could not be selected: the owner of frame ${owner.key}, at ${owner.owner.display}, is not found in the recreation.`);
      } else {
        say(`${label} could not be selected: its path selects no node in the recreation.`);
      }
    });
  };
  attempt(0, null, null);
}

// The chain of frames from the top document to the frame with the key.
function chainOf(key) {
  const byKey = new Map(frameList.map(item => [item.key, item]));
  const chain = [];
  let current = key;
  while (current) {
    const item = byKey.get(current);
    if (!item) {
      break;
    }
    chain.unshift({ key: item.key, owner: item.owner, url: item.documentUrl, way: item.way });
    current = item.parentKey;
  }
  return chain;
}

// A path as written in a document reached through a chain: each owner's
// path, then /#document, then the path in the document.
function chainDisplay(path, chain) {
  return chain.map(link => `${link.owner ? link.owner.display : "?"}/#document`).join("") + path.display;
}

function copy(text, label) {
  const area = element("textarea", text);
  area.setAttribute("aria-hidden", "true");
  area.style.position = "fixed";
  area.style.opacity = "0";
  document.body.appendChild(area);
  area.select();
  const copied = document.execCommand("copy");
  area.remove();
  say(copied ? `${label} copied.` : `${label} could not be copied.`);
}

// A path's cell, with its Select and Copy buttons. The chain is the frames
// from the top document to the path's document, the shown document's when
// none is given (slice 5c).
function pathCell(path, label, chain, focusId) {
  const links = chain || shownChain;
  const cell = element("td");
  if (!path) {
    cell.textContent = "none";
    return cell;
  }
  const display = chainDisplay(path, links);
  cell.appendChild(element("code", display));
  cell.appendChild(element("br"));
  const ids = focusId ? name => ({ "data-focus": `${focusId}:${name}` }) : () => ({});
  const notBuilt = links.find(link => link.way === "not-built");
  if (notBuilt) {
    cell.appendChild(element("p", `Not selectable: frame ${notBuilt.key} is not built in the recreation.`));
  } else {
    const selectButton = element("button", "Select", { type: "button", "aria-label": `Select ${label} in the Elements panel`, ...ids("select") });
    selectButton.addEventListener("click", () => select(path, label, links));
    cell.appendChild(selectButton);
  }
  const copyButton = element("button", "Copy path", { type: "button", "aria-label": `Copy the path of ${label}`, ...ids("copy") });
  copyButton.addEventListener("click", () => copy(display, `The path of ${label}`));
  const copyScopes = element("button", "Copy scopes", { type: "button", "aria-label": `Copy the path of ${label} as a list of scopes`, ...ids("scopes") });
  const scopesText = links.length === 0
    ? JSON.stringify(path.scopes)
    : JSON.stringify({ owners: links.map(link => link.owner ? link.owner.scopes : null), scopes: path.scopes });
  copyScopes.addEventListener("click", () => copy(scopesText, `The scopes of ${label}`));
  cell.append(copyButton, copyScopes);
  return cell;
}

// Slice 4f (protocol 0.52): who scheduled a timer, as recorded. The element's
// path has the same Select and Copy buttons as every other path.
function scheduledByCell(item) {
  const origin = item.scheduledBy;
  const cell = element("td", null, { class: "long" });
  if (!origin) {
    cell.textContent = "not recorded";
    return cell;
  }
  cell.appendChild(element("p", `Owner: ${origin.owner}`));
  if (origin.element) {
    const label = `${origin.element}, for ${item.timerId}`;
    const paragraph = element("p", `Element: ${origin.element}`);
    if (origin.elementPath) {
      // The path and its buttons, moved from the cell pathCell builds.
      paragraph.appendChild(element("br"));
      paragraph.append(...Array.from(pathCell(origin.elementPath, label).childNodes));
    }
    cell.appendChild(paragraph);
  }
  if (origin.elementNote) {
    cell.appendChild(element("p", origin.elementNote));
  }
  if (origin.caller) {
    const paragraph = element("p", `Called from: ${origin.caller}`);
    sourceLink(paragraph, origin.callerSource, `the caller of ${item.timerId}`);
    cell.appendChild(paragraph);
  }
  if (origin.handler === "string") {
    cell.appendChild(element("p", "Handler: a string of code"));
  } else if (origin.callback) {
    const paragraph = element("p", `Callback defined at: ${origin.callback}`);
    sourceLink(paragraph, origin.callbackSource, `the callback of ${item.timerId}`);
    cell.appendChild(paragraph);
  }
  return cell;
}

// Slice 4h (protocol 0.54): a button that opens a recorded script's text at
// a line, added to a paragraph when the script's text is recorded.
function sourceLink(paragraph, link, label) {
  if (!link) {
    return;
  }
  const button = element("button", `View line ${link.line}`, {
    type: "button",
    "aria-label": `View ${label} at line ${link.line} of script ${link.scriptId}`
  });
  button.addEventListener("click", () => viewSource(link.digest, `script ${link.scriptId}`, link.line, button));
  paragraph.appendChild(element("br"));
  paragraph.appendChild(button);
}

function scriptAddress(item) {
  const parts = [];
  if (item.url) {
    parts.push(item.url);
  }
  if (item.sourceUrl && item.sourceUrl !== item.url) {
    parts.push(`sourceURL ${item.sourceUrl}`);
  }
  if (item.sourceMapUrl) {
    parts.push(`source map ${item.sourceMapUrl}, not recorded`);
  }
  return parts.length > 0 ? parts.join("; ") : "none, inline or generated";
}

function scriptSize(size) {
  return size === 1 ? "1 byte" : `${size} bytes`;
}

// The lines of a text as V8 counts them.
function sourceLines(text) {
  return text.split(/\r\n|[\n\r\u2028\u2029]/);
}

// Opens the read-only viewer of a script's recorded text, at a line when one
// is given. The text is inserted as text, never run. Closing the viewer
// returns focus to the control that opened it.
async function viewSource(digest, label, line, opener) {
  say(`Reading the text of ${label}.`);
  let text;
  try {
    const response = await fetch(`${recorderBase}script/${digest}`, { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`the recorder answered ${response.status}`);
    }
    text = await response.text();
  } catch (error) {
    say(`The text of ${label} could not be read: ${error.message}.`);
    return;
  }
  const lines = sourceLines(text);
  viewer.replaceChildren();
  const heading = element("h2", `Recorded source of ${label}`, { tabindex: "-1" });
  const close = element("button", "Back to the evidence", { type: "button" });
  close.addEventListener("click", () => {
    viewer.hidden = true;
    viewer.replaceChildren();
    content.hidden = false;
    if (opener && opener.isConnected) {
      opener.focus();
    }
  });
  viewer.append(
    heading,
    element("p", `${lines.length} lines, ${scriptSize(new TextEncoder().encode(text).length)}. This is the text V8 compiled, as recorded; it is shown as text and does not run.`),
    close);
  const list = element("ol", null, { class: "source", "aria-label": `Lines of ${label}` });
  lines.forEach((value, index) => {
    const number = index + 1;
    const item = element("li", null, { id: `line-${number}`, tabindex: "-1" });
    item.appendChild(element("code", value === "" ? " " : value));
    list.appendChild(item);
  });
  viewer.appendChild(list);
  content.hidden = true;
  viewer.hidden = false;
  const target = line ? document.getElementById(`line-${Math.min(line, lines.length)}`) : null;
  if (target) {
    target.classList.add("target");
    target.scrollIntoView({ block: "center" });
    target.focus();
    say(`Showing ${label} at line ${line} of ${lines.length}.`);
  } else {
    heading.focus();
    say(`Showing ${label}, ${lines.length} lines.`);
  }
}

// Slice 4g: an animation's start, on its timeline and in the recording.
function animationStart(item) {
  if (item.startTimeMilliseconds === null || item.startTimeMilliseconds === undefined) {
    return "unresolved";
  }
  const onTimeline = `${milliseconds(item.startTimeMilliseconds)} on its timeline`;
  return item.startNanoseconds === null || item.startNanoseconds === undefined
    ? onTimeline
    : `${seconds(item.startNanoseconds)} (${onTimeline})`;
}

function animationTiming(item) {
  if (item.durationMilliseconds === null || item.durationMilliseconds === undefined) {
    return "no effect";
  }
  const iterations = item.iterations === null || item.iterations === undefined ? "infinite" : String(item.iterations);
  return `duration ${milliseconds(item.durationMilliseconds)}, delay ${milliseconds(item.delayMilliseconds || 0)}, ` +
    `iterations ${iterations}, direction ${item.direction}, fill ${item.fill}`;
}

function animationProgress(item) {
  if (item.progress === null || item.progress === undefined) {
    return "not in effect";
  }
  const iteration = item.currentIteration === null || item.currentIteration === undefined
    ? "final"
    : String(item.currentIteration + 1);
  let text = `iteration ${iteration}, ${(item.progress * 100).toFixed(1)} % before easing`;
  if (item.recordedProgress !== null && item.recordedProgress !== undefined) {
    text += `; Blink's progress at the record, after easing: ${(item.recordedProgress * 100).toFixed(1)} %`;
  }
  return text;
}

function table(caption, headings, rows, empty) {
  section.appendChild(element("h2", caption));
  if (rows.length === 0) {
    section.appendChild(element("p", empty));
    return;
  }
  const result = element("table");
  result.appendChild(element("caption", `${caption}: ${rows.length}`));
  const head = element("thead");
  const headRow = element("tr");
  for (const heading of headings) {
    headRow.appendChild(element("th", heading, { scope: "col" }));
  }
  head.appendChild(headRow);
  result.appendChild(head);
  const body = element("tbody");
  for (const cells of rows) {
    const row = element("tr");
    for (const cell of cells) {
      row.appendChild(cell instanceof Node ? cell : element("td", cell));
    }
    body.appendChild(row);
  }
  result.appendChild(body);
  section.appendChild(result);
}

function describe(evidence) {
  const recreation = evidence.recreation;
  notice.textContent = recreation.notice;

  section.appendChild(element("h2", "Recreation"));
  const facts = element("dl");
  for (const [term, value] of [
    ["Source", recreation.source === "fixed" ? "fixed content, not a recording" : "recording"],
    ["Title", recreation.title],
    ["Frame", seconds(recreation.frameNanoseconds)],
    ["Recording time", seconds(recreation.recordingNanoseconds)],
    ["Basis", recreation.basis || "not recorded"],
    ["Address", recreation.url || "not recorded"],
    ["Document", recreation.documentKey || "not recorded"]
  ]) {
    facts.append(element("dt", term), element("dd", value));
  }
  section.appendChild(facts);

  const fidelity = evidence.fidelity;
  section.appendChild(element("h2", "Fidelity"));
  section.appendChild(element("p", `${fidelity.status}: ${fidelity.explanation}`));
  if (fidelity.differences.length > 0) {
    table(
      "Differences from the recording",
      ["Node", "Property", "Recorded", "Recreated"],
      fidelity.differences.map(item => [pathCell(item.node, item.node.display), item.property, item.recorded, item.recreated]),
      "");
  }

  if (evidence.notes && evidence.notes.length > 0) {
    section.appendChild(element("h2", "Notes on the recreation"));
    const list = element("ul");
    for (const item of evidence.notes) {
      list.appendChild(element("li", item));
    }
    section.appendChild(list);
  }

  table(
    "Pending timers",
    ["Timer", "Kind", "Requested delay", "Effective delay", "Scheduled", "Last run", "Remaining at the frame", "Scheduled by"],
    evidence.timers.map(item => [
      item.timerId, item.kind, optionalMilliseconds(item.requestedDelayMilliseconds),
      optionalMilliseconds(item.effectiveDelayMilliseconds), seconds(item.scheduledNanoseconds),
      item.lastRunNanoseconds === null || item.lastRunNanoseconds === undefined ? "not run" : seconds(item.lastRunNanoseconds),
      item.remainingMilliseconds === null || item.remainingMilliseconds === undefined
        ? "no due time"
        : item.remainingMilliseconds < 0
          ? `${milliseconds(-item.remainingMilliseconds)} overdue`
          : milliseconds(item.remainingMilliseconds),
      scheduledByCell(item)
    ]),
    "No timers were pending at the frame.");

  // Slice 4g (protocol 0.53): the animations at the frame, as recorded.
  if (evidence.animationsNotRead) {
    section.appendChild(element("h2", "Running animations and transitions"));
    section.appendChild(element("p", evidence.animationsNotRead));
  } else {
    table(
      "Running animations and transitions",
      ["Kind", "Name or property", "Target", "Play state", "Start", "Timing", "Easing", "Current time at the frame", "Iteration and progress at the frame", "Timeline", "On the compositor", "Recorded"],
      evidence.animations.map(item => {
        const name = item.name || "unnamed";
        const label = `the target of ${item.kind} ${name}`;
        const target = pathCell(item.target, label);
        if (item.pseudoElement) {
          target.appendChild(element("p", `Pseudo-element: ${item.pseudoElement}`));
        }
        return [
          item.kind, name, target,
          item.pending ? `${item.playState}, pending` : item.playState,
          animationStart(item),
          longCell(animationTiming(item)),
          item.easing || "not recorded",
          item.currentTimeMilliseconds === null || item.currentTimeMilliseconds === undefined
            ? "unresolved"
            : `${milliseconds(item.currentTimeMilliseconds)}, ${item.currentTimeBasis === "computed" ? "computed at the frame" : "as recorded"}`,
          animationProgress(item),
          item.timeline,
          item.onCompositor ? "yes" : "no",
          seconds(item.recordedNanoseconds)
        ];
      }),
      "No animations or transitions were running at the frame.");
    if (evidence.animationNotes && evidence.animationNotes.length > 0) {
      const list = element("ul");
      for (const item of evidence.animationNotes) {
        list.appendChild(element("li", item));
      }
      section.appendChild(list);
    }
  }

  // Slice 4h (protocol 0.54): the document's scripts at the frame.
  if (evidence.scriptsNotRead) {
    section.appendChild(element("h2", "Scripts"));
    section.appendChild(element("p", evidence.scriptsNotRead));
  } else {
    table(
      "Scripts",
      ["Script", "Kind", "Owner", "Element", "Address", "Starts at", "Eval called from", "Size", "Compiled", "Source"],
      (evidence.scripts || []).map(item => {
        const label = `script ${item.scriptId}`;
        let elementCell;
        if (item.element && item.elementPath) {
          elementCell = pathCell(item.elementPath, `${item.element}, for ${label}`);
          elementCell.insertBefore(element("p", item.element), elementCell.firstChild);
        } else {
          elementCell = element("td", item.element || "none recorded");
        }
        const source = element("td");
        if (item.digest) {
          const button = element("button", "View source", { type: "button", "aria-label": `View the source of ${label}` });
          button.addEventListener("click", () => viewSource(item.digest, label, null, button));
          source.appendChild(button);
        } else {
          source.textContent = "text not recorded";
        }
        return [
          item.scriptId,
          item.kind,
          longCell(item.owner),
          elementCell,
          longCell(scriptAddress(item)),
          item.line ? `line ${item.line}, column ${item.column || 1}` : "not recorded",
          item.evalFrom ? longCell(item.evalFrom) : "",
          scriptSize(item.size),
          item.compileError ? `${seconds(item.recordedNanoseconds)}, failed to compile` : seconds(item.recordedNanoseconds),
          source
        ];
      }),
      "No scripts were recorded in the document at or before the frame.");
    if (evidence.scriptNotes && evidence.scriptNotes.length > 0) {
      const list = element("ul");
      for (const item of evidence.scriptNotes) {
        list.appendChild(element("li", item));
      }
      section.appendChild(list);
    }
  }

  table(
    "Interactive elements",
    ["Element", "Name", "Role", "Focusable", "Listeners", "Accessibility data recorded", "Path"],
    evidence.interactiveElements.map(item => {
      const label = `${item.element} ${item.name || ""}`.trim();
      return [
        item.element, item.name || "none recorded", item.role || "none recorded",
        item.focusable === null || item.focusable === undefined ? "not recorded" : item.focusable ? "yes" : "no",
        longCell(item.listeners.length > 0 ? item.listeners.map(describeListener).join("; ") : "none"),
        longCell(item.accessibilityNanoseconds === null || item.accessibilityNanoseconds === undefined
          ? "none"
          : `${seconds(item.accessibilityNanoseconds)}: ${item.accessibilityProperties || ""}`),
        pathCell(item.node, label)
      ];
    }),
    "No interactive elements were recorded at the frame.");
  if (evidence.recreation.source !== "fixed") {
    section.appendChild(element("p",
      "An element is listed when a listener is registered on it, or when its latest accessibility data has the FOCUSABLE state. " +
      "Focusable and the accessibility data are read from Chromium's accessibility property text as recorded, which is a diagnostic form."));
  }

  if (evidence.otherListeners && evidence.otherListeners.length > 0) {
    table(
      "Listeners on other targets",
      ["Target", "Listener"],
      evidence.otherListeners.map(item => [item.target, longCell(describeListener(item.listener))]),
      "");
  }

  const interaction = evidence.interaction;
  section.appendChild(element("h2", "Focus and selection"));
  const focus = element("table");
  focus.appendChild(element("caption", "Focus and selection at the frame"));
  const focusBody = element("tbody");
  const focusRow = element("tr");
  focusRow.append(element("th", "Focus", { scope: "row" }), pathCell(interaction.focus, "the focused element"));
  const selectionRow = element("tr");
  selectionRow.append(element("th", "Selection", { scope: "row" }), element("td", interaction.selection || "none recorded"));
  focusBody.append(focusRow, selectionRow);
  focus.appendChild(focusBody);
  section.appendChild(focus);

  table(
    "Form control values",
    ["Value", "Path"],
    interaction.formValues.map(item => [longCell(item.value), pathCell(item.node, `the control with value ${item.value}`)]),
    "No form control values were recorded at the frame.");
}

// Navigations the recorder refused, read from the recorder every second
// while the panel is open, each new one announced.
let blockedShown = 0;
let blockedList = null;

function showBlocked(items) {
  if (items.length === blockedShown) {
    return;
  }
  if (!blockedList) {
    content.appendChild(element("h2", "Navigations blocked"));
    content.appendChild(element("p",
      "The recreation does not leave the recorded page. Each attempt to load another page in the recreation browser, such as a followed link, is refused and listed here."));
    blockedList = element("ul");
    content.appendChild(blockedList);
  }
  for (const item of items.slice(blockedShown)) {
    const where = item.inRecreationTab ? "in the recreation's tab" : "in a new tab, which was closed";
    blockedList.appendChild(longItem(`${new Date(item.time).toLocaleTimeString()}: ${item.url}, ${where}`));
    say(`Navigation to ${item.url} was blocked; the recreation does not leave the recorded page.`);
  }
  blockedShown = items.length;
}

function longItem(text) {
  const item = element("li", text);
  item.className = "long";
  return item;
}

async function watchBlocked(address) {
  try {
    const response = await fetch(address, { cache: "no-store" });
    if (response.ok) {
      showBlocked(await response.json());
    }
  } catch (error) {
    // The recorder has closed this recreation; the panel stops asking.
    return;
  }
  setTimeout(() => watchBlocked(address), 1000);
}

// Slice 5b: the page's frames, how each was built or why not, and what
// the recreation browser has done with each, read from the recorder every
// second while the panel is open. Slice 5c: each owner's path has Select
// and Copy buttons, reached through its parent's chain; each frame's
// origin, document choice, times, and a button that shows its evidence.
let framesSection = null;
let framesShown = "";

const frameWays = {
  "served": "served at its recorded address with a page of its own",
  "in-place": "built in place in its about:blank document",
  "srcdoc": "built in place after loading its recorded srcdoc markup",
  "not-built": "not built",
};

function yesNo(value, unknown) {
  return value === true ? "yes" : value === false ? "no" : unknown;
}

function frameName(item) {
  return `frame ${item.key}, the ${item.element} ${item.ownerNodeId}${item.documentUrl ? `, at ${item.documentUrl}` : ""}`;
}

function originText(item) {
  if (!item.origin) {
    return item.documentKey ? "not read from its address" : "none";
  }
  if (item.origin === "opaque") {
    return "opaque: its owner is sandboxed without allow-same-origin";
  }
  return item.originInherited ? `${item.origin}, inherited from its parent` : item.origin;
}

function timesText(item) {
  const times = item.times;
  if (!times) {
    return item.way === "not-built" ? "none: not built" : "not reported yet";
  }
  const parts = [];
  if (typeof times.loadStarted === "number") {
    parts.push(`load started at ${milliseconds(times.loadStarted)}`);
  }
  if (typeof times.built === "number") {
    parts.push(`built at ${milliseconds(times.built)}`);
  } else {
    parts.push("its build did not finish");
  }
  if (typeof times.firstPaint === "number") {
    parts.push(`first painted at ${milliseconds(times.firstPaint)}`);
  }
  if (typeof times.took === "number") {
    parts.push(item.way === "served"
      ? `took ${milliseconds(times.took)} from its load start to that paint`
      : `its build took ${milliseconds(times.took)}`);
  }
  return `${parts.join("; ")}. Basis: ${times.basis}.`;
}

function showFrames(items) {
  const text = JSON.stringify(items);
  if (text === framesShown) {
    return;
  }
  framesShown = text;
  frameList = items;
  if (!framesSection) {
    framesSection = element("section");
    content.appendChild(framesSection);
  }
  // The table is rebuilt when what the recorder reports changes; the
  // control that had focus keeps it.
  const focused = document.activeElement && framesSection.contains(document.activeElement)
    ? document.activeElement.getAttribute("data-focus")
    : null;
  framesSection.replaceChildren();
  framesSection.appendChild(element("h2", "Frames"));
  if (items.length === 0) {
    framesSection.appendChild(element("p", "The page had no frames at this frame of the recording."));
    showDocumentChoice(items);
    return;
  }
  framesSection.appendChild(element("p",
    "Each frame of the page at this frame of the recording, in its parent's document order, a frame of a frame after its parent. The key is the frame's place: its index among its parent's frames, under its parent's key. Asked for and out of process are what the recreation browser has done so far. An origin is read from the recorded address, as the recording does not hold it."));
  framesSection.appendChild(element("p",
    "Times are in milliseconds from the top document's time origin, as each document's builder reports them. A served frame's times are on its own clock, placed against the top document's through each document's performance.timeOrigin, which Chromium reads from the wall clock when the document's timing object is made, so a change of the system clock between two documents moves one against the other. How long a frame took is on one clock."));
  const result = element("table");
  result.appendChild(element("caption", `Frames: ${items.length}`));
  const head = element("thead");
  const headRow = element("tr");
  for (const heading of ["Key", "Owner path", "Element", "Document address", "Origin", "Document chosen", "Built", "In its parent's process when recorded", "Asked for", "Out of process", "Times", "Evidence"]) {
    headRow.appendChild(element("th", heading, { scope: "col" }));
  }
  head.appendChild(headRow);
  result.appendChild(head);
  const body = element("tbody");
  for (const item of items) {
    const row = element("tr");
    const way = frameWays[item.way] ?? item.way;
    row.appendChild(element("td", item.key));
    row.appendChild(item.owner
      ? pathCell(item.owner, `the owner of ${frameName(item)}`, chainOf(item.parentKey), `frame-${item.key}`)
      : longCell(item.ownerPath ?? "none"));
    row.appendChild(element("td", `${item.element} ${item.ownerNodeId}`));
    row.appendChild(longCell(item.documentUrl ?? "none"));
    row.appendChild(longCell(originText(item)));
    row.appendChild(longCell(item.documentKey
      ? `document ${item.documentKey}, ${item.choice}; ${item.basis}`
      : "no document recorded"));
    row.appendChild(longCell(item.reason ? `${way}: ${item.reason}` : way));
    row.appendChild(element("td", yesNo(item.sameProcessAsParentWhenRecorded, "not known")));
    row.appendChild(element("td", yesNo(item.askedFor)));
    row.appendChild(element("td", yesNo(item.outOfProcess)));
    row.appendChild(longCell(timesText(item)));
    const evidenceCell = element("td");
    if (item.evidenceAddress) {
      const button = element("button", "Show evidence", {
        type: "button",
        "aria-label": `Show the evidence of ${frameName(item)}`,
        "data-focus": `frame-${item.key}:evidence`
      });
      button.addEventListener("click", () => showDocument(item.key, true));
      evidenceCell.appendChild(button);
    } else {
      evidenceCell.textContent = "none: no DOM walk of its document was recorded";
    }
    row.appendChild(evidenceCell);
    body.appendChild(row);
  }
  result.appendChild(body);
  framesSection.appendChild(result);
  if (focused) {
    const again = framesSection.querySelector(`[data-focus="${CSS.escape(focused)}"]`);
    if (again) {
      again.focus();
    }
  }
  showDocumentChoice(items);
}

// Slice 5c: the list of documents whose evidence the per-document sections
// show, the top document first, then each frame with evidence. It is made
// once, when the frames are first read; what it lists does not change.
let documentChoice = null;
const evidenceByKey = new Map();

function showDocumentChoice(items) {
  if (documentChoice) {
    return;
  }
  const choiceSection = document.getElementById("document-choice");
  const withEvidence = items.filter(item => item.evidenceAddress);
  if (withEvidence.length === 0) {
    choiceSection.appendChild(element("p", "The evidence below is the top document's. No frame of the page has a recorded document to show."));
    documentChoice = true;
    return;
  }
  const label = element("label", "Document shown", { for: "document-shown" });
  documentChoice = element("select", null, { id: "document-shown" });
  documentChoice.appendChild(element("option", "The top document", { value: "" }));
  for (const item of withEvidence) {
    documentChoice.appendChild(element("option",
      `Frame ${item.key}: the ${item.element} at ${item.ownerPath ?? "no path"}, ${item.documentUrl ?? "no address"}${item.way === "not-built" ? ", not built" : ""}`,
      { value: item.key }));
  }
  documentChoice.addEventListener("change", () => showDocument(documentChoice.value, false));
  choiceSection.append(
    element("p", "The sections below, from Evidence of to Form control values, are those of the document chosen here. Frames, Navigations blocked, and Time to open are of the whole page."),
    label,
    documentChoice);
}

// Shows a document's evidence in the per-document sections, read from the
// recorder once, and announces it. From a frame's Show evidence button the
// list follows, and focus moves to the sections' heading.
async function showDocument(key, fromButton) {
  const item = frameList.find(frame => frame.key === key);
  const name = item ? frameName(item) : "the top document";
  let evidence = evidenceByKey.get(key);
  if (!evidence) {
    try {
      const response = await fetch(`${recorderBase}${item.evidenceAddress}`, { cache: "no-store" });
      if (!response.ok) {
        throw new Error(`the recorder answered ${response.status}`);
      }
      evidence = await response.json();
    } catch (error) {
      say(`The evidence of ${name} could not be read: ${error.message}.`);
      return;
    }
    evidenceByKey.set(key, evidence);
  }
  if (documentChoice && documentChoice !== true) {
    documentChoice.value = key;
  }
  renderDocument(key, evidence);
  if (fromButton) {
    document.getElementById("document-heading").focus();
  }
  say(`Showing the evidence of ${name}.`);
}

function renderDocument(key, evidence) {
  shownKey = key;
  shownChain = key ? chainOf(key) : [];
  documentContent.replaceChildren();
  section = documentContent;
  const item = frameList.find(frame => frame.key === key);
  documentContent.appendChild(element("h2",
    item ? `Evidence of ${frameName(item)}` : "Evidence of the top document",
    { id: "document-heading", tabindex: "-1" }));
  describe(evidence);
  section = content;
}

async function watchFrames(address) {
  try {
    const response = await fetch(address, { cache: "no-store" });
    if (response.ok) {
      showFrames(await response.json());
    }
  } catch (error) {
    // The recorder has closed this recreation; the panel stops asking.
    return;
  }
  setTimeout(() => watchFrames(address), 1000);
}

// Stage 3: how long opening the recreation took. The recorder's steps are
// read from the recorder, and the builder's from the page, which records the
// times at which it finished each part from the page's time origin. Read
// every second until the page has painted its first frame.
let timingsList = null;

function showTimings(steps, times) {
  if (!timingsList) {
    content.appendChild(element("h2", "Time to open the recreation"));
    content.appendChild(element("p",
      "Measured on this machine as the recreation was opened. The recorder's steps are in milliseconds each; the page's are in milliseconds from the start of its load."));
    timingsList = element("ul");
    content.appendChild(timingsList);
  }
  timingsList.replaceChildren();
  for (const step of steps) {
    timingsList.appendChild(element("li", `${step.step}: ${step.milliseconds.toFixed(1)} ms`));
  }
  if (times) {
    const names = [
      ["builderStarted", "The builder started"],
      ["treeRead", "The recorded tree was read"],
      ["fontsLoaded", "The recorded font faces were added and loaded"],
      ["domBuilt", "The DOM was built"],
      ["styleSheetsLoaded", "The page's linked style sheets were loaded"],
      ["styleSheetsApplied", "The recorded style sheet changes and adopted sheets were applied"],
      ["styleAndLayout", "The first style and layout, with the recorded values, finished"],
      ["framesBuilt", "The frames built in place were built"],
      ["builderFinished", "The builder finished"],
      ["firstPaint", "The first frame after the build was painted"],
    ];
    for (const [key, label] of names) {
      if (typeof times[key] === "number") {
        timingsList.appendChild(element("li", `${label} at ${times[key].toFixed(1)} ms`));
      }
    }
    const beforeStyle = typeof times.styleSheetsApplied === "number" ? times.styleSheetsApplied : times.domBuilt;
    if (typeof beforeStyle === "number" && typeof times.styleAndLayout === "number") {
      timingsList.appendChild(element("li",
        `The first style and layout took ${(times.styleAndLayout - beforeStyle).toFixed(1)} ms`));
    }
  }
}

// The page's own size and device pixel ratio once it has painted, beside
// those it is meant to be shown at ("The recorded layout zoom" in
// docs/architecture/accessibility-preferences.md). innerWidth and
// innerHeight are the size media queries see, as the recorded viewport is.
function checkViewport() {
  const top = evidenceByKey.get("");
  const meant = top && top.shownViewport;
  if (!meant) {
    return;
  }
  chrome.devtools.inspectedWindow.eval(
    "JSON.stringify([innerWidth, innerHeight, devicePixelRatio])",
    (result) => {
      if (typeof result !== "string") {
        return;
      }
      const [width, height, ratio] = JSON.parse(result);
      content.appendChild(element("h2", "Viewport as shown"));
      const list = element("ul");
      list.appendChild(element("li",
        `Meant to be shown at: ${meant.width.toFixed(2)} by ${meant.height.toFixed(2)} CSS pixels, device pixel ratio ${meant.devicePixelRatio.toFixed(4)}, laid out at a layout zoom factor of ${meant.layoutZoomFactor.toFixed(4)}`));
      list.appendChild(element("li",
        `Shown at: ${width} by ${height} CSS pixels, device pixel ratio ${Number(ratio).toFixed(4)}`));
      content.appendChild(list);
      const sizeDiffers = Math.abs(width - meant.width) > 1 || Math.abs(height - meant.height) > 1;
      const ratioDiffers = Math.abs(ratio - meant.devicePixelRatio) > 0.001;
      const differences = [];
      if (sizeDiffers) {
        differences.push("its size");
      }
      if (ratioDiffers) {
        differences.push("its device pixel ratio");
      }
      const summary = differences.length === 0
        ? "The page's size, to the nearest CSS pixel, and its device pixel ratio are those it is meant to be shown at."
        : `The page differs in ${differences.join(" and ")} from the viewport it is meant to be shown at, so its media queries and layout may not be the recorded ones.`;
      content.appendChild(element("p", summary));
      if (differences.length > 0) {
        say(summary);
      }
    });
}

function watchTimings(address) {
  chrome.devtools.inspectedWindow.eval(
    "window.__recorderRecreation ? JSON.stringify(window.__recorderRecreation.times) : null",
    async (result) => {
      let steps = [];
      try {
        const response = await fetch(address, { cache: "no-store" });
        if (response.ok) {
          steps = await response.json();
        }
      } catch (error) {
        // The recorder has closed this recreation; the panel stops asking.
        return;
      }
      const times = typeof result === "string" ? JSON.parse(result) : null;
      showTimings(steps, times);
      if (!times || typeof times.firstPaint !== "number") {
        setTimeout(() => watchTimings(address), 1000);
      } else {
        checkViewport();
      }
    });
}

async function load() {
  try {
    const config = await (await fetch(chrome.runtime.getURL("config.json"))).json();
    const response = await fetch(config.evidenceAddress, { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`the recorder answered ${response.status}`);
    }
    recorderBase = config.evidenceAddress.replace(/evidence\.json$/, "");
    // Slice 5c: the document choice, then the per-document sections, then
    // the sections of the whole page.
    content.appendChild(element("section", null, { id: "document-choice" }));
    documentContent = element("section");
    content.appendChild(documentContent);
    const top = await response.json();
    evidenceByKey.set("", top);
    renderDocument("", top);
    watchFrames(config.evidenceAddress.replace(/evidence\.json$/, "frames.json"));
    watchBlocked(config.evidenceAddress.replace(/evidence\.json$/, "blocked.json"));
    watchTimings(config.evidenceAddress.replace(/evidence\.json$/, "timings.json"));
  } catch (error) {
    notice.textContent = `The evidence could not be read from the recorder: ${error.message}. The recorder may have closed this recreation.`;
  }
}

load();
