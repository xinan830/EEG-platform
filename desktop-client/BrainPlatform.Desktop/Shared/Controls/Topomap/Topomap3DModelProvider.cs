using BrainPlatform.Desktop.Modules.Channels.Api;

namespace BrainPlatform.Desktop.Shared.Controls.Topomap;

/// <summary>
/// Workspace-scoped accessor for the cached 3D standard scalp model so Review and Live
/// Acquisition topomap views can switch to 3D on demand without extra ViewModel plumbing.
/// </summary>
public static class Topomap3DModelProvider
{
    private static IElectrodeLocationClient? client;

    public static void Register(IElectrodeLocationClient? locationClient) => client = locationClient;

    public static Task<ElectrodeScalpModel3D> GetScalpModelAsync(CancellationToken cancellationToken)
    {
        var activeClient = client ?? throw new InvalidOperationException("三维头模服务尚未初始化。");
        return activeClient.GetScalpModel3DAsync(cancellationToken);
    }

    public static Task<string> GetCortexModelJsonAsync(CancellationToken cancellationToken)
    {
        var activeClient = client ?? throw new InvalidOperationException("三维大脑皮层服务尚未初始化。");
        return activeClient.GetCortexModel3DJsonAsync(cancellationToken);
    }

    public static Task<ElectrodeMontageCatalog> GetMontageCatalogAsync(CancellationToken cancellationToken)
    {
        var activeClient = client ?? throw new InvalidOperationException("电极模板目录服务尚未初始化。");
        return activeClient.GetMontageCatalogAsync(cancellationToken);
    }

    public static Task<ElectrodeMontagePreview> GetMontagePreviewAsync(string montageName, CancellationToken cancellationToken)
    {
        var activeClient = client ?? throw new InvalidOperationException("电极模板预览服务尚未初始化。");
        return activeClient.GetMontagePreviewAsync(montageName, cancellationToken);
    }
}
