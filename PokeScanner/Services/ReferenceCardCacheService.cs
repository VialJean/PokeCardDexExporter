using OpenCvSharp;
using System.Collections.Concurrent;
using Size = OpenCvSharp.Size;

namespace PokeScanner.Services;
public record CarteReference(string Path, Mat Image, Mat Descriptors, KeyPoint[] KeyPoints);

public class ReferenceCardCacheService
{
    private readonly ORB _orb;
    private readonly List<CarteReference> _references = new();

    public IReadOnlyList<CarteReference> Cartes => _references;

    public ReferenceCardCacheService(int orbFeatures = 300)
    {
        _orb = ORB.Create(nFeatures: orbFeatures);

        ChargerToutesLesCartes(FileSystem.AppDataDirectory);
    }

    public void ChargerToutesLesCartes(string dossierRacine)
    {
        var chemins = Directory.GetFiles(dossierRacine, "*.webp", SearchOption.AllDirectories);

        var resultats = new ConcurrentBag<CarteReference>();

        Parallel.ForEach(chemins, chemin =>
        {
            try
            {
                var image = Cv2.ImRead(chemin, ImreadModes.Color);
                if (image.Empty())
                    return;

                Cv2.Resize(image, image, new Size(400, 600)); // Taille standardisée

                var keypoints = _orb.Detect(image);
                var descriptors = new Mat();
                _orb.Compute(image, ref keypoints, descriptors);

                resultats.Add(new CarteReference(chemin, image, descriptors, keypoints));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur de chargement {chemin} : {ex.Message}");
            }
        });

        _references.Clear();
        _references.AddRange(resultats.OrderBy(r => r.Path));
    }
}
