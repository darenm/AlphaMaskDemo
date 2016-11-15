using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Graphics.Display;
using Windows.Storage.Streams;
using Windows.UI.Composition;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media.Imaging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Robmikh.CompositionSurfaceFactory;
using Windows.UI.Xaml;
using Windows.UI;

namespace AlphaMaskDemo
{
    public class CompositionMaskHelper
    {
        private readonly CanvasDevice _canvasDevice;
        private readonly CompositionGraphicsDevice _compositionDevice;
        private readonly Compositor _compositor;
        private readonly SurfaceFactory _surfaceFactory;
        private CompositionSurfaceBrush _backgroundImageBrush;
        private CompositionDrawingSurface _backgroundSurface;
        private Image _image;

        public bool ImageLoaded { get; private set; }

        public CompositionMaskHelper(Compositor compositor)
        {
            _compositor = compositor;
            _canvasDevice = new CanvasDevice();
            _compositionDevice = CanvasComposition.CreateCompositionGraphicsDevice(_compositor, _canvasDevice);
            _surfaceFactory = SurfaceFactory.CreateFromCompositor(_compositor);
        }

        public async Task LoadBackgroundImage(Image image)
        {
            _image = image;
            var bitmapSource = image.Source as BitmapImage;
            _backgroundSurface = await _surfaceFactory.CreateSurfaceFromUriAsync(bitmapSource.UriSource);
            _backgroundImageBrush = _compositor.CreateSurfaceBrush(_backgroundSurface);
            _backgroundImageBrush.Stretch = CompositionStretch.UniformToFill;
            ImageLoaded = true;
        }

        /// <summary>
        ///     This method will use a portion of the background image, masked by <see cref="opacityMaskImageUri" />,
        ///     to overlay the <see cref="overlayTarget" /> and create an effect where the contents appear to
        ///     slide beneath the <see cref="parentContainer" /> <see cref="backgroundImage" />.
        ///     Note: this method must be called after the background image is opened, otherwise the render
        ///     target bitmap is empty
        /// </summary>
        /// <param name="parentContainer">
        ///     The layout panel (typically a <see cref="Grid" />) that contains
        ///     <see cref="overlayTarget" />
        /// </param>
        /// <param name="overlayTarget">The <see cref="ItemsControl" /> that has an overlay placed upon it.</param>
        /// <param name="backgroundImage">The <see cref="Image" /> control that contains the background image.</param>
        /// <param name="opacityMaskImageUri">A <see cref="Uri" /> that locates the opacity mask png.</param>
        /// <param name="overlay">A <see cref="Rect" /> that specifies the position and dimensions of the overlay</param>
        /// <returns>Once completed, returns a <see cref="SpriteVisual" /> that contains the mask.</returns>
        public async Task ApplyOverlayAsync(Panel parentContainer, ItemsControl overlayTarget,
            Image backgroundImage, Uri opacityMaskImageUri, Rect overlay)
        {
            var areaToRender = DetermineAreaToRender(parentContainer, overlayTarget, overlay);

            var opacityBrush = await CreateOpacityMaskBrush(opacityMaskImageUri);
            var maskbrush = CreateMaskBrush(opacityBrush, _backgroundImageBrush);
            var maskSprite = CreateMaskSprite(overlay, maskbrush);

            ElementCompositionPreview.SetElementChildVisual(overlayTarget, maskSprite);
        }

        public async Task<SpriteVisual[]> ApplyMultipleOverlaysAsync(Panel parentContainer, ItemsControl overlayTarget,
            Image backgroundImage, IEnumerable<Overlay> overlays)
        {
            var overlayArray = overlays as Overlay[] ?? overlays.ToArray();
            if ((overlays == null) || !overlayArray.Any())
            {
                return null;
            }

            var spriteVisuals = new List<SpriteVisual>();

            // as we want to add multiple overlay visuals, we need to add them to 
            // a container visual first.
            // we then add the container visual to the target element.
            var targetContainerVisual = _compositor.CreateContainerVisual();

            foreach (var overlay in overlayArray)
            {
                // this determines the region of the background image brush I need to render over the GridView
                // to create the illusion of transparency
                var areaToRender = DetermineAreaToRender(parentContainer, overlayTarget, overlay.OverlayTargetRect);

                // loads the correct image mask
                if (overlay.OverlayMaskSurfaceBrush == null)
                {
                    overlay.OverlayMaskSurfaceBrush = await CreateOpacityMaskBrush(overlay.OverlayMaskImageUri);
                }

                // starts to create the sprite visual for the overlay
                var maskSprite = _compositor.CreateSpriteVisual();
                // ensures the sprite matches the size of the image element, so that the 
                // background image brush is sized the same
                maskSprite.Size = new Vector2((float) _image.ActualWidth, (float)_image.ActualHeight);
                // here's where I run into a problem
                // Effects can only be performed on brushes...
                // but the only way I can see how to get the fragment I want to display
                // is to load the bacground image brush, then clip the SpriteVisual
                // then move the SpriteVisual.Offset to align correctly on the GridView.
                maskSprite.Brush = _backgroundImageBrush;
                var leftInset = (float)areaToRender.Left;
                var topInset = (float)areaToRender.Top;
                var rightInset = (float)_image.ActualWidth - (float)areaToRender.Width;
                var bottomInset = (float)_image.ActualHeight - (float)areaToRender.Height;
                maskSprite.Clip = _compositor.CreateInsetClip(
                    leftInset,
                    topInset,
                    rightInset - leftInset,
                    bottomInset - topInset);
                maskSprite.Offset = new Vector3(
                    (float)overlay.OverlayTargetRect.Left - leftInset, 
                    (float)overlay.OverlayTargetRect.Top - topInset, 0);

                // initially setting opacity to less than 1 so it is apparent
                maskSprite.Opacity = overlay.InitialRenderTransparent ? 0 : 0.9f;
                targetContainerVisual.Children.InsertAtTop(maskSprite);
                spriteVisuals.Add(maskSprite);
            }

            ElementCompositionPreview.SetElementChildVisual(overlayTarget, targetContainerVisual);
            return spriteVisuals.ToArray();
        }

