const { app, BrowserWindow, ipcMain } = require("electron");
const path = require("path");
const fs = require("fs");
const os = require("os");
const { spawn } = require("child_process");

let splashWindow = null;
let mainWindow = null;

let livekitProcess = null;
let tokenServerProcess = null;
let viewerProcess = null;

function getLauncherRoot() {
  return __dirname;
}

function findFirstFile(folder, predicate) {
  if (!fs.existsSync(folder)) return null;

  const entries = fs.readdirSync(folder, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isFile() && predicate(entry.name)) {
      return path.join(folder, entry.name);
    }
  }

  return null;
}

function findFirstDirectory(folder, predicate) {
  if (!fs.existsSync(folder)) return null;

  const entries = fs.readdirSync(folder, { withFileTypes: true });
  for (const entry of entries) {
    if (entry.isDirectory() && predicate(entry.name)) {
      return path.join(folder, entry.name);
    }
  }

  return null;
}

function getPaths() {
  const root = getLauncherRoot();
  const localServersRoot = path.join(root, "LocalServers");

  const livekitFolder = path.join(localServersRoot, "livekit_1.9.11_windows_amd64");
  const tokenServerFolder = path.join(localServersRoot, "token-server");
  const viewAppFolder = path.join(localServersRoot, "ViewApp");

  const viewerExe = findFirstFile(
    viewAppFolder,
    (file) => file.toLowerCase().endsWith(".exe")
  );

  const viewerDataFolder = findFirstDirectory(
    viewAppFolder,
    (dir) => dir.endsWith("_Data")
  );

  const streamingAssetsFolder = viewerDataFolder
    ? path.join(viewerDataFolder, "StreamingAssets")
    : null;

  const livekitExe = findFirstFile(
    livekitFolder,
    (file) => file.toLowerCase() === "livekit-server.exe"
  );

  const tokenServerScript = path.join(tokenServerFolder, "token-server.js");
  const livekitConfigYaml = path.join(livekitFolder, "config.yaml");

  const livekitConfigPath = streamingAssetsFolder
    ? path.join(streamingAssetsFolder, "livekit_config.json")
    : null;

  const roomBroadcasterConfigPath = streamingAssetsFolder
    ? path.join(streamingAssetsFolder, "roomBroadcaster_config.json")
    : null;

  return {
    root,
    localServersRoot,
    livekitFolder,
    tokenServerFolder,
    viewAppFolder,
    viewerExe,
    viewerDataFolder,
    streamingAssetsFolder,
    livekitExe,
    tokenServerScript,
    livekitConfigYaml,
    livekitConfigPath,
    roomBroadcasterConfigPath,
  };
}

function sendLog(message) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send("launcher-log", message);
  }
}

