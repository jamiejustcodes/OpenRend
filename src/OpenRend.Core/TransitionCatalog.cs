using System.Collections.ObjectModel;

namespace OpenRend.Core;

public sealed record TransitionPresetInfo(
    string Name,
    VideoTransitionKind Kind,
    string Category,
    IReadOnlyDictionary<string, string> Parameters);

public sealed record TransitionPluginInfo(
    VideoTransitionKind Kind,
    string DisplayName,
    string PluginId,
    string Category,
    IReadOnlyList<TransitionPresetInfo> Presets);

public static class TransitionCatalog
{
    public static readonly IReadOnlyList<string> Categories =
    [
        "All Plug-ins",
        "Favorites",
        "Fades",
        "Wipes",
        "Reveals",
        "Transforms",
        "3D and Peels"
    ];

    public static readonly IReadOnlyList<TransitionPluginInfo> Plugins;

    static TransitionCatalog()
    {
        var list = new List<TransitionPluginInfo>();

        // 1. Dissolve (Fades)
        list.Add(CreatePlugin(
            VideoTransitionKind.Dissolve,
            "Dissolve",
            "com.vegascreativesoftware:dissolve",
            "Fades",
            [
                Preset("Default", VideoTransitionKind.Dissolve, "Fades", ("Type", "0")),
                Preset("Additive Dissolve", VideoTransitionKind.Dissolve, "Fades", ("Type", "1")),
                Preset("Subtractive Dissolve", VideoTransitionKind.Dissolve, "Fades", ("Type", "2")),
                Preset("Subtractive Crossfade", VideoTransitionKind.Dissolve, "Fades", ("Type", "2")),
                Preset("Color Bleed", VideoTransitionKind.Dissolve, "Fades", ("Type", "3")),
                Preset("Color Bleed Fast Red", VideoTransitionKind.Dissolve, "Fades", ("Type", "3"), ("ColorBleedSpeedRed", "1.0")),
                Preset("Color Bleed Fast Green", VideoTransitionKind.Dissolve, "Fades", ("Type", "3"), ("ColorBleedSpeedGreen", "1.0")),
                Preset("Color Bleed Fast Blue", VideoTransitionKind.Dissolve, "Fades", ("Type", "3"), ("ColorBleedSpeedBlue", "1.0")),
                Preset("Color Bleed Fast Alpha", VideoTransitionKind.Dissolve, "Fades", ("Type", "3"), ("ColorBleedSpeedAlpha", "1.0")),
                Preset("Color Morph", VideoTransitionKind.Dissolve, "Fades", ("Type", "4")),
                Preset("Color Morph Fast Red", VideoTransitionKind.Dissolve, "Fades", ("Type", "4"), ("ColorMorphSpeedRed", "1.0")),
                Preset("Color Morph Fast Green", VideoTransitionKind.Dissolve, "Fades", ("Type", "4"), ("ColorMorphSpeedGreen", "1.0")),
                Preset("Color Morph Fast Blue", VideoTransitionKind.Dissolve, "Fades", ("Type", "4"), ("ColorMorphSpeedBlue", "1.0")),
                Preset("Color Morph Fast Alpha", VideoTransitionKind.Dissolve, "Fades", ("Type", "4"), ("ColorMorphSpeedAlpha", "1.0")),
                Preset("Threshold Dissolve", VideoTransitionKind.Dissolve, "Fades", ("Type", "5")),
                Preset("Threshold Appear", VideoTransitionKind.Dissolve, "Fades", ("Type", "6")),
                Preset("Fade Through Grayscale", VideoTransitionKind.Dissolve, "Fades", ("Type", "7")),
                Preset("Fade Through Black", VideoTransitionKind.Dissolve, "Fades", ("Type", "8"), ("FadeThroughColor", "0 0 0 1")),
                Preset("Fade Through White", VideoTransitionKind.Dissolve, "Fades", ("Type", "8"), ("FadeThroughColor", "1 1 1 1")),
                Preset("Fade Through Red", VideoTransitionKind.Dissolve, "Fades", ("Type", "8"), ("FadeThroughColor", "1 0 0 1")),
                Preset("Fade Through Green", VideoTransitionKind.Dissolve, "Fades", ("Type", "8"), ("FadeThroughColor", "0 0.501961 0 1")),
                Preset("Fade Through Blue", VideoTransitionKind.Dissolve, "Fades", ("Type", "8"), ("FadeThroughColor", "0 0 1 1")),
                Preset("Fade Through Transparent", VideoTransitionKind.Dissolve, "Fades", ("Type", "8"), ("FadeThroughColor", "0 0 0 0")),
                Preset("Red then Green then Blue Crossfade", VideoTransitionKind.Dissolve, "Fades", ("Type", "9"))
            ]));

        // 2. Flash (Fades)
        list.Add(CreatePlugin(
            VideoTransitionKind.Flash,
            "Flash",
            "com.vegascreativesoftware:flash",
            "Fades",
            [
                Preset("Hard Flash", VideoTransitionKind.Flash, "Fades", ("HorizontalDiffusion", "0.0"), ("VerticalDiffusion", "0.0"), ("Tint", "1 1 1")),
                Preset("Soft Flash", VideoTransitionKind.Flash, "Fades", ("HorizontalDiffusion", "0.5"), ("VerticalDiffusion", "0.5"), ("Tint", "1 1 1")),
                Preset("Yellow Flash", VideoTransitionKind.Flash, "Fades", ("HorizontalDiffusion", "0.3"), ("VerticalDiffusion", "0.3"), ("Tint", "1 1 0"))
            ]));

        // 3. Linear Wipe (Wipes)
        list.Add(CreatePlugin(
            VideoTransitionKind.LinearWipe,
            "Linear Wipe",
            "com.vegascreativesoftware:linearwipe",
            "Wipes",
            [
                Preset("Top-Left, Diagonal, Soft Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "45.0"), ("Feather", "0.1")),
                Preset("Top-Down, Hard Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "90.0"), ("Feather", "0.0")),
                Preset("Left-Right, Hard Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "0.0"), ("Feather", "0.0")),
                Preset("Bottom-Up, Hard Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "270.0"), ("Feather", "0.0")),
                Preset("Right-Left, Hard Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "180.0"), ("Feather", "0.0")),
                Preset("Right-Left, Soft Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "180.0"), ("Feather", "0.5")),
                Preset("Top-Down, Soft Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "90.0"), ("Feather", "0.5")),
                Preset("Left-Right, Soft Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "0.0"), ("Feather", "0.5")),
                Preset("Top-Left, Diagonal, Blend In", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "45.0"), ("Feather", "1.0")),
                Preset("Bottom-Left, Diagonal, Soft Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "315.0"), ("Feather", "0.1")),
                Preset("Bottom-Right, Diagonal, Soft Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "225.0"), ("Feather", "0.1")),
                Preset("Top-Right, Diagonal, Soft Edge", VideoTransitionKind.LinearWipe, "Wipes", ("Angle", "135.0"), ("Feather", "0.1"))
            ]));

        // 4. Clock Wipe (Wipes)
        list.Add(CreatePlugin(
            VideoTransitionKind.ClockWipe,
            "Clock Wipe",
            "com.vegascreativesoftware:clockwipe",
            "Wipes",
            [
                Preset("Clockwise, Soft Edge", VideoTransitionKind.ClockWipe, "Wipes", ("Direction", "0"), ("Feather", "0.1")),
                Preset("Counter Clockwise, Soft Edge", VideoTransitionKind.ClockWipe, "Wipes", ("Direction", "1"), ("Feather", "0.1")),
                Preset("Clockwise, Hard Edge", VideoTransitionKind.ClockWipe, "Wipes", ("Direction", "0"), ("Feather", "0.0")),
                Preset("Counter Clockwise, Hard Edge", VideoTransitionKind.ClockWipe, "Wipes", ("Direction", "1"), ("Feather", "0.0")),
                Preset("Clockwise, Blend In", VideoTransitionKind.ClockWipe, "Wipes", ("Direction", "0"), ("Feather", "1.0")),
                Preset("Counter Clockwise, Blend In", VideoTransitionKind.ClockWipe, "Wipes", ("Direction", "1"), ("Feather", "1.0"))
            ]));

        // 5. Venetian Blinds (Wipes)
        list.Add(CreatePlugin(
            VideoTransitionKind.VenetianBlinds,
            "Venetian Blinds",
            "com.vegascreativesoftware:venetianblinds",
            "Wipes",
            [
                Preset("One Vertical Blind", VideoTransitionKind.VenetianBlinds, "Wipes", ("Angle", "0.0"), ("Feather", "0.0"), ("NumberOfBlinds", "1")),
                Preset("Five Vertical Blinds", VideoTransitionKind.VenetianBlinds, "Wipes", ("Angle", "0.0"), ("Feather", "0.0"), ("NumberOfBlinds", "5")),
                Preset("Seven Horizontal Blinds", VideoTransitionKind.VenetianBlinds, "Wipes", ("Angle", "90.0"), ("Feather", "0.0"), ("NumberOfBlinds", "7")),
                Preset("Sloped Blinds", VideoTransitionKind.VenetianBlinds, "Wipes", ("Angle", "45.0"), ("Feather", "0.07"), ("NumberOfBlinds", "3")),
                Preset("Fifteen Tilted Blinds", VideoTransitionKind.VenetianBlinds, "Wipes", ("Angle", "105.0"), ("Feather", "0.01"), ("NumberOfBlinds", "15")),
                Preset("Many Crooked Blinds", VideoTransitionKind.VenetianBlinds, "Wipes", ("Angle", "190.0"), ("Feather", "0.0"), ("NumberOfBlinds", "30"))
            ]));

        // 6. Star Wipe (Wipes)
        list.Add(CreatePlugin(
            VideoTransitionKind.StarWipe,
            "Star Wipe",
            "com.vegascreativesoftware:starwipe",
            "Wipes",
            [
                Preset("Star", VideoTransitionKind.StarWipe, "Wipes", ("Arms", "5"), ("Feather", "0.1")),
                Preset("Gear", VideoTransitionKind.StarWipe, "Wipes", ("Arms", "12"), ("Feather", "0.1")),
                Preset("Double Circles", VideoTransitionKind.StarWipe, "Wipes", ("Arms", "2"), ("Feather", "0.1")),
                Preset("Diamond", VideoTransitionKind.StarWipe, "Wipes", ("Arms", "4"), ("Feather", "0.1")),
                Preset("Hexagon", VideoTransitionKind.StarWipe, "Wipes", ("Arms", "6"), ("Feather", "0.1")),
                Preset("Sun Rays", VideoTransitionKind.StarWipe, "Wipes", ("Arms", "16"), ("Feather", "0.2")),
                Preset("Opening Eye", VideoTransitionKind.StarWipe, "Wipes", ("Arms", "32"), ("Feather", "0.2"))
            ]));

        // 7. Barn Door (Reveals)
        list.Add(CreatePlugin(
            VideoTransitionKind.BarnDoor,
            "Barn Door",
            "com.vegascreativesoftware:barndoor",
            "Reveals",
            [
                Preset("Vertical, In, No Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "1"), ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Vertical, Out, No Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "1"), ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Horizontal, In, No Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "0"), ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Horizontal, Out, No Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "0"), ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Vertical, In, White Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "1"), ("Direction", "0"), ("BorderSize", "0.05"), ("BorderFeather", "0.0"), ("BorderColor", "1 1 1")),
                Preset("Horizontal, Out, White Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "0"), ("Direction", "1"), ("BorderSize", "0.05"), ("BorderFeather", "0.0"), ("BorderColor", "1 1 1")),
                Preset("Vertical, Out, Black Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "1"), ("Direction", "1"), ("BorderSize", "0.05"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Horizontal, In, Black Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "0"), ("Direction", "0"), ("BorderSize", "0.05"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Horizontal, Out, Yellow Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "0"), ("Direction", "1"), ("BorderSize", "0.05"), ("BorderFeather", "0.0"), ("BorderColor", "1 1 0")),
                Preset("Vertical, Out, Red Border", VideoTransitionKind.BarnDoor, "Reveals", ("Orientation", "1"), ("Direction", "1"), ("BorderSize", "0.05"), ("BorderFeather", "0.0"), ("BorderColor", "1 0 0"))
            ]));

        // 8. Iris (Reveals)
        list.Add(CreatePlugin(
            VideoTransitionKind.Iris,
            "Iris",
            "com.vegascreativesoftware:iris",
            "Reveals",
            [
                Preset("Circle, Out, Center", VideoTransitionKind.Iris, "Reveals", ("Shape", "0"), ("Direction", "1"), ("Center", "0.5 0.5"), ("Feather", "0.0"), ("Border", "0.0"), ("Color", "0 0 0")),
                Preset("Square, In, Center", VideoTransitionKind.Iris, "Reveals", ("Shape", "1"), ("Direction", "0"), ("Center", "0.5 0.5"), ("Feather", "0.0"), ("Border", "0.0"), ("Color", "0 0 0")),
                Preset("Diamond, Out, Center", VideoTransitionKind.Iris, "Reveals", ("Shape", "2"), ("Direction", "1"), ("Center", "0.5 0.5"), ("Feather", "0.0"), ("Border", "0.0"), ("Color", "0 0 0")),
                Preset("Rectangle, In, Center", VideoTransitionKind.Iris, "Reveals", ("Shape", "3"), ("Direction", "0"), ("Center", "0.5 0.5"), ("Feather", "0.0"), ("Border", "0.0"), ("Color", "0 0 0")),
                Preset("Triangle Down, Out, Center", VideoTransitionKind.Iris, "Reveals", ("Shape", "4"), ("Direction", "1"), ("Center", "0.5 0.5"), ("Feather", "0.0"), ("Border", "0.0"), ("Color", "0 0 0")),
                Preset("Circle, Out, White Border", VideoTransitionKind.Iris, "Reveals", ("Shape", "0"), ("Direction", "1"), ("Center", "0.5 0.5"), ("Feather", "0.0"), ("Border", "0.05"), ("Color", "1 1 1")),
                Preset("Square, Out, Black Border", VideoTransitionKind.Iris, "Reveals", ("Shape", "1"), ("Direction", "1"), ("Center", "0.5 0.5"), ("Feather", "0.0"), ("Border", "0.05"), ("Color", "0 0 0")),
                Preset("Diamond, In, Feathered Edge", VideoTransitionKind.Iris, "Reveals", ("Shape", "2"), ("Direction", "0"), ("Center", "0.5 0.5"), ("Feather", "0.5"), ("Border", "0.0"), ("Color", "0 0 0")),
                Preset("Oval, Out, Feathered Blue Border", VideoTransitionKind.Iris, "Reveals", ("Shape", "0"), ("Direction", "1"), ("Center", "0.5 0.5"), ("Feather", "0.2"), ("Border", "0.05"), ("Color", "0 0 1")),
                Preset("Circle, Out, Upper Left, White Border", VideoTransitionKind.Iris, "Reveals", ("Shape", "0"), ("Direction", "1"), ("Center", "0.1 0.9"), ("Feather", "0.0"), ("Border", "0.05"), ("Color", "1 1 1"))
            ]));

        // 9. Split (Reveals)
        list.Add(CreatePlugin(
            VideoTransitionKind.Split,
            "Split",
            "com.vegascreativesoftware:split",
            "Reveals",
            [
                Preset("Push, Out, Center", VideoTransitionKind.Split, "Reveals", ("SplitMode", "0"), ("Direction", "1"), ("Center", "0.5 0.5"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Wipe, Out, Center", VideoTransitionKind.Split, "Reveals", ("SplitMode", "1"), ("Direction", "1"), ("Center", "0.5 0.5"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Squeeze, Out, Center", VideoTransitionKind.Split, "Reveals", ("SplitMode", "2"), ("Direction", "1"), ("Center", "0.5 0.5"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Push, In, Center", VideoTransitionKind.Split, "Reveals", ("SplitMode", "0"), ("Direction", "0"), ("Center", "0.5 0.5"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Wipe, In, Center", VideoTransitionKind.Split, "Reveals", ("SplitMode", "1"), ("Direction", "0"), ("Center", "0.5 0.5"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Squeeze, In, Center", VideoTransitionKind.Split, "Reveals", ("SplitMode", "2"), ("Direction", "0"), ("Center", "0.5 0.5"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Push, In, Top-Left Corner, White Border", VideoTransitionKind.Split, "Reveals", ("SplitMode", "0"), ("Direction", "0"), ("Center", "0.06 0.94"), ("BorderSize", "0.1"), ("BorderFeather", "0.3"), ("BorderColor", "1 1 1")),
                Preset("Push, Out, Top-Right Corner, Blue Border", VideoTransitionKind.Split, "Reveals", ("SplitMode", "0"), ("Direction", "1"), ("Center", "0.94 0.94"), ("BorderSize", "0.1"), ("BorderFeather", "0.2"), ("BorderColor", "0 0 1"))
            ]));

        // 10. Push (Transforms)
        list.Add(CreatePlugin(
            VideoTransitionKind.Push,
            "Push",
            "com.vegascreativesoftware:push",
            "Transforms",
            [
                Preset("Push Up", VideoTransitionKind.Push, "Transforms", ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0"), ("PushOffPreviousImage", "true")),
                Preset("Push Down", VideoTransitionKind.Push, "Transforms", ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0"), ("PushOffPreviousImage", "true")),
                Preset("Push Left", VideoTransitionKind.Push, "Transforms", ("Direction", "2"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0"), ("PushOffPreviousImage", "true")),
                Preset("Push Right", VideoTransitionKind.Push, "Transforms", ("Direction", "3"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0"), ("PushOffPreviousImage", "true")),
                Preset("Push In, Down", VideoTransitionKind.Push, "Transforms", ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0"), ("PushOffPreviousImage", "false")),
                Preset("Push In, Left", VideoTransitionKind.Push, "Transforms", ("Direction", "2"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0"), ("PushOffPreviousImage", "false")),
                Preset("Push In, Right", VideoTransitionKind.Push, "Transforms", ("Direction", "3"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0"), ("PushOffPreviousImage", "false")),
                Preset("Push In, Up", VideoTransitionKind.Push, "Transforms", ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0"), ("PushOffPreviousImage", "false")),
                Preset("Push In, Left, White Border", VideoTransitionKind.Push, "Transforms", ("Direction", "2"), ("BorderSize", "0.05"), ("BorderFeather", "0.0"), ("BorderColor", "1 1 1"), ("PushOffPreviousImage", "false")),
                Preset("Push In, Right, Yellow Border", VideoTransitionKind.Push, "Transforms", ("Direction", "3"), ("BorderSize", "0.08"), ("BorderFeather", "0.08"), ("BorderColor", "1 1 0"), ("PushOffPreviousImage", "false")),
                Preset("Push Up, Red Border", VideoTransitionKind.Push, "Transforms", ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.08"), ("BorderColor", "1 0 0"), ("PushOffPreviousImage", "false")),
                Preset("Push Down, Blue Border", VideoTransitionKind.Push, "Transforms", ("Direction", "1"), ("BorderSize", "0.05"), ("BorderFeather", "0.02"), ("BorderColor", "0 0 1"), ("PushOffPreviousImage", "false"))
            ]));

        // 11. Slide (Transforms)
        list.Add(CreatePlugin(
            VideoTransitionKind.Slide,
            "Slide",
            "com.vegascreativesoftware:slide",
            "Transforms",
            [
                Preset("Slide In, Top-Left Corner", VideoTransitionKind.Slide, "Transforms", ("Angle", "45.0"), ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Slide Out, Bottom-Right Corner", VideoTransitionKind.Slide, "Transforms", ("Angle", "225.0"), ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Slide In, Left-Right", VideoTransitionKind.Slide, "Transforms", ("Angle", "0.0"), ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Slide In, Bottom-Up", VideoTransitionKind.Slide, "Transforms", ("Angle", "270.0"), ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Slide In, Right-Left", VideoTransitionKind.Slide, "Transforms", ("Angle", "180.0"), ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Slide In, Top-Down", VideoTransitionKind.Slide, "Transforms", ("Angle", "90.0"), ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Slide Out, Left-Right", VideoTransitionKind.Slide, "Transforms", ("Angle", "180.0"), ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Slide Out, Bottom-Up", VideoTransitionKind.Slide, "Transforms", ("Angle", "90.0"), ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Slide Out, Right-Left", VideoTransitionKind.Slide, "Transforms", ("Angle", "0.0"), ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Slide Out, Top-Down", VideoTransitionKind.Slide, "Transforms", ("Angle", "270.0"), ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0"))
            ]));

        // 12. Zoom (Transforms)
        list.Add(CreatePlugin(
            VideoTransitionKind.Zoom,
            "Zoom",
            "com.vegascreativesoftware:zoom",
            "Transforms",
            [
                Preset("Zoom In, Center", VideoTransitionKind.Zoom, "Transforms", ("Direction", "0"), ("Center", "0.5 0.5"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom In, Top-Left", VideoTransitionKind.Zoom, "Transforms", ("Direction", "0"), ("Center", "0.0 1.0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom In, Top-Right", VideoTransitionKind.Zoom, "Transforms", ("Direction", "0"), ("Center", "1.0 1.0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom In, Bottom-Left", VideoTransitionKind.Zoom, "Transforms", ("Direction", "0"), ("Center", "0.0 0.0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom In, Bottom-Right", VideoTransitionKind.Zoom, "Transforms", ("Direction", "0"), ("Center", "1.0 0.0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom Out, Center", VideoTransitionKind.Zoom, "Transforms", ("Direction", "1"), ("Center", "0.5 0.5"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom Out, Top-Left", VideoTransitionKind.Zoom, "Transforms", ("Direction", "1"), ("Center", "0.0 1.0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom Out, Top-Right", VideoTransitionKind.Zoom, "Transforms", ("Direction", "1"), ("Center", "1.0 1.0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom Out, Bottom-Left", VideoTransitionKind.Zoom, "Transforms", ("Direction", "1"), ("Center", "0.0 0.0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom Out, Bottom-Right", VideoTransitionKind.Zoom, "Transforms", ("Direction", "1"), ("Center", "1.0 0.0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Zoom Out, Center, Red Border", VideoTransitionKind.Zoom, "Transforms", ("Direction", "1"), ("Center", "0.5 0.5"), ("BorderSize", "0.05"), ("BorderFeather", "0.0"), ("BorderColor", "1 0 0")),
                Preset("Zoom In, Center, White Border", VideoTransitionKind.Zoom, "Transforms", ("Direction", "0"), ("Center", "0.5 0.5"), ("BorderSize", "0.15"), ("BorderFeather", "0.0"), ("BorderColor", "1 1 1")),
                Preset("Zoom Out, Top-Left, Blue Border", VideoTransitionKind.Zoom, "Transforms", ("Direction", "1"), ("Center", "0.0 1.0"), ("BorderSize", "0.03"), ("BorderFeather", "0.05"), ("BorderColor", "0 0 1")),
                Preset("Zoom In, Bottom-Right, Yellow Border", VideoTransitionKind.Zoom, "Transforms", ("Direction", "0"), ("Center", "1.0 0.0"), ("BorderSize", "0.0"), ("BorderFeather", "0.1"), ("BorderColor", "1 1 0"))
            ]));

        // 13. Swap (Transforms)
        list.Add(CreatePlugin(
            VideoTransitionKind.Swap,
            "Swap",
            "com.vegascreativesoftware:swap",
            "Transforms",
            [
                Preset("Swap Left", VideoTransitionKind.Swap, "Transforms", ("Direction", "2"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Swap Right", VideoTransitionKind.Swap, "Transforms", ("Direction", "3"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Swap Up", VideoTransitionKind.Swap, "Transforms", ("Direction", "0"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Swap Down", VideoTransitionKind.Swap, "Transforms", ("Direction", "1"), ("BorderSize", "0.0"), ("BorderFeather", "0.0"), ("BorderColor", "0 0 0")),
                Preset("Swap Left, White Border", VideoTransitionKind.Swap, "Transforms", ("Direction", "2"), ("BorderSize", "0.1"), ("BorderFeather", "0.0"), ("BorderColor", "1 1 1")),
                Preset("Swap Right, Blue Border", VideoTransitionKind.Swap, "Transforms", ("Direction", "3"), ("BorderSize", "0.0"), ("BorderFeather", "0.1"), ("BorderColor", "0 0 1")),
                Preset("Swap Up, Yellow Border", VideoTransitionKind.Swap, "Transforms", ("Direction", "0"), ("BorderSize", "0.05"), ("BorderFeather", "0.05"), ("BorderColor", "1 1 0")),
                Preset("Swap Down, Black Border", VideoTransitionKind.Swap, "Transforms", ("Direction", "1"), ("BorderSize", "0.01"), ("BorderFeather", "0.06"), ("BorderColor", "0 0 0"))
            ]));

        // 14. Squeeze (Transforms)
        list.Add(CreatePlugin(
            VideoTransitionKind.Squeeze,
            "Squeeze",
            "com.vegascreativesoftware:squeeze",
            "Transforms",
            [
                Preset("Squeeze Down", VideoTransitionKind.Squeeze, "Transforms", ("Direction", "1")),
                Preset("Squeeze Up", VideoTransitionKind.Squeeze, "Transforms", ("Direction", "0")),
                Preset("Squeeze Right-Left", VideoTransitionKind.Squeeze, "Transforms", ("Direction", "2")),
                Preset("Squeeze Left-Right", VideoTransitionKind.Squeeze, "Transforms", ("Direction", "3")),
                Preset("Lateral", VideoTransitionKind.Squeeze, "Transforms", ("Direction", "2")),
                Preset("Vertical", VideoTransitionKind.Squeeze, "Transforms", ("Direction", "0"))
            ]));

        // 15. Cross Effect (Transforms)
        list.Add(CreatePlugin(
            VideoTransitionKind.CrossEffect,
            "Cross Effect",
            "com.vegascreativesoftware:crosseffect",
            "Transforms",
            [
                Preset("Cross Zoom A/B", VideoTransitionKind.CrossEffect, "Transforms", ("EffectType", "Zoom")),
                Preset("Cross Zoom A Only", VideoTransitionKind.CrossEffect, "Transforms", ("EffectType", "ZoomA")),
                Preset("Cross Zoom B Only", VideoTransitionKind.CrossEffect, "Transforms", ("EffectType", "ZoomB")),
                Preset("Cross Pixelate A/B", VideoTransitionKind.CrossEffect, "Transforms", ("EffectType", "Pixelate")),
                Preset("Cross Blur A/B", VideoTransitionKind.CrossEffect, "Transforms", ("EffectType", "Blur"))
            ]));

        // 16. Page Loop (3D and Peels)
        list.Add(CreatePlugin(
            VideoTransitionKind.PageLoop,
            "Page Loop",
            "com.vegascreativesoftware:pageloop",
            "3D and Peels",
            [
                Preset("Top-Left, Medium Loop", VideoTransitionKind.PageLoop, "3D and Peels", ("Corner", "TopLeft")),
                Preset("Top-Right, Medium Loop", VideoTransitionKind.PageLoop, "3D and Peels", ("Corner", "TopRight")),
                Preset("Bottom-Right, Small Loop", VideoTransitionKind.PageLoop, "3D and Peels", ("Corner", "BottomRight")),
                Preset("Bottom-Left, Small Loop", VideoTransitionKind.PageLoop, "3D and Peels", ("Corner", "BottomLeft"))
            ]));

        // 17. Page Peel (3D and Peels)
        list.Add(CreatePlugin(
            VideoTransitionKind.PagePeel,
            "Page Peel",
            "com.vegascreativesoftware:pagepeel",
            "3D and Peels",
            [
                Preset("Top-Left, Medium Fold", VideoTransitionKind.PagePeel, "3D and Peels", ("Corner", "TopLeft")),
                Preset("Top-Right, Medium Fold", VideoTransitionKind.PagePeel, "3D and Peels", ("Corner", "TopRight")),
                Preset("Bottom-Right, Medium Fold", VideoTransitionKind.PagePeel, "3D and Peels", ("Corner", "BottomRight")),
                Preset("Bottom-Left, Medium Fold", VideoTransitionKind.PagePeel, "3D and Peels", ("Corner", "BottomLeft"))
            ]));

        // 18. Page Roll (3D and Peels)
        list.Add(CreatePlugin(
            VideoTransitionKind.PageRoll,
            "Page Roll",
            "com.vegascreativesoftware:pageroll",
            "3D and Peels",
            [
                Preset("Top-Left, Medium Curl", VideoTransitionKind.PageRoll, "3D and Peels", ("Corner", "TopLeft")),
                Preset("Top-Right, Medium Curl", VideoTransitionKind.PageRoll, "3D and Peels", ("Corner", "TopRight")),
                Preset("Bottom-Right, Medium Curl", VideoTransitionKind.PageRoll, "3D and Peels", ("Corner", "BottomRight")),
                Preset("Bottom-Left, Medium Curl", VideoTransitionKind.PageRoll, "3D and Peels", ("Corner", "BottomLeft"))
            ]));

        Plugins = list.AsReadOnly();
    }

    private static TransitionPluginInfo CreatePlugin(
        VideoTransitionKind kind,
        string displayName,
        string pluginId,
        string category,
        TransitionPresetInfo[] presets)
    {
        return new TransitionPluginInfo(kind, displayName, pluginId, category, presets);
    }

    private static TransitionPresetInfo Preset(
        string name,
        VideoTransitionKind kind,
        string category,
        params (string Key, string Value)[] parameters)
    {
        var dict = parameters.ToDictionary(p => p.Key, p => p.Value);
        return new TransitionPresetInfo(name, kind, category, new ReadOnlyDictionary<string, string>(dict));
    }

    public static TransitionPluginInfo? FindPlugin(VideoTransitionKind kind)
        => Plugins.FirstOrDefault(p => p.Kind == kind);

    public static TransitionPresetInfo? FindPreset(VideoTransitionKind kind, string presetName)
        => FindPlugin(kind)?.Presets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<TransitionPluginInfo> GetPluginsForCategory(string category, HashSet<string>? favorites = null)
    {
        if (string.Equals(category, "All Plug-ins", StringComparison.OrdinalIgnoreCase))
            return Plugins;
        if (string.Equals(category, "Favorites", StringComparison.OrdinalIgnoreCase))
            return favorites == null ? [] : Plugins.Where(p => favorites.Contains(p.DisplayName)).ToList();
        return Plugins.Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
