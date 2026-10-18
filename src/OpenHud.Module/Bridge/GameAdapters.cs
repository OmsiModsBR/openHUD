using OmsiLaunch.Runtime.Modules;
using OpenHud.Model;
using OpenHud.Packages.Realmap;

namespace OpenHud.Bridge;

// IGameReader over the catalog operations the manifest declares (IModuleRuntime). A failed
// operation keeps its typed OL_E_* code; an exception of the bridge is left to FrameSource,
// which turns it into "this part is unavailable".
internal sealed class GameReaderAdapter : IGameReader
{
    private readonly IModuleRuntime runtime;

    public GameReaderAdapter(IModuleRuntime runtime) => this.runtime = runtime;

    public GameReply Read(string operation, IReadOnlyDictionary<string, string>? arguments = null)
    {
        var result = runtime.Execute(operation, arguments);
        if (result is null) return GameReply.Fail("OL_E_RUNTIME_OPERATION_FAILED");
        if (!result.Succeeded) return GameReply.Fail(result.ErrorCode ?? "OL_E_RUNTIME_OPERATION_FAILED");
        return GameReply.Ok(result.Values ?? new Dictionary<string, string>());
    }
}

// IRealmapSource over IModuleContent. The fingerprint travels as the package's cache key and nothing else.
internal sealed class RealmapSourceAdapter : IRealmapSource
{
    private readonly IModuleContent content;

    public RealmapSourceAdapter(IModuleContent content) => this.content = content;

    public RealmapSourceResult Locate(string mapIdentity)
    {
        var result = content.LocateRealmapPackage(mapIdentity);
        if (result is null) return new RealmapSourceResult(false, null, "", "OL_E_MODULE_CONTENT_INPUT_INVALID", "the bridge returned nothing");
        if (result.Found && result.Package is { } package)
            return new RealmapSourceResult(true, package.Directory, package.Fingerprint, null, null);
        return new RealmapSourceResult(false, null, "", result.ErrorCode, result.Detail);
    }
}
