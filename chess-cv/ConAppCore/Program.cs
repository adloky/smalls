using System;
using System.Drawing;

using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;


namespace ConAppCore
{
    class Program
    {
        static void Main(string[] args)
        {
            CvInvoke.NamedWindow("win1");
            //CvInvoke.NamedWindow("win2");

            var img = new Image<Bgr, byte>("d:/chess-cv-1.jpg");

            // To resize the image 
            var imgWidth = 1000;
            var imgHeight = (int)(((float)img.Height) / img.Width * imgWidth);
            img = img.Resize(imgWidth, imgHeight, Inter.Linear);

            // blur
            CvInvoke.GaussianBlur(img, img, new Size(7, 7), 0);

            // Threshold
            byte loH = 170, upH = 5, loS = 100, loV = 140;
            var imgHsv = new Image<Hsv, byte>(img.Width, img.Height);
            CvInvoke.CvtColor(img, imgHsv, ColorConversion.Bgr2Hsv);
            var mask = new Image<Gray, byte>(img.Width, img.Height);
            var mask2 = new Image<Gray, byte>(img.Width, img.Height);
            CvInvoke.InRange(imgHsv, new ScalarArray(new MCvScalar(0, loS, loV)), new ScalarArray(new MCvScalar(upH, 255, 255)), mask);
            CvInvoke.InRange(imgHsv, new ScalarArray(new MCvScalar(loH, loS, loV)), new ScalarArray(new MCvScalar(180, 255, 255)), mask2);
            CvInvoke.BitwiseOr(mask, mask2, mask);

            CvInvoke.Imshow("win1", img);
            //CvInvoke.Imshow("win2", img);
            CvInvoke.WaitKey(0);  //Wait for the key pressing event
            //CvInvoke.DestroyWindow(win1); //Destroy the window if key is pressed
        }
    }
}

// new Bgr(0, 255, 0).MCvScalar
// CvInvoke.PutText(img, "Hello, world", new System.Drawing.Point(10, 80), FontFace.HersheyComplex, 1.0, new Bgr(0, 255, 0).MCvScalar);