using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenRend.Core;
using OpenRend.Timeline;
using OpenRend.UI;

namespace OpenRend.App;

public sealed class TrackMotionWindow : Window
{
    private readonly EditorViewModel vm;
    private readonly TimelineEditor editor;
    private readonly Guid trackId;
    private readonly Action<long> seek;
    private readonly Dictionary<string, TextBox> fields = [];
    private readonly StackPanel leftStack = new() { Margin = new Thickness(4) };
    private readonly TrackMotionWorkspace workspace = new();
    private readonly TrackMotionKeyframeLane lane = new();
    private readonly ComboBox compModeCombo;
    private readonly ComboBox interpolationCombo;
    private readonly Slider smoothnessSlider;
    private readonly TextBox smoothnessField;
    private readonly ComboBox presetCombo;
    private readonly CheckBox shadowCheck = new();
    private readonly CheckBox glowCheck = new();
    private readonly TextBlock timecodeBlock;

    private long currentFrame;
    private bool refreshing;
    private TrackMotion currentMotion;
    private TrackMotionKeyframe evaluatedKeyframe;

    public TrackMotionWindow(EditorViewModel viewModel, Guid targetTrackId, Action<long> seekPreview)
    {
        vm = viewModel;
        editor = vm.Editor;
        trackId = targetTrackId;
        seek = seekPreview;

        var track = vm.Project.Tracks.FirstOrDefault(t => t.Id == trackId)
            ?? throw new InvalidOperationException("Track not found.");

        Title = $"Track Motion - {track.Name}";
        Width = 780;
        Height = 600;
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = TryFindResource("PanelBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));

        shadowCheck.Foreground = TryFindResource("ForegroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        shadowCheck.VerticalAlignment = VerticalAlignment.Center;
        shadowCheck.Checked += (_, _) => ToggleShadow(true);
        shadowCheck.Unchecked += (_, _) => ToggleShadow(false);

        glowCheck.Foreground = TryFindResource("ForegroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        glowCheck.VerticalAlignment = VerticalAlignment.Center;
        glowCheck.Checked += (_, _) => ToggleGlow(true);
        glowCheck.Unchecked += (_, _) => ToggleGlow(false);

        currentFrame = Math.Max(0, vm.Position);
        currentMotion = track.Motion?.DeepClone() ?? new TrackMotion();
        if (currentMotion.MotionKeyframes.Count == 0)
        {
            currentMotion.MotionKeyframes.Add(TrackMotionKeyframe.Identity(vm.Project.Width, vm.Project.Height));
        }

        evaluatedKeyframe = currentMotion.Evaluate(currentFrame, vm.Project.Width, vm.Project.Height);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); // Preset bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) }); // Mode & Tools bar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Main content (Left tree + Right canvas)
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(130) }); // Bottom timeline & transport

        // --- Row 0: Preset Bar ---
        var presetBar = new DockPanel { Background = TryFindResource("HeaderBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), LastChildFill = false };
        presetBar.Children.Add(new TextBlock
        {
            Text = "Preset",
            Foreground = TryFindResource("ForegroundMutedBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0),
            FontSize = 11
        });

        presetCombo = new ComboBox
        {
            Width = 260,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        presetCombo.Items.Add("(Untitled)");
        presetCombo.Items.Add("Default (1:1 Fullscreen)");
        presetCombo.Items.Add("Picture in Picture (Top Right)");
        presetCombo.Items.Add("Picture in Picture (Bottom Right)");
        presetCombo.Items.Add("Split Screen 2-Up Left");
        presetCombo.Items.Add("Split Screen 2-Up Right");
        presetCombo.Items.Add("3D Left Perspective Wall");
        presetCombo.Items.Add("3D Right Perspective Wall");
        presetCombo.Items.Add("3D Overhead Tilt");
        presetCombo.Items.Add("3D Distance Fly In");
        presetCombo.SelectedIndex = 0;
        presetCombo.SelectionChanged += (_, _) => OnPresetSelected();
        presetBar.Children.Add(presetCombo);

        var savePresetBtn = CreateToolButton("💾", "Save Preset", () => { vm.Status = "Track motion preset saved."; });
        presetBar.Children.Add(savePresetBtn);

        var deletePresetBtn = CreateToolButton("✕", "Delete Preset", () => { vm.Status = "Default preset cannot be deleted."; });
        presetBar.Children.Add(deletePresetBtn);

        var resetBtn = CreateToolButton("Reset", "Reset Track Motion to Full Frame", ResetMotion);
        DockPanel.SetDock(resetBtn, Dock.Right);
        resetBtn.Margin = new Thickness(0, 0, 10, 0);
        presetBar.Children.Add(resetBtn);

        Grid.SetRow(presetBar, 0);
        root.Children.Add(presetBar);

        // --- Row 1: Mode & Tools Bar ---
        var toolsBar = new DockPanel { Background = TryFindResource("PanelBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)), LastChildFill = false };
        compModeCombo = new ComboBox
        {
            Width = 125,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0)
        };
        compModeCombo.Items.Add("Source Alpha ▾");
        compModeCombo.Items.Add("3D Source Alpha");
        var is3DInitial = track.CompositeMode == TrackCompositeMode.SourceAlpha3D || evaluatedKeyframe.Is3D();
        compModeCombo.SelectedIndex = is3DInitial ? 1 : 0;
        compModeCombo.SelectionChanged += (_, _) =>
        {
            if (refreshing) return;
            var is3D = compModeCombo.SelectedIndex == 1;
            editor.SetTrackCompositeMode(trackId, is3D ? TrackCompositeMode.SourceAlpha3D : TrackCompositeMode.SourceAlpha);
            workspace.Is3DMode = is3D;
            RebuildParametersPanel(is3D);
            RefreshFields();
            workspace.InvalidateVisual();
            seek(currentFrame);
        };
        toolsBar.Children.Add(compModeCombo);

        var rotBtn = CreateToggleToolButton("⟲", "Enable Rotation", true, val => workspace.RotationEnabled = val);
        toolsBar.Children.Add(rotBtn);

        var snapBtn = CreateToggleToolButton("🧲", "Enable Snapping", false, val => workspace.SnappingEnabled = val);
        toolsBar.Children.Add(snapBtn);

        var scaleCenterBtn = CreateToggleToolButton("⛶", "Scale About Center", true, val => workspace.ScaleAboutCenter = val);
        toolsBar.Children.Add(scaleCenterBtn);

        var lockAspectBtn = CreateToggleToolButton("🔒", "Lock Aspect Ratio", false, val => workspace.LockAspect = val);
        toolsBar.Children.Add(lockAspectBtn);

        Grid.SetRow(toolsBar, 1);
        root.Children.Add(toolsBar);

        // --- Row 2: Main Split Content (Left Tree Parameters + Right Interactive Canvas) ---
        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left Panel (Parameters Tree)
        var leftScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        RebuildParametersPanel(is3DInitial);

        leftScroll.Content = leftStack;
        Grid.SetColumn(leftScroll, 0);
        mainGrid.Children.Add(leftScroll);

        // Right Panel: Workspace Canvas
        workspace.ProjectWidth = vm.Project.Width;
        workspace.ProjectHeight = vm.Project.Height;
        workspace.Is3DMode = is3DInitial;
        workspace.Keyframe = evaluatedKeyframe;
        workspace.KeyframeModified += (modified) =>
        {
            CommitKeyframe(modified);
        };
        Grid.SetColumn(workspace, 1);
        mainGrid.Children.Add(workspace);

        Grid.SetRow(mainGrid, 2);
        root.Children.Add(mainGrid);

        // --- Row 3: Bottom Timeline & Transport Bar ---
        var bottomGrid = new Grid { Background = TryFindResource("HeaderBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)) };
        bottomGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Keyframe lanes
        bottomGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); // Transport controls

        // Keyframe Lane Area
        var laneContainer = new Grid();
        laneContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        laneContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var laneHeaders = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        laneHeaders.Children.Add(new TextBlock { Text = "Position", Foreground = TryFindResource("ForegroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), Height = 22, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
        laneHeaders.Children.Add(new TextBlock { Text = "2D Shadow", Foreground = TryFindResource("ForegroundMutedBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), Height = 22, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
        laneHeaders.Children.Add(new TextBlock { Text = "2D Glow", Foreground = TryFindResource("ForegroundMutedBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), Height = 22, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
        Grid.SetColumn(laneHeaders, 0);
        laneContainer.Children.Add(laneHeaders);

        lane.FrameRate = vm.Project.FrameRate;
        lane.DurationFrames = Math.Max(100, vm.Project.DurationFrames);
        lane.CursorFrame = currentFrame;
        lane.Keyframes = currentMotion.MotionKeyframes.Select(k => k.Frame).ToList();
        lane.FrameChanged += frame =>
        {
            currentFrame = frame;
            RefreshState();
            seek(currentFrame);
        };
        Grid.SetColumn(lane, 1);
        laneContainer.Children.Add(lane);

        Grid.SetRow(laneContainer, 0);
        bottomGrid.Children.Add(laneContainer);

        // Transport & Interpolation Bar
        var transport = new DockPanel { Background = TryFindResource("PanelBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)), LastChildFill = false };

        var prevKeyBtn = CreateToolButton("◀", "Previous Keyframe", GoToPrevKeyframe);
        transport.Children.Add(prevKeyBtn);

        var nextKeyBtn = CreateToolButton("▶", "Next Keyframe", GoToNextKeyframe);
        transport.Children.Add(nextKeyBtn);

        var addKeyBtn = CreateToolButton("+◇", "Add Keyframe at Cursor", AddKeyframeAtCursor);
        transport.Children.Add(addKeyBtn);

        var delKeyBtn = CreateToolButton("-◇", "Delete Keyframe at Cursor", DeleteKeyframeAtCursor);
        transport.Children.Add(delKeyBtn);

        transport.Children.Add(new TextBlock
        {
            Text = "Interpolation:",
            Foreground = TryFindResource("ForegroundMutedBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 4, 0),
            FontSize = 11
        });

        interpolationCombo = new ComboBox { Width = 90, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        foreach (var val in Enum.GetValues<CropInterpolation>())
        {
            interpolationCombo.Items.Add(val);
        }
        interpolationCombo.SelectedItem = evaluatedKeyframe.Interpolation;
        interpolationCombo.SelectionChanged += (_, _) =>
        {
            if (refreshing || interpolationCombo.SelectedItem is not CropInterpolation mode) return;
            UpdateKeyframeProp((k, _) => k.Interpolation = mode, 0);
        };
        transport.Children.Add(interpolationCombo);

        transport.Children.Add(new TextBlock
        {
            Text = "Smoothness:",
            Foreground = TryFindResource("ForegroundMutedBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 4, 0),
            FontSize = 11
        });

        smoothnessField = new TextBox
        {
            Text = $"{evaluatedKeyframe.Smoothness:0}",
            Width = 35,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };

        smoothnessSlider = new Slider
        {
            Width = 90,
            Minimum = 0,
            Maximum = 100,
            Value = evaluatedKeyframe.Smoothness,
            VerticalAlignment = VerticalAlignment.Center
        };
        smoothnessSlider.ValueChanged += (_, e) =>
        {
            if (refreshing) return;
            smoothnessField.Text = $"{e.NewValue:0}";
            UpdateKeyframeProp((k, v) => k.Smoothness = v, e.NewValue);
        };
        transport.Children.Add(smoothnessSlider);
        transport.Children.Add(smoothnessField);

        timecodeBlock = new TextBlock
        {
            Text = TimelineView.FormatTimecode(currentFrame, vm.Project.FrameRate),
            Foreground = TryFindResource("ForegroundBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        DockPanel.SetDock(timecodeBlock, Dock.Right);
        transport.Children.Add(timecodeBlock);

        Grid.SetRow(transport, 1);
        bottomGrid.Children.Add(transport);

        Grid.SetRow(bottomGrid, 3);
        root.Children.Add(bottomGrid);

        Content = root;
        RefreshFields();
    }

    private void RefreshState()
    {
        var track = vm.Project.Tracks.FirstOrDefault(t => t.Id == trackId);
        currentMotion = track?.Motion?.DeepClone() ?? new TrackMotion();
        if (currentMotion.MotionKeyframes.Count == 0)
        {
            currentMotion.MotionKeyframes.Add(TrackMotionKeyframe.Identity(vm.Project.Width, vm.Project.Height));
        }

        evaluatedKeyframe = currentMotion.Evaluate(currentFrame, vm.Project.Width, vm.Project.Height);
        workspace.Keyframe = evaluatedKeyframe;
        lane.CursorFrame = currentFrame;
        lane.Keyframes = currentMotion.MotionKeyframes.Select(k => k.Frame).ToList();
        timecodeBlock.Text = TimelineView.FormatTimecode(currentFrame, vm.Project.FrameRate);

        RefreshFields();
        workspace.InvalidateVisual();
        lane.InvalidateVisual();
    }

    private void RefreshFields()
    {
        refreshing = true;
        try
        {
            if (fields.TryGetValue("X", out var tbX)) tbX.Text = $"{evaluatedKeyframe.PositionX:0.00}";
            if (fields.TryGetValue("Y", out var tbY)) tbY.Text = $"{evaluatedKeyframe.PositionY:0.00}";
            if (fields.TryGetValue("Z", out var tbZ)) tbZ.Text = $"{evaluatedKeyframe.PositionZ:0.00}";
            if (fields.TryGetValue("Width", out var tbW)) tbW.Text = $"{evaluatedKeyframe.Width:0.00}";
            if (fields.TryGetValue("Height", out var tbH)) tbH.Text = $"{evaluatedKeyframe.Height:0.00}";
            if (fields.TryGetValue("Depth", out var tbD)) tbD.Text = $"{evaluatedKeyframe.Depth:0.00}";
            if (fields.TryGetValue("Angle", out var tbA)) tbA.Text = $"{evaluatedKeyframe.RotationZ:0.0}";
            if (fields.TryGetValue("Rot X", out var tbRx)) tbRx.Text = $"{evaluatedKeyframe.RotationX:0.0}";
            if (fields.TryGetValue("Rot Y", out var tbRy)) tbRy.Text = $"{evaluatedKeyframe.RotationY:0.0}";
            if (fields.TryGetValue("Rot Z", out var tbRz)) tbRz.Text = $"{evaluatedKeyframe.RotationZ:0.0}";
            if (fields.TryGetValue("Orient X", out var tbOx)) tbOx.Text = $"{evaluatedKeyframe.OrientationX:0.0}";
            if (fields.TryGetValue("Orient Y", out var tbOy)) tbOy.Text = $"{evaluatedKeyframe.OrientationY:0.0}";
            if (fields.TryGetValue("Orient Z", out var tbOz)) tbOz.Text = $"{evaluatedKeyframe.OrientationZ:0.0}";
            if (fields.TryGetValue("Center X", out var tbCx)) tbCx.Text = $"{evaluatedKeyframe.RotationOffsetX:0.00}";
            if (fields.TryGetValue("Center Y", out var tbCy)) tbCy.Text = $"{evaluatedKeyframe.RotationOffsetY:0.00}";
            if (fields.TryGetValue("Center Z", out var tbCz)) tbCz.Text = $"{evaluatedKeyframe.RotationOffsetZ:0.00}";
            if (fields.TryGetValue("Smoothness", out var tbS)) tbS.Text = $"{evaluatedKeyframe.Smoothness:0.00}";

            smoothnessSlider.Value = evaluatedKeyframe.Smoothness;
            interpolationCombo.SelectedItem = evaluatedKeyframe.Interpolation;
            shadowCheck.IsChecked = currentMotion.ShadowEnabled;
            glowCheck.IsChecked = currentMotion.GlowEnabled;
        }
        finally
        {
            refreshing = false;
        }
    }

    private void RebuildParametersPanel(bool is3D)
    {
        leftStack.Children.Clear();
        fields.Clear();

        // Position Group
        leftStack.Children.Add(CreateGroupHeader("Position"));
        leftStack.Children.Add(CreateFieldRow("X", "0.00", val => UpdateKeyframeProp((k, v) => k.PositionX = v, val)));
        leftStack.Children.Add(CreateFieldRow("Y", "0.00", val => UpdateKeyframeProp((k, v) => k.PositionY = v, val)));
        if (is3D)
        {
            leftStack.Children.Add(CreateFieldRow("Z", "0.00", val => UpdateKeyframeProp((k, v) => k.PositionZ = v, val)));
        }
        leftStack.Children.Add(CreateFieldRow("Width", vm.Project.Width.ToString(), val => UpdateKeyframeProp((k, v) => k.Width = Math.Max(1, v), val)));
        leftStack.Children.Add(CreateFieldRow("Height", vm.Project.Height.ToString(), val => UpdateKeyframeProp((k, v) => k.Height = Math.Max(1, v), val)));
        if (is3D)
        {
            leftStack.Children.Add(CreateFieldRow("Depth", "0.00", val => UpdateKeyframeProp((k, v) => k.Depth = v, val)));
        }

        // Orientation Group (in 3D)
        if (is3D)
        {
            leftStack.Children.Add(CreateGroupHeader("Orientation"));
            leftStack.Children.Add(CreateFieldRow("Orient X", "0.0", val => UpdateKeyframeProp((k, v) => k.OrientationX = v, val)));
            leftStack.Children.Add(CreateFieldRow("Orient Y", "0.0", val => UpdateKeyframeProp((k, v) => k.OrientationY = v, val)));
            leftStack.Children.Add(CreateFieldRow("Orient Z", "0.0", val => UpdateKeyframeProp((k, v) => k.OrientationZ = v, val)));
        }

        // Rotation Group
        leftStack.Children.Add(CreateGroupHeader("Rotation"));
        if (is3D)
        {
            leftStack.Children.Add(CreateFieldRow("Rot X", "0.0", val => UpdateKeyframeProp((k, v) => k.RotationX = v, val)));
            leftStack.Children.Add(CreateFieldRow("Rot Y", "0.0", val => UpdateKeyframeProp((k, v) => k.RotationY = v, val)));
            leftStack.Children.Add(CreateFieldRow("Rot Z", "0.0", val => UpdateKeyframeProp((k, v) => k.RotationZ = v, val)));
        }
        else
        {
            leftStack.Children.Add(CreateFieldRow("Angle", "0.0", val => UpdateKeyframeProp((k, v) => k.RotationZ = v, val)));
        }

        // Center of Rotation (Offset / Pivot)
        leftStack.Children.Add(CreateGroupHeader(is3D ? "Center of Rotation (3D)" : "Center of Rotation"));
        leftStack.Children.Add(CreateFieldRow("Center X", "0.00", val => UpdateKeyframeProp((k, v) => k.RotationOffsetX = v, val)));
        leftStack.Children.Add(CreateFieldRow("Center Y", "0.00", val => UpdateKeyframeProp((k, v) => k.RotationOffsetY = v, val)));
        if (is3D)
        {
            leftStack.Children.Add(CreateFieldRow("Center Z", "0.00", val => UpdateKeyframeProp((k, v) => k.RotationOffsetZ = v, val)));
        }

        // Keyframe Group
        leftStack.Children.Add(CreateGroupHeader("Keyframe"));
        leftStack.Children.Add(CreateFieldRow("Smoothness", "100.0", val => UpdateKeyframeProp((k, v) => k.Smoothness = Math.Clamp(v, 0, 100), val)));

        // 2D/3D Shadow Group
        var shadowHeader = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
        shadowCheck.Content = is3D ? "3D Shadow" : "2D Shadow";
        shadowCheck.IsChecked = currentMotion.ShadowEnabled;
        if (shadowCheck.Parent is Panel pShadow) pShadow.Children.Remove(shadowCheck);
        shadowHeader.Children.Add(shadowCheck);
        leftStack.Children.Add(shadowHeader);

        // 2D/3D Glow Group
        var glowHeader = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        glowCheck.Content = is3D ? "3D Glow" : "2D Glow";
        glowCheck.IsChecked = currentMotion.GlowEnabled;
        if (glowCheck.Parent is Panel pGlow) pGlow.Children.Remove(glowCheck);
        glowHeader.Children.Add(glowCheck);
        leftStack.Children.Add(glowHeader);
    }

    private void UpdateKeyframeProp(Action<TrackMotionKeyframe, double> setter, double value)
    {
        if (refreshing) return;
        var clone = evaluatedKeyframe.DeepClone();
        setter(clone, value);
        clone.Frame = currentFrame;
        CommitKeyframe(clone);
    }

    private void CommitKeyframe(TrackMotionKeyframe keyframe)
    {
        keyframe.Frame = currentFrame;
        editor.SetTrackMotionKeyframe(trackId, keyframe);
        RefreshState();
        seek(currentFrame);
    }

    private void AddKeyframeAtCursor()
    {
        var key = evaluatedKeyframe.DeepClone();
        key.Frame = currentFrame;
        editor.SetTrackMotionKeyframe(trackId, key);
        RefreshState();
        seek(currentFrame);
        vm.Status = $"Added track motion keyframe at frame {currentFrame}.";
    }

    private void DeleteKeyframeAtCursor()
    {
        editor.RemoveTrackMotionKeyframe(trackId, currentFrame);
        RefreshState();
        seek(currentFrame);
        vm.Status = $"Deleted track motion keyframe at frame {currentFrame}.";
    }

    private void GoToPrevKeyframe()
    {
        var prev = currentMotion.MotionKeyframes.Where(k => k.Frame < currentFrame).OrderByDescending(k => k.Frame).FirstOrDefault();
        if (prev is not null)
        {
            currentFrame = prev.Frame;
            RefreshState();
            seek(currentFrame);
        }
    }

    private void GoToNextKeyframe()
    {
        var next = currentMotion.MotionKeyframes.Where(k => k.Frame > currentFrame).OrderBy(k => k.Frame).FirstOrDefault();
        if (next is not null)
        {
            currentFrame = next.Frame;
            RefreshState();
            seek(currentFrame);
        }
    }

    private void ToggleShadow(bool enabled)
    {
        editor.SetTrackShadowEnabled(trackId, enabled);
        RefreshState();
        seek(currentFrame);
    }

    private void ToggleGlow(bool enabled)
    {
        editor.SetTrackGlowEnabled(trackId, enabled);
        RefreshState();
        seek(currentFrame);
    }

    private void ResetMotion()
    {
        editor.ResetTrackMotion(trackId);
        currentMotion = new TrackMotion();
        currentMotion.MotionKeyframes.Add(TrackMotionKeyframe.Identity(vm.Project.Width, vm.Project.Height));
        RefreshState();
        seek(currentFrame);
        vm.Status = "Track motion reset to default.";
    }

    private void OnPresetSelected()
    {
        if (presetCombo.SelectedIndex <= 0) return;
        var pw = vm.Project.Width;
        var ph = vm.Project.Height;
        var key = TrackMotionKeyframe.Identity(pw, ph);

        switch (presetCombo.SelectedIndex)
        {
            case 1: // Default
                break;
            case 2: // PiP Top Right
                key.Width = pw * 0.35;
                key.Height = ph * 0.35;
                key.PositionX = (pw / 2.0) - (key.Width / 2.0) - 40;
                key.PositionY = (ph / 2.0) - (key.Height / 2.0) - 40;
                break;
            case 3: // PiP Bottom Right
                key.Width = pw * 0.35;
                key.Height = ph * 0.35;
                key.PositionX = (pw / 2.0) - (key.Width / 2.0) - 40;
                key.PositionY = -(ph / 2.0) + (key.Height / 2.0) + 40;
                break;
            case 4: // Split Screen Left
                key.Width = pw * 0.5;
                key.Height = ph;
                key.PositionX = -pw * 0.25;
                key.PositionY = 0;
                break;
            case 5: // Split Screen Right
                key.Width = pw * 0.5;
                key.Height = ph;
                key.PositionX = pw * 0.25;
                key.PositionY = 0;
                break;
            case 6: // 3D Left Perspective Wall
                key.RotationY = 45.0;
                key.PositionX = -pw * 0.25;
                key.PositionZ = 200.0;
                compModeCombo.SelectedIndex = 1;
                break;
            case 7: // 3D Right Perspective Wall
                key.RotationY = -45.0;
                key.PositionX = pw * 0.25;
                key.PositionZ = 200.0;
                compModeCombo.SelectedIndex = 1;
                break;
            case 8: // 3D Overhead Tilt
                key.RotationX = 55.0;
                key.PositionY = -ph * 0.25;
                key.PositionZ = 300.0;
                compModeCombo.SelectedIndex = 1;
                break;
            case 9: // 3D Distance Fly In
                key.PositionZ = 1200.0;
                compModeCombo.SelectedIndex = 1;
                break;
        }

        key.Frame = currentFrame;
        CommitKeyframe(key);
    }

    private static TextBlock CreateGroupHeader(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 8, 0, 4),
        FontSize = 11
    };

    private UIElement CreateFieldRow(string label, string initialValue, Action<double> onCommit)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var lbl = new TextBlock
        {
            Text = $"- {label}:",
            Foreground = TryFindResource("ForegroundMutedBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 80,
            FontSize = 11
        };
        dock.Children.Add(lbl);

        var tb = new TextBox
        {
            Text = initialValue,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11
        };
        tb.LostFocus += (_, _) =>
        {
            if (double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                onCommit(val);
            }
        };
        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            {
                onCommit(val);
                e.Handled = true;
            }
        };

        fields[label] = tb;
        dock.Children.Add(tb);
        return dock;
    }

    private static Button CreateToolButton(string text, string tooltip, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            ToolTip = tooltip,
            Height = 22,
            MinWidth = 26,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static CheckBox CreateToggleToolButton(string text, string tooltip, bool isDefaultChecked, Action<bool> onChange)
    {
        var cb = new CheckBox
        {
            Content = text,
            ToolTip = tooltip,
            IsChecked = isDefaultChecked,
            Height = 22,
            MinWidth = 26,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        cb.Checked += (_, _) => onChange(true);
        cb.Unchecked += (_, _) => onChange(false);
        return cb;
    }
}

public sealed class TrackMotionWorkspace : FrameworkElement
{
    public int ProjectWidth { get; set; } = 1920;
    public int ProjectHeight { get; set; } = 1080;
    public TrackMotionKeyframe Keyframe { get; set; } = TrackMotionKeyframe.Identity(1920, 1080);
    public bool Is3DMode { get; set; }
    public bool RotationEnabled { get; set; } = true;
    public bool SnappingEnabled { get; set; }
    public bool ScaleAboutCenter { get; set; } = true;
    public bool LockAspect { get; set; }

    public event Action<TrackMotionKeyframe>? KeyframeModified;

    private Point dragOrigin;
    private TrackMotionKeyframe? initialDragKeyframe;
    private int dragMode; // 0 = none, 1 = move, 2 = resize top-left, 3 = resize top-right, 4 = resize bottom-right, 5 = resize bottom-left, 6 = rotate

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)), null, new Rect(0, 0, w, h));

        // Dot Grid
        var dotBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        for (double x = 16; x < w; x += 32)
        {
            for (double y = 16; y < h; y += 32)
            {
                dc.DrawRectangle(dotBrush, null, new Rect(x, y, 1.5, 1.5));
            }
        }

        // View scale calculation to fit project dimensions with padding
        var scale = Math.Min((w - 60) / Math.Max(1, ProjectWidth), (h - 60) / Math.Max(1, ProjectHeight));
        var centerX = w / 2.0;
        var centerY = h / 2.0;

        // Project bounds (outer guideline)
        var projRect = new Rect(centerX - (ProjectWidth * scale) / 2.0, centerY - (ProjectHeight * scale) / 2.0, ProjectWidth * scale, ProjectHeight * scale);
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)), 1), projRect);

        var k = Keyframe;

        if (Is3DMode)
        {
            // 3D Perspective Projection
            var f = Math.Max(ProjectWidth, ProjectHeight) * 1.25;

            var px = k.RotationOffsetX;
            var py = k.RotationOffsetY;
            var pz = k.RotationOffsetZ;

            var lw = k.Width;
            var lh = k.Height;

            var angleX = (k.RotationX + k.OrientationX) * Math.PI / 180.0;
            var angleY = (k.RotationY + k.OrientationY) * Math.PI / 180.0;
            var angleZ = (k.RotationZ + k.OrientationZ) * Math.PI / 180.0;

            var cosX = Math.Cos(angleX); var sinX = Math.Sin(angleX);
            var cosY = Math.Cos(angleY); var sinY = Math.Sin(angleY);
            var cosZ = Math.Cos(angleZ); var sinZ = Math.Sin(angleZ);

            Point ProjectPoint3D(double lx, double ly, double lz)
            {
                var dx = lx - px; var dy = ly - py; var dz = lz - pz;
                var y1 = dy * cosX - dz * sinX;
                var z1 = dy * sinX + dz * cosX;
                var x1 = dx;
                var x2 = x1 * cosY + z1 * sinY;
                var z2 = -x1 * sinY + z1 * cosY;
                var y2 = y1;
                var x3 = x2 * cosZ - y2 * sinZ;
                var y3 = x2 * sinZ + y2 * cosZ;
                var z3 = z2;

                var x3d = x3 + px + k.PositionX;
                var y3d = y3 + py + k.PositionY;
                var z3d = z3 + pz + k.PositionZ;

                var denom = f + z3d;
                if (denom <= 1.0) denom = 1.0;
                var pScale = (f / denom) * scale;
                return new Point(centerX + x3d * pScale, centerY - y3d * pScale);
            }

            var pTL = ProjectPoint3D(-lw / 2.0, lh / 2.0, 0);
            var pTR = ProjectPoint3D(lw / 2.0, lh / 2.0, 0);
            var pBR = ProjectPoint3D(lw / 2.0, -lh / 2.0, 0);
            var pBL = ProjectPoint3D(-lw / 2.0, -lh / 2.0, 0);

            // Perspective Quad Outline
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xA2, 0xE8)), 1.5);
            var streamGeom = new StreamGeometry();
            using (var ctx = streamGeom.Open())
            {
                ctx.BeginFigure(pTL, isFilled: false, isClosed: true);
                ctx.LineTo(pTR, isStroked: true, isSmoothJoin: false);
                ctx.LineTo(pBR, isStroked: true, isSmoothJoin: false);
                ctx.LineTo(pBL, isStroked: true, isSmoothJoin: false);
            }
            dc.DrawGeometry(null, borderPen, streamGeom);

            // Diagonal guidelines
            var diagPen = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0xA2, 0xE8)), 1)
            {
                DashStyle = DashStyles.Dash
            };
            dc.DrawLine(diagPen, pTL, pBR);
            dc.DrawLine(diagPen, pTR, pBL);

            // Pivot Center & 3D Axes
            var pivotCenter = ProjectPoint3D(px, py, pz);
            var axisLen = Math.Min(lw, lh) * 0.25;
            var ptAxisX = ProjectPoint3D(px + axisLen, py, pz);
            var ptAxisY = ProjectPoint3D(px, py + axisLen, pz);
            var ptAxisZ = ProjectPoint3D(px, py, pz + axisLen);

            // Red X axis, Green Y axis, Blue Z axis
            dc.DrawLine(new Pen(Brushes.Crimson, 2), pivotCenter, ptAxisX);
            dc.DrawLine(new Pen(Brushes.LimeGreen, 2), pivotCenter, ptAxisY);
            dc.DrawLine(new Pen(Brushes.DodgerBlue, 2), pivotCenter, ptAxisZ);

            // Corner resize handles
            var handleBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xA2, 0xE8));
            var handlePen = new Pen(Brushes.White, 1);
            DrawHandle(dc, handleBrush, handlePen, pTL);
            DrawHandle(dc, handleBrush, handlePen, pTR);
            DrawHandle(dc, handleBrush, handlePen, pBR);
            DrawHandle(dc, handleBrush, handlePen, pBL);
            DrawHandle(dc, Brushes.Gold, handlePen, pivotCenter);
        }
        else
        {
            // 2D Track frame transform
            var frameW = k.Width * scale;
            var frameH = k.Height * scale;
            var frameX = centerX + (k.PositionX * scale);
            var frameY = centerY - (k.PositionY * scale); // Invert Y so positive is up

            dc.PushTransform(new RotateTransform(k.RotationZ, frameX, frameY));

            // Motion bounding box
            var motionRect = new Rect(frameX - frameW / 2.0, frameY - frameH / 2.0, frameW, frameH);
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0xA2, 0xE8)), 1.5);
            dc.DrawRectangle(null, borderPen, motionRect);

            // Center pivot and rotation circle
            var pivotPen = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1);
            var radius = Math.Min(frameW, frameH) * 0.25;
            dc.DrawEllipse(null, pivotPen, new Point(frameX, frameY), radius, radius);

            // Axes (Y pointing up, X pointing right)
            dc.DrawLine(pivotPen, new Point(frameX, frameY), new Point(frameX, frameY - radius));
            dc.DrawLine(pivotPen, new Point(frameX, frameY), new Point(frameX + radius, frameY));

            // Corner resize handles
            var handleBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xA2, 0xE8));
            var handlePen = new Pen(Brushes.White, 1);
            DrawHandle(dc, handleBrush, handlePen, motionRect.TopLeft);
            DrawHandle(dc, handleBrush, handlePen, motionRect.TopRight);
            DrawHandle(dc, handleBrush, handlePen, motionRect.BottomRight);
            DrawHandle(dc, handleBrush, handlePen, motionRect.BottomLeft);

            dc.Pop();
        }
    }

