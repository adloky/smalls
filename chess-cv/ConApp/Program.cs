using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.XPhoto;
using SharpDX.MediaFoundation;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;
using Owin;
using Microsoft.Owin.Cors;
using System.Net;
using System.IO;
using Chess;
using System.Text.RegularExpressions;
using System.Xml.XPath;
using System.Diagnostics;
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace ConApp
{
    public static class OpenCvExtensions {
        public static Scalar Add(this Scalar val, Scalar valAdd) {
            double add(double a, double b) => Math.Min(255, Math.Max(0, a + b));
            val.Val0 = add(val.Val0, valAdd.Val0);
            val.Val1 = add(val.Val1, valAdd.Val1);
            val.Val2 = add(val.Val2, valAdd.Val2);
            val.Val3 = add(val.Val3, valAdd.Val3);

            return val;
        }

        public static Scalar HsvAdd(this Scalar val, Scalar valAdd) {
            double add(double a, double b) => Math.Min(255, Math.Max(0, a + b));
            double addHue(double a, double b) => (a + b + 180) % 180;

            val.Val0 = addHue(val.Val0, valAdd.Val0);
            val.Val1 = add(val.Val1, valAdd.Val1);
            val.Val2 = add(val.Val2, valAdd.Val2);
            val.Val3 = add(val.Val3, valAdd.Val3);

            return val;
        }
    }

    #region state machine classes

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
        none
      , game
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
        public string err { get; set; }
    }

    #endregion

    public class Program
    {
        #region ui range

        public static object[] uiValues = new object[6];
        public static object[] uiNames = new string[] { "hl", "hh", "sl", "sh", "vl", "vh" };

        public static int uiVal(string name, int val) {
            var i = Array.IndexOf(uiNames, name);
            if (i < 0) return 0;

            if (uiValues[i] == null) uiValues[i] = val;
            return (int)uiValues[i];
        }

        #endregion

        #region recognize

        public static void SafeInRange(Mat src, Scalar lowerb, Scalar upperb, Mat dst) {
            if (lowerb.Val0 <= upperb.Val0) {
                Cv2.InRange(src, lowerb, upperb, dst);
            }
            else {
                var dst2 = new Mat();
                Cv2.InRange(src, lowerb, new Scalar(179, upperb.Val1, upperb.Val2), dst);
                Cv2.InRange(src, new Scalar(0, lowerb.Val1, lowerb.Val2), upperb, dst2);
                Cv2.BitwiseOr(dst, dst2, dst);
            }
        }

        public static Scalar FindNearestColor(Mat src, Scalar baseColor) {
            var resultColor = new Scalar();
            var minRedDist = double.MaxValue;

            for (var j = 0; j < src.Height; j++) {
                for (var i = 0; i < src.Width; i++) {
                    var cv3 = src.At<Vec3b>(j, i);
                    var c = new Scalar(cv3[0], cv3[1], cv3[2]);
                    var dist = ColorDistanсe(baseColor, c);
                    if (dist < minRedDist) {
                        resultColor = c;
                        minRedDist = dist;
                    }
                }
            }

            return resultColor;
        }

        public static Scalar ScalarBGR2HSV(Scalar s) {
            Mat bgr = new Mat(1, 1, MatType.CV_8UC3, s);
            Mat hsv = new Mat();
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
            var v = hsv.Get<Vec3b>(0, 0);

            return new Scalar(v[0], v[1], v[2]);
        }

        class GrayLabel {
            public byte gray = 255;
            public char label = '.';
        }

        static void FindPieceColors(GrayLabel[] gl) {
            Mat labels = new Mat();
            var points = new Mat(gl.Length, 1, MatType.CV_32FC1);
            var centers = new Mat(2, 1, points.Type());
            for (var i = 0; i < gl.Length; i++) {
                points.Set<float>(i, gl[i].gray);
            }

            var criteria = new TermCriteria(type: CriteriaTypes.Eps | CriteriaTypes.MaxIter, maxCount: 10, epsilon: 1.0);
            Cv2.Kmeans(data: points, k: 2, bestLabels: labels, criteria: criteria, attempts: 10, flags: KMeansFlags.PpCenters, centers: centers);

            var labelNames = centers.Get<float>(0) > centers.Get<float>(1) ? "wb" : "bw";
            for (var i = 0; i < gl.Length; i++) {
                gl[i].label = labelNames[labels.Get<int>(i)];
            }
        }

        public static Point FindNearestPoint(Point sp, Point[] ps) {
            var minDest = int.MaxValue;
            var result = new Point(0, 0);
            foreach (var p in ps) {
                var dx = p.X - sp.X;
                var dy = p.Y - sp.Y;
                var dest = (int)Math.Sqrt(dx * dx + dy * dy);
                if (dest < minDest) {
                    result = p;
                    minDest = dest;
                }
            }

            return result;
        }

        public static VideoCapture CreateVideoCapture(int n) {
            var mat = new Mat();
            var capture = new VideoCapture(n);
            for (var i = 0; i < 5; i++) {
                capture.Read(mat);
                Cv2.WaitKey(50);
            }
            return capture;
        }

        public static IEnumerable<Point> SquarePoints(Point p, int size, int inc = 1) {
            var incSize = size * inc;
            for (var y = 0; y < incSize; y += inc) {
                for (var x = 0; x < incSize; x += inc) {
                    yield return new Point(p.X + x, p.Y + y);
                }
            }
        }

        public static void SquareStats(Mat img, Point sp, int size, out Scalar avgColor, out double perc) {
            var sum = new Scalar(0, 0, 0);
            var count = 0;

            for (var y = sp.Y; y < sp.Y + size; y++) {
                for (var x = sp.X; x < sp.X + size; x++) {
                    var c = img.At<Vec3b>(y, x);
                    if (c[0] == 255 && c[1] == 255 && c[2] == 255) {
                        continue;
                    }
                    sum.Val0 += c[0];
                    sum.Val1 += c[1];
                    sum.Val2 += c[2];

                    count++;
                }
            }
            avgColor = new Scalar(0, 0, 0);
            avgColor.Val0 = sum.Val0 / count;
            avgColor.Val1 = sum.Val1 / count;
            avgColor.Val2 = sum.Val2 / count;
            perc = (double)count / (size * size);
        }

        public static Scalar SquareAvgColor(Mat img, Point sp, int size) {
            Scalar avgColor;
            double perc;
            SquareStats(img, sp, size, out avgColor, out perc);
            return avgColor;
        }

        public static double ColorDistanсe(Scalar a, Scalar b) {
            var d0 = a.Val0 - b.Val0;
            var d1 = a.Val1 - b.Val1;
            var d2 = a.Val2 - b.Val2;
            return Math.Sqrt(d0 * d0 + d1 * d1 + d2 * d2);
        }

        public static IEnumerable<Point> SquareContour(Point p, int size, int tail = 0) {
            var l = size - 1;
            for (var i = 0; i < l; i++) {
                yield return new Point(p.X + i, p.Y);
            }

            for (var i = 0; i < l; i++) {
                yield return new Point(p.X + l, p.Y + i);
            }

            for (var i = l; i > 0; i--) {
                yield return new Point(p.X + i, p.Y + l);
            }

            for (var i = l; i > 0; i--) {
                yield return new Point(p.X, p.Y + i);
            }

            for (var i = 0; i < tail; i++) {
                yield return new Point(p.X + 1 + i, p.Y);
            }
        }

        private static IEnumerable<Point> rotate90(Point p, int size) {
            for (var i = 0; i < 4; i++) {
                yield return p;
                p = new Point(size - p.Y, p.X);
            }
        }

        private static Size getSize(IEnumerable<Point> ps) {
            var minX = int.MaxValue;
            var maxX = int.MinValue;
            var minY = int.MaxValue;
            var maxY = int.MinValue;

            foreach (var p in ps) {
                if (p.X < minX) { minX = p.X; }
                if (p.X > maxX) { maxX = p.X; }
                if (p.Y < minY) { minY = p.Y; }
                if (p.Y > maxY) { maxY = p.Y; }
            }

            return new Size(maxX - minX, maxY - minY);
        }

        private static CircleSegment[] getCircles(Mat img, int minRadius, int maxRadius) {
            if (img.Channels() > 1) {
                var imgGray = new Mat();
                Cv2.CvtColor(img,imgGray, ColorConversionCodes.BGR2HSV);
                img = imgGray;
            }
            else {
                img = img.Clone();
            }
            
            Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(img, out contours, out hierarchy, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            // filter by size
            contours = contours.Where(ps => {
                var sz = getSize(ps);
                var si = (new int[] { sz.Width / 2, sz.Height / 2 }).OrderBy(x => x).ToArray();
                return si[0] >= minRadius && si[1] <= maxRadius;
            }).ToArray();

            List<List<Point>> pointListList = new List<List<Point>>();
            foreach (var contour in contours) {
                var hull = Cv2.ConvexHull(contour);
                pointListList.Clear();
                pointListList.Add(hull.ToList());
                Cv2.FillPoly(img, pointListList, new Scalar(255));
            }

            var detectorParams = new SimpleBlobDetector.Params {
                FilterByArea = true,
                MinArea = (int)(minRadius * minRadius * 3.14),
                MaxArea = (int)(maxRadius * maxRadius * 3.14),

                FilterByCircularity = true,
                MinCircularity = 0.9f,

                FilterByConvexity = false,
                MinConvexity = 0.9f,

                FilterByInertia = true,
                MinInertiaRatio = 0.9f,

                FilterByColor = false
            };
            var detector = SimpleBlobDetector.Create(detectorParams);
            var keyPoints = detector.Detect(img);

            var circles = keyPoints.Select(kp => new CircleSegment(kp.Pt.ToPoint(), (int)(kp.Size / 2))).ToArray();

            /*
            if (circles.Length != 4) {
                foreach (var circle in circles) {
                    Cv2.Circle(img, circle.Center.ToPoint(), (int)circle.Radius + 5, new Scalar(255));
                }
                new Window("img2", img);
                Cv2.WaitKey();
            }
            */

            return circles;
        }

        public static string recognizeBoard(Mat src) {
            // Resize the image

            Mat img;
            if (src.Height > 720)
            {
                var imgHeight = 720;
                var imgWidth = (int)(((float)src.Width) / src.Height * imgHeight);
                img = src.Resize(new Size(imgWidth, imgHeight));
            }
            else {
                img = src.Clone();
            }

            // Red threshold
            Mat imgSmall;
            {
                var imgHeight = 100;
                var imgWidth = (int)(((float)src.Width) / src.Height * imgHeight);
                imgSmall = src.Resize(new Size(imgWidth, imgHeight), interpolation: InterpolationFlags.Linear);
            }

            var redColor = FindNearestColor(imgSmall, new Scalar(140, 70, 200));

            var imgHsv = new Mat();
            Cv2.CvtColor(img, imgHsv, ColorConversionCodes.BGR2HSV);
            var redHsv = ScalarBGR2HSV(redColor);
            var mask = new Mat();
            var mask2 = new Mat();
            SafeInRange(imgHsv,
                redHsv.HsvAdd(new Scalar(-15, -80, -80)),
                redHsv.HsvAdd(new Scalar(+15, +80, +80)),
                mask);

            var imgGray = new Mat();
            Cv2.CvtColor(img, imgGray, ColorConversionCodes.BGR2GRAY);
            Cv2.AdaptiveThreshold(imgGray, mask2, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, 21, 5);
            Cv2.BitwiseAnd(mask,mask2,mask);
            //new Window("mask", mask);
            //Cv2.WaitKey(1);

            // Circles
            Cv2.GaussianBlur(mask, mask, new Size(7, 7), 0);
            SafeInRange(mask, new Scalar(128, 128, 128), new Scalar(255, 255, 255), mask);
            var circles = getCircles(mask, img.Height / 240, img.Height / 32);

            foreach (var circle in circles) {
                Cv2.Circle(mask, circle.Center.ToPoint(), (int)circle.Radius + 5, new Scalar(255, 255, 255)); // ***
            }
            
            if (circles.Length != 4) {
                throw new Exception("bad_reds");
            }

            // Rotation
            circles = circles.OrderBy(x => x.Center.X).ToArray();
            var rotation = Math.Abs(circles[0].Center.X - circles[1].Center.X) / Math.Abs(circles[0].Center.Y - circles[1].Center.Y);
            if (rotation > 0.5) {
                throw new Exception("bad_rot");
            }

            // Correct points
            Point2f[] srcTrans = new Point2f[4];
            var cirPoints = circles.Select(x => x.Center.ToPoint()).ToArray();
            srcTrans[0] = FindNearestPoint(new Point(0, 0), cirPoints);
            srcTrans[1] = FindNearestPoint(new Point(0, img.Rows), cirPoints);
            srcTrans[2] = FindNearestPoint(new Point(img.Cols, 0), cirPoints);
            srcTrans[3] = FindNearestPoint(new Point(img.Cols, img.Rows), cirPoints);

            // Transform
            var size = 400;
            var sqs = size / 10;

            Point2f[] dstTrans = new Point2f[4];

            dstTrans[0] = new Point2f(sqs / 2, sqs / 2);
            dstTrans[1] = new Point2f(sqs / 2, size - sqs / 2);
            dstTrans[2] = new Point2f(size - sqs / 2, sqs / 2);
            dstTrans[3] = new Point2f(size - sqs / 2, size - sqs / 2);
            var warp = Cv2.GetPerspectiveTransform(srcTrans, dstTrans);
            Cv2.WarpPerspective(img, img, warp, new Size(size, size));

            // Test contour
            Cv2.CvtColor(img, imgGray, ColorConversionCodes.BGR2GRAY);

            var black = rotate90(new Point(3, 3), size)
                .Select(p => (int)SquareAvgColor(imgGray, p.Add(new Point(-3, -3)), 6).Val0)
                .OrderByDescending(x => x).Skip(1).First();

            Cv2.InRange(imgGray, new Scalar(0), new Scalar(black + 50), mask);

            var blackCount = 0;
            var conturQue = new Queue<byte>();
            foreach (var p in SquareContour(new Point(0,0), size)) {
                var c = mask.At<byte>(p.Y,p.X);
                conturQue.Enqueue(c);
                if (c == 0) blackCount++;
                if (conturQue.Count > sqs) {
                    if (conturQue.Dequeue() == 0) blackCount--;
                }

                if (blackCount > sqs / 4) {
                    throw new Exception("bad_contour");
                }
            }

            // rotate to white side
            var colorRotate = rotate90(new Point(sqs * 0.5, size - sqs * 1.5), size)
                .Select(p => new Point(SquareAvgColor(imgGray, p.Add(new Point(-3, -3)), 6).Val0, 0)).ToArray();

            colorRotate[0].Y = -1;
            colorRotate[1].Y = (int)RotateFlags.Rotate90Counterclockwise;
            colorRotate[2].Y = (int)RotateFlags.Rotate180;
            colorRotate[3].Y = (int)RotateFlags.Rotate90Clockwise;
            var rotate = colorRotate.OrderByDescending(x => x.X).First().Y;
            if (rotate > -1) {
                Cv2.Rotate(img, img, (RotateFlags)rotate);
            }

            // Squares mask
            Cv2.CvtColor(img, imgHsv, ColorConversionCodes.BGR2HSV);
            var squareColor = rotate90(new Point(sqs * 0.5, sqs * 1.5), size)
                .Select(p => SquareAvgColor(imgHsv, p.Add(new Point(-3, -3)), 6))
                .Aggregate(new Scalar(0,0,0), (a,b) => new Scalar(a.Val0 + b.Val0 / 4, a.Val1 + b.Val1 / 4, a.Val2 + b.Val2 / 4));

            Cv2.InRange(imgHsv,
                squareColor.HsvAdd(new Scalar(uiVal("hl", -20), uiVal("sl", -100), uiVal("vl", -70))),
                squareColor.HsvAdd(new Scalar(uiVal("hh", +25), uiVal("sh", +255), uiVal("vh", +100))),
                mask);    

            mask2 = new Mat(mask.Size(), mask.Type(), new Scalar());
            Cv2.Rectangle(mask2, new Rect(sqs, sqs, size - 2 * sqs, size - 2 * sqs), new Scalar(255), -1);
            Cv2.BitwiseAnd(mask, mask2, mask);

            Cv2.CvtColor(mask, mask, ColorConversionCodes.GRAY2BGR);
            Cv2.BitwiseOr(img, mask, img);

            // hide leds
            for (var y = 1; y < 10; y++) {
                for (var x = 1; x < 10; x++) {
                    Cv2.Circle(img, new Point(sqs * x, sqs * y), sqs/6, new Scalar(255, 255, 255), -1);
                }
            }

            new Window("img2", img);
            Cv2.WaitKey(1);

            // Calc pieces
            imgGray = new Mat();
            Cv2.CvtColor(img, imgGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(imgGray, imgGray, ColorConversionCodes.GRAY2BGR);
            var grayLabels = new List<GrayLabel>();
            // var percs = new List<double>(); // ***
            foreach (var p in SquarePoints(new Point(sqs, sqs), 8, sqs)) {
                Scalar avgColor;
                double perc;
                SquareStats(imgGray, p + new Point(sqs / 8, sqs / 8), sqs - sqs / 8 * 2, out avgColor, out perc);
                var gl = new GrayLabel();
                // if (perc != 0) percs.Add(perc); // ***
                if (perc >= 0.16) {
                    gl.gray = (byte)((int)avgColor.Val0);
                }
                grayLabels.Add(gl);
            }

            // foreach (var perc in percs.OrderBy(x => x)) Console.WriteLine(perc); // ***
            // Cv2.WaitKey();

            var pieceGrayLabels = grayLabels.Where(x => x.gray < 255).ToArray();
            FindPieceColors(pieceGrayLabels);

            // Build result
            var resultSb = new StringBuilder();
            var sqi = 0;
            foreach (var gl in grayLabels) {
                if (sqi > 0 && sqi % 8 == 0) {
                    resultSb.Append("/");
                }
                sqi++;
                resultSb.Append(gl.label);
            }

            return resultSb.ToString();
        }

        #endregion

        #region lichess

        public static string curl(string url, string method = "GET", int timeout = int.MaxValue) {
            var request = WebRequest.Create(url);
            request.Method = method;
            request.Headers.Add("Authorization: Bearer " + token);
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

        public static StreamReader GetHttpReader(string url) {
            var request = WebRequest.Create(url);
            request.Headers.Add("Authorization: Bearer " + token);
            request.Timeout = 1000;
            var reader = (StreamReader)null;
            try {
                var response = request.GetResponse();
                var dataStream = response.GetResponseStream();
                reader = new StreamReader(dataStream);
            } catch { }

            return reader;
        }

        public static bool move(string gameId, string move) {
            var s = curl($"https://lichess.org/api/board/game/{gameId}/move/{move}", "POST");
            var isSuccess = s.IndexOf(@"""ok"":true") > -1;
            if (!isSuccess) {
                Console.WriteLine(s);
            }
            return isSuccess;
        }
        public static void lichessThread() {
            var dt = DateTime.Now.AddMilliseconds(-1000);
            while (true) {
                var dtDiff = (DateTime.Now - dt).TotalMilliseconds;
                Thread.Sleep(Math.Max(1, 1000 - (int)dtDiff));
                dt = DateTime.Now;
                var eventReader = GetHttpReader("https://lichess.org/api/stream/event");
                if (eventReader == null) {
                    continue;
                }
                using (eventReader) {
                    while (true) {
                        var eventStr = (string)null;
                        try {
                            eventStr = eventReader.ReadLine();
                        } catch {
                            break;
                        }
                        if (eventStr == "") {
                            continue;
                        }
                        var eventXml = JsonHelper.JsonToXml(eventStr);
                        var eventType = eventXml.XPathSelectElement("type").Value;
                        if (eventType == "gameStart") {
                            var gameId = eventXml.XPathSelectElement("game/gameId").Value;
                            cmdQue.Enqueue(new CcvCommand() {
                                name = CcvCommandEnum.game
                              , id = gameId
                              , side = (eventXml.XPathSelectElement("game/color").Value == "white") ? 1 : -1
                            });

                            var stateReader = GetHttpReader($"https://lichess.org/api/board/game/stream/{gameId}");
                            if (stateReader == null) {
                                continue;
                            }

                            while (true) {
                                var stateStr = (string)null;
                                try {
                                    stateStr = stateReader.ReadLine();
                                } catch {
                                    break;
                                }

                                if (stateStr == "") {
                                    continue;
                                }

                                if (stateStr == null) {
                                    break;
                                }

                                var stateXml = JsonHelper.JsonToXml(stateStr);
                                var stateType = stateXml.XPathSelectElement("type").Value;
                                if (stateType != "gameState" && stateType != "gameFull") {
                                    continue;
                                }
                                var movesPath = (stateType == "gameFull") ? "state/moves" : "moves";
                                var movesStr = stateXml.XPathSelectElement(movesPath).Value;


                                // fen command
                                var cmd = new CcvCommand() { name = CcvCommandEnum.fen
                                    , cur = Board.DEFAULT_STARTING_FEN
                                    , prev = Board.DEFAULT_STARTING_FEN
                                };

                                var board = Board.Load();
                                var moves = movesStr.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var move in moves) {
                                    board.Move(move);
                                    cmd.prev = cmd.cur;
                                    cmd.cur = board.GetFEN();
                                    cmd.last = move;
                                }

                                // normalize castling move
                                if (cmd.last == "e1h1" || cmd.last == "e1a1" || cmd.last == "e8h8" || cmd.last == "e8a8") {
                                    var san = FEN.Uci2San(cmd.prev, cmd.last);
                                    cmd.last = FEN.San2Uci(cmd.prev, san);
                                }

                                cmdQue.Enqueue(cmd);
                            }
                        }
                        else if (eventType == "gameFinish") {
                            cmdQue.Enqueue(new CcvCommand() { name = CcvCommandEnum.game });;
                        }
                    }
                }
            }

        }

        #endregion

        #region find move

        public static string GetFenMask(string fen) {
            var fen0 = fen.Split(' ')[0];
            fen0 = fen0.Replace("1", ".").Replace("2", "..").Replace("3", "...").Replace("4", "....")
                .Replace("5", ".....").Replace("6", "......").Replace("7", ".......").Replace("8", "........");

            fen0 = Regex.Replace(fen0, "[pnbrqk]", "b");
            fen0 = Regex.Replace(fen0, "[PNBRQK]", "w");

            return fen0;
        }

        public static string FindMoves2Captures(string fen, string white, string black) {
            var squareStrs = (fen.IndexOf(" w ") > -1) ? new string[] { white, black } : new string[] { black, white };
            var sources = squareStrs.Select(x => (Square)Enum.Parse(typeof(Square), x.ToUpper())).ToArray();
            var board = Board.Load(fen);
            var targets = board[sources[0]].GetValidMoves().Where(x => x.CapturedPiece != null).Select(x => x.Target).ToArray();
            var validTargetList = new List<Square>();
            foreach (var target in targets) {
                board = Board.Load(fen);
                if (board.Move(sources[0], target, typeof(Chess.Pieces.Queen))
                 && board.Move(sources[1], target, typeof(Chess.Pieces.Queen)))
                {
                    validTargetList.Add(target);
                }
            }

            if (validTargetList.Count != 1) {
                throw new Exception("Find move error.");
            }

            var targetStr = validTargetList[0].ToString().ToLower();

            return squareStrs[0] + targetStr + " " + squareStrs[1] + targetStr;
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
                        ps.Add(new Point(x,y));
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

            if (ps.Count == 2) {
                ps = ps.OrderByDescending(p => a[p.Y][p.X]).ToList();
                var srcPtrn = string.Join("", ps.Select(p => a[p.Y][p.X]));
                if (srcPtrn != "wb") {
                    throw new Exception("Find move error.");
                }
                return FindMoves2Captures(fen, getSquare(ps[0]), getSquare(ps[1]));
            }

            // target
            ps = ps.OrderBy(p => b[p.Y][p.X]).ToList();
            var targetPtrn = string.Join("", ps.Select(p => b[p.Y][p.X]));
            if (targetPtrn != "..bw" && targetPtrn != "..b" && targetPtrn != "..w" && targetPtrn != ".bw") {
                throw new Exception("Find move error.");
            }

            var targetWhiteSquare = (string)null;
            var targetBlackSquare = (string)null;
            if (targetPtrn == "..b" || targetPtrn == "..w") {
                targetWhiteSquare = getSquare(ps[2]);
                targetBlackSquare = getSquare(ps[2]);
            }
            else {
                targetWhiteSquare = getSquare(ps.First(p => b[p.Y][p.X] == 'w'));
                targetBlackSquare = getSquare(ps.First(p => b[p.Y][p.X] == 'b'));
            }

            // source
            var sourceWhiteSquare = (string)null;
            var sourceBlackSquare = (string)null;
            if (targetPtrn == ".bw") {
                var empty = ps[0];
                var emptyChar = a[empty.Y][empty.X];
                var nonEmpty = ps.First(p => b[p.Y][p.X] == emptyChar);
                if (emptyChar == 'w') {
                    sourceWhiteSquare = getSquare(empty);
                    sourceBlackSquare = getSquare(nonEmpty);
                }
                else {
                    sourceWhiteSquare = getSquare(nonEmpty);
                    sourceBlackSquare = getSquare(empty);
                }
            }
            else {
                ps = ps.Take(2).OrderBy(p => a[p.Y][p.X]).ToList();
                var sourcePtrn = string.Join("", ps.Select(p => a[p.Y][p.X]));
                if (sourcePtrn != "bw") {
                    throw new Exception("Find move error.");
                }
                sourceWhiteSquare = getSquare(ps[1]);
                sourceBlackSquare = getSquare(ps[0]);
            }

            var whiteMove = sourceWhiteSquare + targetWhiteSquare;
            var blackMove = sourceBlackSquare + targetBlackSquare;

            if (fen.IndexOf(" w ") > -1) {
                return whiteMove + " " + blackMove;
            }
            else {
                return blackMove + " " + whiteMove;
            }
        }

        #endregion

        public static void recognizeThread() {
            var img = new Mat();
            var capture = CreateVideoCapture(2);

            var dt = DateTime.Now;
            for (var gi = 0; ; gi++) {
                if (gi % 100 == 0) { GC.Collect(); };
                // var state = (string)null;

                var dtDiff = (DateTime.Now - dt).TotalMilliseconds;
                Cv2.WaitKey(1);
                Thread.Sleep(Math.Max(0, 100 - (int)dtDiff));
                dt = DateTime.Now;

                capture.Read(img);

                var mask = (string)null;
                var err = (string)null;
                try {
                    mask = recognizeBoard(img);
                } catch (Exception e) {
                    err = e.Message;
                }

                var cmd = new CcvCommand() { name = CcvCommandEnum.mask, mask = mask, err = err };
                cmdQue.Enqueue(cmd);
            }
        }

        #region state macine

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

            return string.Join(" ", ps.Select(p => getSquare(p)));
        }

        public static int isPossibleMove(string fen, string mask, out string nextFen) {
            var m = (string)null;
            nextFen = null;
            try {
                m = FindMoves(fen, mask);
                if (m == null) return 0;

                nextFen = FEN.Move(fen,m);
            }
            catch {
                return -1;
            }

            return 1;
        }

        public static int isPossibleMove(string fen, string mask) {
            var nextFen = (string)null;
            return isPossibleMove(fen, mask, out nextFen);
        }

        /*
        public static int isPossible2Move(string fen, string mask) {
            var nextFen = (string)null;
            var r = isPossibleMove(fen, mask, out nextFen);
            if (r != 1) return r;

            return diff(nextFen, mask) == null ? 1 : -1;
        }
        */

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
            var mask = startMask;
            cmdVarProxy = f => { fen = f; };
            while (true) {
                var input = Console.ReadLine();
                var split = input.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length == 0) continue;

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
                            mask = startMask;
                        }

                        if (args.Length > 1) {
                            cmd.side = int.Parse(args[1]);
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
                        mask = startMask;
                        cmd.mask = mask;

                        break;

                    case "cor":
                        cmd.name = CcvCommandEnum.mask;
                        mask = GetFenMask(fen);
                        cmd.mask = mask;

                        break;

                    default:
                        if (!Regex.IsMatch(input + " ", "^(([a-h][1-8]){2} )+$")) break;

                        foreach (var move in split) {
                            mask = simpleMaskMove(mask, move);
                        }

                        cmd.name = CcvCommandEnum.mask;
                        cmd.mask = mask;

                        break;
                }

                if (cmd.name != CcvCommandEnum.none) {
                    cmdQue.Enqueue(cmd);
                }
            }
        }

        #endregion

        #region leds

        private static int[] skipLeds = { 0, 1, 11, 12, 22, 23, 33, 34, 44, 45, 55, 56, 66, 67, 77, 78, 88, 89, 99 };

        private static List<int> leds = Enumerable.Range(0, 100).Where(x => !skipLeds.Contains(x)).ToList();

        private static int[][] matrix = initMatrix();

        private static bool ledsIsAvailable = testLedsAvailability();

        private static bool testLedsAvailability() {
            var r = curl("http://192.168.0.3/ping", timeout: 3000) != null;
            if (!r) Console.WriteLine("Leds disconnected.");
            return r;
        }

        private static int[][] initMatrix() {
            int[][] matrix = new int[9][];
            var i = 0;
            for (var y = 0; y < 9; y++) {
                matrix[y] = new int[9];
                for (var x = 0; x < 9; x++) {
                    var xx = (y % 2 == 1) ? x : 8 - x;
                    matrix[y][xx] = leds[i];
                    i++;
                }
            }

            return matrix;
        }

        private static int[] sqLeds(string s) {
            var x = "abcdefgh".IndexOf(s[0]);
            var y = "87654321".IndexOf(s[1]);
            return new int[] { matrix[y][x], matrix[y][x + 1], matrix[y + 1][x], matrix[y + 1][x + 1] };
        }

        private static int[] squaresLeds(string s) {
            if (s[2] != ' ') {
                s = s.Substring(0, 2) + " " + s.Substring(2);
            }

            var leds = s.Split(' ').SelectMany(x => sqLeds(x)).Distinct().ToArray();
            if (leds.Length > 10) {
                return new int[] { matrix[0][0], matrix[0][8], matrix[8][0], matrix[8][8] };
            }
            return leds;
        }

        #endregion

        //         private static DateTime lastSendSquareTime = DateTime.Now;

        private static Task lastSendSquareTask = Task.Run(() => { });
        private static CancellationTokenSource delayTaskCts = new CancellationTokenSource();

        private static void sendSquares(string s, int timeout = int.MaxValue) {
            if (!ledsIsAvailable) return;

            var q = (s == null || s.Length <= 2) ? "" : s[0] + "-" + string.Join("-", squaresLeds(s.Substring(2)));

            delayTaskCts.Cancel();
            lastSendSquareTask = lastSendSquareTask.ContinueWith(t => {
                curl("http://192.168.0.3/leds?q=" + q, timeout: 2000);
            });

            if (timeout == int.MaxValue) return;

            delayTaskCts.Dispose();
            delayTaskCts = new CancellationTokenSource();

            lastSendSquareTask = lastSendSquareTask.ContinueWith(async t => {
                await Task.Delay(timeout, delayTaskCts.Token);
                if (delayTaskCts.IsCancellationRequested) return;
                curl("http://192.168.0.3/leds?q=", timeout: 2000);
            });
        }

        private static string token = "lip_hfsqBESVItGp6FmW9FFk";

        private static string startMask = GetFenMask(Board.DEFAULT_STARTING_FEN);

        public static SyncQueue<CcvCommand> cmdQue = new SyncQueue<CcvCommand>();

        private static Action<string> cmdVarProxy = (f) => { }; 

        static void Main(string[] args) {
            /*
            var imgS = new Mat();
            var captureS = CreateVideoCapture(2);
            captureS.Read(imgS);

            imgS.SaveImage("d:/chess-cv-colors.jpg");

            return;
            */

            /*
            var imgR = new Mat("d:/chess-cv-1.jpg", ImreadModes.Color);
            Console.WriteLine(recognizeBoard(imgR));

            new Window("src", imgR);
            Cv2.WaitKey();

            return;
            */

            WebApp.Start("https://+:8081/");

            var mask = (string)null;
            var cur = (string)null;
            var prev = (string)null;
            var last = (string)null;
            var gameId = (string)null;
            var side = 1;
            var maskErr = (string)null;
            var lastJson = (string)null;

            Func<int> poss = () => isPossibleMove(cur,mask);
            // Func<int> poss2 = () => isPossible2Move(cur, mask);
            Func<bool> twoMoves = () => diff(cur,mask, side) != null;
            Func<string,string> diffOp = fen => diff(fen, mask, -1 * side);
            Action<string> push = fen => { prev = cur; cur = fen; };

            var reset = new CmState("reset", s => {
                mask = startMask.Replace("w", ".").Replace("b", ".");
                cur = Board.DEFAULT_STARTING_FEN;
                prev = cur;
                last = null;
                gameId = null;
                sendSquares("4 a1 a8 h1 h8");
                Console.WriteLine(s.name);
            });

            var noGame = new CmState("noGame", s => { sendSquares(null); Console.WriteLine(s.name); });
            var startGame = new CmState("startGame", s => { Console.WriteLine(s.name); });
            var wait = new CmState("wait", s => { sendSquares("2 " + last, 1500); Console.WriteLine(s.name); });

            var waitOp = new CmState("waitOp", s => {
                var m = FindMoves(cur, mask);
                if (m != null) {
                    push(FEN.Move(cur, m));
                    move(gameId, m);
                    last = m;
                    sendSquares("2 " + last, 1500);
                }
                Console.WriteLine(s.name);
            });

            var corOp = new CmState("corOp", s => { sendSquares("7 " + last); Console.WriteLine(s.name); });
            var err = new CmState("err", s => { sendSquares("4 " +  diff(cur, mask)); Console.WriteLine(s.name); });
            var errOp = new CmState("errOp", s => { sendSquares("4 " + diff(twoMoves() ? cur : prev, mask)); Console.WriteLine(s.name); });

            new CmGuard(reset, noGame, () => startMask == mask);
            new CmGuard(noGame, startGame, () => gameId != null);
            new CmGuard(startGame, wait, () => side == 1);
            new CmGuard(startGame, waitOp, () => side == -1);

            new CmGuard(wait, waitOp, () => poss() == 1);
            new CmGuard(corOp, waitOp, () => poss() == 1);
            new CmGuard(waitOp, corOp, () => diffOp(cur) != null && diffOp(prev) == null);
            new CmGuard(corOp, wait, () => diffOp(cur) == null);

            new CmGuard(wait, err, () => poss() == -1);
            new CmGuard(err, waitOp, () => poss() == 1);
            new CmGuard(corOp, errOp, () => diffOp(cur) != null && diffOp(prev) != null && poss() != 1);
            new CmGuard(errOp, wait, () => diffOp(cur) == null);
            new CmGuard(errOp, corOp, () => diffOp(prev) == null);
            new CmGuard(err, wait, () => poss() == 0);

            CmState.cur = reset;
            reset.a();
            CmState.run();

            (new Thread(lichessThread)).Start();
            (new Thread(commandThread)).Start();
            (new Thread(recognizeThread)).Start();

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
                        if (cmd.mask != null) {
                            mask = cmd.mask;
                        }
                        maskErr = cmd.err;
                        break;
                }

                CmState.run();
                cmdVarProxy(cur);

                var state = new { fen = cur, mask = mask, side = side, err = maskErr };
                var json = JsonConvert.SerializeObject(state);
                if (json == lastJson && cmd.name != CcvCommandEnum.none) continue;

                lastJson = json;
                foreach (var hub in CvHub.Hubs) {
                    hub.Clients.All.setState(state);
                }
            }
        }
    }

    class Startup {
        public void Configuration(IAppBuilder app) {
            app.UseCors(CorsOptions.AllowAll);
            app.MapSignalR();
        }
    }

    public class CvHub : Hub {
        static Queue<CvHub> hubQueue = new Queue<CvHub>();

        public static CvHub[] Hubs {
            get { return hubQueue.ToArray(); }
        }

        public void enqueHub() {
            hubQueue.Enqueue(this);
            while (hubQueue.Count > 10) {
                hubQueue.Dequeue();
            }
        }

        public void enqueNoneCmd() {
            var cmd = new CcvCommand();
            Program.cmdQue.Enqueue(cmd);
        }

        public int val(string name) {
            var i = Array.IndexOf(Program.uiNames, name);
            if (i < 0) return 0;
            return (int)Program.uiValues[i];
        }

        public void val(string name, int val) {
            var i = Array.IndexOf(Program.uiNames, name);
            if (i < 0) return;
            Program.uiValues[i] = val;
        }
    }
}

/*
public static void Kmeans(Mat input, Mat output, int k) {
    using (Mat points = new Mat())
    using (Mat labels = new Mat())
    using (Mat centers = new Mat()) {
        int width = input.Cols;
        int height = input.Rows;

        points.Create(width * height, 1, MatType.CV_32FC3);
        centers.Create(k, 1, points.Type());
        output.Create(height, width, input.Type());

        // Input Image Data
        int i = 0;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++, i++) {
                var vec3f = new Vec3f() {
                    Item0 = input.At<Vec3b>(y, x).Item0,
                    Item1 = input.At<Vec3b>(y, x).Item1,
                    Item2 = input.At<Vec3b>(y, x).Item2
                };

                points.Set(i, vec3f);
            }
        }

        // Criteria:
        // – Stop the algorithm iteration if specified accuracy, epsilon, is reached.
        // – Stop the algorithm after the specified number of iterations, MaxIter.
        var criteria = new TermCriteria(type: CriteriaTypes.Eps | CriteriaTypes.MaxIter, maxCount: 10, epsilon: 1.0);

        // Finds centers of clusters and groups input samples around the clusters.
        Cv2.Kmeans(data: points, k: k, bestLabels: labels, criteria: criteria, attempts: 3, flags: KMeansFlags.PpCenters, centers: centers);

        // Output Image Data
        i = 0;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++, i++) {
                int index = labels.Get<int>(i);

                var vec3b = new Vec3b();

                int firstComponent = Convert.ToInt32(Math.Round(centers.At<Vec3f>(index).Item0));
                firstComponent = firstComponent > 255 ? 255 : firstComponent < 0 ? 0 : firstComponent;
                vec3b.Item0 = Convert.ToByte(firstComponent);

                int secondComponent = Convert.ToInt32(Math.Round(centers.At<Vec3f>(index).Item1));
                secondComponent = secondComponent > 255 ? 255 : secondComponent < 0 ? 0 : secondComponent;
                vec3b.Item1 = Convert.ToByte(secondComponent);

                int thirdComponent = Convert.ToInt32(Math.Round(centers.At<Vec3f>(index).Item2));
                thirdComponent = thirdComponent > 255 ? 255 : thirdComponent < 0 ? 0 : thirdComponent;
                vec3b.Item2 = Convert.ToByte(thirdComponent);

                output.Set(y, x, vec3b);
            }
        }
    }
}
*/