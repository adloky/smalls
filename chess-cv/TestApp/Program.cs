using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Chess;

namespace TestApp {

    public class CmState {
        public static CmState cur { get; set; }
        public string name { get; }
        public List<CmGuard> guards { get; } = new List<CmGuard>();
        private Action<CmState> _a;
        
        public CmState(string n, Action<CmState> a) {
            name = n;
            _a = a;
        }

        public bool TryTrans() {
            foreach (var g in guards) {
                if (!g.TryTrans()) continue;

                cur._a(cur);
                return true;
            }
            return false;
        }

        public static void run() {
            while (cur.TryTrans());
        }

        public void a() {
            _a(this);
        }
    }

    public class CmGuard {
        private Func<bool> _f;
        private CmState _sb;

        public CmGuard(CmState sa, CmState sb, Func<bool> f) {
            _f = f;
            _sb = sb;
            sa.guards.Add(this);
        }

        public bool TryTrans() {
            var r = _f();
            if (r) {
                CmState.cur = _sb;
            };
            
            return r;
        }
    }

    public class SyncQueue<T> {
        private AutoResetEvent _waiter = new AutoResetEvent(false);
        private ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

        public void Enqueue(T item) {
            _queue.Enqueue(item);
            _waiter.Set();
        }

        public T Dequeue() {
            while (true) {
                T item;
                if (_queue.TryDequeue(out item)) return item;
                
                _waiter.WaitOne();
            }
        }
    }

    public enum CcvCommandEnum {
        game
      , fen
      , mask
    }

    public class CcvCommand {
        public CcvCommandEnum name { get; set; }
        public string id { get; set; }
        public int side { get; set; }
        public string cur { get; set; }
        public string prev { get; set; }
        public string last { get; set; }
        public string mask { get; set; }
    }

    class Program {
        private static string[] startMasks = { GetFenMask(Board.DEFAULT_STARTING_FEN)
                                             , rotateMask(GetFenMask(Board.DEFAULT_STARTING_FEN), -1) };

        private static SyncQueue<CcvCommand> cmdQue = new SyncQueue<CcvCommand>();

        public static string GetFenMask(string fen) {
            var fen0 = fen.Split(' ')[0];
            fen0 = fen0.Replace("1", ".").Replace("2", "..").Replace("3", "...").Replace("4", "....")
                .Replace("5", ".....").Replace("6", "......").Replace("7", ".......").Replace("8", "........");

            fen0 = Regex.Replace(fen0, "[pnbrqk]", "b");
            fen0 = Regex.Replace(fen0, "[PNBRQK]", "w");

            return fen0;
        }

        public static string FindMoves(string fen, string mask) {
            Func<Point, string> getSquare = (p) => "" + "abcdefgh"[p.X] + "87654321"[p.Y];

            var fenMask = GetFenMask(fen);

            var a = fenMask.Split('/');
            var b = mask.Split('/');

            var ps = new List<Point>();
            for (var y = 0; y < 8; y++) {
                for (var x = 0; x < 8; x++) {
                    if (a[y][x] != b[y][x]) {
                        ps.Add(new Point(x, y));
                    }
                }
            }

            if (ps.Count == 0) {
                return null;
            }

            if (ps.Count == 2) {
                ps = ps.OrderBy(p => b[p.Y][p.X]).ToList();
                var bcs = string.Join("", ps.Select(p => b[p.Y][p.X]));
                if (bcs != ".w" && bcs != ".b" && bcs != "..") {
                    throw new Exception("Find move error.");
                }

                if (bcs == "..") {
                    goto twoMoves;
                }

                return getSquare(ps[0]) + getSquare(ps[1]);
            }
            else if (ps.Count == 3) {
                var xMin = ps.Select(p => p.X).Min();
                var xMax = ps.Select(p => p.X).Max();
                var yMin = ps.Select(p => p.Y).Min();
                var yMax = ps.Select(p => p.Y).Max();

                if (xMax - xMin != 1 || yMax - yMin != 1) {
                    goto twoMoves;
                }

                var ptrnA = "xxxx".ToArray();
                foreach (var p in ps) {
                    var i = (p.X - xMin) + (p.Y - yMin) * 2;
                    ptrnA[i] = b[p.Y][p.X];
                }
                var ptrn = string.Join("", ptrnA);

                if (yMin == 2) {
                    if (ptrn == "xw..") {
                        return getSquare(ps[1]) + getSquare(ps[0]);
                    }
                    else if (ptrn == "wx..") {
                        return getSquare(ps[2]) + getSquare(ps[0]);
                    }
                    else {
                        goto twoMoves;
                    }
                }
                else if (yMin == 4) {
                    if (ptrn == "..xb") {
                        return getSquare(ps[0]) + getSquare(ps[2]);
                    }
                    else if (ptrn == "..bx") {
                        return getSquare(ps[1]) + getSquare(ps[2]);
                    }
                    else {
                        goto twoMoves;
                    }
                }
                else {
                    goto twoMoves;
                }
            }
            else if (ps.Count == 4) {
                var ys = ps.Select(p => p.Y).Distinct().ToArray();
                if (ys.Length > 1 || (ys[0] != 0 && ys[0] != 7)) {
                    goto twoMoves;
                }
                var y = ys[0];
                var xs = string.Join("", ps.Select(p => p.X.ToString()));
                if (xs != "0234" && xs != "4567") {
                    goto twoMoves;
                }

                var bcs = string.Join("", ps.Select(p => b[p.Y][p.X]));
                if (bcs != ".ww." && bcs != ".bb.") {
                    goto twoMoves;
                }

                var targetX = (ps[0].X == 0) ? 2 : 6;

                return getSquare(new Point(4, y)) + getSquare(new Point(targetX, y));
            }
            else {
                throw new Exception($"Find move diff count is {ps.Count}.");
            }

        twoMoves:
            throw new Exception("Find move error.");
        }

