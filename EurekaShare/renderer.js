console.log("[RENDERER] launcherApi =", window.launcherApi);

const mainPage = document.getElementById("mainPage");
const advancedPage = document.getElementById("advancedPage");

const ipValue = document.getElementById("ipValue");
const viewerPath = document.getElementById("viewerPath");
const roomNameInput = document.getElementById("roomName");
const passwordInput = document.getElementById("password");
const launchBtn = document.getElementById("launchBtn");
const stopBtn = document.getElementById("stopBtn");
const advancedBtn = document.getElementById("advancedBtn");
const closeBtn = document.getElementById("closeBtn");
const returnBtn = document.getElementById("returnBtn");
const statusText = document.getElementById("statusText");
const logOutput = document.getElementById("logOutput");

function setStatus(text) {
  statusText.textContent = text;
}

function appendLog(text) {
  const line = `[${new Date().toLocaleTimeString()}] ${text}`;
  logOutput.value += `${line}\n`;
  logOutput.scrollTop = logOutput.scrollHeight;
}

function showMainPage() {
  mainPage.classList.remove("hidden");
  advancedPage.classList.add("hidden");
}

function showAdvancedPage() {
  mainPage.classList.add("hidden");
  advancedPage.classList.remove("hidden");
}

async function loadStartupInfo() {
  try {
    if (!window.launcherApi) {
      setStatus("launcherApi Missing");
      appendLog("launcherApi is undefined");
      return;
    }

    const info = await window.launcherApi.getStartupInfo();
    ipValue.textContent = info.ip || "Unavailable";
    viewerPath.textContent = info.paths?.viewerExe || "Viewer exe not found";
  } catch (err) {
    setStatus("Failed To Load Startup Info");
    appendLog(`Startup info error: ${err.message}`);
    console.error(err);
  }
}

launchBtn.addEventListener("click", async () => {
  const roomName = roomNameInput.value.trim();
  const password = passwordInput.value.trim();

  setStatus("Launching...");
  appendLog(`Launch requested for room '${roomName}'`);

  try {
    if (!window.launcherApi) {
      throw new Error("launcherApi is undefined");
    }

    const result = await window.launcherApi.launch({ roomName, password });

    if (!result.ok) {
      setStatus(result.error || "Launch Failed");
      appendLog(`Launch failed: ${result.error || "Unknown error"}`);
      return;
    }

    setStatus(`Running Room '${result.roomName}' On ${result.ip}`);
    appendLog(`Launch complete for room '${result.roomName}' on ${result.ip}`);
  } catch (err) {
    setStatus("Launch Failed");
    appendLog(`Launch threw error: ${err.message}`);
    console.error(err);
  }
});

stopBtn.addEventListener("click", async () => {
  try {
    if (!window.launcherApi) {
      throw new Error("launcherApi is undefined");
    }

    await window.launcherApi.stop();
    setStatus("Stopped");
    appendLog("Stopped all managed processes");
  } catch (err) {
    setStatus("Stop Failed");
    appendLog(`Stop error: ${err.message}`);
    console.error(err);
  }
});

advancedBtn.addEventListener("click", () => {
  showAdvancedPage();
});

returnBtn.addEventListener("click", () => {
  showMainPage();
});

closeBtn.addEventListener("click", async () => {
  try {
    if (!window.launcherApi) {
      throw new Error("launcherApi is undefined");
    }

    await window.launcherApi.stop();
    await window.launcherApi.close();
  } catch (err) {
    setStatus("Close Failed");
    appendLog(`Close error: ${err.message}`);
    console.error(err);
  }
});

if (window.launcherApi) {
  window.launcherApi.onLog((message) => {
    appendLog(message);
  });
}

showMainPage();
loadStartupInfo();