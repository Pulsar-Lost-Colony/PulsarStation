using Content.Shared.Robotics;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.Robotics.UI;

public sealed class RoboticsConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    public RoboticsConsoleWindow RoboticsWindow = default!;

    public RoboticsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        RoboticsWindow = this.CreateWindow<RoboticsConsoleWindow>();
        RoboticsWindow.SetEntity(Owner);

        RoboticsWindow.OnDisablePressed += address =>
        {
            SendMessage(new RoboticsConsoleDisableMessage(address));
        };
        RoboticsWindow.OnDestroyPressed += address =>
        {
            SendMessage(new RoboticsConsoleDestroyMessage(address));
        };
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not RoboticsConsoleState cast)
            return;

        RoboticsWindow.UpdateState(cast);
    }
}
