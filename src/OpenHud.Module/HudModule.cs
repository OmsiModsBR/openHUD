using OmsiLaunch.Api;
using OmsiLaunch.Runtime.Modules;

namespace OpenHud;

// The entry of the openHUD module. Everything it does goes through the context
// the bridge hands it; it holds no address and no pointer.
public sealed class HudModule : IRuntimeModule, ITickingModule
{
    private IRuntimeModuleContext? context;

    public void Arm(IRuntimeModuleContext context)
    {
        this.context = context;
        context.Emit("armed", new Dictionary<string, string> { ["omsi_version"] = context.OmsiVersion });
    }

    public void GameplayReady() => context?.Emit("gameplay-ready");

    public void Tick(TimeSpan elapsed) { }

    public IReadOnlyDictionary<string, string> Execute(string operation, IReadOnlyDictionary<string, string> arguments) =>
        throw new ModuleOperationException(PublicErrorCodes.MODULE_OPERATION_UNKNOWN, operation);

    public void Stop() => context?.Emit("stopping");
}
