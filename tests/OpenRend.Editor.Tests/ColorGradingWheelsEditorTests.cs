using System.Globalization;
using System.Threading;
using System.Windows.Media;
using OpenRend.App;
using Xunit;

namespace OpenRend.Editor.Tests;

public sealed class ColorGradingWheelsEditorTests
{
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AggregateException(error);
    }

    [Fact]
    public void ColorGradingWheel_NeutralDefaults_MapToCenterZeroAndRoundtrip()
    {
        Sta(() =>
        {
            var wheel = new ColorGradingWheelControl();

            // 1. Lift / Offset (defaults at 0.0)
            wheel.Configure("Shadows (Lift)", "ColorWheel_Shadows", -1, 1, 0, -1, 1, 0, -1, 1, 0, -1, 1, 0);
            var (x0, y0) = wheel.RgbToCoordinates(0.0, 0.0, 0.0);
            Assert.True(Math.Abs(x0) < 0.0001, $"Expected x0=0, got {x0}");
            Assert.True(Math.Abs(y0) < 0.0001, $"Expected y0=0, got {y0}");

            var (r0, g0, b0) = wheel.CoordinatesToRgb(0.0, 0.0);
            Assert.Equal(0.0, r0, 3);
            Assert.Equal(0.0, g0, 3);
            Assert.Equal(0.0, b0, 3);

            // 2. Gamma / Gain (defaults at 1.0)
            wheel.Configure("Midtones (Gamma)", "ColorWheel_Midtones", 0, 5, 1, 0, 5, 1, 0, 5, 1, 0, 5, 1);
            var (x1, y1) = wheel.RgbToCoordinates(1.0, 1.0, 1.0);
            Assert.True(Math.Abs(x1) < 0.0001, $"Expected x1=0, got {x1}");
            Assert.True(Math.Abs(y1) < 0.0001, $"Expected y1=0, got {y1}");

            var (r1, g1, b1) = wheel.CoordinatesToRgb(0.0, 0.0);
            Assert.Equal(1.0, r1, 3);
            Assert.Equal(1.0, g1, 3);
            Assert.Equal(1.0, b1, 3);
        });
    }

    [Fact]
    public void ColorGradingWheel_DirectionalPuckMovement_ProjectsAlongVegasColorAxes()
    {
        Sta(() =>
        {
            var wheel = new ColorGradingWheelControl();
            wheel.Configure("Shadows (Lift)", "ColorWheel_Shadows", -1, 1, 0, -1, 1, 0, -1, 1, 0, -1, 1, 0);

            // Red axis at 185°
            var radR = 185.0 * Math.PI / 180.0;
            var (xR, yR) = (Math.Cos(radR) * 0.7, Math.Sin(radR) * 0.7);
            var (rRed, gRed, bRed) = wheel.CoordinatesToRgb(xR, yR);
            Assert.True(rRed > 0.0, $"Expected Red > 0 along 185°, got {rRed}");
            Assert.True(gRed < 0.0, $"Expected Green < 0 along 185°, got {gRed}");
            Assert.True(bRed < 0.0, $"Expected Blue < 0 along 185°, got {bRed}");

            // Green axis at -40°
            var radG = -40.0 * Math.PI / 180.0;
            var (xG, yG) = (Math.Cos(radG) * 0.7, Math.Sin(radG) * 0.7);
            var (rGreen, gGreen, bGreen) = wheel.CoordinatesToRgb(xG, yG);
            Assert.True(gGreen > 0.0, $"Expected Green > 0 along -40°, got {gGreen}");
            Assert.True(rGreen < 0.0, $"Expected Red < 0 along -40°, got {rGreen}");
            Assert.True(bGreen < 0.0, $"Expected Blue < 0 along -40°, got {bGreen}");

            // Blue axis at 65°
            var radB = 65.0 * Math.PI / 180.0;
            var (xB, yB) = (Math.Cos(radB) * 0.7, Math.Sin(radB) * 0.7);
            var (rBlue, gBlue, bBlue) = wheel.CoordinatesToRgb(xB, yB);
            Assert.True(bBlue > 0.0, $"Expected Blue > 0 along 65°, got {bBlue}");
            Assert.True(rBlue < 0.0, $"Expected Red < 0 along 65°, got {rBlue}");
            Assert.True(gBlue < 0.0, $"Expected Green < 0 along 65°, got {gBlue}");

            // Invertible roundtrip check: (x, y) -> (R, G, B) -> (x, y)
            var (roundX, roundY) = wheel.RgbToCoordinates(rRed, gRed, bRed);
            Assert.Equal(xR, roundX, 3);
            Assert.Equal(yR, roundY, 3);
        });
    }

    [Fact]
    public void ColorGradingWheel_ConfigureAndSetValues_UpdatesChannelsAndFiresEvents()
    {
        Sta(() =>
        {
            var wheel = new ColorGradingWheelControl();
            wheel.Configure("Highlights (Gain)", "ColorWheel_Highlights",
                0.0, 2.0, 1.0,
                0.0, 2.0, 1.0,
                0.0, 2.0, 1.0,
                0.0, 2.0, 1.0,
                0.01, true);

            var eventFired = false;
            double receivedR = 0, receivedG = 0, receivedB = 0, receivedY = 0;
            wheel.AllValuesChanged += (r, g, b, y) =>
            {
                eventFired = true;
                receivedR = r; receivedG = g; receivedB = b; receivedY = y;
            };

            var resetFired = false;
            wheel.ResetRequested += () => resetFired = true;

            // Set custom values
            wheel.SetValues(1.25, 0.90, 1.10, 1.05);
            Assert.Equal(1.25, wheel.R, 2);
            Assert.Equal(0.90, wheel.G, 2);
            Assert.Equal(1.10, wheel.B, 2);
            Assert.Equal(1.05, wheel.Y, 2);
            Assert.Equal(1.05, wheel.YSlider.Value, 2);
            Assert.Contains("1.25", wheel.ReadoutTextBlock.Text);
            Assert.Contains("0.90", wheel.ReadoutTextBlock.Text);

            // Test Reset
            wheel.Reset();
            Assert.True(resetFired, "Expected ResetRequested event to fire");
            Assert.True(eventFired, "Expected AllValuesChanged event to fire");
            Assert.Equal(1.0, receivedR, 2);
            Assert.Equal(1.0, receivedG, 2);
            Assert.Equal(1.0, receivedB, 2);
            Assert.Equal(1.0, receivedY, 2);
            Assert.Equal(1.0, wheel.R, 2);
            Assert.Equal(1.0, wheel.G, 2);
            Assert.Equal(1.0, wheel.B, 2);
            Assert.Equal(1.0, wheel.Y, 2);
        });
    }

    [Fact]
    public void ColorGradingWheel_ModeToggle_SwitchesBetweenWheelAndSliders()
    {
        Sta(() =>
        {
            var wheel = new ColorGradingWheelControl();
            Assert.Equal(ColorWheelDisplayMode.Wheel, wheel.DisplayMode);

            wheel.DisplayMode = ColorWheelDisplayMode.Sliders;
            Assert.Equal(ColorWheelDisplayMode.Sliders, wheel.DisplayMode);

            wheel.DisplayMode = ColorWheelDisplayMode.Wheel;
            Assert.Equal(ColorWheelDisplayMode.Wheel, wheel.DisplayMode);
        });
    }

    [Fact]
    public void ColorGradingWheel_ClipboardCopyPaste_TransfersValuesBetweenWheels()
    {
        Sta(() =>
        {
            var wheel1 = new ColorGradingWheelControl();
            wheel1.Configure("Shadows (Lift)", "Wheel1", -1, 1, 0, -1, 1, 0, -1, 1, 0, -1, 1, 0);
            wheel1.SetValues(0.15, -0.08, 0.22, -0.05);
            wheel1.CopyValues();

            var wheel2 = new ColorGradingWheelControl();
            wheel2.Configure("Offset", "Wheel2", -1, 1, 0, -1, 1, 0, -1, 1, 0, -1, 1, 0);
            wheel2.SetValues(0.0, 0.0, 0.0, 0.0);

            wheel2.PasteValues();
            Assert.Equal(0.15, wheel2.R, 2);
            Assert.Equal(-0.08, wheel2.G, 2);
            Assert.Equal(0.22, wheel2.B, 2);
            Assert.Equal(-0.05, wheel2.Y, 2);
        });
    }

    [Fact]
    public void ColorGradingWheel_EyedropperSampling_CalculatesNeutralBalance()
    {
        Sta(() =>
        {
            // 1. Lift sampling dark tint (e.g. slight warm cast: R=35, G=20, B=20)
            var liftWheel = new ColorGradingWheelControl();
            liftWheel.Configure("Shadows (Lift)", "ColorWheel_Shadows", -1, 1, 0, -1, 1, 0, -1, 1, 0, -1, 1, 0, 0.01, true);

            var pickedFired = false;
            Color pickedColor = default;
            liftWheel.PickerColorPicked += c => { pickedFired = true; pickedColor = c; };

            var sampleDark = Color.FromRgb(35, 20, 20);
            liftWheel.SampleColor(sampleDark);

            Assert.True(pickedFired);
            Assert.Equal(sampleDark, pickedColor);
            Assert.Equal(sampleDark, liftWheel.PickerColor);

            // Red was higher than green/blue in the sample, so Lift Red should be offset downward (< 0)
            // to neutralize the warm shadow cast
            Assert.True(liftWheel.R < liftWheel.G, $"Expected Lift R ({liftWheel.R}) < Lift G ({liftWheel.G}) to neutralize warm cast");

            // 2. Gain sampling light tint (e.g. slight yellow cast: R=240, G=230, B=200)
            var gainWheel = new ColorGradingWheelControl();
            gainWheel.Configure("Highlights (Gain)", "ColorWheel_Highlights", 0, 2, 1, 0, 2, 1, 0, 2, 1, 0, 2, 1, 0.01, true);

            var sampleLight = Color.FromRgb(240, 230, 200);
            gainWheel.SampleColor(sampleLight);

            // Blue was lowest in the sample, so Gain Blue should be scaled up relative to Red/Green
            // to neutralize the yellow highlight cast
            Assert.True(gainWheel.B > gainWheel.R, $"Expected Gain B ({gainWheel.B}) > Gain R ({gainWheel.R}) to neutralize yellow cast");
        });
    }
}