        private SpriteVisual CreateMaskSprite(Rect overlay, CompositionMaskBrush maskbrush)
        {
            var maskSprite = _compositor.CreateSpriteVisual();
            maskSprite.Size = new Vector2((float) overlay.Width, (float) overlay.Height);
            maskSprite.Offset = new Vector3((float) overlay.X, (float) overlay.Y, 0);
            maskSprite.Brush = maskbrush; // paint it with the maskbrush
            return maskSprite;
        }

        private CompositionMaskBrush CreateMaskBrush(CompositionSurfaceBrush opacityBrush,
            CompositionSurfaceBrush backgroundImageBrush)
        {
            var maskbrush = _compositor.CreateMaskBrush();
            maskbrush.Mask = opacityBrush; // surfacebrush with gradient opacity mask asset
            maskbrush.Source = backgroundImageBrush; // surfacebrush with background image that is to be masked
            return maskbrush;
        }

        // Capture the image from our background.
        //
        // Note: this is just the <Image/> element, not the Grid. If we took the <Grid/>, 
        // we would also have all of the child elements, such as the <GridView/> rendered as well -
        // which defeats the purpose!
        // 
        // Note 2: this method must be called after the background image is opened, otherwise
        // the render target bitmap is empty
        private static async Task<RenderTargetBitmap> CreateBackgroundBitmap(Image backgroundImage)
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(backgroundImage);
            return bitmap;
        }

        private async Task<CompositionSurfaceBrush> CreateOpacityMaskBrush(Uri opacityMaskImageUri)
        {
            // load in our opacity mask image.
            // this is created in a graphic tool such as paint.net
            var opacityMaskSurface = await _surfaceFactory.CreateSurfaceFromUriAsync(opacityMaskImageUri);

            // create surfacebrush with ICompositionSurface that contains the gradient opacity mask asset
            var opacityBrush = _compositor.CreateSurfaceBrush(opacityMaskSurface);
            opacityBrush.Stretch = CompositionStretch.UniformToFill;
            return opacityBrush;
        }

        /// <summary>
        ///     Determines what region of the background we need to "cut out" for the overlay
        /// </summary>
        /// <param name="parentContainer"></param>
        /// <param name="overlayTarget"></param>
        /// <param name="overlay"></param>
        /// <returns></returns>
        private static Rect DetermineAreaToRender(Panel parentContainer, ItemsControl overlayTarget, Rect overlay)
        {
            var gt = overlayTarget.TransformToVisual(parentContainer);
            var elementPosition = gt.TransformPoint(new Point(overlay.X, overlay.Y));

            var areaToRender = new Rect(elementPosition.X, elementPosition.Y, overlay.Width, overlay.Height);
            return areaToRender;
        }

        /// <summary>
        ///     This method allows us to reuse a background image that has already been processed.
        /// </summary>
        /// <param name="pixels">The pixel buffer.</param>
        /// <param name="bitmap">The output from RenderTargetBitmap.</param>
        /// <param name="dpi">The view DPI where the background was rendered.</param>
        /// <param name="areaToRender">The region of the background we wish to cut out.</param>
        /// <returns>A <see cref="CompositionSurfaceBrush" /> containing the portion of the background we want.</returns>
        private CompositionSurfaceBrush CreateBackgroundBrush(IBuffer pixels, RenderTargetBitmap bitmap, float dpi,
            Rect areaToRender)
        {
            // load the pixels from RenderTargetBitmap onto a CompositionDrawingSurface
            CompositionDrawingSurface uiElementBitmapSurface;
            using (
                // this is the entire background image
                // Note we are using the display DPI here.
                var canvasBitmap = CanvasBitmap.CreateFromBytes(
                    _canvasDevice, pixels.ToArray(),
                    bitmap.PixelWidth,
                    bitmap.PixelHeight,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    dpi)
            )
            {
                // we create a surface we can draw on in memory.
                // note we are using the desired size of our overlay
                uiElementBitmapSurface =
                    _compositionDevice.CreateDrawingSurface(
                        new Size(areaToRender.Width, areaToRender.Height),
                        DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
                using (var session = CanvasComposition.CreateDrawingSession(uiElementBitmapSurface))
                {
                    // here we draw just the part of the background image we wish to use to overlay
                    session.DrawImage(canvasBitmap, 0, 0, areaToRender);
                }
            }

            var backgroundBrush = _compositor.CreateSurfaceBrush(uiElementBitmapSurface);
            backgroundBrush.Stretch = CompositionStretch.UniformToFill;
            return backgroundBrush;
        }
    }

    public class Overlay
    {
        public Uri OverlayMaskImageUri { get; set; }
        public Rect OverlayTargetRect { get; set; }
        public bool InitialRenderTransparent { get; set; }
        public CompositionSurfaceBrush OverlayMaskSurfaceBrush { get; set; }
    }
}