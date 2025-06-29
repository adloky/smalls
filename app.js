const express = require('express');
const fs = require('fs');
const os = require('os');
const axios = require('axios');
const path = require('path');

const app = express();
const apiBase = "https://cloud-api.yandex.net/v1/disk/";
const routeRe = /^\/yadisk\//;
const token = process.env.YADISK_TOKEN;

app.use(express.json());
app.use(express.urlencoded({ extended: true }));
app.use((req, res, next) => {
  res.header('Access-Control-Allow-Origin', '*'); // Разрешить все домены
  res.header('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE');
  res.header('Access-Control-Allow-Headers', true);
  next();
});

/*
app.get('/users', async (req, res) => {
    // res.json(users);
    // os.homedir();
    // JSON.stringify(user, null, 2);
    // fs.writeFile("d:/test.txt", "hello", e => { });
    
    const uploadResponse = await axios.get(
      apiBase + "resources/download",
      {
        params: {
          path: "/rw/test.txt",
        },
        headers: {
          'Authorization': "OAuth y0__xD7gJDfARjR0jggh_Dh1hP-xrev19kTtxX1_XFScprPfBKD4Q",
          'Accept': "application/json"
        }
      }
    );
    
    await axios.put(uploadResponse.data.href, "hello", {
      headers: {
        'Content-Type': 'text/plain'
      }
    });

    res.json({});
});
*/

async function diskReq(p, m, d) {
    try {
        var r = await axios.get(
            apiBase + "resources/" + (m === 'get' ? 'download' : 'upload'), {
            //apiBase + "resources/" + 'download', {
                params: { path: p, },
                headers: { 'Authorization': "OAuth " + token }
            }
        );
        
        if (m === 'get') {
            r = await axios.get(r.data.href, {
                responseType: 'text'
            });
            return r.data;
        }
        else if (m === 'put') {
            await axios.put(r.data.href, d);
        }
    }
    catch {
        return null;
    }
}

async function diskHandler(req, res, m) {
    var user = req.query.user;
    if (!user) throw new Error("User undefined!");;
    
    var p = req.path.replace(routeRe, "");
    p = path.join("/rw/", p).replaceAll("\\", "/");

    var pa = p.split("/").slice(0,-1).join("/") + "/.access";
    var acl = (await diskReq(pa, "get")).split(/\r?\n/)
        .filter(x => x.startsWith(user + " ")).find(x => true);
    if (!acl) throw new Error("Access denied!");;
    
    var name = p.split("/").at(-1);
    var allow = acl.split(" ").filter(x => x !== "").slice(1)
        .map(x => x.replace("*", "")).find(x => name.startsWith(x)) !== null;
    if (!allow) throw new Error("Access denied!");;
    
    if (m === "read") {
        var r = await diskReq(p, "get");
        if (r === null) throw new Error("File not exists!");;
        return res.send(r);
    }
    else if (m === "write") {
        if (req.body.data === undefined) throw new Error("Form param 'data' undefined!");;
        var r = await diskReq(p, "put", req.body.data);
        //if (r === null) throw new Error("Can't write file!");;
        res.send('Form submitted!');
    }
}

app.get(routeRe, async (req, res) => {
    return await diskHandler(req, res, "read");
});

app.post(routeRe, async (req, res) => {
    return await diskHandler(req, res, "write");
});


app.get("/upload", async (req, res) => {
    await diskReq("/rw/ttg/test.json", "put", "hello");
    return res.send("good");
});

app.listen(3000, () => {
  console.log('Server running on http://localhost:3000');
});