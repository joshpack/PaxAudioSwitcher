using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace PaxAudioSwitcher;

/// <summary>The app's speaker mark, drawn from vector geometry so it stays crisp at every size.</summary>
public static class AppIcon
{
    /// <summary>The speaker body, filled, in a 100x100 box.</summary>
    public static readonly Geometry Speaker = Geometry.Parse("M8,36 L28,36 L52,14 L52,86 L28,64 L8,64 Z");

    /// <summary>The sound waves, stroked.</summary>
    public static readonly Geometry Waves = Geometry.Parse("M64,34 A20,20 0 0 1 64,66 M74,20 A36,36 0 0 1 74,80");

    /// <summary>A slash across the waves, for a muted microphone.</summary>
    public static readonly Geometry Slash = Geometry.Parse("M60,30 L92,70");

    /// <summary>Renders the app icon (speaker on a rounded accent tile) for the tray and windows.</summary>
    public static WindowIcon Create(int size = 64, bool muted = false)
    {
        var dark = new SolidColorBrush(Color.Parse("#04140C"));
        var tile = new Grid { Width = size, Height = size };
        tile.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.22),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse(muted ? "#FF7A6B" : "#00C8FF"), 0),
                    new GradientStop(Color.Parse(muted ? "#F2666A" : "#19F58C"), 1),
                },
            },
        });

        var art = new Grid { Width = 100, Height = 100 };
        art.Children.Add(new ShapePath { Data = Speaker, Fill = dark });
        art.Children.Add(new ShapePath
        {
            Data = muted ? Slash : Waves, Stroke = dark, StrokeThickness = 10, StrokeLineCap = PenLineCap.Round,
        });
        tile.Children.Add(new Viewbox { Margin = new Thickness(size * 0.14), Child = art });

        tile.Measure(new Size(size, size));
        tile.Arrange(new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(tile);
        return new WindowIcon(bitmap);
    }
}
