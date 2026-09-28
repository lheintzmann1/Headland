using Headland.Core.Input;

namespace Headland.Core.Tests;

public class InputTests
{
    private const InputContext OnFoot = InputContext.World | InputContext.OnFoot;
    private const InputContext Driving = InputContext.World | InputContext.Vehicle;

    /// <summary>The simulation's actions, with a camera key and a menu key on the same Q, and a light key.</summary>
    private static Bindings Catalog() => new(InputActions.Defs.Concat(
    [
        new InputActionDef("camera_left", "Rotate the camera", InputContext.World, "Q"),
        new InputActionDef("tab_left", "Previous tab", InputContext.Menu, "Q"),
        new InputActionDef("lights", "Lights", InputContext.Vehicle, "L"),
        new InputActionDef("signal_left", "Turn signal left", InputContext.Vehicle, "Ctrl+Q"),
        new InputActionDef("pan", "Pan", InputContext.World, "Mouse Middle") { Analog = true },
    ]));

    private static InputRouter Router(InputContext context, Bindings? bindings = null) => new(bindings ?? Catalog()) { Context = context };

    [Theory]
    [InlineData("W", "W", Modifiers.None, InputTrigger.Press)]
    [InlineData("Shift+Tab", "Tab", Modifiers.Shift, InputTrigger.Press)]
    [InlineData("hold ctrl+alt+V", "V", Modifiers.Ctrl | Modifiers.Alt, InputTrigger.Hold)]
    [InlineData("Double Shift", "Shift", Modifiers.None, InputTrigger.DoubleTap)]
    [InlineData("mouse wheel up", "Mouse Wheel Up", Modifiers.None, InputTrigger.Press)]
    [InlineData("Ctrl+Mouse Left", "Mouse Left", Modifiers.Ctrl, InputTrigger.Press)]
    [InlineData("Joy LX-", "Joy LX-", Modifiers.None, InputTrigger.Press)]
    [InlineData("Hold joy a", "Joy A", Modifiers.None, InputTrigger.Hold)]
    public void BindingsReadFromTheirTextForm(string text, string input, Modifiers modifiers, InputTrigger trigger)
    {
        var binding = InputBinding.Parse(text);
        Assert.Equal(new InputBinding(input, modifiers, trigger), binding);
        Assert.Equal(binding, InputBinding.Parse(binding.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Hold Alt+")]
    [InlineData("Mouse Thumb")]
    [InlineData("Joy Z")]
    [InlineData("Shift+Joy A")]
    public void WrongBindingsAreRefused(string text)
    {
        Assert.False(InputBinding.TryParse(text, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void TheDefaultsShareNoBinding()
    {
        Assert.Empty(Catalog().AllConflicts());
    }

    [Fact]
    public void ConflictsAreCheckedPerContext()
    {
        var bindings = Catalog();
        // Q rotates the camera walking and driving, so lowering on it would clash; the menu's Q never meets them.
        Assert.Equal(["camera_left"], bindings.Conflicts(InputActions.Lower, InputBinding.Parse("Q")));
        Assert.Empty(bindings.Conflicts("tab_left", InputBinding.Parse("Tab")));
        // Different modifiers or triggers are told apart; an analog action takes its input whatever they are.
        Assert.Empty(bindings.Conflicts(InputActions.Lower, InputBinding.Parse("Ctrl+V")));
        Assert.Empty(bindings.Conflicts(InputActions.Lower, InputBinding.Parse("Hold B")));
        Assert.Equal([InputActions.MoveLeft], bindings.Conflicts("lights", InputBinding.Parse("Ctrl+A")));

        bindings.Set(InputActions.Use, [InputBinding.Parse("Q")]);
        Assert.Equal(new InputConflict(InputActions.Use, "camera_left", InputBinding.Parse("Q")), Assert.Single(bindings.AllConflicts()));
    }

    [Fact]
    public void TheMostSpecificModifiersWin()
    {
        var router = Router(Driving);
        router.Press("Tab", Modifiers.Shift, 0);
        Assert.Equal([InputActions.PrevVehicle], router.Poll(0));
        router.Release("Tab", 0.1);

        // Running with Shift held, F still gets in.
        router.Context = OnFoot;
        router.Press("F", Modifiers.Shift, 1);
        Assert.Equal([InputActions.Enter], router.Poll(1));

        // Ctrl+Q signals, Q alone rotates the camera.
        router.Context = Driving;
        router.Press("Q", Modifiers.Ctrl, 2);
        router.Release("Q", 2.1);
        router.Press("Q", Modifiers.None, 3);
        Assert.Equal(["signal_left", "camera_left"], router.Poll(3));
    }

    [Fact]
    public void TheSameKeyDoesWhatTheContextNeeds()
    {
        var router = Router(OnFoot);
        router.Press("Q", Modifiers.None, 0);
        router.Release("Q", 0.1);
        router.Context = InputContext.Menu;
        router.Press("Q", Modifiers.None, 1);
        // Out of context: lowering on V, walking on W.
        router.Press("V", Modifiers.None, 1);
        router.Press("W", Modifiers.None, 1);
        Assert.Equal(["camera_left", "tab_left"], router.Poll(1));
        Assert.Equal(0f, router.Strength(InputActions.MoveForward));
        router.Context = OnFoot;
        Assert.Equal(1f, router.Strength(InputActions.MoveForward));
    }

    [Fact]
    public void AKeyWithAHoldFiresItsPressOnAQuickRelease()
    {
        var bindings = Catalog();
        bindings.Set("lights", [InputBinding.Parse("L")]);
        bindings.Set(InputActions.Helper, [InputBinding.Parse("Hold L")]);
        var router = Router(Driving, bindings);

        router.Press("L", Modifiers.None, 0);
        Assert.Empty(router.Poll(0.2));
        router.Release("L", 0.25);
        Assert.Equal(["lights"], router.Poll(0.25));

        router.Press("L", Modifiers.None, 1);
        Assert.Empty(router.Poll(1.3));
        Assert.Equal([InputActions.Helper], router.Poll(1.41));
        router.Release("L", 2);
        Assert.Empty(router.Poll(2));
    }

    [Fact]
    public void ADoubleTapFiresInsteadOfTwoPresses()
    {
        var bindings = Catalog();
        bindings.Set(InputActions.Steering, [InputBinding.Parse("Double L")]);
        var router = Router(Driving, bindings);

        router.Press("L", Modifiers.None, 0);
        router.Release("L", 0.1);
        router.Press("L", Modifiers.None, 0.3);
        router.Release("L", 0.35);
        Assert.Equal([InputActions.Steering], router.Poll(1));

        // A single tap fires once the second didn't come.
        router.Press("L", Modifiers.None, 2);
        router.Release("L", 2.1);
        Assert.Empty(router.Poll(2.3));
        Assert.Equal(["lights"], router.Poll(2.41));
    }

    [Fact]
    public void AnalogActionsReadKeysAndSticksPastTheDeadZone()
    {
        var router = Router(Driving);
        router.Press("Space", Modifiers.Ctrl, 0);
        Assert.True(router.Held(InputActions.Brake));
        Assert.Empty(router.Poll(0));

        router.Axis("Joy LY", -0.6f, 0);
        Assert.Equal(0.5f, router.Strength(InputActions.MoveForward), 3);
        Assert.Equal(0f, router.Strength(InputActions.MoveBack));
        router.Axis("Joy LY", 0.15f, 0);
        Assert.Equal(0f, router.Strength(InputActions.MoveBack));
        router.Axis("Joy LT", 1f, 0);
        router.Release("Space", 0);
        Assert.True(router.Held(InputActions.Brake));

        router.Press("Mouse Middle", Modifiers.None, 0);
        Assert.True(router.Held("pan"));
    }

    [Fact]
    public void MouseAxesGiveTheMotionOfTheFrame()
    {
        var bindings = Catalog();
        bindings.Add(new InputActionDef("look_right", "Look right", InputContext.World, "Mouse X+") { Analog = true });
        var router = Router(Driving, bindings);
        router.Axis("Mouse X", 12f, 0);
        router.Axis("Mouse X", -2f, 0);
        router.Axis("Mouse X", 3f, 0);
        Assert.Equal(15f, router.Strength("look_right"));
        router.EndFrame();
        Assert.Equal(0f, router.Strength("look_right"));
    }

    [Fact]
    public void AStickPushedPastHalfWayActsAsAButton()
    {
        var bindings = Catalog();
        bindings.Set(InputActions.NextVehicle, [InputBinding.Parse("Joy RX+")]);
        var router = Router(Driving, bindings);
        router.Axis("Joy RX", 0.3f, 0);
        Assert.Empty(router.Poll(0));
        // Pressed at half way, released below 0.4 only.
        router.Axis("Joy RX", 0.7f, 0);
        router.Axis("Joy RX", 0.45f, 0.1);
        router.Axis("Joy RX", 0.9f, 0.2);
        Assert.Equal([InputActions.NextVehicle], router.Poll(0.2));
        router.Axis("Joy RX", 0f, 0.3);
        router.Axis("Joy RX", 0.8f, 0.4);
        Assert.Equal([InputActions.NextVehicle], router.Poll(0.4));
    }

    [Fact]
    public void ChangingContextOrLosingFocusDropsWhatWasPending()
    {
        var bindings = Catalog();
        bindings.Set(InputActions.Helper, [InputBinding.Parse("Hold H")]);
        var router = Router(Driving, bindings);
        router.Press("H", Modifiers.None, 0);
        router.Context = InputContext.Menu;
        Assert.Empty(router.Poll(1));
        router.Context = Driving;
        router.ReleaseAll();
        router.Press("H", Modifiers.None, 2);
        router.ReleaseAll();
        Assert.Empty(router.Poll(3));
        Assert.Empty(router.Down);
    }
}
