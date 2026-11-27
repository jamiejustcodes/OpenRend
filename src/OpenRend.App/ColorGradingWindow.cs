using System.Windows;
using System.Windows.Automation;
using OpenRend.Core;

namespace OpenRend.App;

public sealed class ColorGradingWindow : Window
{
    private readonly ColorGradingDockControl panel;
    internal event Action<GradingPreview?>? GradingPreviewRequested;
    internal event Action? ScopeLimitsChanged;
    internal LuminanceThresholds? ScopeLimits => panel.ScopeLimits;

    public ColorGradingWindow(EditorViewModel viewModel, EffectScope scope, Guid targetId, Guid? effectId = null, Action? onPreview = null)
        : this(viewModel, scope, targetId, effectId, onPreview, null, 1)
    {
    }

    internal ColorGradingWindow(EditorViewModel viewModel, EffectScope scope, Guid targetId, Guid? effectId,
        Action? onPreview, Func<EffectScope, Guid?>? resolveTarget, int page)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "Color Grading";
        Width = 840;
        Height = 590;
        MinWidth = 420;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this, "ColorGradingWindow");

        panel = new ColorGradingDockControl();
        panel.GradingPreviewRequested += preview => GradingPreviewRequested?.Invoke(preview);
        panel.ScopeLimitsChanged += () => ScopeLimitsChanged?.Invoke();
        panel.BindScope(viewModel, scope, targetId, effectId, onPreview, resolveTarget, page);
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