    private static void DrawHandle(DrawingContext dc, Brush brush, Pen pen, Point pt)
    {
        dc.DrawRectangle(brush, pen, new Rect(pt.X - 3.5, pt.Y - 3.5, 7, 7));
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Is3DMode)
        {
            var updated = Keyframe.DeepClone();
            updated.PositionZ += e.Delta > 0 ? 50.0 : -50.0;
            Keyframe = updated;
            InvalidateVisual();
            KeyframeModified?.Invoke(updated);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        dragOrigin = e.GetPosition(this);
        initialDragKeyframe = Keyframe.DeepClone();

        var w = ActualWidth;
        var h = ActualHeight;
        var scale = Math.Min((w - 60) / Math.Max(1, ProjectWidth), (h - 60) / Math.Max(1, ProjectHeight));
        var centerX = w / 2.0;
        var centerY = h / 2.0;
        var frameX = centerX + (Keyframe.PositionX * scale);
        var frameY = centerY - (Keyframe.PositionY * scale);

        // Check if clicked near center pivot or body
        var distToCenter = Math.Sqrt(Math.Pow(dragOrigin.X - frameX, 2) + Math.Pow(dragOrigin.Y - frameY, 2));
        var radius = Math.Min(Keyframe.Width * scale, Keyframe.Height * scale) * 0.25;

        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            dragMode = 7; // 3D Tilt / Orbit (RotationX & RotationY)
        }
        else if (RotationEnabled && !Is3DMode && Math.Abs(distToCenter - radius) < 10)
        {
            dragMode = 6; // Rotate
        }
        else
        {
            dragMode = 1; // Move
        }

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (initialDragKeyframe is null || dragMode == 0) return;

