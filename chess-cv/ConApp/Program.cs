using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SharpDX.MediaFoundation;

namespace ConApp
{
    class Program
    {
        static void HueShift(Mat src, int shift) {
            for (var j = 0; j < src.Rows; j++) {
                for (var i = 0; i < src.Cols; i++) {
                    src.At<Vec3b>(j, i)[0] = (byte)((src.At<Vec3b>(j, i)[0] + shift) % 180);
                }
            }
        }

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

        public static Point FindNearest(Point sp, Point[] ps) {
            var minDest = int.MaxValue;
            var result = new Point(0,0);
            foreach (var p in ps) {
                var dx = p.X - sp.X;
                var dy = p.Y - sp.Y;
                var dest = (int)Math.Sqrt(dx *dx + dy * dy);
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
            var sum = new Scalar(0,0,0);
            var count = 0;
            foreach (var p in SquarePoints(sp,size)) {
                var c = img.At<Vec3b>(p.Y,p.X);
                if (c[0] == 255 && c[1] == 255 && c[2] == 255) {
                    continue;
                }
                sum.Val0 += c[0];
                sum.Val1 += c[1];
                sum.Val2 += c[2];

                count++;
            }
            avgColor = new Scalar(0,0,0);
            avgColor.Val0 = sum.Val0 / count;
            avgColor.Val1 = sum.Val1 / count;
            avgColor.Val2 = sum.Val2 / count;
            perc = (double)count / (size * size);
        }

        public static double ColorDistanсe(Scalar a, Scalar b) {
            var d0 = a.Val0 - b.Val0;
            var d1 = a.Val1 - b.Val1;
            var d2 = a.Val2 - b.Val2;
            return Math.Sqrt(d0 * d0 + d1 * d1 + d2 * d2);
        }

        public static string recognizeBoard(Mat src) {
            // To resize the image
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

            // Threshold
            var imgHsv = new Mat();
            Cv2.CvtColor(img, imgHsv, ColorConversionCodes.BGR2HSV);
            HueShift(imgHsv, 20);
            var mask = new Mat();
            Cv2.InRange(imgHsv, new Scalar(10, 100, 140), new Scalar(25, 255, 255), mask);

            // Circles
            Cv2.GaussianBlur(mask, mask, new Size(9, 9), 0);
            var circles = Cv2.HoughCircles(mask, HoughModes.Gradient, 1,
                         10,  // change this value to detect circles with different distances to each other
                         100, 30,
                         5, 30 // change the last two parameters (min_radius & max_radius) to detect larger circles
            );

            foreach (var circle in circles)
            {
                // Cv2.Circle(img, circle.Center.ToPoint(), (int)circle.Radius, new Scalar(0, 255, 0), 3, LineTypes.AntiAlias);
            }

            // Correct points
            Point2f[] srcTrans = new Point2f[4];
            var cirPoints = circles.Select(x => x.Center.ToPoint()).ToArray();
            srcTrans[0] = FindNearest(new Point(0, 0), cirPoints);
            srcTrans[1] = FindNearest(new Point(0, img.Rows), cirPoints);
            srcTrans[2] = FindNearest(new Point(img.Cols, 0), cirPoints);
            srcTrans[3] = FindNearest(new Point(img.Cols, img.Rows), cirPoints);

            // Transform
            var normSize = 400;
            var sqs = normSize / 10;

            Point2f[] dstTrans = new Point2f[4];

            dstTrans[0] = new Point2f(sqs / 2, sqs / 2);
            dstTrans[1] = new Point2f(sqs / 2, normSize - sqs / 2);
            dstTrans[2] = new Point2f(normSize - sqs / 2, sqs / 2);
            dstTrans[3] = new Point2f(normSize - sqs / 2, normSize - sqs / 2);
            var warp = Cv2.GetPerspectiveTransform(srcTrans, dstTrans);
            Cv2.WarpPerspective(img, img, warp, new Size(normSize, normSize));

            // Squares mask
            Cv2.CvtColor(img, imgHsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(imgHsv, new Scalar(40, 1, 1), new Scalar(160, 255, 255), mask);
            Cv2.CvtColor(mask, mask, ColorConversionCodes.GRAY2BGR);

            Cv2.Rectangle(mask, new Rect(0, 0, sqs, normSize), new Scalar(0), -1);
            Cv2.Rectangle(mask, new Rect(0, 0, normSize, sqs), new Scalar(0), -1);
            Cv2.Rectangle(mask, new Rect(normSize - sqs, 0, sqs, normSize), new Scalar(0), -1);
            Cv2.Rectangle(mask, new Rect(0, normSize - sqs, normSize, sqs), new Scalar(0), -1);

            Cv2.BitwiseOr(img, mask, img);
            Cv2.Rectangle(img, new Rect(sqs, sqs, normSize - 2 * sqs, normSize - 2 * sqs), new Scalar(255, 255, 255), 2);

            // Calc pieces
            var imgGray = new Mat();
            Cv2.CvtColor(img, imgGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(imgGray, imgGray, ColorConversionCodes.GRAY2BGR);
            var squareColors = new List<int>();
            foreach (var p in SquarePoints(new Point(sqs, sqs), 8, sqs))
            {
                Scalar avgColor;
                double perc;
                SquareStats(imgGray, p, sqs, out avgColor, out perc);

                var color = (perc < 0.06) ? 255 : (int)avgColor.Val0;
                squareColors.Add(color);
                //Cv2.Rectangle(img, new Rect(p, new Size(sqs,sqs)), fColor, -1);
            }

            var pieceColors = squareColors.Where(x => x < 255).ToArray();
            var avgPieceColor = pieceColors.Sum(x => x) / pieceColors.Length;

            var resultSb = new StringBuilder();
            var i = 0;
            foreach (var sc in squareColors)
            {
                if (i > 0 && i % 8 == 0)
                {
                    resultSb.Append("/");
                }
                i++;
                resultSb.Append(sc == 255 ? "." : sc > avgPieceColor ? "w" : "b");

            }

            return resultSb.ToString();
        }

        static void Main(string[] args)
        {
            /*
            var frame = new Mat();
            var capture = CreateVideoCapture(2);
            capture.Read(frame);

            frame.SaveImage("d:/chess-cv-3.jpg");

            return;
            */
            var img = new Mat("d:/chess-cv-3.jpg", ImreadModes.Color);

            Console.WriteLine(recognizeBoard(img));

            // Quantize
            //Kmeans(img,img,15);

            new Window("src image", img);
            Cv2.WaitKey();
        }
    }
}
