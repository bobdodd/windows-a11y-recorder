// The slice 4f timer origin fixture's external script. It schedules one timer
// as it runs and adds the second button's listener, which schedules another.
function externalTimer() {}
setTimeout(externalTimer, 600000);

document.addEventListener("DOMContentLoaded", () => {
  document.getElementById("listener-button").addEventListener("click", () => {
    setTimeout(function fromListener() {}, 600000);
    document.getElementById("status").textContent = "The listener scheduled a timer.";
  });
});