function createSplashWindow() {
  splashWindow = new BrowserWindow({
    width: 1100,
    height: 650,
    frame: false,
    resizable: false,
    movable: true,
    backgroundColor: "#ffffff",
    show: false,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  splashWindow.loadFile(path.join(__dirname, "splash.html"));
  splashWindow.once("ready-to-show", () => splashWindow.show());
}

function createMainWindow() {
  mainWindow = new BrowserWindow({
    width: 1100,
    height: 760,
    show: false,
    backgroundColor: "#f8f8f8",
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
    },
  });

  mainWindow.loadFile(path.join(__dirname, "index.html"));

  mainWindow.webContents.on("did-finish-load", () => {
    console.log("[MAIN] main window finished load");
  });

  mainWindow.once("ready-to-show", () => {
    setTimeout(() => {
      if (splashWindow && !splashWindow.isDestroyed()) {
        splashWindow.close();
      }
      mainWindow.show();
    }, 3000);
  });
}

function getLocalIPv4() {
  const interfaces = os.networkInterfaces();

  for (const name of Object.keys(interfaces)) {
    for (const net of interfaces[name]) {
      if (net.family === "IPv4" && !net.internal && !net.address.startsWith("169.254.")) {
        return net.address;
      }
    }
  }

  for (const name of Object.keys(interfaces)) {
    for (const net of interfaces[name]) {
      if (net.family === "IPv4" && !net.internal) {
        return net.address;
      }
    }
  }

  return "127.0.0.1";
}

function readJsonSafe(filePath) {
  if (!filePath || !fs.existsSync(filePath)) {
    throw new Error(`Missing file: ${filePath}`);
  }

  const raw = fs.readFileSync(filePath, "utf8");
  return JSON.parse(raw);
}

function writeJson(filePath, data) {
  fs.writeFileSync(filePath, JSON.stringify(data, null, 2), "utf8");
}

function updateViewerConfigs(roomName, password) {
  const paths = getPaths();
  const ip = getLocalIPv4();

  if (!paths.livekitConfigPath) {
    throw new Error("Could not resolve livekit_config.json path.");
  }

  const livekitConfig = readJsonSafe(paths.livekitConfigPath);
  livekitConfig.mode = "view";
  livekitConfig.autoStart = true;
  livekitConfig.roomName = roomName;
  livekitConfig.password = password;
  livekitConfig.url = `ws://${ip}:7880`;
  livekitConfig.tokenServerUrl = `http://${ip}:3000/token`;

  writeJson(paths.livekitConfigPath, livekitConfig);
  sendLog(`Updated livekit_config.json for room '${roomName}' at ${ip}`);

  if (paths.roomBroadcasterConfigPath && fs.existsSync(paths.roomBroadcasterConfigPath)) {
    const broadcasterConfig = readJsonSafe(paths.roomBroadcasterConfigPath);
    broadcasterConfig.roomName = roomName;
    broadcasterConfig.password = password;
    broadcasterConfig.ip = ip;
    broadcasterConfig.tokenPort = 3000;
    broadcasterConfig.livekitPort = 7880;
    writeJson(paths.roomBroadcasterConfigPath, broadcasterConfig);
    sendLog("Updated roomBroadcaster_config.json");
  }

  return { ip, paths };
}

function wireChildLogs(child, prefix) {
  if (!child) return;

  if (child.stdout) {
    child.stdout.on("data", (data) => {
      const msg = data.toString().trim();
      if (msg) sendLog(`[${prefix}] ${msg}`);
    });
  }

  if (child.stderr) {
    child.stderr.on("data", (data) => {
      const msg = data.toString().trim();
      if (msg) sendLog(`[${prefix}][ERR] ${msg}`);
    });
  }

  child.on("exit", (code) => sendLog(`[${prefix}] exited with code ${code}`));
  child.on("error", (err) => sendLog(`[${prefix}][ERR] ${err.message}`));
}

function startLivekit(paths) {
  if (!paths.livekitExe || !fs.existsSync(paths.livekitExe)) {
    throw new Error("LiveKit executable not found.");
  }

  if (!paths.livekitConfigYaml || !fs.existsSync(paths.livekitConfigYaml)) {
    throw new Error("LiveKit config.yaml not found.");
  }

  if (livekitProcess && !livekitProcess.killed) {
    sendLog("LiveKit already running.");
    return;
  }

  livekitProcess = spawn(paths.livekitExe, ["--config", "config.yaml"], {
    cwd: paths.livekitFolder,
    shell: false,
    windowsHide: true,
  });

  wireChildLogs(livekitProcess, "LIVEKIT");
  sendLog("Started LiveKit server");
}

function startTokenServer(paths) {
  if (!paths.tokenServerScript || !fs.existsSync(paths.tokenServerScript)) {
    throw new Error("token-server.js not found.");
  }

  if (tokenServerProcess && !tokenServerProcess.killed) {
    sendLog("Token server already running.");
    return;
  }

  tokenServerProcess = spawn("node", ["token-server.js"], {
    cwd: paths.tokenServerFolder,
    shell: false,
    windowsHide: true,
  });

  wireChildLogs(tokenServerProcess, "TOKEN");
  sendLog("Started token server");
}

function startViewer(paths) {
  if (!paths.viewerExe || !fs.existsSync(paths.viewerExe)) {
    throw new Error("Viewer executable not found.");
  }

  if (viewerProcess && !viewerProcess.killed) {
    sendLog("Viewer already running.");
    return;
  }

  viewerProcess = spawn(paths.viewerExe, [], {
    cwd: paths.viewAppFolder,
    shell: false,
    windowsHide: false,
  });

  wireChildLogs(viewerProcess, "VIEWER");
  sendLog("Started viewer build");
}

function stopProcess(child, name) {
  if (!child || child.killed) return null;

  try {
    child.kill();
    sendLog(`Stopped ${name}`);
  } catch (err) {
    sendLog(`[${name}][ERR] ${err.message}`);
  }

  return null;
}

function stopAllProcesses() {
  viewerProcess = stopProcess(viewerProcess, "viewer");
  tokenServerProcess = stopProcess(tokenServerProcess, "token server");
  livekitProcess = stopProcess(livekitProcess, "livekit server");
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

ipcMain.handle("launcher:get-startup-info", async () => {
  const paths = getPaths();
  const ip = getLocalIPv4();
  return { ip, paths };
});

ipcMain.handle("launcher:launch", async (_event, payload) => {
  const roomName = (payload?.roomName || "").trim();
  const password = (payload?.password || "").trim();

  if (!roomName) {
    return { ok: false, error: "Room Name Is Required" };
  }

  if (!password) {
    return { ok: false, error: "Password Is Required" };
  }

  try {
    const { ip, paths } = updateViewerConfigs(roomName, password);

    sendLog(`Launcher root: ${getLauncherRoot()}`);
    sendLog(`LiveKit exe: ${paths.livekitExe}`);
    sendLog(`LiveKit config: ${paths.livekitConfigYaml}`);
    sendLog(`Token server script: ${paths.tokenServerScript}`);
    sendLog(`Viewer exe: ${paths.viewerExe}`);
    sendLog(`Viewer livekit_config.json: ${paths.livekitConfigPath}`);

    startLivekit(paths);
    await delay(1000);

    startTokenServer(paths);
    await delay(1200);

    startViewer(paths);

    return {
      ok: true,
      ip,
      roomName,
    };
  } catch (err) {
    sendLog(`[LAUNCHER][ERR] ${err.message}`);
    return { ok: false, error: err.message };
  }
});

ipcMain.handle("launcher:stop", async () => {
  stopAllProcesses();
  return { ok: true };
});

ipcMain.handle("launcher:close", async () => {
  stopAllProcesses();
  app.quit();
  return { ok: true };
});

app.whenReady().then(() => {
  createSplashWindow();
  createMainWindow();
});

app.on("before-quit", () => {
  stopAllProcesses();
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") {
    app.quit();
  }
});