using OpenCvSharp;

namespace PokeScanner.Services;

public class ImageComparerService
{
    private Mat LoadImageFromStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        return Cv2.ImDecode(data, ImreadModes.Color);
    }

    public double CompareImages(Mat img1, Mat img2)
    {
        using var orb = ORB.Create();
        var keyPoints1 = orb.Detect(img1);
        var keyPoints2 = orb.Detect(img2);

        using var descriptors1 = new Mat();
        using var descriptors2 = new Mat();

        orb.Compute(img1, ref keyPoints1, descriptors1);
        orb.Compute(img2, ref keyPoints2, descriptors2);

        if (descriptors1.Empty() || descriptors2.Empty())
            return double.MaxValue;

        using var bf = new BFMatcher(NormTypes.Hamming, crossCheck: true);
        var matches = bf.Match(descriptors1, descriptors2);

        if (matches.Length == 0)
            return double.MaxValue;

        return matches.Average(m => m.Distance);
    }

    public async Task<(string BestMatchName, double BestScore)> FindBestMatchAsync(Stream pickedImageStream, IEnumerable<string> resourcePaths)
    {
        double bestScore = double.MaxValue;
        string bestMatch = null!;
        var locker = new object(); // Pour sécuriser accès multi-thread

        pickedImageStream.Seek(0, SeekOrigin.Begin);
        using var pickedImageCopy = new MemoryStream();
        await pickedImageStream.CopyToAsync(pickedImageCopy);
        pickedImageCopy.Seek(0, SeekOrigin.Begin);

        using var pickedImageMat = LoadImageFromStream(pickedImageCopy);

        await Parallel.ForEachAsync(resourcePaths, async (resourcePath, token) =>
        {
            await using var resourceStream = await FileSystem.OpenAppPackageFileAsync(resourcePath);
            using var resourceImageMat = LoadImageFromStream(resourceStream);

            var score = CompareImages(pickedImageMat, resourceImageMat);

            lock (locker) // Empêcher les threads d'écraser les résultats
            {
                if (score < bestScore)
                {
                    bestScore = score;
                    bestMatch = resourcePath;
                }
            }
        });

        return (bestMatch, bestScore);
    }
}
