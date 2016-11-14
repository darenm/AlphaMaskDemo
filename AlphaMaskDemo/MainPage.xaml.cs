using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace AlphaMaskDemo
{
    /// <summary>
    ///     An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly CompositionMaskHelper _compositionMaskHelper;
        private bool _imageLoaded;
        private SpriteVisual[] _masks;

        public MainPage()
        {
            InitializeComponent();

            // setup composition
            _compositionMaskHelper = new CompositionMaskHelper(ElementCompositionPreview.GetElementVisual(this).Compositor);
            Posters.SizeChanged += OnSizeChanged;
        }

        private async void OnSizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
        {
            if (!_imageLoaded)
            {
                return;
            }
            await RenderOverlay();
        }

        private async void ImageBrush_OnImageOpened(object sender, RoutedEventArgs e)
        {
            _imageLoaded = true;
            await RenderOverlay();
        }

        private async Task RenderOverlay()
        {
            _masks = await _compositionMaskHelper.ApplyMultipleOverlaysAsync(LayoutRoot, Posters, BackgroundImage,
                new[]
                {
                    new Overlay
                    {
                        OverlayMaskImageUri = new Uri("ms-appx:///Assets/OpacityMask.Png"),
                        OverlayTargetRect = new Rect(0, 0, Posters.ActualWidth, 30),
                        InitialRenderTransparent = false
                    },
                    new Overlay
                    {
                        OverlayMaskImageUri = new Uri("ms-appx:///Assets/BottomOpacityMask.Png"),
                        OverlayTargetRect = new Rect(0, Posters.ActualHeight - 30, Posters.ActualWidth, 30),
                        InitialRenderTransparent = false
                    }
                });
        }

        private void TopClick(object sender, RoutedEventArgs e)
        {
            if (_masks != null && _masks.Length == 2)
            {
                _masks[0].Opacity = _masks[0].Opacity == 0f ? 1.0f : 0f;
            }
        }

        private void BottomClick(object sender, RoutedEventArgs e)
        {
            if (_masks != null && _masks.Length == 2)
            {
                _masks[1].Opacity = _masks[1].Opacity == 0f ? 1.0f : 0f;
            }
        }
    }
}