        public static string rotateMask(string mask, int side) {
            if (side != -1) return mask;

            return string.Join("", mask.Reverse());
        }

        public static string diff(string fen, string mask, int side = 0) {
            Func<Point, string> getSquare = (p) => "" + "abcdefgh"[p.X] + "87654321"[p.Y];

            var fenMask = GetFenMask(fen);

            if (side == 1) {
                mask = mask.Replace("b", ".");
                fenMask = fenMask.Replace("b", ".");
            }
            else if (side == -1) {
                mask = mask.Replace("w", ".");
                fenMask = fenMask.Replace("w", ".");
            }

            var a = fenMask.Split('/');
            var b = mask.Split('/');

            var ps = new List<Point>();
            for (var y = 0; y < 8; y++) {
                for (var x = 0; x < 8; x++) {
                    if (a[y][x] != b[y][x]) {
                        ps.Add(new Point(x, y));
                    }
                }
            }

            if (ps.Count == 0) return null;

            return string.Join("", ps.Select(p => getSquare(p)));
        }

        public static int isPossibleMove(string fen, string mask) {
            var m = (string)null;
            try {
                m = FindMoves(fen, mask);
                if (m == null) return 0;

                FEN.Move(fen,m);
            }
            catch {
                return -1;
            }

            return 1;
        }

        public static string simpleMaskMove(string mask, string move) {
            Func<string, Point> getPoint = x => new Point("abcdefgh".IndexOf(x[0]), "87654321".IndexOf(x[1]));

            var ms = mask.Split('/').Select(x => new StringBuilder(x)).ToArray();
            var src = getPoint(move);
            var dst = getPoint(move.Substring(2));
            ms[dst.Y][dst.X] = ms[src.Y][src.X];
            ms[src.Y][src.X] = '.';

            return string.Join("/", ms.Select(x => x.ToString()));
        }

        public static void commandThread() {
            var fen = Board.DEFAULT_STARTING_FEN;
            var mask = startMasks[0];
            var side = 1;
            while (true) {
                var split = Console.ReadLine().Split(' ');
                var name = split[0];
                var args = split.Skip(1).ToArray();

                var cmd = new CcvCommand();
                switch (name) {
                    case "game":
                        cmd.name = CcvCommandEnum.game;
                        if (args[0] != "null") {
                            cmd.id = args[0];
                        }
                        else {
                            fen = Board.DEFAULT_STARTING_FEN;
                            mask = startMasks[0];
                        }

                        if (args.Length > 1) {
                            side = int.Parse(args[1]);
                            cmd.side = side;
                        }

                        break;

                    case "fen":
                        cmd.name = CcvCommandEnum.fen;
                        foreach (var arg in args) {
                            cmd.prev = fen;
                            cmd.last = arg;
                            fen = FEN.Move(fen, arg);
                            cmd.cur = fen;
                        }
                        break;

                    case "mask":
                        cmd.name = CcvCommandEnum.mask;
                        foreach (var arg in args) {
                            mask = simpleMaskMove(mask, arg);
                        }

                        cmd.mask = rotateMask(mask, side);

                        break;
                }

                cmdQue.Enqueue(cmd);
            }
        }

