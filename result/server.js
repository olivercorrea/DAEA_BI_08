var express = require("express"),
  async = require("async"),
  { Pool } = require("pg"),
  cookieParser = require("cookie-parser"),
  app = express(),
  server = require("http").Server(app),
  io = require("socket.io")(server),
  path = require("path"); // Asegúrate de incluir 'path'

var port = process.env.PORT || 4000;

io.on("connection", function (socket) {
  socket.emit("message", { text: "Welcome!" });

  socket.on("subscribe", function (data) {
    socket.join(data.channel);
  });
});

var pool = new Pool({
  connectionString: "postgres://postgres:postgres@db/postgres",
});

async.retry(
  { times: 1000, interval: 1000 },
  function (callback) {
    pool.connect(function (err, client, done) {
      if (err) {
        console.error("Waiting for db");
      }
      callback(err, client);
    });
  },
  function (err, client) {
    if (err) {
      return console.error("Giving up");
    }
    console.log("Connected to db");
    getRecommendations(client);
  }
);

function getRecommendations(client) {
  client.query(
    "SELECT user_id, recommendation FROM recommendations",
    [],
    function (err, result) {
      if (err) {
        console.error("Error performing query: " + err);
      } else {
        var recommendations = result.rows;
        io.sockets.emit("recommendations", JSON.stringify(recommendations));
      }

      setTimeout(function () {
        getRecommendations(client);
      }, 1000);
    }
  );
}

app.use(cookieParser());
app.use(express.urlencoded());
app.use(express.static(__dirname + "/views"));

app.get("/", function (req, res) {
  res.sendFile(path.resolve(__dirname + "/views/index.html"));
});

server.listen(port, function () {
  var port = server.address().port;
  console.log("App running on port " + port);
});