        var pos = e.GetPosition(this);
        var delta = pos - dragOrigin;
        var w = ActualWidth;
        var h = ActualHeight;
        var scale = Math.Min((w - 60) / Math.Max(1, ProjectWidth), (h - 60) / Math.Max(1, ProjectHeight));

        var updated = initialDragKeyframe.DeepClone();
        if (dragMode == 1) // Move
        {
            updated.PositionX += delta.X / scale;
            updated.PositionY -= delta.Y / scale;
            if (SnappingEnabled)
            {
                if (Math.Abs(updated.PositionX) < 15) updated.PositionX = 0;
                if (Math.Abs(updated.PositionY) < 15) updated.PositionY = 0;
            }
        }
        else if (dragMode == 6) // Rotate
        {
            var centerX = w / 2.0 + (initialDragKeyframe.PositionX * scale);
            var centerY = h / 2.0 - (initialDragKeyframe.PositionY * scale);
            var rad = Math.Atan2(pos.Y - centerY, pos.X - centerX);
            var initialRad = Math.Atan2(dragOrigin.Y - centerY, dragOrigin.X - centerX);
            var angleDelta = (rad - initialRad) * 180.0 / Math.PI;
            updated.RotationZ = initialDragKeyframe.RotationZ + angleDelta;
            if (SnappingEnabled)
            {
                var snapped = Math.Round(updated.RotationZ / 15.0) * 15.0;
                if (Math.Abs(snapped - updated.RotationZ) < 5) updated.RotationZ = snapped;
            }
        }
        else if (dragMode == 7) // 3D Orbit (Pitch & Yaw)
        {
            updated.RotationY = initialDragKeyframe.RotationY + (delta.X * 0.5);
            updated.RotationX = initialDragKeyframe.RotationX - (delta.Y * 0.5);
        }

