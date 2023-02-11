using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

    class Program {
        private static string startMask = GetFenMask(Board.DEFAULT_STARTING_FEN);

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

        static void Main(string[] args) {
            var mask = startMask;
            var cur = Board.DEFAULT_STARTING_FEN;
            var prev = cur;
            var gameId = (string)null;
            var side = 0;

            Func<int> isPossible = () => {
                var s = (string)null;
                try {
                    s = FindMoves(cur, mask);
                }
                catch {
                    return -1;
                }

                if (s == null) return 0;

                return 1;
            };

            Func<string,string> diffOp = fen => diff(fen, mask, -1 * side);

            Action<string> push = fen => {
                prev = cur;
                cur = fen;
            };

            var reset = new CmState("reset", s => { Console.WriteLine(s.name); });
            var noGame = new CmState("noGame", s => { Console.WriteLine(s.name); });
            var startGame = new CmState("startGame", s => { Console.WriteLine(s.name); });
            var wait = new CmState("wait", s => { Console.WriteLine(s.name); });
            var waitOp = new CmState("waitOp", s => { var m = FindMoves(cur, mask); push(FEN.Move(cur, m)); Console.WriteLine(m); Console.WriteLine(s.name); });
            var corOp = new CmState("corOp", s => { Console.WriteLine(s.name); });
            var err = new CmState("err", s => { Console.WriteLine(s.name); });
            var errOp = new CmState("errOp", s => { Console.WriteLine(s.name); });

            new CmGuard(reset, noGame, () => mask == startMask);
            new CmGuard(noGame, startGame, () => gameId != null);
            new CmGuard(startGame, wait, () => side == 1);
            new CmGuard(startGame, waitOp, () => side == -1);

            new CmGuard(wait, waitOp, () => isPossible() == 1);
            new CmGuard(waitOp, corOp, () => diffOp(cur) != null && diffOp(prev) == null);
            new CmGuard(corOp, wait, () => diffOp(cur) == null);

            new CmGuard(wait, err, () => isPossible() == -1);
            new CmGuard(err, waitOp, () => isPossible() == 1);
            new CmGuard(corOp, errOp, () => diffOp(cur) != null && diffOp(prev) != null);
            new CmGuard(errOp, wait, () => diffOp(cur) == null);

            CmState.cur = reset;
            reset.a();

            CmState.run();

            mask = startMask;

            CmState.run();

            gameId = "1";
            side = 1;

            CmState.run();

            mask = GetFenMask(FEN.Move(cur, "e2e4"));
            
            CmState.run();

            push(FEN.Move(cur, "e7e5"));

            CmState.run();

            mask = GetFenMask(FEN.Move(prev, "e7e5"));

            CmState.run();

            Console.WriteLine();
            Console.ReadLine();
        }
    }
}
