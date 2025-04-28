using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeScanner.Services;

namespace PokeScanner;

public partial class MainViewModel : ObservableObject
{
    private readonly ImageComparerService _comparerService = new ImageComparerService();

    [ObservableProperty]
    private string result;

    [ObservableProperty]
    private string imageSource;

    public MainViewModel()
    {
        Result = string.Empty;
    }

    [RelayCommand]
    public async Task PickAndCompareAsync()
    {
        var cartesDirectory = Path.Combine(FileSystem.AppDataDirectory, "Cartes");
        var allCardPaths = Directory.GetFiles(cartesDirectory, "*.webp");
        var picked = await FilePicker.PickAsync(new PickOptions
        {
            FileTypes = FilePickerFileType.Images
        });

        if (picked == null) return;

        ImageSource = string.Empty;
        Result = "Scan en cours...";

        await using var pickedStream = await picked.OpenReadAsync(); ;

        var (bestMatchPath, bestScore) = await _comparerService.FindBestMatchAsync(pickedStream, allCardPaths);

        if (bestScore < 40)
        {
            var cardName = System.IO.Path.GetFileNameWithoutExtension(bestMatchPath);
            ImageSource = bestMatchPath;
            Result = $"Carte reconnue : {cardName} (Score {bestScore:F2})";
        }
        else
        {
            Result = $"Aucune correspondance. Meilleur score : {bestScore:F2}";
        }
    }
}
