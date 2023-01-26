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

    public class Program
    {
        public static object[] uiValues = new object[6];
        public static object[] uiNames = new string[] { "hl", "hh", "sl", "sh", "vl", "vh" };

        public static int uiVal(string name, int val) {
            var i = Array.IndexOf(uiNames,name) ;
            if (i < 0) return 0;

            if (uiValues[i] == null) uiValues[i] = val;
            return (int)uiValues[i];
        }

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

        static void HueShift(Mat src, int shift) {
            var src3b = new Mat<Vec3b>(src);
            var indexer = src3b.GetIndexer();
            for (var j = 0; j < src.Rows; j++) {
                for (var i = 0; i < src.Cols; i++) {
                    var color = indexer[j, i];
                    color.Item0 = (byte)((color.Item0 + shift + 180) % 180);
                    indexer[j, i] = color;
                    // src.At<Vec3b>(j, i)[0] = (byte)((src.At<Vec3b>(j, i)[0] + shift + 180) % 180);
                }
            }
            src3b.CopyTo(src);
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

        public static string[] ListOfAttachedCameras()
        {
            var cameras = new List<string>();
            var attributes = new MediaAttributes(1);
            attributes.Set(CaptureDeviceAttributeKeys.SourceType.Guid, CaptureDeviceAttributeKeys.SourceTypeVideoCapture.Guid);
            var devices = MediaFactory.EnumDeviceSources(attributes);
            for (var i = 0; i < devices.Count(); i++)
            {
                var friendlyName = devices[i].Get(CaptureDeviceAttributeKeys.FriendlyName);
                cameras.Add(friendlyName);
            }
            return cameras.ToArray();
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

        public static IEnumerable<Point> SquareContour(Point p, int size) {
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

            // White balance
            // var balancer = SimpleWB.Create();
            // balancer.BalanceWhite(img,img);
            //new Window("img", img);
            //Cv2.WaitKey(1);

            // blur
            //Cv2.GaussianBlur(img, img, new Size(3, 3), 0);

            // Red threshold
            Mat imgSmall;
            {
                var imgHeight = 100;
                var imgWidth = (int)(((float)src.Width) / src.Height * imgHeight);
                imgSmall = src.Resize(new Size(imgWidth, imgHeight), interpolation: InterpolationFlags.Linear);
            }

            var redColor = FindNearestColor(imgSmall, new Scalar(120, 70, 200));

            var imgHsv = new Mat();
            Cv2.CvtColor(img, imgHsv, ColorConversionCodes.BGR2HSV);
            var redHsv = ScalarBGR2HSV(redColor);
            var mask = new Mat();
            var mask2 = new Mat();
            SafeInRange(imgHsv,
                redHsv.HsvAdd(new Scalar(-10, -80, -80)),
                redHsv.HsvAdd(new Scalar(+10, +80, +80)),
                mask);

            var imgGray = new Mat();
            Cv2.CvtColor(img, imgGray, ColorConversionCodes.BGR2GRAY);
            Cv2.AdaptiveThreshold(imgGray, mask2, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, 21, 5);
            Cv2.BitwiseAnd(mask,mask2,mask);
            //new Window("mask", mask);
            //Cv2.WaitKey(1);

            // Circles
            Cv2.GaussianBlur(mask, mask, new Size(9, 9), 0);
            var circles = Cv2.HoughCircles(mask, HoughModes.Gradient, 1,
                         10,  // change this value to detect circles with different distances to each other
                         100, 30,
                         5, 50 // change the last two parameters (min_radius & max_radius) to detect larger circles
            );

            foreach (var circle in circles) {
                Cv2.Circle(img, circle.Center.ToPoint(), (int)circle.Radius, new Scalar(0,255,0));
            }
            
            //new Window("img", img);
            //Cv2.WaitKey(1);
            
            if (circles.Length != 4) {
                // img.SaveImage("d:/" + Guid.NewGuid().ToString("D") + ".jpg");
                Console.WriteLine("Red circles count (" + circles.Length + ") != 4.");
                throw new Exception("Red circles count (" + circles.Length + ") != 4.");
            }

            // Rotation
            circles = circles.OrderBy(x => x.Center.X).ToArray();
            var rotation = Math.Abs(circles[0].Center.X - circles[1].Center.X) / Math.Abs(circles[0].Center.Y - circles[1].Center.Y);
            if (rotation > 0.5) {
                throw new Exception("Bad rotation.");
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
            var blackColor = SquareAvgColor(img, new Point(0,0), 5);
            Cv2.InRange(img, blackColor.Add(new Scalar(-80, -80, -80)), blackColor.Add(new Scalar(80, 80, 80)), mask);
            var blackSeq = 0;
            foreach (var p in SquareContour(new Point(0,0), size)) {
                var c = mask.At<byte>(p.Y,p.X);
                if (c == 0) {
                    blackSeq++;
                }
                else {
                    blackSeq = 0;
                }
                if (blackSeq > sqs / 4) {
                    throw new Exception("Black contour is not closed.");
                }
            }

            // Squares mask
            Cv2.CvtColor(img, imgHsv, ColorConversionCodes.BGR2HSV);
            var squareColor = SquareAvgColor(imgHsv, new Point(sqs * 0.5 - 5, sqs * 1.5 - 5), 10);
            Cv2.InRange(imgHsv,
                squareColor.HsvAdd(new Scalar(uiVal("hl", -20), uiVal("sl", -70), uiVal("vl", -70))),
                squareColor.HsvAdd(new Scalar(uiVal("hh", +20), uiVal("sh", +100), uiVal("vh", +50))),
                mask);    

            mask2 = new Mat(mask.Size(), mask.Type(), new Scalar());
            Cv2.Rectangle(mask2, new Rect(sqs, sqs, size - 2 * sqs, size - 2 * sqs), new Scalar(255), -1);
            Cv2.BitwiseAnd(mask, mask2, mask);

            Cv2.CvtColor(mask, mask, ColorConversionCodes.GRAY2BGR);
            Cv2.BitwiseOr(img, mask, img);

            new Window("img2", img);
            Cv2.WaitKey(1);

            // Calc pieces
            imgGray = new Mat();
            Cv2.CvtColor(img, imgGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(imgGray, imgGray, ColorConversionCodes.GRAY2BGR);
            var grayLabels = new List<GrayLabel>();
            // var percs = new List<double>();
            foreach (var p in SquarePoints(new Point(sqs, sqs), 8, sqs)) {
                Scalar avgColor;
                double perc;
                SquareStats(imgGray, p, sqs, out avgColor, out perc);
                var gl = new GrayLabel();
                // if (perc != 0) percs.Add(perc);
                if (perc >= 0.09) {
                    gl.gray = (byte)((int)avgColor.Val0);
                }
                grayLabels.Add(gl);
            }

            // Console.WriteLine(percs.Min());

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

        public static string Curl(string url, string method = "GET") {
            var request = WebRequest.Create(url);
            request.Method = method;
            request.Headers.Add("Authorization: Bearer lip_vCoNPyCoEXaQ5tDBUwDA");
            WebResponse response = null;
            try {
                response = request.GetResponse();
            }
            catch (WebException e) {
                response = e.Response;
                // ((HttpWebResponse)e.Response).StatusCode
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
            request.Headers.Add("Authorization: Bearer lip_vCoNPyCoEXaQ5tDBUwDA");
            request.Timeout = 1000;
            var reader = (StreamReader)null;
            try {
                var response = request.GetResponse();
                var dataStream = response.GetResponseStream();
                reader = new StreamReader(dataStream);
            } catch { }

            return reader;
        }

        public static string GetGameId() {
            var pgnStr = Curl("https://lichess.org/api/user/adlokyy/current-game");
            var pgn = Pgn.Load(pgnStr);
            return pgn.Site.Split('/').Last();
        }

        public static bool Move(string gameId, string move) {
            var s = Curl($"https://lichess.org/api/board/game/{gameId}/move/{move}", "POST");
            return s.IndexOf(@"""ok"":true") > -1;
        }

        public static string GetFenByMoves(string moveStr) {
            var board = Board.Load();
            var moves = moveStr.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var move in moves) {
                board.Move(move);
            }

            return board.GetFEN();
        }

        public static void LichessMain() {
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
                            lock (syncRoot) {
                                fen = Board.DEFAULT_STARTING_FEN;
                                gameId = eventXml.XPathSelectElement("game/gameId").Value;
                                isWhite = eventXml.XPathSelectElement("game/color").Value == "white";
                            }

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
                                var moves = stateXml.XPathSelectElement(movesPath).Value;
                                var newFen = GetFenByMoves(moves);
                                lock (syncRoot) {
                                    fen = newFen;
                                }
                            }
                        } else if (eventType == "gameFinish") {
                            lock (syncRoot) {
                                fen = Board.DEFAULT_STARTING_FEN;
                                gameId = null;
                            }
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

        public static string FindMoves(string fen, string board) {
            Func<Point, string> getSquare = (p) => "" + "abcdefgh"[p.X] + "87654321"[p.Y];

            var fenMask = GetFenMask(fen);

            var a = fenMask.Split('/');
            var b = board.Split('/');

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

        public static void SendState(string s, int i) {
            if ((i % 5) != 0) return;
            foreach (var hub in CvHub.Hubs) {
                hub.Clients.All.setBoardState(s);
            }
        }

        private static object syncRoot = new object();
        private static volatile string fen = Board.DEFAULT_STARTING_FEN;
        private static volatile string gameId;
        private static volatile bool isWhite = true;

        static void Main(string[] args) {

            /*

            Cv2.WaitKey();
            return;
            */

            //var _moves = FindMoves("r1bqkbnr/1p3ppp/p1n1p3/1BppP3/3P4/2P5/PP3PPP/RNBQK1NR w KQkq - 0 6", GetFenMask("r1bqkbnr/5ppp/p1p1p3/2ppP3/3P4/2P5/PP3PPP/RNBQK1NR w KQkq - 0 7"));
            //return;

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

            using (WebApp.Start("http://192.168.0.2:8081")) {
                Console.WriteLine("ChessCv started...");

                var lichessThread = new Thread(LichessMain);
                lichessThread.Start();

                var img = new Mat();
                var capture = CreateVideoCapture(2);

                var dt = DateTime.Now;
                for (var gi = 0; ; gi++) {
                    if (gi % 100 == 0) { GC.Collect(); };
                    var state = (string)null;

                    var dtDiff = (DateTime.Now - dt).TotalMilliseconds;
                    Cv2.WaitKey(Math.Max(1, 100 - (int)dtDiff));
                    dt = DateTime.Now;

                    capture.Read(img);
                    // new Window("src", img);
                    // Cv2.WaitKey(1);

                    var board = (string)null;
                    var boardError = (string)null;
                    try {
                        board = recognizeBoard(img);
                    } catch (Exception e) {
                        boardError = e.Message;
                    }

                    if (board == null) {
                        SendState(boardError, gi);
                        continue;
                    }

                    state = board.Replace("/", "\n");
                    
                    lock (syncRoot) {
                        try {
                            if (gameId == null && (
                                board == "bbbbbbbb/bbbbbbbb/......../......../......../......../wwwwwwww/wwwwwwww"
                             || board == "wwwwwwww/wwwwwwww/......../......../......../......../bbbbbbbb/bbbbbbbb"))
                            {
                                fen = Board.DEFAULT_STARTING_FEN;
                                isWhite = board[0] == 'b';
                            }

                            board = (isWhite) ? board : string.Join("", board.Reverse());

                            var moveStr = FindMoves(fen, board);
                            if (moveStr != null) {
                                var sendMove = isWhite == (fen.IndexOf(" w ") > -1);
                                // fen = FEN.Move(fen, moveStr);
                                if (sendMove) {
                                    // gameId = gameId ?? GetGameId();
                                    Move(gameId, moveStr);
                                }
                                /*
                                else {
                                    foreach (var hub in CvHub.Hubs) {
                                        hub.Clients.All.beep();
                                    }
                                }
                                */
                            }
                        } catch (Exception e) {
                            state += "\n" + e.Message;
                        }
                    }
                    SendState(state, gi);
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
        public void test() {
            Console.WriteLine("test");
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