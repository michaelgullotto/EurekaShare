const express = require("express");
const { AccessToken } = require("livekit-server-sdk");
const fs = require("fs");
const path = require("path");

const app = express();

const API_KEY = "devkey";
const API_SECRET = "secret";

// point this to the SAME config file the viewer launcher writes
const CONFIG_PATH = path.join(__dirname, "..", "ViewApp", "EurekaShare(Viewer)_Data", "StreamingAssets", "livekit_config.json");

function getConfig() {
    const raw = fs.readFileSync(CONFIG_PATH, "utf8");
    return JSON.parse(raw);
}

app.get("/token", async (req, res) => {
    try {
        const cfg = getConfig();

        const identity = req.query.identity || "unity";
        const name = req.query.name || identity;
        const room = req.query.room || "";
        const password = req.query.password || "";

        if (room !== cfg.roomName) {
            return res.status(403).send("Invalid room");
        }

        if (password !== cfg.password) {
            return res.status(403).send("Invalid password");
        }

        const at = new AccessToken(API_KEY, API_SECRET, {
            identity,
            name,
            ttl: "24h",
        });

        at.addGrant({
            roomJoin: true,
            room: cfg.roomName,
            canPublish: true,
            canSubscribe: true,
            canPublishData: true,
        });

        const jwt = await at.toJwt();
        res.send(jwt);
    } catch (err) {
        console.error(err);
        res.status(500).send("Failed to generate token");
    }
});

// LAN
app.listen(3000, "0.0.0.0", () => {
    console.log("Token server listening on http://0.0.0.0:3000");
});