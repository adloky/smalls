const express = require('express');
const fs = require('fs');
const os = require('os');

const app = express();

// Middleware для парсинга JSON
app.use(express.json());

// "База данных" (для примера)
let users = [
  { id: 1, name: "Alice" },
  { id: 2, name: "Bob" }
];

// GET /users — получить всех пользователей
app.get('/users', (req, res) => {
  res.json(users);
  // os.homedir();
  // JSON.stringify(user, null, 2);
  // fs.writeFile("d:/test.txt", "hello", e => { });
  
});

// GET /users/:id — получить пользователя по ID
app.get('/users/:id', (req, res) => {
  const user = users.find(u => u.id === parseInt(req.params.id));
  if (!user) return res.status(404).json({ error: "User not found" });
  res.json(user);
});

// POST /users — создать пользователя
app.post('/users', (req, res) => {
  const newUser = { id: users.length + 1, name: req.body.name };
  users.push(newUser);
  res.status(201).json(newUser);
});

// PUT /users/:id — обновить пользователя
app.put('/users/:id', (req, res) => {
  const user = users.find(u => u.id === parseInt(req.params.id));
  if (!user) return res.status(404).json({ error: "User not found" });
  user.name = req.body.name;
  res.json(user);
});

// DELETE /users/:id — удалить пользователя
app.delete('/users/:id', (req, res) => {
  users = users.filter(u => u.id !== parseInt(req.params.id));
  res.status(204).send(); // 204 = No Content
});

// Запуск сервера
app.listen(3000, () => {
  console.log('Server running on http://localhost:3000');
});