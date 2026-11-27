using System.Windows;
using System.Windows.Automation;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class ColorGradingWindow : Window
{
    internal event Action<CurvePreview?>? CurvePreviewRequested;

    public ColorGradingWindow(EditorViewModel viewModel, EffectScope scope, Guid targetId, Guid? effectId = null, Action? onPreview = null)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "Color Grading";
        Width = 840;
        Height = 590;
        MinWidth = 420;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "ColorGradingWindow");

        var panel = new ColorGradingDockControl();
        panel.CurvePreviewRequested += preview => CurvePreviewRequested?.Invoke(preview);
        panel.BindScope(viewModel, scope, targetId, effectId, onPreview);
        Content = panel;
        Loaded += OnLoaded;
        Closed += (_, _) => viewModel.Changed -= panel.RefreshTarget;

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            viewModel.Changed += panel.RefreshTarget;
            panel.RefreshTarget();
        }
    }

    public ColorGradingWindow(EditorViewModel viewModel, Guid clipId, Guid effectId, Action? onPreview = null)
        : this(viewModel, EffectScope.Event, clipId, effectId, onPreview)
    {
    }
}
