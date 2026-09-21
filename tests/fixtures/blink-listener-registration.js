// Registering a listener from a separate script file gives the recorded
// location a script URL that cannot be the document URL, so a recorded URL
// that names this file is evidence that the location came from the script that
// made the call. The call is made inside a named function so the recorded
// enclosing function name is observable as well, and the handler stops
// propagation so the window click counts stay unchanged.
function registerExternalScriptListener() {
  const externalTarget = document.getElementById("external-script-handler");
  externalTarget.addEventListener("click", (event) => {
    event.stopPropagation();
    document.getElementById("status").dataset.externalScriptHandlerRan = "true";
  });
}

registerExternalScriptListener();