        public static string Curl(string url, string method = "GET", int timeout = int.MaxValue) {
            var request = WebRequest.Create(url);
            request.Method = method;
            if (timeout != int.MaxValue) {
                request.Timeout = timeout;
            }
            
            WebResponse response = null;
            try {
                response = request.GetResponse();
            }
            catch (WebException e) {
                response = e.Response;
                // ((HttpWebResponse)e.Response).StatusCode
            }

            if (response == null) {
                return null;
            }

            string result;
            using (Stream dataStream = response.GetResponseStream()) {
                var reader = new StreamReader(dataStream);
                result = reader.ReadToEnd();
            }

            response.Close();

            return result;
        }

        private static Task lastSendSquareTask = Task.Run(() => { });
        private static CancellationTokenSource delayTaskCts = new CancellationTokenSource();

        private static void sendSquares(string s, int timeout = int.MaxValue) {
            delayTaskCts.Cancel();
            lastSendSquareTask = lastSendSquareTask.ContinueWith(t => {
                Console.WriteLine(s);
            });

            if (timeout == int.MaxValue) return;

            delayTaskCts.Dispose();
            delayTaskCts = new CancellationTokenSource();

            lastSendSquareTask = lastSendSquareTask.ContinueWith(async t => {
                await Task.Delay(timeout, delayTaskCts.Token);
                if (delayTaskCts.IsCancellationRequested) return;
                Console.WriteLine("clear");
            });
        }

        static void Main(string[] args) {
            while (true) {
                var s = Console.ReadLine();
                sendSquares(s,2000);
            }

            return;

            var mask = (string)null;
            var cur = (string)null;
            var prev = (string)null;
            var last = (string)null;
            var gameId = (string)null;
            var side = 1;

            Func<int> isPossible = () => isPossibleMove(cur,mask);
            Func<string,string> diffOp = fen => diff(fen, mask, -1 * side);
            Action<string> push = fen => { prev = cur; cur = fen; };

            var reset = new CmState("reset", s => {
                mask = startMasks[0].Replace("w", ".").Replace("b", ".");
                cur = Board.DEFAULT_STARTING_FEN;
                prev = cur;
                last = null;
                gameId = null;
                Console.WriteLine(s.name);
            });

            var noGame = new CmState("noGame", s => { side = (mask[0] == 'b') ? 1 : -1; Console.WriteLine(s.name); });
            var startGame = new CmState("startGame", s => { Console.WriteLine(s.name); });
            var wait = new CmState("wait", s => { Console.WriteLine(s.name); });

            var waitOp = new CmState("waitOp", s => {
                var m = FindMoves(cur, mask);
                if (m != null) {
                    push(FEN.Move(cur, m));
                }
                Console.WriteLine(s.name);
            });

            var corOp = new CmState("corOp", s => { Console.WriteLine(s.name); });
            var err = new CmState("err", s => { Console.WriteLine(s.name); });
            var errOp = new CmState("errOp", s => { Console.WriteLine(s.name); });

            new CmGuard(reset, noGame, () => startMasks.Contains(mask));
            new CmGuard(noGame, startGame, () => gameId != null);
            new CmGuard(startGame, wait, () => side == 1);
            new CmGuard(startGame, waitOp, () => side == -1);

            new CmGuard(wait, waitOp, () => isPossible() == 1);
            new CmGuard(corOp, waitOp, () => isPossible() == 1);
            new CmGuard(waitOp, corOp, () => diffOp(cur) != null && diffOp(prev) == null);
            new CmGuard(corOp, wait, () => diffOp(cur) == null);

            new CmGuard(wait, err, () => isPossible() == -1);
            new CmGuard(err, waitOp, () => isPossible() == 1);
            new CmGuard(corOp, errOp, () => diffOp(cur) != null && diffOp(prev) != null && isPossible() != 1);
            new CmGuard(errOp, wait, () => diffOp(cur) == null);

            (new Thread(commandThread)).Start();

            CmState.cur = reset;
            reset.a();
            CmState.run();

            while (true) {
                var cmd = cmdQue.Dequeue();

                switch (cmd.name) {
                    case CcvCommandEnum.game:
                        if (cmd.id == null) {
                            CmState.cur = reset;
                            reset.a();
                            break;
                        }

                        gameId = cmd.id;
                        side = cmd.side;

                        break;

                    case CcvCommandEnum.fen:
                        cur = cmd.cur;
                        prev = cmd.prev;
                        last = cmd.last;

                        break;

                    case CcvCommandEnum.mask:
                        mask = rotateMask(cmd.mask, side);
                        break;
                }

                CmState.run();
            }
        }
    }
}
