const { contextBridge, ipcRenderer } = require("electron");

console.log("[PRELOAD] preload loaded");

contextBridge.exposeInMainWorld("launcherApi", {
  getStartupInfo: () => ipcRenderer.invoke("launcher:get-startup-info"),
  launch: (payload) => ipcRenderer.invoke("launcher:launch", payload),
  stop: () => ipcRenderer.invoke("launcher:stop"),
  close: () => ipcRenderer.invoke("launcher:close"),
  onLog: (callback) => ipcRenderer.on("launcher-log", (_event, message) => callback(message)),
});