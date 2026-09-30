// The evidence panel: shows the recorded evidence of the recreation at its
// frame, read from the recorder's server. Every recorded value is page content
// and is inserted as text, never as markup. See
// docs/architecture/page-recreation.md, "Slice 3 design".
"use strict";

const content = document.getElementById("content");
const notice = document.getElementById("notice");
const status = document.getElementById("status");

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
function findNode(scopes) {
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
  let node = document.evaluate(scopes[0], document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
  if (!node) { return { failure: "not-found", scope: 0 }; }
  for (let index = 1; index < scopes.length; index++) {
    const root = node.shadowRoot;
    if (!root) { return { failure: "no-shadow-root", scope: index }; }
    const [step, rest] = firstStep(scopes[index]);
    node = matchStep(root, step);
    if (node && rest) {
      node = document.evaluate("." + rest, node, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
    }
    if (!node) { return { failure: "not-found", scope: index }; }
  }
  return { node };
}

// Selects the node of a path in the Elements panel.
function select(path, label) {
  const expression = `(() => {
    const found = (${findNode.toString()})(${JSON.stringify(path.scopes)});
    if (!found.node) { return found.failure; }
    inspect(found.node);
    return "selected";
  })()`;
  chrome.devtools.inspectedWindow.eval(expression, (result, error) => {
    if (error) {
      say(`${label} could not be selected: ${error.value || error.description || error.code}.`);
    } else if (result === "selected") {
      say(`${label} is selected in the Elements panel.`);
    } else if (result === "no-shadow-root") {
      say(`${label} could not be selected: its shadow root is closed or missing, and the panel reaches open shadow roots only.`);
    } else {
      say(`${label} could not be selected: its path selects no node in the recreation.`);
    }
  });
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

function pathCell(path, label) {
  const cell = element("td");
  if (!path) {
    cell.textContent = "none";
    return cell;
  }
  cell.appendChild(element("code", path.display));
  cell.appendChild(element("br"));
  const selectButton = element("button", "Select", { type: "button", "aria-label": `Select ${label} in the Elements panel` });
  selectButton.addEventListener("click", () => select(path, label));
  const copyButton = element("button", "Copy path", { type: "button", "aria-label": `Copy the path of ${label}` });
  copyButton.addEventListener("click", () => copy(path.display, `The path of ${label}`));
  const copyScopes = element("button", "Copy scopes", { type: "button", "aria-label": `Copy the path of ${label} as a list of scopes` });
  copyScopes.addEventListener("click", () => copy(JSON.stringify(path.scopes), `The scopes of ${label}`));
  cell.append(selectButton, copyButton, copyScopes);
  return cell;
}

function table(caption, headings, rows, empty) {
  content.appendChild(element("h2", caption));
  if (rows.length === 0) {
    content.appendChild(element("p", empty));
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
  content.appendChild(result);
}

function describe(evidence) {
  const recreation = evidence.recreation;
  notice.textContent = recreation.notice;

  content.appendChild(element("h2", "Recreation"));
  const facts = element("dl");
  for (const [term, value] of [
    ["Source", recreation.source === "fixed" ? "fixed content, not a recording" : "recording"],
    ["Title", recreation.title],
    ["Frame", seconds(recreation.frameNanoseconds)],
    ["Recording time", seconds(recreation.recordingNanoseconds)],
    ["Basis", recreation.basis || "not recorded"]
  ]) {
    facts.append(element("dt", term), element("dd", value));
  }
  content.appendChild(facts);

  const fidelity = evidence.fidelity;
  content.appendChild(element("h2", "Fidelity"));
  content.appendChild(element("p", `${fidelity.status}: ${fidelity.explanation}`));
  if (fidelity.differences.length > 0) {
    table(
      "Differences from the recording",
      ["Node", "Property", "Recorded", "Recreated"],
      fidelity.differences.map(item => [pathCell(item.node, item.node.display), item.property, item.recorded, item.recreated]),
      "");
  }

  table(
    "Pending timers",
    ["Timer", "Kind", "Delay", "Scheduled", "Remaining at the frame", "Scheduled by"],
    evidence.timers.map(item => [
      String(item.timerId), item.kind, milliseconds(item.delayMilliseconds), seconds(item.scheduledNanoseconds),
      milliseconds(item.remainingMilliseconds), pathCell(item.owner, `the owner of timer ${item.timerId}`)
    ]),
    "No timers were pending at the frame.");

  table(
    "Running animations and transitions",
    ["Kind", "Name or property", "Target", "Start", "Duration", "Progress"],
    evidence.animations.map(item => [
      item.kind, item.name, pathCell(item.target, `the target of ${item.kind} ${item.name}`), seconds(item.startNanoseconds),
      milliseconds(item.durationMilliseconds), `${(item.progress * 100).toFixed(1)} %`
    ]),
    "No animations or transitions were running at the frame.");

  table(
    "Interactive elements",
    ["Element", "Name", "Role", "Focusable", "Listeners", "Path"],
    evidence.interactiveElements.map(item => {
      const label = `${item.element} ${item.name || ""}`.trim();
      return [
        item.element, item.name || "none recorded", item.role || "none recorded", item.focusable ? "yes" : "no",
        item.listeners.length > 0 ? item.listeners.join(", ") : "none", pathCell(item.node, label)
      ];
    }),
    "No interactive elements were recorded at the frame.");

  const interaction = evidence.interaction;
  content.appendChild(element("h2", "Focus and selection"));
  const focus = element("table");
  focus.appendChild(element("caption", "Focus and selection at the frame"));
  const focusBody = element("tbody");
  const focusRow = element("tr");
  focusRow.append(element("th", "Focus", { scope: "row" }), pathCell(interaction.focus, "the focused element"));
  const selectionRow = element("tr");
  selectionRow.append(element("th", "Selection", { scope: "row" }), element("td", interaction.selection || "none recorded"));
  focusBody.append(focusRow, selectionRow);
  focus.appendChild(focusBody);
  content.appendChild(focus);

  table(
    "Form control values",
    ["Value", "Path"],
    interaction.formValues.map(item => [item.value, pathCell(item.node, `the control with value ${item.value}`)]),
    "No form control values were recorded at the frame.");
}

async function load() {
  try {
    const config = await (await fetch(chrome.runtime.getURL("config.json"))).json();
    const response = await fetch(config.evidenceAddress, { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`the recorder answered ${response.status}`);
    }
    describe(await response.json());
  } catch (error) {
    notice.textContent = `The evidence could not be read from the recorder: ${error.message}. The recorder may have closed this recreation.`;
  }
}

load();