        Keyframe = updated;
        InvalidateVisual();
        KeyframeModified?.Invoke(updated);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragMode != 0)
        {
            dragMode = 0;
            initialDragKeyframe = null;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }
}

public sealed class TrackMotionKeyframeLane : FrameworkElement
{
    public Rational FrameRate { get; set; } = new(30, 1);
    public long DurationFrames { get; set; } = 100;
    public long CursorFrame { get; set; }
    public List<long> Keyframes { get; set; } = [];

    public event Action<long>? FrameChanged;

    private bool draggingCursor;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Background
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), null, new Rect(0, 0, w, h));

        // Time ruler at top
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), null, new Rect(0, 0, w, 20));
        var textBrush = TryFindResource("ForegroundMutedBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));

        // Keyframe track line
        var linePen = new Pen(new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x38)), 1);
        dc.DrawLine(linePen, new Point(0, 32), new Point(w, 32));
        dc.DrawLine(linePen, new Point(0, 54), new Point(w, 54));
        dc.DrawLine(linePen, new Point(0, 76), new Point(w, 76));

        // Keyframe diamond/flag icons (matching Vegas Pro salmon/orange right triangle)
        var keyBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x7A, 0x5F));
        var keyPen = new Pen(Brushes.White, 1);

        foreach (var frame in Keyframes)
        {
            var x = FrameToX(frame, w);
            // Draw flag triangle pointing right
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(new Point(x, 26), true, true);
                ctx.LineTo(new Point(x + 8, 32), true, false);
                ctx.LineTo(new Point(x, 38), true, false);
            }
            geom.Freeze();
            dc.DrawGeometry(keyBrush, keyPen, geom);
        }

        // Playhead cursor
        var cursorX = FrameToX(CursorFrame, w);
        var cursorPen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)), 1.5);
        dc.DrawLine(cursorPen, new Point(cursorX, 0), new Point(cursorX, h));
    }

    private double FrameToX(long frame, double width)
    {
        return Math.Clamp((double)frame / Math.Max(1, DurationFrames) * width, 0, width);
    }

    private long XToFrame(double x, double width)
    {
        return (long)Math.Round(Math.Clamp(x / width, 0.0, 1.0) * DurationFrames);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        draggingCursor = true;
        CursorFrame = XToFrame(e.GetPosition(this).X, ActualWidth);
        InvalidateVisual();
        FrameChanged?.Invoke(CursorFrame);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (draggingCursor)
        {
            CursorFrame = XToFrame(e.GetPosition(this).X, ActualWidth);
            InvalidateVisual();
            FrameChanged?.Invoke(CursorFrame);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (draggingCursor)
        {
            draggingCursor = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }
}

