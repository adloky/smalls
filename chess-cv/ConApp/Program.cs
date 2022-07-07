using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;

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

        static void Main(string[] args)
        {
            var img = new Mat("d:/chess-cv-1.jpg", ImreadModes.Color);

            // To resize the image 
            var imgWidth = 1000;
            var imgHeight = (int)(((float)img.Height) / img.Width * imgWidth);
            img = img.Resize(new Size(imgWidth, imgHeight));

            // blur
            Cv2.GaussianBlur(img, img, new Size(7, 7), 0);

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

            foreach (var circle in circles) {
                // Cv2.Circle(img, circle.Center.ToPoint(), (int)circle.Radius, new Scalar(0, 255, 0), 3, LineTypes.AntiAlias);
            }

            // Correct points
            Point2f[] srcTrans = new Point2f[4];
            var cirPoints = circles.Select(x => x.Center.ToPoint()).ToArray();
            srcTrans[0] = FindNearest(new Point(0,0), cirPoints);
            srcTrans[1] = FindNearest(new Point(0, imgHeight), cirPoints);
            srcTrans[2] = FindNearest(new Point(imgWidth, 0), cirPoints);
            srcTrans[3] = FindNearest(new Point(imgWidth, imgHeight), cirPoints);

            // Transform
            Point2f[] dstTrans = new Point2f[4];

            dstTrans[0] = new Point2f(10,10);
            dstTrans[1] = new Point2f(10, 190);
            dstTrans[2] = new Point2f(190, 10);
            dstTrans[3] = new Point2f(190, 190);
            var warp = Cv2.GetPerspectiveTransform(srcTrans, dstTrans);
            Cv2.WarpPerspective(img,img,warp,new Size(200,200));

            // Quantize
            Kmeans(img,img,15);

            using (new Window("src image", img)) {
                Cv2.WaitKey();
            }
        }
    }
}
