using System.Diagnostics;
using Timer = System.Timers.Timer;
namespace PokeScanner
{
    public partial class MainPage : ContentPage
    {
        private Timer _timer;
        private readonly MainViewModel vm;

        public MainPage(MainViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
            _timer = new Timer();
            _timer.Elapsed += _timer_Elapsed;
            _timer.Interval = 1000;
            this.vm = vm;
        }

        private async void _timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            //var a = await cameraView.CaptureImage(CancellationToken.None);
            //if (a != null)
            //{
            //    Debug.WriteLine("Nouvelle image");
            //}
            await cameraView.CaptureImage(CancellationToken.None);
        }

        private void Button_Clicked(object sender, EventArgs e)
        {
            if (_timer != null && _timer.Enabled)
            {
                _timer.Stop();
            }
            else
            {
                _timer.Start();
            }
        }

        private async void cameraView_MediaCaptured(object sender, CommunityToolkit.Maui.Views.MediaCapturedEventArgs e)
        {
            Debug.WriteLine("Nouvelle image");
            await vm.NouvelleImage(e.Media);
        }
    }

}
