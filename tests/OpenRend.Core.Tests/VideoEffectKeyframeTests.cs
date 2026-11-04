using System;
using System.Collections.Generic;
using System.IO;
using OpenRend.Core;
using Xunit;

namespace OpenRend.Core.Tests;

public sealed class VideoEffectKeyframeTests
{
    [Fact]
    public void VideoEffectKeyframe_Validation_CatchesInvalidValues()
    {
        var kf = new VideoEffectKeyframe { Frame = 10, Amount = 0.5 };
        kf.Validate();

        var invalidFrame = new VideoEffectKeyframe { Frame = -1 };
        Assert.Throws<InvalidDataException>(() => invalidFrame.Validate());

        var invalidAmt = new VideoEffectKeyframe { Frame = 0, Amount = double.NaN };
        Assert.Throws<InvalidDataException>(() => invalidAmt.Validate());

        var invalidParam = new VideoEffectKeyframe { Frame = 0 };
        invalidParam.Parameters.Add("", 1.0);
        Assert.Throws<InvalidDataException>(() => invalidParam.Validate());
    }

    [Fact]
    public void VideoEffectAnimation_Evaluation_LinearInterpolation()
    {
        var fx = VideoEffect.Create(VideoEffectKind.BrightnessContrast);
        var anim = new VideoEffectAnimation { IsEnabled = true };

        anim.Keyframes.Add(new VideoEffectKeyframe
        {
            Frame = 0,
            Amount = 0.0,
            SecondaryAmount = 0.0,
            Parameters = new Dictionary<string, double> { ["Brightness"] = 0.0, ["Contrast"] = 0.0 },
            Interpolation = EffectInterpolation.Linear
        });

        anim.Keyframes.Add(new VideoEffectKeyframe
        {
            Frame = 100,
            Amount = 1.0,
            SecondaryAmount = 2.0,
            Parameters = new Dictionary<string, double> { ["Brightness"] = 10.0, ["Contrast"] = 20.0 },
            Interpolation = EffectInterpolation.Linear
        });

        // Frame before first: clamp
        var (amtBefore, secBefore, paramsBefore) = anim.Evaluate(-10, fx);
        Assert.Equal(0.0, amtBefore);
        Assert.Equal(0.0, paramsBefore["Brightness"]);

        // Frame mid: linear
        var (amtMid, secMid, paramsMid) = anim.Evaluate(50, fx);
        Assert.Equal(0.5, amtMid, precision: 4);
        Assert.Equal(1.0, secMid, precision: 4);
        Assert.Equal(5.0, paramsMid["Brightness"], precision: 4);
        Assert.Equal(10.0, paramsMid["Contrast"], precision: 4);

        // Frame after last: clamp
        var (amtAfter, _, paramsAfter) = anim.Evaluate(150, fx);
        Assert.Equal(1.0, amtAfter);
        Assert.Equal(10.0, paramsAfter["Brightness"]);
    }

    [Fact]
    public void VideoEffectAnimation_Evaluation_HoldFastSlowSmoothSharp()
    {
        var fx = VideoEffect.Create(VideoEffectKind.GaussianBlur);
        var anim = new VideoEffectAnimation { IsEnabled = true };

        anim.Keyframes.Add(new VideoEffectKeyframe
        {
            Frame = 0,
            Amount = 0.0,
            Interpolation = EffectInterpolation.Hold
        });
        anim.Keyframes.Add(new VideoEffectKeyframe
        {
            Frame = 100,
            Amount = 10.0,
            Interpolation = EffectInterpolation.Slow
        });
        anim.Keyframes.Add(new VideoEffectKeyframe
        {
            Frame = 200,
            Amount = 20.0,
            Interpolation = EffectInterpolation.Fast
        });
        anim.Keyframes.Add(new VideoEffectKeyframe
        {
            Frame = 300,
            Amount = 30.0,
            Interpolation = EffectInterpolation.Smooth,
            Smoothness = 100.0
        });
        anim.Keyframes.Add(new VideoEffectKeyframe
        {
            Frame = 400,
            Amount = 40.0,
            Interpolation = EffectInterpolation.Sharp
        });
        anim.Keyframes.Add(new VideoEffectKeyframe
        {
            Frame = 500,
            Amount = 50.0
        });

        // Hold segment [0..100): stays at 0.0
        Assert.Equal(0.0, anim.Evaluate(50, fx).Amount);
        Assert.Equal(0.0, anim.Evaluate(99, fx).Amount);

        // Slow segment [100..200): progress^2. At 50% progress (frame 150), progress^2 = 0.25 -> 10 + 0.25*10 = 12.5
        Assert.Equal(12.5, anim.Evaluate(150, fx).Amount, precision: 3);

        // Fast segment [200..300): 1 - (1-p)^2. At 50% progress (frame 250), 1 - 0.25 = 0.75 -> 20 + 0.75*10 = 27.5
        Assert.Equal(27.5, anim.Evaluate(250, fx).Amount, precision: 3);

        // Smooth segment [300..400): Hermite S-curve 3p^2 - 2p^3. At 50% progress (frame 350), weight is 0.5 -> 35.0
        Assert.Equal(35.0, anim.Evaluate(350, fx).Amount, precision: 3);
        // At 25% progress (frame 325): weight = 3*(0.25)^2 - 2*(0.25)^3 = 0.1875 - 0.03125 = 0.15625 -> 30 + 1.5625 = 31.5625 < linear 32.5
        Assert.True(anim.Evaluate(325, fx).Amount < 32.5);

        // Sharp segment [400..500): 0.5 + 4*centered^3. At 50% (frame 450), weight is 0.5 -> 45.0
        Assert.Equal(45.0, anim.Evaluate(450, fx).Amount, precision: 3);
    }

    [Fact]
    public void VideoEffect_EvaluateAt_ClonesAndAppliesEvaluatedParameters()
    {
        var fx = VideoEffect.Create(VideoEffectKind.BrightnessContrast);
        fx.Amount = 0.2;

        fx.Animation = new VideoEffectAnimation { IsEnabled = true };
        fx.Animation.Keyframes.Add(new VideoEffectKeyframe { Frame = 0, Amount = 0.1 });
        fx.Animation.Keyframes.Add(new VideoEffectKeyframe { Frame = 50, Amount = 0.5 });

        var at0 = fx.EvaluateAt(0);
        Assert.Equal(0.1, at0.Amount, precision: 3);

        var at25 = fx.EvaluateAt(25);
        Assert.Equal(0.3, at25.Amount, precision: 3);

        var at50 = fx.EvaluateAt(50);
        Assert.Equal(0.5, at50.Amount, precision: 3);

        // Base effect remains untouched
        Assert.Equal(0.2, fx.Amount);
    }
}

