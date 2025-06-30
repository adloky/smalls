const express = require('express');
const fs = require('fs');
const os = require('os');
const axios = require('axios');
const pathJs = require('path');

const app = express();
const apiBase = "https://cloud-api.yandex.net/v1/disk/";
const yadiskRe = /^\/yadisk\/(acl\/)?/;
const token = process.env.YADISK_TOKEN;
const cache = new Map();

app.use(express.json());
app.use(express.urlencoded({ extended: true }));
app.use((req, res, next) => {
    res.header('Access-Control-Allow-Origin', '*');
    res.header('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE');
    res.header('Access-Control-Allow-Headers', true);
    next();
});

function asyncHandler(fn) {
    return function(req, res, next) {
        Promise.resolve(fn(req, res, next)).catch(next);
    };
}

function fileMaskRe(s) {
    s = s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
        .replaceAll("\\*", ".*");
    s = "^" + s + "$";
    return new RegExp(s);
}

function httpError(code, message) {
    var e = new Error(message);
    e.status = code;
    return e;
}

async function diskReq(path, method, data) {
    const cmd = { get: 'download', post: 'upload' };
    var params = { path: path };
    if (method === 'post') {
        params.overwrite = true;
    }
    var r = await axios.get(
        apiBase + "resources/" + (cmd[method] || 'error'), {
            timeout: 60000,
            params: params,
            headers: { 'Authorization': "OAuth " + token }
        }
    );
    
    if (method === 'get') {
        r = await axios.get(r.data.href, {
            timeout: 60000,
            responseType: (path.endsWith(".json") ? 'json' : 'text')
        });
        return r.data;
    }
    else if (method === 'post') {
        await axios.put(r.data.href, data, {
            timeout: 60000
        });
    }
}

function diskPath(path) {
    path = path.replace(yadiskRe, "");
    return pathJs.join("/rw/", path).replaceAll("\\", "/");
}

async function diskAcl(path, user) {
    if (!user || user === "*") throw httpError(401, "User undefined!");;

    path = pathJs.join(path, ".access").replaceAll("\\", "/");
    if (cache.has(path)) {
        return cache.get(path);
    }
    
    var access = ""; try { access = await diskReq(path, "get"); } catch {}
    
    var acl = access.split(/\r?\n/)
        .filter(x => x.startsWith(user + " ") || x.startsWith("* "))
        .map(x => x.replace(/^[^ ]+ +/, "").trim().split(/ +/))
        .reduce(function(a, b){ return a.concat(b); }, []);
    
    var r = Array.from(new Set(acl));
    cache.set(path, r);
    
    return r;
}

async function diskHandler(req, res, op) {
    var path = diskPath(req.path);
    var route = req.path.match(yadiskRe)[0];

    pathAcl = path.replace(/[^\/]+$/, "");
    var acl = await diskAcl(pathAcl, req.query.user);
    
    if (route.endsWith("/acl/") && op === "read") {
        res.json(acl);
        return;
    }
    
    acl = acl.map(x => fileMaskRe(x));
    var name = path.split("/").at(-1);
    if (!acl.some(r => r.test(name))) throw httpError(403, "Access denied!");

    if (op === "write" && req.body.data === undefined) throw httpError(400, "Form param 'data' undefined!");

    if (op === "read") {
        var r = await diskReq(path, "get");
        res.send(r);
    }
    else if (op === "write") {
        var r = await diskReq(path, "post", req.body.data);
        res.send('Form submitted!');
    }
}

app.post("/yadisk/cache", asyncHandler(async (req, res) => {
    cache.clear();
}));

app.get(yadiskRe, asyncHandler(async (req, res) => {
    return await diskHandler(req, res, "read");
}));

app.post(yadiskRe, asyncHandler(async (req, res) => {
    return await diskHandler(req, res, "write");
}));

app.listen(3000, () => {
  console.log('Server running on http://localhost:3000');
});