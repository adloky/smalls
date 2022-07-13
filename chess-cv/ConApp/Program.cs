using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SharpDX.MediaFoundation;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin.Hosting;
using Owin;
using Microsoft.Owin.Cors;
using System.Net;
using System.IO;
using Chess;
using System.Text.RegularExpressions;

namespace ConApp
{
    public static class OpenCvExtensions {
        public static Scalar Add(this Scalar val, Scalar valAdd) {
            Func<double, double, double> addD = (a, b) => {
                a += b;
                if (a > 255) {
                    a = 255;
                }
                else if (a < 0) {
                    a = 0;
                }

                return a;
            };

            val.Val0 = addD(val.Val0, valAdd.Val0);
            val.Val1 = addD(val.Val1, valAdd.Val1);
            val.Val2 = addD(val.Val2, valAdd.Val2);
            val.Val3 = addD(val.Val3, valAdd.Val3);

            return val;
        }
    }

    public class Program
    {
        #region recognize

        public static Scalar ScalarBGR2HSV(Scalar s) {
            Mat bgr = new Mat(1, 1, MatType.CV_8UC3, s);
            Mat hsv = new Mat();
            Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
            var v = hsv.Get<Vec3b>(0, 0);

            return new Scalar(v[0], v[1], v[2]);
        }

