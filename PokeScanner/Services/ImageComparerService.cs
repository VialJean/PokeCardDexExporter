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

    public double CompareImageStreams(Stream stream1, Stream stream2)
    {
        using var img1 = LoadImageFromStream(stream1);
        using var img2 = LoadImageFromStream(stream2);

        using var orb = ORB.Create();
        var keyPoints1 = orb.Detect(img1);
        var keyPoints2 = orb.Detect(img2);

        using var descriptors1 = new Mat();
        using var descriptors2 = new Mat();

        orb.Compute(img1, ref keyPoints1, descriptors1);
        orb.Compute(img2, ref keyPoints2, descriptors2);

        using var bf = new BFMatcher(NormTypes.Hamming, crossCheck: true);
        var matches = bf.Match(descriptors1, descriptors2);

        double averageDistance = matches.Average(m => m.Distance);

        return averageDistance;
    }

    public async Task<(string BestMatchName, double BestScore)> FindBestMatchAsync(Stream pickedImageStream, IEnumerable<string> resourcePaths)
    {
        double bestScore = double.MaxValue;
        string bestMatch = null!;

        foreach (var resourcePath in resourcePaths)
        {
            await using var resourceStream = await FileSystem.OpenAppPackageFileAsync(resourcePath);
            var score = CompareImageStreams(resourceStream, pickedImageStream);

            if (score < bestScore)
            {
                bestScore = score;
                bestMatch = resourcePath;
            }

            // Important : remettre pickedImageStream au début pour chaque comparaison
            if (pickedImageStream.CanSeek)
                pickedImageStream.Seek(0, SeekOrigin.Begin);
        }

        return (bestMatch, bestScore);
    }
}
