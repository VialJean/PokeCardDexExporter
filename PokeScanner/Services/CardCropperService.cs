using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace PokeScanner.Services;

public class CardCropperService
{
    public Mat? DetectAndCropCard(Mat inputImage)
    {
        // 1. Convertir en niveau de gris
        using var gray = new Mat();
        Cv2.CvtColor(inputImage, gray, ColorConversionCodes.BGR2GRAY);

        // 2. Flouter pour réduire le bruit
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        // 3. Détection de contours (Canny)
        using var edged = new Mat();
        Cv2.Canny(blurred, edged, 75, 200);

        // 4. Trouver les contours
        Cv2.FindContours(edged, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

        // 5. Trouver le plus grand quadrilatère
        var sortedContours = contours
            .Select(c => Cv2.ApproxPolyDP(c, 0.02 * Cv2.ArcLength(c, true), true))
            .Where(c => c.Length == 4)
            .OrderByDescending(c => Cv2.ContourArea(c))
            .ToList();

        if (!sortedContours.Any())
            return null;

        var cardContour = sortedContours.First();

        // 6. Redressement de perspective
        var warped = WarpPerspective(inputImage, cardContour);
        return warped;
    }

    private Mat WarpPerspective(Mat image, Point[] contour)
    {
        // Tri des points dans l’ordre : top-left, top-right, bottom-right, bottom-left
        var rect = OrderPoints(contour);

        // Calcul des dimensions de la nouvelle image
        double widthA = Math.Sqrt(Math.Pow(rect[2].X - rect[3].X, 2) + Math.Pow(rect[2].Y - rect[3].Y, 2));
        double widthB = Math.Sqrt(Math.Pow(rect[1].X - rect[0].X, 2) + Math.Pow(rect[1].Y - rect[0].Y, 2));
        double maxWidth = Math.Max(widthA, widthB);

        double heightA = Math.Sqrt(Math.Pow(rect[1].X - rect[2].X, 2) + Math.Pow(rect[1].Y - rect[2].Y, 2));
        double heightB = Math.Sqrt(Math.Pow(rect[0].X - rect[3].X, 2) + Math.Pow(rect[0].Y - rect[3].Y, 2));
        double maxHeight = Math.Max(heightA, heightB);

        // Points destination
        var dst = new[]
        {
        new Point2f(0, 0),
        new Point2f((float)maxWidth - 1, 0),
        new Point2f((float)maxWidth - 1, (float)maxHeight - 1),
        new Point2f(0, (float)maxHeight - 1)
    };

        // Matrice de transformation
        var M = Cv2.GetPerspectiveTransform(rect.Select(p => new Point2f(p.X, p.Y)).ToArray(), dst);

        // Warp
        var warped = new Mat();
        Cv2.WarpPerspective(image, warped, M, new Size((int)maxWidth, (int)maxHeight));
        return warped;
    }

    private Point[] OrderPoints(Point[] pts)
    {
        var sorted = pts.OrderBy(p => p.X + p.Y).ToArray(); // top-left is smallest sum
        var tl = sorted[0];
        var br = sorted[3];
        var tr = pts.OrderBy(p => p.X - p.Y).Last(); // top-right has biggest diff
        var bl = pts.OrderBy(p => p.X - p.Y).First(); // bottom-left has smallest diff

        return new[] { tl, tr, br, bl };
    }
}