        static void HueShift(Mat src, int shift) {
            for (var j = 0; j < src.Rows; j++) {
                for (var i = 0; i < src.Cols; i++) {
                    src.At<Vec3b>(j, i)[0] = (byte)((src.At<Vec3b>(j, i)[0] + shift) % 180);
                }
            }
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
            foreach (var p in SquarePoints(sp, size)) {
                var c = img.At<Vec3b>(p.Y, p.X);
                if (c[0] == 255 && c[1] == 255 && c[2] == 255) {
                    continue;
                }
                sum.Val0 += c[0];
                sum.Val1 += c[1];
                sum.Val2 += c[2];

                count++;
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

            // blur
            Cv2.GaussianBlur(img, img, new Size(3, 3), 0);

            // Red threshold
            Mat imgSmall;
            {
                var imgHeight = 100;
                var imgWidth = (int)(((float)src.Width) / src.Height * imgHeight);
                imgSmall = src.Resize(new Size(imgWidth, imgHeight), interpolation: InterpolationFlags.Linear);
            }

            var redColor = new Scalar();
            {
                var minRedDist = double.MaxValue;
                var redColorStd = new Scalar(0,0,255);
                for (var j = 0; j < imgSmall.Height; j++) {
                    for (var i = 0; i < imgSmall.Width; i++) {
                        var cv3 = imgSmall.At<Vec3b>(j, i);
                        var c = new Scalar(cv3[0], cv3[1], cv3[2]);
                        var dist = ColorDistanсe(redColorStd, c);
                        if (dist < minRedDist) {
                            redColor = c;
                            minRedDist = dist;
                        }
                    }
                }
            }

            var imgHsv = new Mat();
            Cv2.CvtColor(img, imgHsv, ColorConversionCodes.BGR2HSV);
            var redColorHsv = ScalarBGR2HSV(redColor);
            HueShift(imgHsv,30);
            redColorHsv.Val0 = (redColorHsv.Val0 + 30) % 180;
            var mask = new Mat();
            Cv2.InRange(imgHsv, redColorHsv.Add(new Scalar(-10, -60, -80)), redColorHsv.Add(new Scalar(10, 80, 80)), mask);
            //new Window("mask", mask);
            //Cv2.WaitKey(1);

            // Circles
            Cv2.GaussianBlur(mask, mask, new Size(13, 13), 0);
            var circles = Cv2.HoughCircles(mask, HoughModes.Gradient, 1,
                         10,  // change this value to detect circles with different distances to each other
                         100, 30,
                         5, 30 // change the last two parameters (min_radius & max_radius) to detect larger circles
            );

            if (circles.Length != 4) {
                throw new Exception("Red circles count (" + circles.Length + ") != 4.");
            }

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
            Cv2.InRange(img, blackColor.Add(new Scalar(-50, -50, -50)), blackColor.Add(new Scalar(50, 50, 50)), mask);
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
            var blackSquareColor = SquareAvgColor(imgHsv, new Point(sqs * 0.5 - 5, sqs * 1.5 - 5), 10);
            var whiteSquareColor = SquareAvgColor(imgHsv, new Point(sqs * 0.5 - 5, sqs * 2.5 - 5), 10);
            Cv2.InRange(imgHsv, blackSquareColor.Add(new Scalar(-20, -110, -110)), blackSquareColor.Add(new Scalar(20, 110, 110)), mask);

            Cv2.Rectangle(mask, new Rect(sqs, sqs, size - 2 * sqs, size - 2 * sqs), new Scalar(255), 4);
            var mask2 = new Mat(mask.Size(), mask.Type(), new Scalar());
            Cv2.Rectangle(mask2, new Rect(sqs, sqs, size - 2 * sqs, size - 2 * sqs), new Scalar(255), -1);
            Cv2.BitwiseAnd(mask, mask2, mask);

            Cv2.CvtColor(mask, mask, ColorConversionCodes.GRAY2BGR);
            Cv2.BitwiseOr(img, mask, img);

            //new Window("img", img);
            //Cv2.WaitKey(1);

            // Calc pieces
            var imgGray = new Mat();
            Cv2.CvtColor(img, imgGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(imgGray, imgGray, ColorConversionCodes.GRAY2BGR);
            var grayLabels = new List<GrayLabel>();
            foreach (var p in SquarePoints(new Point(sqs, sqs), 8, sqs)) {
                Scalar avgColor;
                double perc;
                SquareStats(imgGray, p, sqs, out avgColor, out perc);
                var gl = new GrayLabel();
                if (perc >= 0.1) {
                    gl.gray = (byte)((int)avgColor.Val0);
                }
                grayLabels.Add(gl);
            }

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
            WebResponse response;
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

        
        public static string GetGameId() {
            var pgnStr = Curl("https://lichess.org/api/user/adlokyy/current-game");
            var pgn = Pgn.Load(pgnStr);
            return pgn.Site.Split('/').Last();
        }

        public static bool Move(string gameId, string move) {
            var s = Curl($"https://lichess.org/api/board/game/{gameId}/move/{move}", "POST");
            return s.IndexOf(@"""ok"":true") > -1;
        }

        #endregion

        #region find move

        public static string FindMove(string fen, string board) {
            Func<Point, string> getSquare = (p) => "" + "abcdefgh"[p.X] + "87654321"[p.Y];

            var fen0 = fen.Split(' ')[0];
            fen0 = fen0.Replace("1", ".").Replace("2", "..").Replace("3", "...").Replace("4", "....")
                .Replace("5", ".....").Replace("6", "......").Replace("7", ".......").Replace("8", "........");

            fen0 = Regex.Replace(fen0, "[pnbrqk]", "b");
            fen0 = Regex.Replace(fen0, "[PNBRQK]", "w");

            var a = fen0.Split('/');
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

            if (ps.Count != 2 && ps.Count != 4) {
                throw new Exception($"Find move diff count is {ps.Count}.");
            }

            if (ps.Count == 2) {
                ps = ps.OrderBy(p => b[p.Y][p.X]).ToList();
                var bcs = string.Join("", ps.Select(p => b[p.Y][p.X])).Replace("b", "w");
                if (bcs != ".w") {
                    throw new Exception("Find move error.");
                }

                return getSquare(ps[0]) + getSquare(ps[1]);
            }
            else if (ps.Count == 4) {
                var ys = ps.Select(p => p.Y).Distinct().ToArray();
                if (ys.Length > 1 || (ys[0] != 0 && ys[0] != 7)) {
                    throw new Exception("Find move error.");
                }
                var y = ys[0];
                var xs = string.Join("", ps.Select(p => p.X.ToString()));
                if (xs != "0234" && xs != "4567") {
                    throw new Exception("Find move error.");
                }

                var bcs = string.Join("", ps.Select(p => b[p.Y][p.X])).Replace("b", "w");
                if (bcs != ".ww.") {
                    throw new Exception("Find move error.");
                }

                var targetX = (ps[0].X == 0) ? 2 : 6;

                return getSquare(new Point(4,y)) + getSquare(new Point(targetX, y));
            }

            return null;
        }

        #endregion

        public static void SendState(string s) {
            foreach (var hub in CvHub.Hubs) {
                hub.Clients.All.setBoardState(s);
            }
        }

        static void Main(string[] args)
        {
            /*
            var gameId = GetGameId();

            while (true) {
                var s = Console.ReadLine();
                if (s == "q") {
                    break;
                }

                Move(gameId, s);
            }

            
            return;
            */
            /*
            var imgS = new Mat();
            var captureS = CreateVideoCapture(2);
            captureS.Read(imgS);

            imgS.SaveImage("d:/chess-cv-3.jpg");

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

                var img = new Mat();
                var capture = CreateVideoCapture(2);

                var fen = Board.DEFAULT_STARTING_FEN;
                var gameId = (string)null;
                var isWhite = true;
                while (true) {
                    var state = "";
                    Cv2.WaitKey(333);
                    capture.Read(img);

                    var board = (string)null;
                    var boardError = (string)null;
                    try {
                        board = recognizeBoard(img);
                    } catch (Exception e) {
                        boardError = e.Message;
                    }

                    if (board == null) {
                        SendState(boardError);
                        continue;
                    }

                    state += board.Replace("/", "\n");

                    if (board == "bbbbbbbb/bbbbbbbb/......../......../......../......../wwwwwwww/wwwwwwww"
                     || board == "wwwwwwww/wwwwwwww/......../......../......../......../bbbbbbbb/bbbbbbbb")
                    {
                        gameId = null;
                        fen = Board.DEFAULT_STARTING_FEN;
                        isWhite = board[0] == 'b';
                        SendState(state);
                        continue;
                    }

                    board = (isWhite) ? board : string.Join("", board.Reverse());
                    try {
                        var move = FindMove(fen, board);
                        if (move != null) {
                            var sendMove = isWhite == (fen.IndexOf(" w ") > -1);
                            fen = FEN.Move(fen, move);
                            if (sendMove) {
                                gameId = gameId ?? GetGameId();
                                Move(gameId, move);
                            }
                        }
                    } catch (Exception e) {
                        state += "\n" + e.Message;
                    }
                    SendState(state);
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